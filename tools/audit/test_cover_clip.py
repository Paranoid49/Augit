"""裁切比较必须报告差异并拒绝越界，避免Pillow静默补黑。"""
import importlib.util
from pathlib import Path
import unittest
import json
import tempfile
import hashlib
from PIL import Image

spec = importlib.util.spec_from_file_location('cover_clip', Path(__file__).with_name('compare-cover-clip.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CoverClipTests(unittest.TestCase):
    def native_fixture(self, root):
        Image.new('RGB', (80, 80), (10, 20, 30)).save(root / 'full.png')
        Image.new('RGB', (40, 40), (10, 20, 30)).save(root / 'clip.png')
        screen = Image.new('RGB', (50, 50), (10, 20, 30))
        screen.putpixel((5, 5), (20, 20, 30))
        screen.save(root / 'settings.png')
        entries = [{'pair': 0, 'kind': kind, 'file': kind + '.png', 'bytes': (root / (kind + '.png')).stat().st_size, 'elapsedMilliseconds': 1} for kind in ['full', 'clip']]
        probe = {'method': 'cdp-raw-png', 'systemScreenshot': False, 'geometry': {'rect': {'x': 10, 'y': 10, 'width': 40, 'height': 40}, 'dpr': 1}, 'captures': entries}
        capture = {'pid': 100, 'modal': {'window': 200, 'rect': {'x': 100, 'y': 100, 'width': 40, 'height': 40}, 'liveBounds': {'x': -10, 'y': -10}}}
        info = {'capture': 'screen', 'method': 'System.Drawing.Graphics.CopyFromScreen', 'resampled': False, 'stableActivation': True, 'stableProcessFrameAndDpi': True, 'sha256': hashlib.sha256((root / 'settings.png').read_bytes()).hexdigest(), 'processId': 100, 'windowHandle': 200, 'dpi': 96, 'frameRectPhysical': [100, 100, 140, 140], 'regionPhysical': [95, 95, 50, 50]}
        for name, value in [('cover-clip-probe.json', probe), ('capture.json', capture), ('settings.png.json', info)]:
            (root / name).write_text(json.dumps(value), encoding='utf-8')

    def test_native_crop_uses_host_identity_and_reports_edges(self):
        with tempfile.TemporaryDirectory(prefix='Augit-native-clip-test-') as folder:
            root = Path(folder)
            self.native_fixture(root)
            result = module.compare_native_screen(root)
            self.assertEqual(result['physicalCrop'], [10, 10, 50, 50])
            self.assertEqual(result['comparisons'][0]['full']['changedPixels'], 1)
            self.assertEqual(result['comparisons'][0]['interior16']['changedPixels'], 0)
            self.assertTrue(result['diagnosticOnly'])

    def test_native_capture_rejects_unmatched_evidence(self):
        for patch in [{'processId': 101}, {'windowHandle': 201}, {'sha256': 'bad'}, {'resampled': True}, {'dpi': 120}, {'frameRectPhysical': [0, 0, 40, 40]}, {'regionPhysical': [95, 95, 49, 50]}]:
            with self.subTest(patch=patch), tempfile.TemporaryDirectory(prefix='Augit-native-clip-test-') as folder:
                root = Path(folder)
                self.native_fixture(root)
                path = root / 'settings.png.json'
                info = json.loads(path.read_text(encoding='utf-8'))
                info.update(patch)
                path.write_text(json.dumps(info), encoding='utf-8')
                with self.assertRaises(ValueError):
                    module.compare_native_screen(root)

    def test_fractional_origin_cannot_pass_exact_acceptance(self):
        with tempfile.TemporaryDirectory(prefix='Augit-clip-test-') as folder:
            root = Path(folder)
            Image.new('RGB', (4, 4)).save(root / 'full.png')
            Image.new('RGB', (2, 2)).save(root / 'clip.png')
            entries = [{'pair': 0, 'kind': kind, 'file': kind + '.png', 'bytes': (root / (kind + '.png')).stat().st_size, 'elapsedMilliseconds': 1} for kind in ['full', 'clip']]
            manifest = {'method': 'cdp-raw-png', 'systemScreenshot': False, 'geometry': {'rect': {'x': 0, 'y': .5, 'width': 2, 'height': 2}, 'dpr': 1}, 'captures': entries}
            (root / 'cover-clip-probe.json').write_text(json.dumps(manifest), encoding='utf-8')
            report = module.analyze(root)
            self.assertFalse(report['pixelAligned'])
            self.assertFalse(report['exactPixelMatch'])
            self.assertEqual(report['pairs'][0]['changedPixels'], 0)

    def test_exact_and_changed_pixel(self):
        full = Image.new('RGB', (4, 4), (10, 20, 30))
        clip = full.crop((1, 1, 3, 3))
        self.assertEqual(module.compare(full, clip, (1, 1, 3, 3))['changedPixels'], 0)
        clip.putpixel((0, 0), (20, 20, 30))
        self.assertEqual(module.compare(full, clip, (1, 1, 3, 3))['overTolerancePixels'], 1)

    def test_reject_invalid_geometry(self):
        full = Image.new('RGB', (4, 4))
        for box in [(-1, 0, 1, 2), (0, 0, 5, 4), (0, 0, 0, 1), (0.0, 0, 2, 2), (0, 0, 3, 3)]:
            with self.assertRaises(ValueError):
                module.compare(full, Image.new('RGB', (2, 2)), box)


if __name__ == '__main__':
    unittest.main()
