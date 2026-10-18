"""以两行中相同字符的墨迹上、下界测行步长；不以不同字形行顶冒充基线。"""
import hashlib
from pathlib import Path
from statistics import median

from PIL import Image


def measure_repeated_rows(filename, left, right, expected_rows, physical_step, threshold=80, top=0, bottom=None, ink="dark"):
    """量化独立重复字形夹具的累计步长；不得用于含不同文字或光标的任意区域。"""
    if expected_rows < 3 or physical_step <= 0 or not 0 <= threshold <= 255:
        raise ValueError('需要至少三行、有效步长与阈值')
    if ink not in ("dark", "light"):
        raise ValueError('墨迹极性必须明确为dark或light')
    with Image.open(filename) as source:
        if source.format != 'PNG':
            raise ValueError('必须使用原始PNG')
        image = source.convert('RGB')
    bottom = image.height if bottom is None else bottom
    if not 0 <= left < right <= image.width or not 0 <= top < bottom <= image.height:
        raise ValueError('采样框越界')
    bands = []
    for y in range(top, bottom):
        if any((max(image.getpixel((x, y))) <= threshold if ink == "dark"
                else min(image.getpixel((x, y))) >= threshold) for x in range(left, right)):
            if not bands or y > bands[-1][-1] + 1:
                bands.append([y])
            else:
                bands[-1].append(y)
    if len(bands) != expected_rows:
        raise ValueError('重复字形行数不符，禁止截短或补零')
    tops = [band[0] for band in bands]
    drift = [top - tops[0] - index * physical_step for index, top in enumerate(tops)]
    return {'file': str(filename), 'sha256': hashlib.sha256(Path(filename).read_bytes()).hexdigest(),
            'expectedRows': expected_rows, 'physicalStep': physical_step, 'tops': tops,
            'region': [left, top, right, bottom], 'threshold': threshold, 'ink': ink,
            'steps': [right - left for left, right in zip(tops, tops[1:])],
            'cumulativeDrift': drift, 'maxAbsoluteDrift': max(map(abs, drift)),
            'pixelRestorationVerified': False}


def measure_row_step(filename, texts, origin_x, cell_width, bands, threshold=80):
    if len(texts) != 2 or len(bands) != 2 or cell_width <= 0 or not 0 <= threshold <= 255:
        raise ValueError('需要两行文本、有效网格与阈值')
    with Image.open(filename) as source:
        if source.format != 'PNG':
            raise ValueError('必须使用原始PNG')
        image = source.convert('RGB')
    repeated = sorted(set(texts[0]) & set(texts[1]) - {' '})
    pairs = []
    for char in repeated:
        rows = []
        for text, (top, bottom) in zip(texts, bands):
            bounds = []
            for column, value in enumerate(text):
                if value != char:
                    continue
                left, right = round(origin_x + column * cell_width), round(origin_x + (column + 1) * cell_width)
                if not (0 <= left < right <= image.width and 0 <= top < bottom <= image.height):
                    raise ValueError('字符采样框越界')
                ys = [y for y in range(top, bottom) for x in range(left, right)
                      if max(image.getpixel((x, y))) <= threshold]
                if not ys:
                    raise ValueError('字符采样框缺少墨迹')
                bounds.append({'column': column, 'top': min(ys), 'bottom': max(ys) + 1})
            rows.append(bounds)
        top_step = median(x['top'] for x in rows[1]) - median(x['top'] for x in rows[0])
        bottom_step = median(x['bottom'] for x in rows[1]) - median(x['bottom'] for x in rows[0])
        pairs.append({'character': char, 'rows': rows, 'topStep': top_step, 'bottomStep': bottom_step})
    if not pairs:
        raise ValueError('两行没有可配对的非空白字符')
    return {'file': str(filename), 'sha256': hashlib.sha256(Path(filename).read_bytes()).hexdigest(),
            'texts': texts, 'originX': origin_x, 'cellWidth': cell_width, 'bands': bands,
            'threshold': threshold, 'unit': '物理像素', 'pairs': pairs,
            'medianStep': median([p[key] for p in pairs for key in ('topStep', 'bottomStep')]),
            'pixelRestorationVerified': False}
