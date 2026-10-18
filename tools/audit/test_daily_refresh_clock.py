"""正常入口阶段适配必须保留双时钟，并拒绝缺失和漂移。"""
import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location('daily_clock', Path(__file__).with_name('correlate-daily-refresh.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class DailyClockTests(unittest.TestCase):
    def row(self):
        return dict(method='window/modal-refresh', started=10, ended=30, milliseconds=20,
                    startedUtcMilliseconds=1010, endedUtcMilliseconds=1030)

    def test_保留实际双时钟并关联阶段(self):
        result = module.clock.correlate([1020], module.bridge_events([self.row()]), [{'frame': 0}])
        self.assertEqual(result[0]['overlappingBridgePhases'][0]['method'], 'window/modal-refresh')

    def test_缺失应答或真实墙钟不能用timeOrigin代替(self):
        for key in ['ended', 'startedUtcMilliseconds', 'endedUtcMilliseconds', 'milliseconds']:
            row = self.row()
            del row[key]
            with self.assertRaises(ValueError):
                module.bridge_events([row])

    def test_时钟跳变必须拒绝(self):
        row = self.row()
        row['endedUtcMilliseconds'] += 50
        with self.assertRaises(ValueError):
            module.clock.correlate([1020], module.bridge_events([row]), [{'frame': 0}])

    def test_非模态请求不能冒充阶段(self):
        row = self.row()
        row['method'] = 'git/status'
        with self.assertRaises(ValueError):
            module.bridge_events([row])

    def test_非有限值和单调时钟倒退必须拒绝(self):
        for patch in [{'started': float('nan')}, {'endedUtcMilliseconds': float('inf')},
                      {'milliseconds': -1}, {'ended': 9}]:
            with self.assertRaises(ValueError):
                module.bridge_events([{**self.row(), **patch}])


if __name__ == '__main__':
    unittest.main()
