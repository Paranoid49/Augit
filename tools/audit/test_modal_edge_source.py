"""验证完整计数及源图证据门禁，不把局部中性色当作整窗合格。"""
import importlib.util
import json
import hashlib
import tempfile
import unittest
from pathlib import Path
import numpy as np
from PIL import Image

spec = importlib.util.spec_from_file_location('edge', Path(__file__).with_name('compare-modal-edge-source.py'))
edge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(edge)


class EdgeTests(unittest.TestCase):
    def test_背景使用实测客户区原点而非业务裁切(self):
        info = {'stableOwnerGeometry': True, 'processId': 1, 'dpi': 168, 'ownerGeometry': {'window': 2, 'processId': 1, 'dpi': 168, 'clientOriginPhysical': [0, 0], 'clientRectPhysical': [0, 0, 2880, 1716]}}
        native = {'owner': 2, 'rect': {'x': 1136, 'y': 721, 'width': 585, 'height': 252}}
        self.assertEqual(edge.background_box(info, native), [1136, 721, 1721, 973])
        for key, value in [('stableOwnerGeometry', False), ('processId', 3), ('dpi', 96)]:
            with self.assertRaises(ValueError):
                edge.background_box({**info, key: value}, native)

    def test_混合诊断包含未参与拟合的边缘(self):
        rng = np.random.default_rng(1)
        background = rng.integers(0, 128, (100, 200, 3), dtype=np.uint8)*2
        screen = (background//2+40).astype(np.uint8)
        result = edge.blend_diagnostic(background, screen)
        self.assertAlmostEqual(result['slope'], .5)
        self.assertLess(result['topHeldOut']['mse'], 1e-20)
        screen[20:80, 1] = 0
        changed = edge.blend_diagnostic(background, screen)
        self.assertGreater(changed['leftHeldOut']['mse'], 100)
        self.assertEqual(changed['trainingMse'], result['trainingMse'])

    def test_边缘与内部计数完整且颜色诊断不作为通过(self):
        a = np.full((20, 20, 3), 240, dtype=np.uint8)
        b = a.copy()
        b[1, 5] = [180, 150, 100]
        b[10, 10] = [10, 10, 10]
        result = edge.compare(a, b)
        self.assertEqual(result['differentPixels'], 2)
        self.assertEqual(result['edge3Pixels'], 1)
        self.assertEqual(result['interiorPixels'], 1)
        self.assertEqual(result['neutralSourceChromaticScreenEdgePixels'], 1)
        self.assertEqual(result['excludedPixels'], 0)
        self.assertFalse(result['fullPresentationVerified'])

    def test_拒绝尺寸和像素类型不符(self):
        a = np.zeros((20, 20, 3), dtype=np.uint8)
        for b in [a[:10], a.astype(float)]:
            with self.assertRaises(ValueError):
                edge.compare(a, b)

    def fixture(self, folder):
        for name, size in [('edge-source.png', (40, 40)), ('before.png', (20, 20))]:
            Image.new('RGB', size, (240, 240, 240)).save(folder/name)
        report = {'method': 'cdp-full-surface-png', 'sha256': hashlib.sha256((folder/'edge-source.png').read_bytes()).hexdigest(),
                  'sourceBox': [10, 10, 30, 30], 'geometry': {'dpr': 1},
                  'native': {'active': True, 'window': 2, 'liveBounds': {'x': -10, 'y': -10, 'width': 40, 'height': 40},
                             'rect': {'x': 100, 'y': 100, 'width': 20, 'height': 20}}}
        screen = {'method': 'System.Drawing.Graphics.CopyFromScreen', 'resampled': False,
                  'sha256': hashlib.sha256((folder/'before.png').read_bytes()).hexdigest(), 'windowHandle': 2, 'processId': 1,
                  'stableActivation': True, 'stableProcessFrameAndDpi': True, 'dpi': 96,
                  'frameRectPhysical': [100, 100, 120, 120], 'regionPhysical': [100, 100, 20, 20]}
        for name, value in [('edge-source.json', report), ('before.png.json', screen), ('launch.json', {'pid': 1})]:
            (folder/name).write_text(json.dumps(value), encoding='utf-8')

    def test_身份哈希比例和边界不能被默认通过(self):
        patches = [('before.png.json', {'sha256': 'bad'}), ('before.png.json', {'windowHandle': 3}),
                   ('before.png.json', {'dpi': 120}), ('edge-source.json', {'sourceBox': [9, 10, 29, 30]}),
                   ('before.png.json', {'regionPhysical': [100, 100, 19, 20]})]
        for file, patch in patches:
            with self.subTest(patch=patch), tempfile.TemporaryDirectory(prefix='Augit-edge-test-') as tmp:
                folder = Path(tmp)
                self.fixture(folder)
                self.assertEqual(edge.analyze(folder)['comparison']['differentPixels'], 0)
                target = folder/file
                data = json.loads(target.read_text())
                data.update(patch)
                target.write_text(json.dumps(data), encoding='utf-8')
                with self.assertRaises(ValueError):
                    edge.analyze(folder)


if __name__ == '__main__':
    unittest.main()
