"""原始PNG的局部纯色连通区量测；不缩放，保留物理边界和单位。"""
import hashlib
import json
import sys
from pathlib import Path

from PIL import Image


def components(image, color, region):
    x0, y0, x1, y1 = region
    rgb = image.convert("RGB")
    points = {(x, y) for y in range(y0, y1) for x in range(x0, x1)
              if rgb.getpixel((x, y)) == color}
    boxes = []
    while points:
        pending = [points.pop()]
        found = []
        while pending:
            x, y = pending.pop()
            found.append((x, y))
            for point in ((x-1, y), (x+1, y), (x, y-1), (x, y+1)):
                if point in points:
                    points.remove(point)
                    pending.append(point)
        bounds = [min(x for x, _ in found), min(y for _, y in found),
                  max(x for x, _ in found)+1, max(y for _, y in found)+1]
        if bounds[2]-bounds[0] >= 24 and bounds[3]-bounds[1] >= 24:
            boxes.append(bounds)
    return sorted(boxes, key=lambda box: (box[1], box[0]))


def measure(filename, region, color=(53, 116, 240)):
    image = Image.open(filename)
    boxes = components(image, color, region)
    assert len(boxes) >= 2, "量测区域至少包含分组和文件的同色选中框"
    group, child = boxes[:2]
    return {"file": str(filename), "sha256": hashlib.sha256(filename.read_bytes()).hexdigest(),
            "image_size": list(image.size), "unit": "物理像素", "encoding": "原始PNG；不缩放、不重采样",
            "region": region, "color": "#%02X%02X%02X" % color, "bounds_ltrb": boxes,
            "group": [group[0], group[1], group[2]-group[0], group[3]-group[1]],
            "child": [child[0], child[1], child[2]-child[0], child[3]-child[1]],
            "relative_indent": child[0]-group[0], "row_step": child[1]-group[1]}


if __name__ == "__main__":
    root = Path("artifacts/pycharm-reference-2026-10-13")
    reference = measure(root / "commit-log-dark-original.png", [120, 205, 230, 315])
    rgb = Image.open(root / "commit-log-dark-original.png").convert("RGB")
    points = [(x, y) for y in range(680, 750) for x in range(85, 140) if rgb.getpixel((x, y)) == (90, 93, 99)]
    amend = [min(x for x, _ in points), min(y for _, y in points), max(x for x, _ in points)+1, max(y for _, y in points)+1]
    assert reference["group"][2:] == [28, 28] and reference["child"][2:] == [28, 28]
    assert reference["relative_indent"] == 33 and reference["row_step"] == 42
    assert amend == [95, 700, 123, 728]
    reference["amend_edge"] = {"color": "#5A5D63", "bounds_ltrb": amend, "method": "区域内纯色总包围盒；四边描边在圆角处未必连通"}
    reference["conditions"] = {"version": "2026.2.1", "theme": "Islands Dark", "ui_size": 12, "dpr": 1.75,
                               "formula": "ClassicPainter: max(round(7s),floor(round(16s)/2))+round(11s)"}
    records = {"reference": reference, "comparison_status": "局部组件量测；完整页面尚未配准"}
    if len(sys.argv) > 1:
        actual = measure(Path(sys.argv[1]), [115, 195, 235, 305])
        records["actual"] = actual
        records["difference_actual_minus_reference"] = {key: actual[key]-reference[key] for key in ("relative_indent", "row_step")}
    output = root / "commit-components-metrics.json"
    output.write_text(json.dumps(records, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    print(json.dumps(records, ensure_ascii=False))
