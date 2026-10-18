"""同尺寸窗口完整外沿逐像素比较；八区无重叠，不以中央中位数代表完整阴影。"""
import argparse
import hashlib
import importlib.util
import json
from math import isfinite
from pathlib import Path

spec = importlib.util.spec_from_file_location('shadow_corners', Path(__file__).with_name('compare-shadow-corners.py'))
corners = importlib.util.module_from_spec(spec)
spec.loader.exec_module(corners)


def boundary_extent(pair, radius):
    """最外侧一圈仍有暗化时，不允许把有限采样带当作完整阴影。"""
    front, back, rect, *_ = pair
    left, top, right, bottom = rect
    x0, y0, x1, y1 = left-radius, top-radius, right+radius-1, bottom+radius-1
    points = [(x,y) for y in (y0,y1) for x in range(x0,x1+1)]
    points += [(x,y) for x in (x0,x1) for y in range(y0+1,y1)]
    changes = [max(abs(a-b) for a,b in zip(front.getpixel(p),back.getpixel(p))) for p in points]
    return {'pixels':len(points),'nonzeroPixels':sum(v != 0 for v in changes),
        'maximumDifferenceFromBackground':max(changes),'returnedToBackground':not any(changes)}


def compare(actual, reference, radius=128, tolerance=3):
    # 八区仅占矩形外侧，无内向角区重叠，不要求窗口边长大于两倍半径。
    if type(radius) is not int or radius < 1 or not isfinite(tolerance) or tolerance < 0:
        raise ValueError('半径须为正整数，容差须为有限非负数')
    if actual[3] != reference[3] or not isfinite(actual[3]) or actual[3] <= 0:
        raise ValueError('两端须使用相同有效缩放')
    if actual[4] != reference[4] or actual[4] not in ('active','inactive'):
        raise ValueError('两端活动状态不同或未知')
    for front,back,rect,*_ in (actual,reference):
        if front.size != back.size or len(rect) != 4 or any(type(v) is not int for v in rect):
            raise ValueError('尺寸或边界格式错误')
        l,t,r,b=rect
        if not (radius <= l < r <= front.width-radius and radius <= t < b <= front.height-radius):
            raise ValueError('完整外沿超出原始图片')
    aw, ah = actual[2][2]-actual[2][0], actual[2][3]-actual[2][1]
    rw, rh = reference[2][2]-reference[2][0], reference[2][3]-reference[2][1]
    if (aw, ah) != (rw, rh):
        raise ValueError('完整外沿比较要求窗口物理宽高一致，不允许缩放或沿边归一化')
    zones = {
        'topLeft': (-radius, -radius, 0, 0), 'top': (0, -radius, aw, 0),
        'topRight': (aw, -radius, aw+radius, 0), 'left': (-radius, 0, 0, ah),
        'right': (aw, 0, aw+radius, ah), 'bottomLeft': (-radius, ah, 0, ah+radius),
        'bottom': (0, ah, aw, ah+radius), 'bottomRight': (aw, ah, aw+radius, ah+radius),
    }
    results = {}
    for name, (x0, y0, x1, y1) in zones.items():
        count = unmatched = above = matched_above = 0
        maximum = matched_maximum = 0
        examples = []
        for y in range(y0, y1):
            for x in range(x0, x1):
                samples = []
                for front, back, rect, *_ in (actual, reference):
                    point = (rect[0]+x, rect[1]+y)
                    bg, fg = back.getpixel(point), front.getpixel(point)
                    samples.append((bg, tuple(b-f for b, f in zip(bg, fg))))
                same = samples[0][0] == samples[1][0]
                delta = max(abs(a-b) for a, b in zip(samples[0][1], samples[1][1]))
                count += 1
                unmatched += not same
                above += delta > tolerance
                maximum = max(maximum, delta)
                if same:
                    matched_above += delta > tolerance
                    matched_maximum = max(matched_maximum, delta)
                if (not same or delta > tolerance) and len(examples) < 5:
                    examples.append({'offset':[x,y], 'sameBackground':same, 'difference':delta})
        results[name] = {'totalPixels':count, 'unmatchedBackgroundPixels':unmatched,
            'pixelsAboveTolerance':above, 'maxDifference':maximum,
            'matchedPixelsAboveTolerance':matched_above, 'maxDifferenceMatched':matched_maximum,
            'passed':unmatched == 0 and above == 0, 'examples':examples}
    total = sum(item['totalPixels'] for item in results.values())
    assert total == (aw+2*radius)*(ah+2*radius)-aw*ah
    extent = {'actual':boundary_extent(actual,radius), 'reference':boundary_extent(reference,radius)}
    pixels_passed = all(item['passed'] for item in results.values())
    boundary_clear = all(item['returnedToBackground'] for item in extent.values())
    return {'windowPhysicalSize':[aw,ah], 'radiusPhysical':radius, 'scale':actual[3],
        'activationState':actual[4], 'toleranceRgb':tolerance, 'totalPixels':total,
        'resampled':False, 'coordinateMapping':'按DWM矩形左上角整数偏移对应，不修改原图',
        'zones':results, 'allExternalPixelsPassed':pixels_passed,
        'outerBoundary':extent, 'samplingBoundaryClear':boundary_clear,
        'completeExternalShadowPassed':pixels_passed and boundary_clear,
        'fullShadowVerified':False,
        'limitation':'完整矩形外侧；圆角矩形内侧、人工无遮挡核验、其他主题及活动状态仍须分别验证。'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('actual','actual_background','reference','reference_background'):
        parser.add_argument(name)
    parser.add_argument('--radius', type=int, default=128)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    output = Path(args.output)
    if output.exists():raise SystemExit('拒绝覆盖已有证据')
    result = compare(corners.load_pair(args.actual,args.actual_background),
        corners.load_pair(args.reference,args.reference_background),args.radius)
    result['sources'] = {name:{'path':str(Path(getattr(args,name)).resolve()),
        'sha256':hashlib.sha256(Path(getattr(args,name)).read_bytes()).hexdigest()}
        for name in ('actual','actual_background','reference','reference_background')}
    output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+chr(10),encoding='utf-8',newline=chr(10))
    print(json.dumps(result,ensure_ascii=False))
    raise SystemExit(0 if result['completeExternalShadowPassed'] else 1)
