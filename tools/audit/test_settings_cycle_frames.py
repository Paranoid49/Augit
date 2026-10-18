"""包络诊断的反例测试；不把合成数组当作桌面证据。"""
import importlib.util
from pathlib import Path
import unittest
import tempfile
import numpy as np
spec=importlib.util.spec_from_file_location('cycle',Path(__file__).with_name('analyze-settings-cycle.py'))
cycle=importlib.util.module_from_spec(spec);spec.loader.exec_module(cycle)

class CycleTests(unittest.TestCase):
    def test_纠正分析目录不得覆盖已有证据(self):
        with tempfile.TemporaryDirectory() as temporary:
            output=Path(temporary)/'corrected'
            output.mkdir()
            evidence=output/'cycle-whole-state-analysis.json'
            evidence.write_text('existing',encoding='utf-8')
            with self.assertRaises(FileExistsError):
                cycle.analyze(Path(temporary)/'source',6.5,True,output)
            self.assertEqual(evidence.read_text(encoding='utf-8'),'existing')

    def test_整帧比较拒绝包络内的透明混合(self):
        refs=[np.full((40,40,3),v,dtype=np.uint8) for v in (20,100,150)]
        frame=np.full_like(refs[0],80)
        self.assertEqual(cycle.envelope_difference(frame,refs),0)
        self.assertEqual(cycle.nearest_state_difference(frame,refs)['differentPixels'],1600)
        for index, reference in enumerate(refs):
            result=cycle.nearest_state_difference(reference,refs)
            self.assertEqual(result['differentPixels'],0)
            self.assertEqual(result['nearestState'],index)

    def test_整帧比较拒绝上下拼接状态(self):
        refs=[np.full((40,40,3),v,dtype=np.uint8) for v in (20,100,150)]
        frame=refs[0].copy();frame[20:]=refs[1][20:]
        self.assertEqual(cycle.envelope_difference(frame,refs),0)
        self.assertEqual(cycle.nearest_state_difference(frame,refs)['differentPixels'],800)
        with self.assertRaises(ValueError):cycle.nearest_state_difference(frame,refs,-1)

    def test_三个状态及混合不伪装成同态验证(self):
        refs=[np.full((40,40,3),v,dtype=np.uint8) for v in (20,100,150)]
        for ref in refs: self.assertEqual(cycle.envelope_difference(ref,refs),0)
        self.assertEqual(cycle.envelope_difference(np.full_like(refs[0],80),refs),0)

    def test_白窗及边缘单像素都必须检出(self):
        refs=[np.full((40,40,3),v,dtype=np.uint8) for v in (20,100,150)]
        self.assertEqual(cycle.envelope_difference(np.full_like(refs[0],255),refs),1600)
        for xy in [(0,0),(39,39),(20,20)]:
            frame=refs[0].copy();frame[xy]=255
            self.assertEqual(cycle.envelope_difference(frame,refs),1)

    def test_容差边界与无效输入(self):
        frame=np.full((40,40,3),100,dtype=np.uint8);refs=[frame]*3
        self.assertEqual(cycle.envelope_difference(frame+2,refs),0)
        self.assertEqual(cycle.envelope_difference(frame+3,refs),1600)
        with self.assertRaises(ValueError):cycle.envelope_difference(frame,refs[:2])
        with self.assertRaises(ValueError):cycle.envelope_difference(frame,refs,-1)

if __name__=='__main__':unittest.main()
