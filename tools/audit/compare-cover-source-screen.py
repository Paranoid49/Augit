# 将原始源图按已记录的物理偏移裁取，与系统客户区直接比较；边缘分组只定位，不放宽验收。
import json
import sys
import hashlib
from pathlib import Path
from PIL import Image, ImageChops

def difference(a, b):
    bands = ImageChops.difference(a, b).split()
    mask = ImageChops.lighter(ImageChops.lighter(bands[0], bands[1]), bands[2])
    counts = mask.histogram()
    return {'changedPixels':sum(counts[1:]), 'overTolerancePixels':sum(counts[3:]),
            'maximumChannelDifference':mask.getextrema()[1], 'changedBounds':mask.getbbox()}

def analyze(folder):
    folder = Path(folder)
    source_info = json.loads((folder / 'cover-source.json').read_text(encoding='utf-8'))
    if source_info.get('method') != 'cdp-loaded-resource-content':
        raise ValueError('必须使用已加载原始图片资源')
    scale = source_info['dpr']
    raw_x, raw_y = -source_info['rect']['x'] * scale, -source_info['rect']['y'] * scale
    x, y = round(raw_x), round(raw_y)
    if scale <= 0 or max(abs(raw_x-x), abs(raw_y-y)) > .01:
        raise ValueError('源图偏移不是设备像素对齐')
    result = {'sourcePhysicalOffset':[x, y], 'diagnosticOnly':True, 'images':{}}
    with Image.open(folder / 'cover-source.png') as image:
        if image.size != (source_info['width'], source_info['height']):
            raise ValueError('源图尺寸不符')
        source = image.convert('RGB')
    first = None
    for name in ['settings', 'cover-visible-live', 'cover-hidden-live']:
        file = folder / (name + '.png')
        info = json.loads((folder / (name + '.png.json')).read_text(encoding='utf-8'))
        if info.get('capture') != 'screen' or info.get('method') != 'System.Drawing.Graphics.CopyFromScreen' or info.get('resampled') is not False:
            raise ValueError('拒绝非原始系统图')
        if info.get('sha256') != hashlib.sha256(file.read_bytes()).hexdigest():
            raise ValueError('系统截图哈希不符')
        if not info.get('stableActivation') or not info.get('stableProcessFrameAndDpi') or abs(info['dpi']/96-scale) > .001:
            raise ValueError('系统截图状态或DPI不稳定')
        if first and any(info[key] != first[key] for key in ['frameRectPhysical','regionPhysical','processId','windowHandle','activationState']):
            raise ValueError('三图身份或区域不一致')
        first = info
        region, frame = info['regionPhysical'], info['frameRectPhysical']
        w, h = frame[2] - frame[0], frame[3] - frame[1]
        box = (frame[0]-region[0], frame[1]-region[1], frame[2]-region[0], frame[3]-region[1])
        if min(w,h) <= 32 or x < 0 or y < 0 or x+w > source.width or y+h > source.height:
            raise ValueError('源图裁取越界或内部区域不足')
        with Image.open(file) as image:
            if image.size != tuple(region[2:]) or not (0 <= box[0] < box[2] <= image.width and 0 <= box[1] < box[3] <= image.height):
                raise ValueError('系统图尺寸或裁取区域不符')
            screen = image.convert('RGB').crop(box)
        expected = source.crop((x, y, x+w, y+h))
        result['images'][name] = {'full':difference(expected, screen),
            'interior16':difference(expected.crop((16,16,w-16,h-16)), screen.crop((16,16,w-16,h-16)))}
    return result

if __name__ == '__main__':
    folder = Path(sys.argv[1])
    result = analyze(folder)
    (folder / 'source-screen-analysis.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+chr(10),encoding='utf-8',newline=chr(10))
    print(json.dumps(result,ensure_ascii=False))
