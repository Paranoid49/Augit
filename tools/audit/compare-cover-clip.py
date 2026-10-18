"""核对裁切捕获是否保留整页原始像素，不缩放、不配准、不排除边缘。"""
import hashlib
import json
import statistics
import sys
from pathlib import Path
from PIL import Image, ImageChops


def compare(full, clipped, box):
    x, y, right, bottom = box
    if not all(type(v) is int for v in box) or not (0 <= x < right <= full.width and 0 <= y < bottom <= full.height):
        raise ValueError('裁切范围必须为原图内的整数像素')
    expected = full.convert('RGB').crop(box)
    if expected.size != clipped.size:
        raise ValueError('裁切捕获尺寸与物理区域不符')
    bands = ImageChops.difference(expected, clipped.convert('RGB')).split()
    delta = ImageChops.lighter(ImageChops.lighter(bands[0], bands[1]), bands[2])
    counts = delta.histogram()
    return {'changedPixels': sum(counts[1:]), 'overTolerancePixels': sum(counts[3:]),
            'maximumChannelDifference': delta.getextrema()[1], 'bounds': delta.getbbox()}


def analyze(folder):
    folder = Path(folder)
    manifest = json.loads((folder / 'cover-clip-probe.json').read_text(encoding='utf-8'))
    if manifest.get('method') != 'cdp-raw-png' or manifest.get('systemScreenshot') is not False:
        raise ValueError('仅接受已登记的原始CDP捕获，不冒充系统截图')
    rect, scale = manifest['geometry']['rect'], manifest['geometry']['dpr']
    physical = [v * scale for v in (rect['x'], rect['y'], rect['x'] + rect['width'], rect['y'] + rect['height'])]
    box = tuple(round(v) for v in physical)
    if scale <= 0:
        raise ValueError('缩放必须为正数')
    aligned = all(abs(a - b) <= .01 for a, b in zip(physical, box))
    entries = manifest['captures']
    hashes, pairs = {}, []
    for item in entries:
        filename = item['file']
        if Path(filename).name != filename:
            raise ValueError('原图文件必须位于证据目录')
        raw = (folder / filename).read_bytes()
        if len(raw) != item['bytes']:
            raise ValueError('原图长度与采集记录不符')
        hashes[filename] = hashlib.sha256(raw).hexdigest()
    for item in entries:
        if item['kind'] == 'full':
            continue
        full = [e for e in entries if e['pair'] == item['pair'] and e['kind'] == 'full']
        if len(full) != 1:
            raise ValueError('每组必须恰有一份整页原图')
        with Image.open(folder / full[0]['file']) as original, Image.open(folder / item['file']) as clipped:
            pairs.append({'pair': item['pair'], 'kind': item['kind'], **compare(original, clipped, box)})
    return {'physicalCrop': box, 'rawPhysicalBounds': physical, 'pixelAligned': aligned,
            'comparisonMeaning': '原图整数区域直接比较' if aligned else '仅诊断：按最近整数裁取，不证明裁切API导致差异',
            'pairs': pairs, 'rawPngHashes': hashes,
            'medians': {kind: statistics.median(e['elapsedMilliseconds'] for e in entries if e['kind'] == kind) for kind in sorted({e['kind'] for e in entries})},
            'exactPixelMatch': aligned and bool(pairs) and all(e['changedPixels'] == 0 for e in pairs),
            'diagnosticOnly': True}


def compare_native_screen(folder):
    folder = Path(folder)
    probe = json.loads((folder / 'cover-clip-probe.json').read_text(encoding='utf-8'))
    capture = json.loads((folder / 'capture.json').read_text(encoding='utf-8'))
    info = json.loads((folder / 'settings.png.json').read_text(encoding='utf-8'))
    modal = capture['modal']
    raw = (folder / 'settings.png').read_bytes()
    if (info.get('capture') != 'screen' or info.get('method') != 'System.Drawing.Graphics.CopyFromScreen'
            or info.get('resampled') is not False or not info.get('stableActivation')
            or not info.get('stableProcessFrameAndDpi')
            or info.get('sha256') != hashlib.sha256(raw).hexdigest()):
        raise ValueError('系统原图来源、哈希或稳定状态无效')
    if info['processId'] != capture['pid'] or info['windowHandle'] != modal['window']:
        raise ValueError('系统图与宿主实例身份不一致')
    if abs(info['dpi'] / 96 - probe['geometry']['dpr']) > .001:
        raise ValueError('系统图与网页缩放不一致')
    frame, region = info['frameRectPhysical'], info['regionPhysical']
    rect = modal['rect']
    if frame != [rect['x'], rect['y'], rect['x'] + rect['width'], rect['y'] + rect['height']]:
        raise ValueError('宿主与系统图外框不一致')
    x, y = -modal['liveBounds']['x'], -modal['liveBounds']['y']
    w, h = rect['width'], rect['height']
    if min(w, h) <= 32 or not all(type(v) is int for v in [x, y, w, h]):
        raise ValueError('宿主裁切不是有效物理像素矩形')
    box = (frame[0]-region[0], frame[1]-region[1], frame[2]-region[0], frame[3]-region[1])
    with Image.open(folder / 'settings.png') as image:
        if image.size != tuple(region[2:]):
            raise ValueError('系统图大小不符')
        # 复用严格裁切检查，禁止图像库越界补黑。
        compare(image, Image.new('RGB', (w, h)), box)
        screen = image.convert('RGB').crop(box)
    analyze(folder)  # 校验每个原始PNG的长度、成对关系及裁切边界。
    results = []
    for entry in probe['captures']:
        with Image.open(folder / entry['file']) as image:
            if entry['kind'] == 'full':
                compare(image, screen, (x, y, x+w, y+h))
                actual = image.convert('RGB').crop((x, y, x+w, y+h))
            else:
                actual = image.convert('RGB')
            full = compare(actual, screen, (0, 0, w, h))
            inner = compare(actual, screen.crop((16, 16, w-16, h-16)), (16, 16, w-16, h-16))
            results.append({'pair': entry['pair'], 'kind': entry['kind'], 'full': full, 'interior16': inner})
    return {'source': 'native-liveBounds', 'physicalCrop': [x, y, x+w, y+h],
            'screenSha256': info['sha256'], 'comparisons': results, 'diagnosticOnly': True,
            'limitation': '系统图与CDP依次采样，内部相同不代表全表面或所有过渡帧通过'}


if __name__ == '__main__':
    folder = Path(sys.argv[1])
    result = analyze(folder)
    (folder / 'comparison.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in result.items() if k != 'rawPngHashes'}, ensure_ascii=False))
    if '--native-screen' in sys.argv:
        native = compare_native_screen(folder)
        (folder / 'native-screen-comparison.json').write_text(json.dumps(native, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        print(json.dumps({'physicalCrop': native['physicalCrop'], 'comparisons': native['comparisons'][:3]}, ensure_ascii=False))
