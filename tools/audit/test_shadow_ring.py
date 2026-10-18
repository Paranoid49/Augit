"""完整外沿审计定向测试；合成图片不计参考实机证据。"""
import importlib.util
from pathlib import Path
import unittest
from PIL import Image

spec = importlib.util.spec_from_file_location('shadow_ring',Path(__file__).with_name('compare-shadow-ring.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def pair(rect=(12,12,36,36)):
    return (Image.new('RGB',(48,48),'white'), Image.new('RGB',(48,48),'white'), list(rect), 1.75, 'active')


class RingTests(unittest.TestCase):
    def test_窄窗口完整外沿无重叠且不依赖角部半径下限(self):
        small=pair((20,20,28,28))
        result=module.compare(small,small,12)
        self.assertEqual(result['totalPixels'],32*32-8*8)
        self.assertTrue(result['allExternalPixelsPassed'])
        self.assertTrue(result['samplingBoundaryClear'])
        for radius in (0,1.5):
            with self.assertRaises(ValueError):module.compare(small,small,radius)
        with self.assertRaises(ValueError):module.compare(small,(*small[:3],0,'active'),12)

    def test_相同但被截断的阴影必须明确标记(self):
        actual, reference = pair(), pair()
        for item in (actual,reference): item[0].putpixel((24,4),(229,229,229))
        result=module.compare(actual,reference,8)
        self.assertTrue(result['allExternalPixelsPassed'])
        self.assertFalse(result['samplingBoundaryClear'])
        self.assertFalse(result['completeExternalShadowPassed'])
        self.assertEqual(result['outerBoundary']['actual']['maximumDifferenceFromBackground'],26)
        self.assertEqual(result['outerBoundary']['actual']['pixels'],156)
        self.assertTrue(module.compare(pair(),pair(),8)['samplingBoundaryClear'])
        self.assertTrue(module.compare(pair(),pair(),8)['completeExternalShadowPassed'])

    def test_完整面积不重叠且不宣称全部阴影(self):
        result=module.compare(pair(),pair(),8)
        self.assertEqual(result['totalPixels'],40*40-24*24)
        self.assertTrue(result['allExternalPixelsPassed'])
        self.assertFalse(result['fullShadowVerified'])

    def test_八区与远端单像素缺陷均检出(self):
        for point,zone in [((4,4),'topLeft'),((24,4),'top'),((43,4),'topRight'),
                ((4,24),'left'),((43,24),'right'),((4,43),'bottomLeft'),((24,43),'bottom'),((43,43),'bottomRight')]:
            actual=pair();actual[0].putpixel(point,(251,251,251))
            result=module.compare(actual,pair(),8)
            self.assertEqual(result['zones'][zone]['pixelsAboveTolerance'],1)
            self.assertFalse(result['allExternalPixelsPassed'])

    def test_背景差异不能以暗化量相同放行(self):
        actual=pair()
        for image in actual[:2]:image.putpixel((24,4),(254,254,254))
        result=module.compare(actual,pair(),8)['zones']['top']
        self.assertEqual(result['maxDifference'],0)
        self.assertEqual(result['unmatchedBackgroundPixels'],1)
        self.assertFalse(result['passed'])

    def test_窗口可整数平移但不能改尺寸或活动状态(self):
        self.assertTrue(module.compare(pair(),pair((13,13,37,37)),8)['allExternalPixelsPassed'])
        with self.assertRaises(ValueError):module.compare(pair(),pair((12,12,37,36)),8)
        with self.assertRaises(ValueError):module.compare(pair(),(*pair()[:4],'inactive'),8)

    def test_容差边界与内部排除均明确(self):
        actual=pair();actual[0].putpixel((24,4),(252,252,252));actual[0].putpixel((24,24),(0,0,0))
        self.assertTrue(module.compare(actual,pair(),8)['allExternalPixelsPassed'])
        with self.assertRaises(ValueError):module.compare(pair(),pair(),13)


if __name__ == '__main__':unittest.main()
