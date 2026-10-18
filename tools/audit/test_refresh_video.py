"""验证录像探针缺失与时间戳间隙不能被通过结果掩盖。"""
import importlib.util
import unittest
from pathlib import Path

spec=importlib.util.spec_from_file_location('refresh_video',Path(__file__).with_name('verify-refresh-video.py'))
module=importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class RefreshVideoTests(unittest.TestCase):
    def test_三色完整和单像素消失(self):
        colors=['#e91159','#0de3ad','#472bf1']
        row=b''.join(bytes.fromhex(color[1:])*5 for color in colors)
        frame=bytearray(row*5)
        self.assertTrue(module.inspect_pixels(frame,colors)['passed'])
        frame[0]=0
        self.assertFalse(module.inspect_pixels(frame,colors)['passed'])
        with self.assertRaises(ValueError):module.inspect_pixels(frame[:-1],colors)

    def test_时间戳缺口与重复必须显式暴露(self):
        result=module.timing_summary([0,.016,.033,.09])
        self.assertEqual(result['gapsAbove25Milliseconds'],1)
        self.assertAlmostEqual(result['maximumGapMilliseconds'],57)
        self.assertFalse(module.timing_summary([0,0])['strictTimingPassed'])
        self.assertEqual(module.timing_summary([0,0])['duplicateTimestamps'],1)
        with self.assertRaises(ValueError):module.timing_summary([1,0])

if __name__=='__main__':unittest.main()
