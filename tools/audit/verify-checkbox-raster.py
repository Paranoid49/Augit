"""核对真实DPR1.75截图中的复选框纯色边界；不缩放、不重采样。"""
import json
import sys
from pathlib import Path

from PIL import Image


def verify(directory: Path) -> list[dict]:
    results = []
    for theme, color in {"Light": (168, 173, 189), "Dark": (90, 93, 99)}.items():
        filename = f"checkbox-{theme}-12.png"
        image = Image.open(directory / filename).convert("RGB")
        assert image.size == (42, 42), "24逻辑像素画布在DPR1.75下应为42物理像素"
        points = [(x, y) for y in range(image.height) for x in range(image.width)
                  if image.getpixel((x, y)) == color]
        assert points, f"{theme}截图缺少复选框权威边框色"
        bounds = (min(x for x, _ in points), min(y for _, y in points),
                  max(x for x, _ in points) + 1, max(y for _, y in points) + 1)
        assert bounds[2] - bounds[0] == 28 and bounds[3] - bounds[1] == 28, "16逻辑像素外框应为28物理像素"
        results.append({"file": filename, "canvas": image.size, "edge": color,
                        "pure_color_bbox": bounds, "width": 28, "height": 28})
    return results


if __name__ == "__main__":
    target = Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts/pixel-restoration-2026-10-13/shell")
    rows = verify(target)
    (target / "raster.json").write_text(json.dumps(rows, indent=2) + "\n", encoding="utf-8")
    print("真实浅深截图的42px画布、28×28px绘制外框及专用边框色通过。")
