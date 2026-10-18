"""防止无纹理留出区提供虚假的混合验证。"""
import importlib.util
import unittest
from unittest.mock import patch
from pathlib import Path
import numpy as np

spec = importlib.util.spec_from_file_location('transition', Path(__file__).with_name('analyze-edge-background-transition.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class TransitionTests(unittest.TestCase):
    def test_指定帧完整解码且拒绝截断(self):
        from types import SimpleNamespace
        pixels = np.arange(60, dtype=np.uint8).reshape(4, 5, 3)
        with patch.object(module.subprocess, 'run', return_value=SimpleNamespace(stdout=pixels.tobytes())) as run:
            self.assertTrue(np.array_equal(module.decode_frame('movie.mkv', 7, pixels.shape), pixels))
            self.assertIn('select=eq(n'+chr(92)+',7)', run.call_args.args[0])
        with patch.object(module.subprocess, 'run', return_value=SimpleNamespace(stdout=b'bad')):
            with self.assertRaises(ValueError):
                module.decode_frame('movie.mkv', 7, pixels.shape)
    def test_独立纹理样本检出混合与消失(self):
        rng = np.random.default_rng(6)
        bg = rng.integers(0, 128, (40, 200, 3), dtype=np.uint8)*2
        mixed = bg//2+40
        result = module.fit_edge(bg, mixed)
        self.assertAlmostEqual(result['slope'], .5)
        self.assertLess(result['heldOutMse'], 1e-20)
        result = module.fit_edge(bg, np.full_like(bg, 185))
        self.assertAlmostEqual(result['slope'], 0)
        self.assertAlmostEqual(result['intercept'], 185)

    def test_训练或留出纹理不足必须拒绝(self):
        rng = np.random.default_rng(7)
        for parity in [0, 1]:
            bg = rng.integers(0, 255, (40, 200, 3), dtype=np.uint8)
            bg[1, parity::2] = 200
            with self.assertRaises(ValueError):
                module.fit_edge(bg, bg)

    def test_留出失败不能被训练通过掩盖(self):
        rng = np.random.default_rng(8)
        bg = rng.integers(0, 128, (40, 200, 3), dtype=np.uint8)*2
        target = bg//2+40
        target[1, 1::2] = 0
        result = module.fit_edge(bg, target)
        self.assertLess(result['trainingMse'], 1e-20)
        self.assertGreater(result['heldOutMse'], 100)


if __name__ == '__main__':
    unittest.main()
