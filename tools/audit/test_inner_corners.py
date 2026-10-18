"""内侧角区门禁反例；合成图不充当实机证据。"""
import importlib.util
from pathlib import Path
import unittest
from PIL import Image

spec = importlib.util.spec_from_file_location('inner', Path(__file__).with_name('compare-inner-corners.py'))
inner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(inner)


def pair():
    return (Image.new('RGB', (64, 64), 'white'), Image.new('RGB', (64, 64), 'white'), [8, 8, 56, 56], 1.75, 'active')


class InnerTests(unittest.TestCase):
    def test_四角缺陷均能检出(self):
        for point, name in [((8, 8), 'topLeft'), ((55, 8), 'topRight'), ((8, 55), 'bottomLeft'), ((55, 55), 'bottomRight')]:
            actual = pair()
            actual[0].putpixel(point, (248, 255, 255))
            result = inner.compare(actual, pair())
            self.assertFalse(result['corners'][name]['passed'])
            self.assertEqual(result['corners'][name]['pixelsAboveTolerance'], 1)

    def test_同图通过不等于整窗(self):
        result = inner.compare(pair(), pair())
        self.assertTrue(result['allInnerCornersPassed'])
        self.assertFalse(result['fullWindowVerified'])
        self.assertEqual(sum(v['totalPixels'] for v in result['corners'].values()), 1024)

    def test_背景不同不能掩盖为通过(self):
        actual = pair()
        actual[1].putpixel((8, 8), (254, 255, 255))
        self.assertFalse(inner.compare(actual, pair())['allInnerCornersPassed'])

    def test_边界与尺度状态严格校验(self):
        for radius in (0, 25, 1.5):
            with self.assertRaises(ValueError): inner.compare(pair(), pair(), radius)
        for changed in [(*pair()[:3], 1, 'active'), (*pair()[:4], 'inactive'),
                        (*pair()[:2], [8, 8, 55, 56], *pair()[3:]),
                        (*pair()[:2], [-1, 8, 55, 56], *pair()[3:])]:
            with self.assertRaises(ValueError): inner.compare(changed, pair())

    def test_跳转行采集禁止鼠标进入采样区(self):
        source = Path(__file__).with_name('capture-shadow-shell.cjs').read_text(encoding='utf-8')
        self.assertIn("if(gotoLine)args.push('-PointerClearance','128')", source)
        self.assertIn("page.locator('.code-view').focus()", source)
        self.assertNotIn("page.locator('.code-view').click()", source)
        self.assertIn("if(gotoLine)await page.keyboard.press('Escape')", source)


if __name__ == '__main__':
    unittest.main()
