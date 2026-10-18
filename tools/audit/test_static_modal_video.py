"""全客户区分析必须保留边缘及仅一帧异常。"""
import importlib.util
from pathlib import Path
import unittest
import tempfile
import numpy as np

spec = importlib.util.spec_from_file_location('modal_video', Path(__file__).with_name('analyze-static-modal-video.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class StaticModalVideoTests(unittest.TestCase):
    def test_last_pixel_and_one_frame_are_not_ignored(self):
        base = np.zeros((7, 11, 3), dtype=np.uint8)
        changed = base.copy()
        changed[-1, -1] = 255
        self.assertEqual(module.compare_frame(changed, base)['bounds'], [10, 6, 11, 7])
        self.assertEqual([module.compare_frame(f, base)['differentPixels'] for f in (base, changed, base)], [0, 1, 0])

    def test_tolerance_and_shape(self):
        base = np.zeros((2, 3, 3), dtype=np.uint8)
        self.assertEqual(module.compare_frame(base+2, base)['differentPixels'], 0)
        self.assertEqual(module.compare_frame(base+3, base)['differentPixels'], 6)
        with self.assertRaises(ValueError):
            module.compare_frame(base[:1], base)

    def test_existing_evidence_is_never_overwritten(self):
        with tempfile.TemporaryDirectory() as directory:
            file = Path(directory)/'worst-frame.png'
            file.write_bytes(b'preserve')
            with self.assertRaisesRegex(ValueError, '拒绝覆盖'):
                module.analyze(directory)
            self.assertEqual(file.read_bytes(), b'preserve')


if __name__ == '__main__':
    unittest.main()
