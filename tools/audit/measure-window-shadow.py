"""从同位置的窗口/无窗口背景PNG计算四边阴影剖面；不推算高斯模糊半径。"""
import argparse
import json
from math import isfinite
from pathlib import Path
from statistics import median

from PIL import Image


def measure(foreground, background, rect, scale, radius=64, threshold=2):
    if foreground.format != "PNG" or background.format != "PNG":
        raise ValueError("定量输入必须是原始无损PNG，不能使用JPEG或转码JPEG")
    if foreground.size != background.size:
        raise ValueError("窗口与背景截图尺寸必须一致且不得重采样")
    if not isfinite(scale) or scale <= 0:
        raise ValueError("缩放必须为正数")
    if not isinstance(radius, int) or radius < 4 or not 0 <= threshold < 255:
        raise ValueError("采样半径至少4物理像素，阈值在0至255之间")
    if len(rect) != 4 or any(type(value) is not int for value in rect):
        raise ValueError("窗口边界必须是四个整数物理坐标")
    left, top, right, bottom = rect
    width, height = foreground.size
    if not (radius <= left < right <= width-radius and radius <= top < bottom <= height-radius):
        raise ValueError("截图必须完整容纳窗口及四周采样带")
    if min(right-left, bottom-top) < 8:
        raise ValueError("窗口过小，无法排除圆角")
    front, back = foreground.convert("RGB"), background.convert("RGB")
    xs = range(left+(right-left)//4, right-(right-left)//4)
    ys = range(top+(bottom-top)//4, bottom-(bottom-top)//4)
    profiles = {}
    for side in ("left", "right", "top", "bottom"):
        rows = []
        for distance in range(1, radius+1):
            points = ([(left-distance, y) for y in ys] if side == "left" else
                      [(right+distance-1, y) for y in ys] if side == "right" else
                      [(x, top-distance) for x in xs] if side == "top" else
                      [(x, bottom+distance-1) for x in xs])
            differences = [tuple(b-a for a, b in zip(front.getpixel(p), back.getpixel(p))) for p in points]
            # 正值表示窗口出现后变暗；不用绝对差把背景变亮误认为阴影。
            darkness = [sum(delta)/3 for delta in differences]
            rows.append({"distance_physical": distance, "distance_logical": distance/scale,
                         "median_darkening_rgb": median(darkness),
                         "max_channel_change": max(abs(v) for delta in differences for v in delta)})
        extent = max((row["distance_physical"] for row in rows
                      if row["median_darkening_rgb"] > threshold), default=0)
        profiles[side] = {"extent_above_threshold_physical": extent,
                          "extent_above_threshold_logical": extent/scale,
                          "tail_settled": all(row["max_channel_change"] <= threshold for row in rows[-4:]),
                          "samples": rows}
    return {"method": "四边中央半段RGB通道平均暗化量的中位数；边界采用右下排他坐标",
            "scale": scale, "rect_physical": rect, "threshold_rgb": threshold,
            "radius_physical": radius, "profiles": profiles,
            "limitation": "需人工核对同背景、同状态、无遮挡及原始无损来源；结果不是高斯模糊半径或验收通过结论"}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("foreground")
    parser.add_argument("background")
    parser.add_argument("--rect", nargs=4, type=int, required=True)
    parser.add_argument("--scale", type=float, required=True)
    parser.add_argument("--radius", type=int, default=64)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    with Image.open(args.foreground) as foreground, Image.open(args.background) as background:
        result = measure(foreground, background, args.rect, args.scale, args.radius)
    Path(args.output).write_text(json.dumps(result, ensure_ascii=False, indent=2)+"\n", encoding="utf-8", newline="\n")
