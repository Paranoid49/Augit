"""合成反例验证全行覆盖，不作为产品像素验收。"""
import importlib.util
from pathlib import Path
import unittest

import numpy as np

spec = importlib.util.spec_from_file_location('viewport', Path(__file__).with_name('measure-terminal-viewport.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class TerminalViewportTests(unittest.TestCase):
    def test_双端并集保留参考顶部与最右下差异(self):
        reference = np.full((25, 20, 3), 255, dtype=np.uint8)
        actual = reference.copy(); actual[1, 0] = 0; actual[19, 17] = 0
        regions = module.row_regions(self.snapshot(), 1.75, (20, 25))
        result = module.compare_surfaces(reference, actual, regions, (0, 1, 18, 19))
        self.assertEqual(result['regionPhysical'], [0, 1, 18, 20])
        self.assertEqual(result['pixels'], 342)
        self.assertEqual(result['differentPixelsTolerance2'], 2)
        self.assertFalse(result['pixelRestorationVerified'])

    def test_双端不同尺寸和参考越界拒绝(self):
        reference = np.zeros((25, 20, 3), dtype=np.uint8)
        regions = module.row_regions(self.snapshot(), 1.75, (20, 25))
        with self.assertRaises(ValueError): module.compare_surfaces(reference, reference[:20], regions, (0, 0, 20, 20))
        with self.assertRaises(ValueError): module.compare_surfaces(reference, reference, regions, (0, 0, 21, 20))
        with self.assertRaises(ValueError): module.compare_surfaces(reference, reference, regions, (0, 0, 20, 20), (3, 4, 16, 19))

    def test_实际网格之外内边距纳入比较(self):
        reference = np.full((25, 20, 3), 255, dtype=np.uint8)
        actual = reference.copy(); actual[21, 18] = 0
        regions = module.row_regions(self.snapshot(), 1.75, (20, 25))
        result = module.compare_surfaces(reference, actual, regions, (0, 1, 18, 19), (1, 2, 19, 22))
        self.assertEqual(result['regionPhysical'], [0, 1, 19, 22])
        self.assertEqual(result['pixels'], 399)
        self.assertEqual(result['differentPixelsTolerance2'], 1)

    def snapshot(self):
        return {'rowCount': 3, 'viewportY': 8, 'screen': {'x': 1.25, 'y': 2.25, 'width': 8, 'height': 9},
                'rows': [{'screenRow': i+1, 'bufferIndex': i+8, 'text': '' if i else '文本',
                          'bounds': {'x': 1.25, 'y': 2.25+i*3, 'width': 8, 'height': 3}} for i in range(3)]}

    def test_分数边界全覆盖空白行尾且每像素一次(self):
        regions = module.row_regions(self.snapshot(), 1.75, (20, 25))
        coverage = np.zeros((25, 20), dtype=int)
        for x0, y0, x1, y1 in regions:
            coverage[y0:y1, x0:x1] += 1
        self.assertTrue(np.all(coverage[3:20, 2:17] == 1))
        self.assertEqual(int(coverage.sum()), 255)
        self.assertEqual(coverage[19, 16], 1)

    def test_缺行错序和滚动索引不一致均拒绝(self):
        for kind in ('missing', 'order', 'scroll'):
            snapshot = self.snapshot()
            if kind == 'missing': snapshot['rows'].pop()
            elif kind == 'order': snapshot['rows'][0]['screenRow'] = 2
            else: snapshot['viewportY'] = 0
            with self.assertRaises(ValueError): module.row_regions(snapshot, 1.75, (20, 25))

    def test_行尾裁切和行间缺口重叠均拒绝(self):
        for key, value in [('width', 7), ('y', 6), ('y', 5), ('height', 2)]:
            snapshot = self.snapshot(); snapshot['rows'][1]['bounds'][key] = value
            with self.assertRaises(ValueError): module.row_regions(snapshot, 1.75, (20, 25))

    def test_非有限缩放和越界拒绝(self):
        for scale in (0, -1, float('nan'), float('inf'), 10):
            with self.assertRaises(ValueError): module.row_regions(self.snapshot(), scale, (20, 25))


if __name__ == '__main__':
    unittest.main()
