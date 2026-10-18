"""设置时钟关联必须拒绝错实例、截断录像及倒退阶段。"""
import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('settings_clock',Path(__file__).with_name('correlate-settings-clock.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class SettingsClockTests(unittest.TestCase):
    def setUp(self):
        self.trace = {'processId':1,'originTimestamp':100,'frequency':1000,
            'utc':'2026-10-17T00:00:00+00:00','samples':[
                {'Stage':'created','Timestamp':101,'Thread':2},
                {'Stage':'presented','Timestamp':105,'Thread':2}]}
        self.origin = 1792195200000
        self.clocks = [self.origin+i for i in [0,2,4,6]]

    def test_关联邻帧不把阶段当画面(self):
        rows = module.correlate(self.trace,self.clocks,1)
        self.assertEqual([(r['beforeFrame'],r['afterFrame']) for r in rows],[(0,1),(2,3)])
        self.assertEqual(rows[-1]['elapsedMilliseconds'],5)
        self.assertEqual(rows[-1]['beforeFrameUtcMilliseconds'],self.origin+4)
        self.assertEqual(rows[-1]['afterFrameUtcMilliseconds'],self.origin+6)

    def test_错进程无频率无时区必须拒绝(self):
        for key,value in [('processId',2),('frequency',0),('utc','2026-10-17T00:00:00')]:
            trace = copy.deepcopy(self.trace);trace[key] = value
            with self.assertRaises(ValueError):module.correlate(trace,self.clocks,1)

    def test_录像截断和倒退必须拒绝(self):
        for clocks in [[],self.clocks[:-1],self.clocks[1:],list(reversed(self.clocks))]:
            with self.assertRaises(ValueError):module.correlate(self.trace,clocks,1)

    def test_原生阶段缺失与倒退必须拒绝(self):
        for samples in [[],self.trace['samples'][:-1],list(reversed(self.trace['samples']))]:
            trace = {**self.trace,'samples':samples}
            with self.assertRaises(ValueError):module.correlate(trace,self.clocks,1)


if __name__ == '__main__':unittest.main()
