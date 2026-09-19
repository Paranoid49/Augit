#!/usr/bin/env python3
"""同引擎像素比对：把"实时外壳 vs 视觉稿"拆成"布局差异"和"文字差异"两个指标。

背景（见 docs/ui-refactor-baseline.md 第 278-282 轮）：
- 两侧必须由**同一个 WebView2（Windows）**渲染（tools/audit/compare-pixels.ps1 负责，
  抓图用 CDP Page.captureScreenshot；PrintWindow 会裁掉底部约 22px）；
- 直接逐像素比会被**文字内容**淹没（实时是真实数据、视觉稿是样例数据），
  所以这里先做**边缘掩膜**：两侧都"平坦"（局部梯度小）的像素才算布局像素，
  其余算文字像素，分别统计 —— 这样 `flat` 指标接近 0 就证明背景/边框/间距/圆角逐像素一致。

用法：
  python3 tools/audit/compare-pixels.py --mock <mockup.png> --live <live.png> \
      [-o <outdir>] [--bands titlebar:0:44,statusbar:H-24:H] [--threshold 8] [--edge 12]
"""
import argparse
import os
import sys

from PIL import Image, ImageChops, ImageFilter


def parse_bands(spec, height):
    bands = []
    for item in spec:
        for piece in str(item).split(","):
            piece = piece.strip()
            if not piece:
                continue
            name, y0, y1 = piece.split(":")
            to_int = lambda v: height if v == "H" else (height - int(v[2:]) if v.startswith("H-") else int(v))
            bands.append((name, to_int(y0), to_int(y1)))
    return bands


def flat_mask(image, edge_threshold):
    """两侧都平坦才留下的掩膜：255 = 平坦（背景/边框），0 = 有边缘（文字/图标）。"""
    gray = image.convert("L")
    edges = gray.filter(ImageFilter.FIND_EDGES).point(lambda v: 255 if v > edge_threshold else 0)
    edges = edges.filter(ImageFilter.MaxFilter(3))  # 边缘外扩一圈，避免抗锯齿像素混进平坦区
    return ImageChops.invert(edges)


def band_metrics(mock, live, mask, y0, y1, threshold):
    box = (0, y0, mock.width, y1)
    m = mock.crop(box)
    l = live.crop(box)
    k = mask.crop(box)
    diff = ImageChops.difference(m, l).convert("L").point(lambda v: 255 if v > threshold else 0)
    total = m.width * m.height
    differing = sum(diff.point(lambda v: 1 if v else 0).getdata())
    flat_total = sum(k.point(lambda v: 1 if v else 0).getdata())
    both = ImageChops.multiply(diff, k)
    flat_diff = sum(both.point(lambda v: 1 if v else 0).getdata())
    pct = lambda a, b: round(100.0 * a / b, 2) if b else 0.0
    return {
        "diff": differing,
        "total": total,
        "visiblePercent": pct(differing, total),
        "flatTotal": flat_total,
        "flatDiff": flat_diff,
        "layoutPercent": pct(flat_diff, flat_total),
        "textPercent": pct(differing - flat_diff, total - flat_total),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--mock", required=True)
    parser.add_argument("--live", required=True)
    parser.add_argument("-o", "--outdir", default="")
    parser.add_argument("--bands", action="append", default=None)
    parser.add_argument("--threshold", type=int, default=8)
    parser.add_argument("--edge", type=int, default=12)
    args = parser.parse_args()

    mock = Image.open(args.mock).convert("RGB")
    live = Image.open(args.live).convert("RGB")
    print("SIZES mock=%dx%d live=%dx%d" % (mock.width, mock.height, live.width, live.height))
    if (mock.width, mock.height) != (live.width, live.height):
        print("SIZE_MISMATCH")
        return 4

    bands = parse_bands(args.bands or ["titlebar:0:44", "statusbar:H-24:H"], mock.height)
    mask_mock = flat_mask(mock, args.edge)
    mask_live = flat_mask(live, args.edge)
    mask = ImageChops.multiply(mask_mock, mask_live)  # 两侧都平坦

    worst = 0
    for name, y0, y1 in bands:
        m = band_metrics(mock, live, mask, y0, y1, args.threshold)
        worst = max(worst, m["layoutPercent"])
        print(
            "BAND %s y=%d..%d layoutPercent=%.2f textPercent=%.2f visiblePercent=%.2f flatDiff=%d/%d"
            % (name, y0, y1, m["layoutPercent"], m["textPercent"], m["visiblePercent"], m["flatDiff"], m["flatTotal"])
        )
    if args.outdir:
        os.makedirs(args.outdir, exist_ok=True)
        diff = ImageChops.difference(mock, live).convert("L").point(lambda v: 255 if v > args.threshold else 0)
        heat = Image.merge("RGB", (
            ImageChops.multiply(diff, mask),          # 布局差异 -> 红
            ImageChops.multiply(diff, ImageChops.invert(mask)),  # 文字差异 -> 绿
            Image.new("L", diff.size, 0),
        ))
        heat.save(os.path.join(args.outdir, "heatmap.png"))
        print("HEATMAP " + os.path.join(args.outdir, "heatmap.png"))
    print("SUMMARY worstLayoutPercent=%.2f" % worst)
    return 0


if __name__ == "__main__":
    sys.exit(main())
