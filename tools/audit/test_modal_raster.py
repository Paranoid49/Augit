"""负向验收应拒绝旧主窗PNG及错误标记位置。"""
import io
import importlib.util
import unittest
from pathlib import Path

from PIL import Image, ImageDraw
spec = importlib.util.spec_from_file_location("modal_raster", Path(__file__).with_name("verify-modal-raster.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
COLORS, verify = module.COLORS, module.verify


def png(image):
    output = io.BytesIO()
    image.save(output, format="PNG")
    return output.getvalue()


class ModalRasterTests(unittest.TestCase):
    def test_实际客户区标记在三档DPI通过(self):
        for dpr in (1, 1.25, 1.75):
            image = Image.new("RGB", (100, 60), (255, 255, 255))
            draw = ImageDraw.Draw(image)
            for index, color in enumerate(COLORS):
                draw.rectangle((10 + index * 6 * dpr, 10, 10 + (index + 1) * 6 * dpr - 1, 10 + 6 * dpr - 1), fill=color)
            self.assertEqual(len(verify(png(image), {"x": 10, "y": 10, "dpr": dpr})["markers"]), 3)

    def test_旧主窗PNG不能因尺寸相同通过(self):
        with self.assertRaisesRegex(AssertionError, "未包含实际模态"):
            verify(png(Image.new("RGB", (100, 60))), {"x": 10, "y": 10, "dpr": 1.75})

    def test_图中其他位置的同色不能冒充目标客户区(self):
        image = Image.new("RGB", (100, 60))
        draw = ImageDraw.Draw(image)
        for index, color in enumerate(COLORS):
            draw.rectangle((50 + index * 6, 30, 55 + index * 6, 35), fill=color)
        with self.assertRaisesRegex(AssertionError, "未包含实际模态"):
            verify(png(image), {"x": 10, "y": 10, "dpr": 1})


if __name__ == "__main__":
    unittest.main()
