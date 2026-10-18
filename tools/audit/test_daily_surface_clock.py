"""隐藏阶段配对必须拒绝缺失、倒序及漂移，边界不能强行归属。"""
import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location('surface_clock', Path(__file__).with_name('analyze-daily-surface-clock.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def trace():
    def row(time, **fields):
        return dict(utcMilliseconds=1000+time, monotonicMilliseconds=time, **fields)
    return [row(0, kind='installed'), row(10, kind='response', method='window/modal-cover', native=dict(active=True, captureCoverReady=True, captureCoverWindowVisible=True, captureCoverTopmostChild=True)),
            row(11, kind='dom-attribute', attribute='data-native-background-capture', isHidden=True, hiddenDialogs=1),
            row(31, kind='dom-attribute', attribute='data-native-background-capture', isHidden=False, hiddenDialogs=0)]


class SurfaceClockTests(unittest.TestCase):
    def test_真实隐藏区间与边界不确定性(self):
        intervals = module.hidden_intervals(trace())
        frames = [dict(captureUtcMilliseconds=time, differentPixels=10) for time in (1011, 1014, 1020, 1028, 1031)]
        self.assertEqual(module.summarize(intervals, frames)[0]['unambiguousChangedFrames'], 1)

    def test_其他批次记录和缺失桥接拒绝(self):
        rows = [dict(kind='request', method='window/modal-cover', utcMilliseconds=1010, monotonicMilliseconds=10),
                dict(kind='response', method='window/modal-cover', utcMilliseconds=1030, monotonicMilliseconds=30)]
        requests = [dict(method='window/modal-cover', started=10, ended=30, milliseconds=20, startedUtcMilliseconds=1010, endedUtcMilliseconds=1030)]
        module.validate_trace_requests(rows, requests)
        rows[0]['utcMilliseconds'] += 1000
        with self.assertRaises(ValueError):
            module.validate_trace_requests(rows, requests)
        with self.assertRaises(ValueError):
            module.validate_trace_requests(rows[1:], requests)

    def test_截断缺少恢复或缺少遮盖拒绝(self):
        for rows in (trace()[1:], trace()[:-1], [trace()[0], *trace()[2:]]):
            with self.assertRaises(ValueError):
                module.hidden_intervals(rows)

    def test_失败遮盖和时钟漂移拒绝(self):
        rows = trace()
        rows[1]['native']['captureCoverTopmostChild'] = False
        with self.assertRaises(ValueError):
            module.hidden_intervals(rows)
        rows = trace()
        rows[-1]['utcMilliseconds'] += 20
        with self.assertRaises(ValueError):
            module.hidden_intervals(rows)


if __name__ == '__main__':
    unittest.main()
