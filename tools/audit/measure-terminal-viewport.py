"""量化原图中全部可见终端行；无参考时只报告覆盖，不冒充逐行配对。"""
import argparse
import hashlib
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image


def row_regions(snapshot, scale, image_size):
    rows, screen = snapshot['rows'], snapshot['screen']
    if not math.isfinite(scale) or scale <= 0 or not rows or len(rows) != snapshot['rowCount']:
        raise ValueError('缩放或可见行数无效')
    if any(not math.isfinite(screen[k]) for k in ('x', 'y', 'width', 'height')):
        raise ValueError('屏幕边界无效')
    if screen['width'] <= 0 or screen['height'] <= 0:
        raise ValueError('屏幕面积无效')
    x0, x1 = math.floor(screen['x'] * scale), math.ceil((screen['x'] + screen['width']) * scale)
    y0, y1 = math.floor(screen['y'] * scale), math.ceil((screen['y'] + screen['height']) * scale)
    if not 0 <= x0 < x1 <= image_size[0] or not 0 <= y0 < y1 <= image_size[1]:
        raise ValueError('终端区域超出原图')
    boundaries = [y0]
    expected_top = screen['y']
    for index, row in enumerate(rows):
        rect = row['bounds']
        if row['screenRow'] != index + 1 or row['bufferIndex'] != snapshot['viewportY'] + index:
            raise ValueError('屏幕行与buffer索引不连续')
        if any(not math.isfinite(rect[k]) for k in ('x', 'y', 'width', 'height')) or rect['height'] <= 0:
            raise ValueError('行边界无效')
        if any(abs(a-b) > .001 for a, b in ((rect['x'], screen['x']), (rect['width'], screen['width']), (rect['y'], expected_top))):
            raise ValueError('行存在缺口、重叠或未覆盖全宽')
        expected_top = rect['y'] + rect['height']
        boundaries.append(y1 if index == len(rows)-1 else math.floor(expected_top * scale))
    if abs(expected_top - screen['y'] - screen['height']) > .001:
        raise ValueError('可见行未覆盖屏幕底部')
    if any(b <= a for a, b in zip(boundaries, boundaries[1:])):
        raise ValueError('物理行高度无效')
    return [(x0, top, x1, bottom) for top, bottom in zip(boundaries, boundaries[1:])]


def measure(directory):
    directory = Path(directory)
    geometry = json.loads((directory/'actual-geometry.json').read_text(encoding='utf-8-sig'))
    snapshot = json.loads((directory/'visible-rows.json').read_text(encoding='utf-8-sig'))
    filename = directory / ('actual-' + geometry['theme'].lower() + '.png')
    metadata = json.loads(Path(str(filename)+'.json').read_text(encoding='utf-8-sig'))
    digest = hashlib.sha256(filename.read_bytes()).hexdigest()
    if (metadata.get('sha256') != digest or metadata.get('capture') != 'screen' or metadata.get('resampled') is not False
            or metadata.get('stableProcessFrameAndDpi') is not True or metadata.get('foregroundBeforeAndAfter') is not True
            or metadata.get('scale') != geometry['dpr'] or metadata.get('processId') != geometry['pid']):
        raise ValueError('原图来源、进程、缩放或哈希不符')
    with Image.open(filename) as source:
        if source.format != 'PNG' or list(source.size) != metadata.get('imageSize'):
            raise ValueError('原图尺寸或格式不符')
        pixels = np.array(source.convert('RGB'))
        regions = row_regions(snapshot, geometry['dpr'], source.size)
    rows = []
    for row, (x0, y0, x1, y1) in zip(snapshot['rows'], regions):
        sample = pixels[y0:y1, x0:x1]
        rows.append({'screenRow': row['screenRow'], 'bufferIndex': row['bufferIndex'], 'text': row['text'],
                     'regionPhysical': [x0, y0, x1, y1], 'pixels': int(sample.shape[0]*sample.shape[1]),
                     'darkPixelsThreshold80': int((sample.max(axis=2) <= 80).sum()),
                     'rgbSha256': hashlib.sha256(sample.tobytes()).hexdigest()})
    return {'source': str(filename), 'sha256': digest, 'rowCount': len(rows), 'rows': rows,
            'totalPixels': sum(row['pixels'] for row in rows), 'allVisibleRowsCovered': True,
            'referencePaired': False, 'pixelRestorationVerified': False, 'resampled': False,
            'limitation': '仅覆盖实际端全宽可见网格；边界向外取整且每像素仅计一次。未配准参考、不排除行尾或空白，不代表完整终端面板或整页通过。'}


def compare_surfaces(reference, actual, regions, reference_region, actual_region=None):
    # 使用双方内容区的并集；参考多出的顶部和两侧也计入，禁止取交集隐去差异。
    if reference.shape != actual.shape or reference.ndim != 3 or reference.shape[2] != 3:
        raise ValueError('双端原图必须同尺寸RGB')
    if not regions:
        raise ValueError('实际可见行不能为空')
    rx0, ry0, rx1, ry1 = reference_region
    if not 0 <= rx0 < rx1 <= reference.shape[1] or not 0 <= ry0 < ry1 <= reference.shape[0]:
        raise ValueError('参考内容区越界')
    ax0, ay0, ax1, ay1 = actual_region or (regions[0][0], regions[0][1], regions[0][2], regions[-1][3])
    if not 0 <= ax0 <= regions[0][0] < regions[0][2] <= ax1 <= actual.shape[1] or not 0 <= ay0 <= regions[0][1] < regions[-1][3] <= ay1 <= actual.shape[0]:
        raise ValueError('实际内容区越界或未包含全部网格')
    x0, x1 = min(rx0, ax0), max(rx1, ax1)
    y0, y1 = min(ry0, ay0), max(ry1, ay1)
    edges = sorted(set([y0, y1] + [r[1] for r in regions] + [regions[-1][3]]))
    rows = []
    for top, bottom in zip(edges, edges[1:]):
        difference = np.abs(reference[top:bottom, x0:x1].astype(np.int16) - actual[top:bottom, x0:x1].astype(np.int16))
        rows.append({'regionPhysical': [x0, top, x1, bottom], 'pixels': int((x1-x0)*(bottom-top)),
                     'differentPixels': int(np.any(difference, axis=2).sum()),
                     'differentPixelsTolerance2': int(np.any(difference > 2, axis=2).sum()),
                     'maxChannelDifference': int(difference.max())})
    return {'regionPhysical': [x0, y0, x1, y1], 'strips': rows, 'pixels': sum(r['pixels'] for r in rows),
            'differentPixels': sum(r['differentPixels'] for r in rows),
            'differentPixelsTolerance2': sum(r['differentPixelsTolerance2'] for r in rows),
            'translated': False, 'resampled': False, 'pixelRestorationVerified': False,
            'limitation': '按实际行边界分段，完整覆盖双方内容区并集；分段不等同文字行配准，面板原点、提示符块和光标差异全部保留。'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('directory')
    parser.add_argument('--reference')
    parser.add_argument('--reference-region', type=int, nargs=4)
    parser.add_argument('--actual-region', type=int, nargs=4)
    args = parser.parse_args()
    result = measure(args.directory)
    if args.reference:
        if not args.reference_region or not args.actual_region:
            parser.error('双端比较必须显式提供两端完整内容区边界')
        reference_path = Path(args.reference)
        metadata = json.loads(Path(str(reference_path)+'.json').read_text(encoding='utf-8-sig'))
        actual_metadata = json.loads(Path(result['source']+'.json').read_text(encoding='utf-8-sig'))
        reference_hash = hashlib.sha256(reference_path.read_bytes()).hexdigest()
        if (metadata.get('sha256') != reference_hash or metadata.get('capture') != 'screen'
                or metadata.get('resampled') is not False or metadata.get('stableProcessFrameAndDpi') is not True
                or metadata.get('foregroundBeforeAndAfter') is not True or metadata.get('dpi') != actual_metadata.get('dpi')
                or metadata.get('scale') != actual_metadata.get('scale')
                or reference_path.resolve() == Path(result['source']).resolve() or reference_hash == result['sha256']):
            raise ValueError('参考原图身份、缩放、独立来源或前景状态不符')
        with Image.open(reference_path) as source, Image.open(result['source']) as actual:
            if source.format != 'PNG' or list(source.size) != metadata.get('imageSize'):
                raise ValueError('参考原图格式或尺寸不符')
            comparison = compare_surfaces(np.array(source.convert('RGB')), np.array(actual.convert('RGB')),
                                          [r['regionPhysical'] for r in result['rows']], args.reference_region, args.actual_region)
        comparison.update(actualSha256=result['sha256'], referenceSha256=reference_hash,
                          actualRegion=args.actual_region, referenceRegion=args.reference_region,
                          pairComparable=False, conditionsNotMatched='未由该像素工具核验内容、焦点、字体及面板同态，必须另附人工与运行时证据。')
        result = comparison
    print(json.dumps(result, ensure_ascii=False, indent=2))
