"""量测原始屏幕 PNG 的文字亮色墨迹；局部诊断不代表整页同态通过。"""
import hashlib
import json
from pathlib import Path
import sys

from PIL import Image


def measure_ink(filename, region, threshold=140, polarity='light'):
    if polarity not in ('light', 'dark') or type(threshold) is not int or not 0 <= threshold <= 255:
        raise ValueError('墨迹方向或阈值无效')
    with Image.open(filename) as source:
        if source.format != "PNG":
            raise ValueError("必须使用原始 PNG")
        image = source.convert("RGB")
    x0, y0, x1, y1 = region
    if not (0 <= x0 < x1 <= image.width and 0 <= y0 < y1 <= image.height):
        raise ValueError("区域必须位于原图内")
    # 三通道共同过阈值，排除单通道亚像素边缘；方向和阈值随结果公开。
    rows = []
    for y in range(y0, y1):
        xs = [x for x in range(x0, x1) if (min(image.getpixel((x, y))) >= threshold
              if polarity == 'light' else max(image.getpixel((x, y))) <= threshold)]
        if xs:
            rows.append((y, min(xs), max(xs) + 1, len(xs)))
    bands = []
    for y, left, right, count in rows:
        if not bands or bands[-1]["bounds"][3] != y:
            bands.append({"bounds": [left, y, right, y + 1], "pixels": count})
        else:
            band = bands[-1]
            band["bounds"][0] = min(band["bounds"][0], left)
            band["bounds"][2] = max(band["bounds"][2], right)
            band["bounds"][3] = y + 1
            band["pixels"] += count
    return {"file": str(filename), "sha256": hashlib.sha256(Path(filename).read_bytes()).hexdigest(),
            "imageSize": list(image.size), "region": list(region), "threshold": threshold, "polarity": polarity,
            "unit": "物理像素；边界右下不包含", "bands": bands,
            "topSteps": [b["bounds"][1] - a["bounds"][1] for a, b in zip(bands, bands[1:])],
            "pixelRestorationVerified": False}


if __name__ == "__main__":
    result = measure_ink(Path(sys.argv[1]), tuple(map(int, sys.argv[2:6])))
    print(json.dumps(result, ensure_ascii=False, indent=2))
