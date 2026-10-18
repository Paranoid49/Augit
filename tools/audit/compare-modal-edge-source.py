"""按宿主物理裁切核对源图与系统边缘；保留系统描边差异，不作通过判定。"""
import hashlib
import json
import sys
from pathlib import Path
import numpy as np
from PIL import Image


def compare(source, screen):
    if source.shape != screen.shape or source.ndim != 3 or source.shape[2] != 3:
        raise ValueError('必须是同尺寸RGB图像')
    if source.dtype != np.uint8 or screen.dtype != np.uint8:
        raise ValueError('只接受原始8位像素')
    delta = np.abs(source.astype(np.int16)-screen.astype(np.int16)).max(axis=2)
    different = delta > 2
    edge = np.zeros(different.shape, dtype=bool)
    edge[:3, :] = edge[-3:, :] = True
    edge[:, :3] = edge[:, -3:] = True
    chromatic = np.ptp(screen.astype(np.int16), axis=2) > 12
    neutral_source = np.ptp(source.astype(np.int16), axis=2) <= 2
    ys, xs = np.nonzero(different)
    return {'differentPixels': int(different.sum()), 'edge3Pixels': int((different & edge).sum()),
            'interiorPixels': int((different & ~edge).sum()),
            'neutralSourceChromaticScreenEdgePixels': int((different & edge & chromatic & neutral_source).sum()),
            'maximumChannelDifference': int(delta.max()),
            'bounds': [int(xs.min()), int(ys.min()), int(xs.max()+1), int(ys.max()+1)] if len(xs) else None,
            'excludedPixels': 0, 'fullPresentationVerified': False}


def background_box(info, native):
    owner = info.get('ownerGeometry')
    if info.get('stableOwnerGeometry') is not True or not owner:
        raise ValueError('缺少稳定的所有者客户区坐标')
    if owner['window'] != native['owner'] or owner['processId'] != info['processId'] or owner['dpi'] != info['dpi']:
        raise ValueError('背景所有者身份或DPI不符')
    origin, client, rect = owner['clientOriginPhysical'], owner['clientRectPhysical'], native['rect']
    if client[:2] != [0, 0] or any(type(v) is not int for v in origin+client):
        raise ValueError('客户区坐标无效')
    box = [rect['x']-origin[0], rect['y']-origin[1], rect['x']-origin[0]+rect['width'], rect['y']-origin[1]+rect['height']]
    if not (0 <= box[0] < box[2] <= client[2] and 0 <= box[1] < box[3] <= client[3]):
        raise ValueError('模态背景区域超出所有者客户区')
    return box


def blend_diagnostic(background, screen):
    # 上边前半段拟合，后半段及左边独立验证；不搜索偏移、不修图。
    h, w = screen.shape[:2]
    if background.shape != screen.shape or min(h, w) < 50:
        raise ValueError('背景与系统图尺寸不符或太小')
    train = np.zeros((h, w), dtype=bool)
    train[1, 20:w//2] = True
    regions = {'topHeldOut': (1, slice(w//2, w-20)), 'leftHeldOut': (slice(20, h-20), 1)}
    x, y = background[train].astype(float).ravel(), screen[train].astype(float).ravel()
    if np.std(x) < 1:
        raise ValueError('背景变化不足以拟合')
    slope, intercept = np.linalg.lstsq(np.column_stack([x, np.ones_like(x)]), y, rcond=None)[0]
    result = {'slope': float(slope), 'intercept': float(intercept), 'trainingMse': float(np.mean((slope*x+intercept-y)**2))}
    for name, region in regions.items():
        residual = slope*background[region].astype(float)+intercept-screen[region].astype(float)
        result[name] = {'pixels': int(residual.size//3), 'mse': float(np.mean(residual**2)), 'maxChannelError': float(np.max(np.abs(residual)))}
    return result


def analyze(folder):
    folder = Path(folder)
    report = json.loads((folder/'edge-source.json').read_text(encoding='utf-8'))
    info = json.loads((folder/'before.png.json').read_text(encoding='utf-8'))
    launch = json.loads((folder/'launch.json').read_text(encoding='utf-8'))
    if report.get('method') != 'cdp-full-surface-png' or info.get('method') != 'System.Drawing.Graphics.CopyFromScreen' or info.get('resampled') is not False:
        raise ValueError('来源不符')
    for file, manifest in [('edge-source.png', report), ('before.png', info)]:
        if hashlib.sha256((folder/file).read_bytes()).hexdigest() != manifest.get('sha256'):
            raise ValueError('图片哈希不符')
    native, geometry = report['native'], report['geometry']
    if native.get('active') is not True or info['windowHandle'] != native['window'] or info['processId'] != launch['pid']:
        raise ValueError('窗口或进程身份不符')
    if info.get('stableActivation') is not True or info.get('stableProcessFrameAndDpi') is not True or abs(info['dpi']/96-geometry['dpr']) > .001:
        raise ValueError('激活、边界或DPI不符')
    bounds, rect = native['liveBounds'], native['rect']
    box = [-bounds['x'], -bounds['y'], -bounds['x']+rect['width'], -bounds['y']+rect['height']]
    if report['sourceBox'] != box or any(type(v) is not int for v in box):
        raise ValueError('裁切不是宿主实际整数边界')
    expected_frame = [rect['x'], rect['y'], rect['x']+rect['width'], rect['y']+rect['height']]
    if info['frameRectPhysical'] != expected_frame or info['regionPhysical'] != [rect['x'], rect['y'], rect['width'], rect['height']]:
        raise ValueError('系统截图边界不符')
    source = Image.open(folder/'edge-source.png').convert('RGB')
    screen = Image.open(folder/'before.png').convert('RGB')
    if screen.size != (rect['width'], rect['height']) or source.width < bounds['width'] or source.height < bounds['height']:
        raise ValueError('图片尺寸不足')
    if not (0 <= box[0] < box[2] <= source.width and 0 <= box[1] < box[3] <= source.height):
        raise ValueError('源图裁切越界')
    a, b = np.asarray(source.crop(box)), np.asarray(screen)
    result = {'sourceSha256': report['sha256'], 'screenSha256': info['sha256'], 'sourceBox': box,
            'comparison': compare(a, b), 'diagnosticOnly': True,
            'limitation': '两次采样并非原子同时；系统边框与源图本就不同。中性色变彩色仅定位线索，不证明像素来自哪一底层窗口。'}
    if info.get('ownerGeometry'):
        background_info = json.loads((folder/'edge-background.json').read_text(encoding='utf-8'))
        digest = hashlib.sha256((folder/'edge-background.png').read_bytes()).hexdigest().upper()
        if background_info.get('method') != 'cdp-loaded-resource-content' or digest != background_info.get('sha256') or digest != native['backgroundHash']:
            raise ValueError('背景资源来源或哈希不符')
        mapped = background_box(info, native)
        background = Image.open(folder/'edge-background.png').convert('RGB')
        if background.width < mapped[2] or background.height < mapped[3]:
            raise ValueError('背景资源尺寸不足')
        result['backgroundScreenBox'] = mapped
        result['backgroundBlendDiagnostic'] = blend_diagnostic(np.asarray(background.crop(mapped)), b)
    return result


if __name__ == '__main__':
    folder = Path(sys.argv[1])
    result = analyze(folder)
    with (folder/'edge-source-analysis.json').open('x', encoding='utf-8', newline=chr(10)) as output:
        json.dump(result, output, ensure_ascii=False, indent=2)
        output.write(chr(10))
    print(json.dumps(result, ensure_ascii=False))
