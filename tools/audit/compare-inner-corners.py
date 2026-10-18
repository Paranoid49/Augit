"""比较DWM矩形内侧四角；独立于外部阴影，不以局部通过代表整窗。"""
import argparse
import hashlib
import importlib.util
import json
from math import isfinite
from pathlib import Path

spec = importlib.util.spec_from_file_location('corners', Path(__file__).with_name('compare-shadow-corners.py'))
corners = importlib.util.module_from_spec(spec)
spec.loader.exec_module(corners)


def compare(actual, reference, radius=16, tolerance=3):
    if type(radius) is not int or radius < 1 or type(tolerance) is not int or not 0 <= tolerance <= 255:
        raise ValueError('半径和容差无效')
    if actual[3] != reference[3] or not isfinite(actual[3]) or actual[3] <= 0:
        raise ValueError('两端缩放不同或无效')
    if actual[4] != reference[4] or actual[4] not in ('active', 'inactive'):
        raise ValueError('两端活动状态不同或未知')
    sizes = []
    for front, back, rect, *_ in (actual, reference):
        if front.size != back.size or len(rect) != 4 or any(type(v) is not int for v in rect):
            raise ValueError('尺寸或矩形格式无效')
        left, top, right, bottom = rect
        if not (0 <= left < right <= front.width and 0 <= top < bottom <= front.height):
            raise ValueError('矩形超出原始图')
        if min(right-left, bottom-top) < 2*radius:
            raise ValueError('内角采样重叠')
        sizes.append((right-left, bottom-top))
    if sizes[0] != sizes[1]:
        raise ValueError('窗口尺寸必须相同，不允许缩放')
    result = {}
    for name, right_side, bottom_side in [('topLeft', False, False), ('topRight', True, False),
                                          ('bottomLeft', False, True), ('bottomRight', True, True)]:
        differences, backgrounds = [], []
        for dy in range(radius):
            for dx in range(radius):
                samples = []
                for front, back, (left, top, right, bottom), *_ in (actual, reference):
                    point = (right-1-dx if right_side else left+dx, bottom-1-dy if bottom_side else top+dy)
                    samples.append((front.getpixel(point), back.getpixel(point)))
                differences.append(max(abs(a-b) for a, b in zip(samples[0][0], samples[1][0])))
                backgrounds.append(samples[0][1] == samples[1][1])
        result[name] = {'totalPixels': radius*radius, 'differentPixels': sum(d != 0 for d in differences),
                        'pixelsAboveTolerance': sum(d > tolerance for d in differences),
                        'maximumDifference': max(differences), 'unmatchedBackgroundPixels': backgrounds.count(False),
                        'passed': all(backgrounds) and max(differences) <= tolerance}
    return {'radiusPhysical': radius, 'windowPhysicalSize': sizes[0], 'scale': actual[3],
            'activationState': actual[4], 'toleranceRgb': tolerance, 'corners': result,
            'allInnerCornersPassed': all(value['passed'] for value in result.values()),
            'fullWindowVerified': False, 'resampled': False,
            'limitation': '仅四个内侧角区的原始RGB比较；背景一致性独立校验，不覆盖外部阴影、正文或其他状态。'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('actual', 'actual_background', 'reference', 'reference_background'):
        parser.add_argument(name)
    parser.add_argument('--radius', type=int, default=16)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    output = Path(args.output)
    if output.exists():
        raise SystemExit('拒绝覆盖已有证据')
    result = compare(corners.load_pair(args.actual, args.actual_background),
                     corners.load_pair(args.reference, args.reference_background), args.radius)
    result['sources'] = {name: {'path': str(Path(getattr(args, name)).resolve()),
                               'sha256': hashlib.sha256(Path(getattr(args, name)).read_bytes()).hexdigest()}
                         for name in ('actual', 'actual_background', 'reference', 'reference_background')}
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2)+chr(10), encoding='utf-8', newline=chr(10))
    print(json.dumps(result, ensure_ascii=False))
    raise SystemExit(0 if result['allInnerCornersPassed'] else 1)
