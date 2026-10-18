"""刷新内容起始量测的纯函数测试：阈值、旧内容回放和裁切边界。"""
import importlib.util
import unittest
from pathlib import Path

import numpy as np

MODULE_PATH = Path(__file__).with_name('measure-refresh-content-onset.py')
SPEC = importlib.util.spec_from_file_location('measure_refresh_content_onset', MODULE_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class RefreshContentOnsetTests(unittest.TestCase):
    def setUp(self):
        self.old = np.zeros((10, 10), dtype=np.uint8)
        self.new = self.old.copy()
        self.new[0:3, 0:3] = 255

    def test_old_replay_then_new_content(self):
        frames = [self.old, self.old, self.old, self.new]
        first_change, first_new = MODULE.onset_indices(
            frames, self.old, self.new, 0, 3, 16, 4)
        self.assertEqual(first_change, 3)
        self.assertEqual(first_new, 3)

    def test_small_partial_patch_does_not_count(self):
        partial = self.old.copy()
        partial[0:2, 0:2] = 255
        frames = [self.old, partial, self.new]
        first_change, first_new = MODULE.onset_indices(
            frames, self.old, self.new, 0, 2, 16, 5)
        self.assertEqual(first_change, 2)
        self.assertEqual(first_new, 2)

    def test_missing_new_content_returns_none(self):
        frames = [self.old, self.old]
        first_change, first_new = MODULE.onset_indices(
            frames, self.old, self.new, 0, 1, 16, 4)
        self.assertIsNone(first_change)
        self.assertIsNone(first_new)

    def test_crop_bounds_are_checked(self):
        MODULE.validate_crop((0, 0, 10, 10), 10, 10)
        with self.assertRaises(ValueError):
            MODULE.validate_crop((1, 0, 10, 10), 10, 10)
        with self.assertRaises(ValueError):
            MODULE.validate_crop((-1, 0, 4, 4), 10, 10)
        with self.assertRaises(ValueError):
            MODULE.validate_crop((0, 0, 0, 4), 10, 10)

    def test_frame_range_is_checked(self):
        with self.assertRaises(ValueError):
            MODULE.onset_indices([self.old], self.old, self.new, 0, 1, 16, 4)


if __name__ == '__main__':
    unittest.main()
