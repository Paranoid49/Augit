"""按实测屏幕坐标检验背景纹理在边缘闪变中的贡献；不配准、不判无闪通过。"""
import hashlib
import importlib.util
import json
import subprocess
import sys
from pathlib import Path
import numpy as np
from PIL import Image

spec = importlib.util.spec_from_file_location('edge', Path(__file__).with_name('compare-modal-edge-source.py'))
edge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(edge)


def fit_edge(background, screen):
    if background.shape != screen.shape or background.dtype != np.uint8 or screen.dtype != np.uint8:
        raise ValueError('必须为相同尺寸原始RGB')
    width = screen.shape[1]
    # 偶数横坐标拟合，奇数横坐标独立验证；两组都必须包含背景纹理。
    xs = np.arange(20, width-20)
    train, held = xs[xs % 2 == 0], xs[xs % 2 == 1]
    x = background[1, train].astype(float).ravel()
    y = screen[1, train].astype(float).ravel()
    hx = background[1, held].astype(float).ravel()
    hy = screen[1, held].astype(float).ravel()
    if min(np.std(x), np.std(hx)) < 10:
        raise ValueError('拟合或留出区域纹理不足，不能推断混合')
    a, b = np.linalg.lstsq(np.column_stack([x, np.ones_like(x)]), y, rcond=None)[0]
    return {'slope': float(a), 'intercept': float(b), 'trainingPixels': len(train),
            'heldOutPixels': len(held), 'trainingBackgroundStd': float(np.std(x)),
            'heldOutBackgroundStd': float(np.std(hx)), 'trainingMse': float(np.mean((a*x+b-y)**2)),
            'heldOutMse': float(np.mean((a*hx+b-hy)**2)),
            'heldOutMaxError': float(np.max(np.abs(a*hx+b-hy)))}


def decode_frame(movie, number, shape):
    decoded = subprocess.run(['ffmpeg', '-v', 'error', '-i', str(movie),
                              '-vf', 'select=eq(n'+chr(92)+','+str(number)+')', '-frames:v', '1',
                              '-pix_fmt', 'rgb24', '-f', 'rawvideo', 'pipe:1'], capture_output=True, check=True)
    if len(decoded.stdout) != int(np.prod(shape)):
        raise ValueError('指定帧解码尺寸不符')
    return np.frombuffer(decoded.stdout, dtype=np.uint8).reshape(shape)


def analyze(folder):
    folder = Path(folder)
    source = edge.analyze(folder)
    initial = json.loads((folder/'edge-source.json').read_text(encoding='utf-8'))['native']
    final = json.loads((folder/'edge-background-settled.json').read_text(encoding='utf-8'))
    state = final['native']
    for key in ['window', 'owner', 'rect', 'liveBounds']:
        if state[key] != initial[key]:
            raise ValueError('前后窗口或布局身份不同')
    if (final['method'] != 'cdp-loaded-resource-content' or not final['complete']
            or state['backgroundImageRevision'] != state['backgroundLoadedRevision']
            or state['backgroundImageRevision'] <= initial['backgroundImageRevision']
            or state['captureCoverReady'] or state['captureCoverWindowVisible']):
        raise ValueError('末态不是已完成背景刷新')
    digest = hashlib.sha256((folder/'edge-background-settled.png').read_bytes()).hexdigest().upper()
    if digest != final['sha256'] or digest != state['backgroundHash']:
        raise ValueError('末态背景哈希不符')
    box = source['backgroundScreenBox']
    old_image = Image.open(folder/'edge-background.png').convert('RGB')
    new_image = Image.open(folder/'edge-background-settled.png').convert('RGB')
    if old_image.size != new_image.size or new_image.width < box[2] or new_image.height < box[3]:
        raise ValueError('背景尺寸改变或区域越界')
    old = np.asarray(old_image.crop(box))
    new = np.asarray(new_image.crop(box))
    baseline = np.asarray(Image.open(folder/'before.png').convert('RGB'))
    worst = np.asarray(Image.open(folder/'worst-frame.png').convert('RGB'))
    whole = json.loads((folder/'full-frame-analysis.json').read_text(encoding='utf-8'))
    with (folder/'refresh-screen.mkv').open('rb') as video:
        if hashlib.file_digest(video, 'sha256').hexdigest() != whole['videoSha256']:
            raise ValueError('录像身份不符')
    frame_number = whole['worst']['frame']
    decoded = decode_frame(folder/'refresh-screen.mkv', frame_number, worst.shape)
    if not np.array_equal(decoded, worst):
        raise ValueError('最差PNG不是录像指定帧')
    last = decode_frame(folder/'refresh-screen.mkv', whole['frameCount']-1, worst.shape)
    changes = edge.compare(baseline, worst)
    if changes['differentPixels'] != whole['worst']['differentPixels']:
        raise ValueError('最差帧与完整分析不符')
    delta = np.abs(old.astype(np.int16)-new.astype(np.int16)).max(axis=2)
    return {'backgroundScreenBox': box, 'initialBackgroundSha256': initial['backgroundHash'],
            'settledBackgroundSha256': digest, 'backgroundChangedPixels': int((delta > 2).sum()),
            'backgroundTopTwoRowsChangedPixels': int((delta[:2] > 2).sum()),
            'baselineFit': fit_edge(old, baseline), 'worstFit': fit_edge(old, worst),
            'lastFrameFit': fit_edge(new, last), 'lastFrameChange': edge.compare(baseline, last),
            'screenChange': changes, 'videoSha256': whole['videoSha256'], 'worstFrame': whole['worst']['frame'],
            'diagnosticOnly': True, 'fullPresentationVerified': False,
            'limitation': '末态背景不是瞬变时的原子采样；回归相关性不证明DWM实现因果。所有像素仍保留在完整帧分析。'}


if __name__ == '__main__':
    folder = Path(sys.argv[1])
    result = analyze(folder)
    with (folder/'edge-background-transition.json').open('x', encoding='utf-8', newline=chr(10)) as out:
        json.dump(result, out, ensure_ascii=False, indent=2)
        out.write(chr(10))
    print(json.dumps(result, ensure_ascii=False))
