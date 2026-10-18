"""比较窗口外侧四角原始屏幕像素；背景不一致时拒绝给出整角通过。"""
import argparse
import hashlib
import json
from math import isfinite
from pathlib import Path

from PIL import Image


def activation(source):
    state = source.get('activationState')
    # 旧采集器只支持前景；缺少新字段时只接受原有明确的活动态证明。
    if state is None:
        if source.get('foregroundBeforeAndAfter') is True:
            return 'active'
        raise ValueError('缺少活动状态证据')
    if state not in ('active', 'inactive') or source.get('stableActivation') is not True:
        raise ValueError('活动状态不稳定或未知')
    active = state == 'active'
    if source.get('foregroundBeforeAndAfter') is not active:
        raise ValueError('活动状态字段矛盾')
    if (not isinstance(source.get('foregroundWindow'), int) or source['foregroundWindow'] <= 0
            or not isinstance(source.get('foregroundProcessId'), int) or source['foregroundProcessId'] <= 0
            or (source['foregroundWindow'] == source.get('windowHandle')) != active):
        raise ValueError('活动状态与前景身份不符')
    return state


def load_pair(foreground, background):
    images, metadata = [], []
    for path in (Path(foreground), Path(background)):
        source = json.loads(Path(str(path)+'.json').read_text(encoding='utf-8-sig'))
        if (source.get('capture') != 'screen' or source.get('method') != 'System.Drawing.Graphics.CopyFromScreen'
                or source.get('resampled') is not False
                or source.get('stableProcessFrameAndDpi') is not True
                or hashlib.sha256(path.read_bytes()).hexdigest() != source.get('sha256')):
            raise ValueError('屏幕来源、稳定性或哈希不符')
        with Image.open(path) as image:
            if image.format != 'PNG' or list(image.size) != source.get('imageSize'):
                raise ValueError('必须为原尺寸无损PNG')
            images.append(image.convert('RGB'))
        metadata.append(source)
        activation(source)
    first, second = metadata
    if (first['regionPhysical'] != second['regionPhysical'] or first['scale'] != second['scale']
            or images[0].size != images[1].size or first['processId'] != second['processId']):
        raise ValueError('窗口与背景不属于同实例、区域和DPI')
    if activation(first) != activation(second):
        raise ValueError('窗口与背景活动状态不同')
    return (*images, first['frameRectInImage'], first['scale'], activation(first))


def compare(actual, reference, radius=64, tolerance=3):
    if type(radius) is not int or radius < 1 or not isfinite(tolerance) or tolerance < 0:
        raise ValueError('半径须为正整数，容差须为有限非负数')
    if actual[3] != reference[3] or not isfinite(actual[3]) or actual[3] <= 0:
        raise ValueError('两端须使用相同有效缩放')
    actual_state = actual[4] if len(actual) > 4 else 'active'
    reference_state = reference[4] if len(reference) > 4 else 'active'
    if actual_state != reference_state or actual_state not in ('active','inactive'):
        raise ValueError('两端窗口活动状态不同或未知')
    for item in (actual, reference):
        front, back, rect, scale = item[:4]
        if front.size != back.size or len(rect) != 4 or any(type(v) is not int for v in rect):
            raise ValueError('尺寸或边界格式错误')
        l, t, r, b = rect
        if not (radius <= l < r <= front.width-radius and radius <= t < b <= front.height-radius
                and min(r-l, b-t) >= 2*radius):
            raise ValueError('完整角部采样带超出图片或窗口过小')
    results = {}
    for name, right, bottom in [('topLeft',False,False),('topRight',True,False),
                                 ('bottomLeft',False,True),('bottomRight',True,True)]:
        differences, matched, unmatched = [], [], 0
        for dy in range(-radius, radius):
            for dx in range(-radius, radius):
                # 负偏移在窗口外侧；只采外侧三个象限，不将窗口内容算作阴影。
                if dx >= 0 and dy >= 0:
                    continue
                samples = []
                for item in (actual, reference):
                    front, back, (l,t,r,b), scale = item[:4]
                    x = r-1-dx if right else l+dx
                    y = b-1-dy if bottom else t+dy
                    bg, fg = back.getpixel((x,y)), front.getpixel((x,y))
                    samples.append((bg, tuple(v-u for u,v in zip(fg,bg))))
                delta = max(abs(a-b) for a,b in zip(samples[0][1],samples[1][1]))
                differences.append(delta)
                if samples[0][0] == samples[1][0]:
                    matched.append(delta)
                else:
                    unmatched += 1
        comparable = unmatched == 0
        results[name] = {
            'totalPixels': len(differences), 'matchedBackgroundPixels': len(matched),
            'unmatchedBackgroundPixels': unmatched, 'maxDifferenceAll': max(differences),
            'maxDifferenceMatched': max(matched) if matched else None,
            'matchedPixelsAboveTolerance': sum(d > tolerance for d in matched),
            'fullyComparable': comparable,
            'passed': comparable and max(differences) <= tolerance,
        }
    return {'radiusPhysical':radius,'scale':actual[3],'activationState':actual_state,'toleranceRgb':tolerance,'corners':results,
            'allExternalCornersPassed':all(r['passed'] for r in results.values()),
            'fullShadowVerified':False,
            'limitation':'仅当前活动状态的窗口矩形外侧三个象限；圆角内侧、遮挡及其他状态需另验。背景不同不筛掉后宣称通过。'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('actual','actual_background','reference','reference_background'):
        parser.add_argument(name)
    parser.add_argument('--radius',type=int,default=64)
    parser.add_argument('--output',required=True)
    options = parser.parse_args()
    result = compare(load_pair(options.actual,options.actual_background),
                     load_pair(options.reference,options.reference_background),options.radius)
    Path(options.output).write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
    print(json.dumps(result,ensure_ascii=False))
    # 结果可用于定位缺口；只有完整可比较且差异在容差内才返回成功。
    raise SystemExit(0 if result['allExternalCornersPassed'] else 1)
