"""同位置三份系统图与源PNG的静态对照；不把静态采样当成无闪帧证明。"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
from PIL import Image

spec = importlib.util.spec_from_file_location('surface', Path(__file__).with_name('verify-refresh-surface.py'))
surface = importlib.util.module_from_spec(spec)
spec.loader.exec_module(surface)


def crop_surface(image, metadata):
    if metadata.get('capture') != 'screen' or metadata.get('method') != 'System.Drawing.Graphics.CopyFromScreen':
        raise ValueError('仅接受原始系统截图')
    if metadata.get('resampled') is not False or metadata.get('stableActivation') is not True or metadata.get('stableProcessFrameAndDpi') is not True:
        raise ValueError('采样必须保持原尺寸、窗口身份及活动状态')
    region, frame = metadata['regionPhysical'], metadata['frameRectPhysical']
    if image.size != tuple(region[2:]):
        raise ValueError('图像尺寸与屏幕元数据不同')
    left, top, right, bottom = frame[0]-region[0], frame[1]-region[1], frame[2]-region[0], frame[3]-region[1]
    if not (0 <= left < right <= image.width and 0 <= top < bottom <= image.height):
        raise ValueError('模态边界不在采样区域内')
    return image.convert('RGB').crop((left, top, right, bottom))


def verify(folder):
    folder = Path(folder)
    frames, metadata, hashes = [], [], {}
    for name in ['settings', 'cover-visible-live', 'cover-hidden-live']:
        path = folder/(name+'.png')
        item = json.loads((folder/(name+'.png.json')).read_text(encoding='utf-8'))
        if metadata:
            for key in ['frameRectPhysical', 'regionPhysical', 'dpi', 'processId', 'windowHandle', 'activationState']:
                if item.get(key) != metadata[0].get(key):
                    raise ValueError('采样身份或边界不同：'+key)
        metadata.append(item)
        hashes[name] = hashlib.sha256(path.read_bytes()).hexdigest()
        if item.get('sha256') != hashes[name]:
            raise ValueError('截图哈希与采样元数据不同')
        with Image.open(path) as image:
            frames.append(crop_surface(image, item))
    path = folder/'cover-source.png'
    source_metadata = json.loads((folder/'cover-source.json').read_text(encoding='utf-8'))
    if source_metadata.get('method') != 'cdp-loaded-resource-content':
        raise ValueError('源图必须来自已加载资源，不能用控制器截图替代')
    with Image.open(path) as source:
        if source.size != (source_metadata['width'], source_metadata['height']):
            raise ValueError('源图真实尺寸与元数据不同')
        alpha = source.convert('RGBA').getchannel('A').getextrema()
        source_info = {'mode': source.mode, 'size': source.size, 'alphaExtrema': alpha,
                       'opaque': alpha == (255, 255), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
    state_path = folder/'cover-state.json'
    state = json.loads(state_path.read_text(encoding='utf-8')) if state_path.exists() else None
    result = {'source': source_info, 'screenHashes': hashes,
              'priorCoverBytes': state['beforeCover']['captureCoverBytes'] if state else None,
              'beforeToCover': surface.difference(frames[0], frames[1]),
              'visibleToHidden': surface.difference(frames[1], frames[2], tolerance=0),
              'fullPresentationVerified': False,
              'limitation': '静态对照只定位源图与遮盖合成差异；未覆盖过渡帧、全部主题或全部模态。'}
    for frame in frames:
        frame.close()
    (folder/'cover-composition-analysis.json').write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8', newline='\n')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder')
    args = parser.parse_args()
    print(json.dumps(verify(args.folder), ensure_ascii=False))
