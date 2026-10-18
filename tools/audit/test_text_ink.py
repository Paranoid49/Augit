"""验证墨迹分行、边界和格式；测试样本不能充当产品截图。"""
import importlib.util
from pathlib import Path
import tempfile
import unittest
from PIL import Image, ImageDraw

spec = importlib.util.spec_from_file_location("ink", Path(__file__).with_name("measure-text-ink.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class TextInkTests(unittest.TestCase):
    def test_浅底深色墨迹及参数拒绝(self):
        with tempfile.TemporaryDirectory(prefix="Augit-ink-test-") as temp:
            file=Path(temp)/"sample.png"
            image=Image.new("RGB",(20,24),"white")
            ImageDraw.Draw(image).rectangle((3,4,8,7),fill=(20,22,25))
            image.putpixel((0,0),(0,200,200))
            image.save(file)
            result=module.measure_ink(file,(0,0,20,24),80,'dark')
            self.assertEqual([b['bounds'] for b in result['bands']],[[3,4,9,8]])
            self.assertEqual(result['polarity'],'dark')
            for threshold,polarity in [(-1,'dark'),(256,'light'),(80,'invalid')]:
                with self.assertRaises(ValueError):module.measure_ink(file,(0,0,20,24),threshold,polarity)

    def test_分行物理边界及排除亚像素(self):
        with tempfile.TemporaryDirectory(prefix="Augit-ink-test-") as temp:
            file = Path(temp) / "sample.png"
            image = Image.new("RGB", (20, 24), (25, 26, 28))
            draw = ImageDraw.Draw(image)
            draw.rectangle((3, 4, 8, 7), fill=(188, 190, 196))
            draw.rectangle((4, 14, 10, 17), fill=(188, 190, 196))
            image.putpixel((0, 0), (255, 20, 30))
            image.save(file)
            result = module.measure_ink(file, (0, 0, 20, 24))
            self.assertEqual([b["bounds"] for b in result["bands"]], [[3, 4, 9, 8], [4, 14, 11, 18]])
            self.assertEqual(result["topSteps"], [10])
            self.assertFalse(result["pixelRestorationVerified"])
            with self.assertRaises(ValueError):
                module.measure_ink(file, (-1, 0, 20, 24))

    def test_空区域和伪装格式(self):
        with tempfile.TemporaryDirectory(prefix="Augit-ink-test-") as temp:
            file = Path(temp) / "sample.png"
            image = Image.new("RGB", (10, 10), "black")
            image.save(file)
            self.assertEqual(module.measure_ink(file, (0, 0, 10, 10))["bands"], [])
            image.save(file, format="JPEG")
            with self.assertRaises(ValueError):
                module.measure_ink(file, (0, 0, 10, 10))


if __name__ == "__main__":
    unittest.main()
