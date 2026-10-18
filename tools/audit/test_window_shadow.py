"""验证阴影剖面坐标、亮暗方向及不合格输入拒绝；合成样本不是实机证据。"""
import importlib.util
import io
import unittest
from pathlib import Path

from PIL import Image, ImageDraw

spec = importlib.util.spec_from_file_location("window_shadow", Path(__file__).with_name("measure-window-shadow.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def encoded(image, format="PNG"):
    output = io.BytesIO()
    image.save(output, format=format)
    return Image.open(io.BytesIO(output.getvalue()))


class WindowShadowTests(unittest.TestCase):
    def test_四侧非对称范围与缩放(self):
        back = Image.new("RGB", (100, 100), "white")
        front = back.copy()
        draw = ImageDraw.Draw(front)
        for box in [(27, 30, 29, 69), (70, 30, 74, 69), (30, 28, 69, 29), (30, 70, 69, 76)]:
            draw.rectangle(box, fill=(235, 235, 235))
        result = module.measure(encoded(front), encoded(back), [30, 30, 70, 70], 1.75, 16)
        for side, extent in zip(("left", "right", "top", "bottom"), (3, 5, 2, 7)):
            profile = result["profiles"][side]
            self.assertEqual(profile["extent_above_threshold_physical"], extent)
            self.assertEqual(profile["extent_above_threshold_logical"], extent/1.75)
            self.assertTrue(profile["tail_settled"])

    def test_背景变亮不算阴影且尾部差异不被忽略(self):
        front = encoded(Image.new("RGB", (100, 100), "white"))
        back = encoded(Image.new("RGB", (100, 100), (200, 200, 200)))
        result = module.measure(front, back, [30, 30, 70, 70], 1, 16)
        for profile in result["profiles"].values():
            self.assertEqual(profile["extent_above_threshold_physical"], 0)
            self.assertFalse(profile["tail_settled"])

    def test_拒绝JPEG与缺失外沿(self):
        image = Image.new("RGB", (100, 100), "white")
        with self.assertRaisesRegex(ValueError, "PNG"):
            module.measure(encoded(image, "JPEG"), encoded(image), [30, 30, 70, 70], 1, 16)
        with self.assertRaisesRegex(ValueError, "采样带"):
            module.measure(encoded(image), encoded(image), [0, 0, 70, 70], 1, 16)

    def test_拒绝尺寸不等及无效缩放(self):
        image = encoded(Image.new("RGB", (100, 100)))
        with self.assertRaisesRegex(ValueError, "尺寸"):
            module.measure(image, encoded(Image.new("RGB", (101, 100))), [30, 30, 70, 70], 1, 16)
        with self.assertRaisesRegex(ValueError, "缩放"):
            module.measure(image, image, [30, 30, 70, 70], float("nan"), 16)


if __name__ == "__main__":
    unittest.main()
