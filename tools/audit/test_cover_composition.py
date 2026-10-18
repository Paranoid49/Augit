"""遮盖静态取证校验测试，不作为实机通过证据。"""
import importlib.util
import json
import hashlib
from pathlib import Path
import tempfile
import unittest
from PIL import Image

spec = importlib.util.spec_from_file_location('composition', Path(__file__).with_name('verify-cover-composition.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CompositionTests(unittest.TestCase):
    def fixture(self, folder):
        metadata = {'capture': 'screen', 'method': 'System.Drawing.Graphics.CopyFromScreen',
                    'regionPhysical': [100, 200, 8, 6], 'frameRectPhysical': [102, 201, 106, 205],
                    'dpi': 168, 'processId': 123, 'windowHandle': 456, 'activationState': 'active',
                    'resampled': False, 'stableActivation': True, 'stableProcessFrameAndDpi': True}
        for name in ['settings', 'cover-visible-live', 'cover-hidden-live']:
            image = Image.new('RGB', (8, 6), (20, 30, 40))
            if name != 'settings':
                image.putpixel((2, 1), (25, 30, 40))
            image.save(folder/(name+'.png'))
            metadata['sha256'] = hashlib.sha256((folder/(name+'.png')).read_bytes()).hexdigest()
            (folder/(name+'.png.json')).write_text(json.dumps(metadata), encoding='utf-8')
        Image.new('RGB', (10, 8), (20, 30, 40)).save(folder/'cover-source.png')
        (folder/'cover-source.json').write_text(json.dumps({'method': 'cdp-loaded-resource-content', 'width': 10, 'height': 8}), encoding='utf-8')

    def test_边缘单像素差异不排除且隐藏前后严格相等(self):
        with tempfile.TemporaryDirectory(prefix='Augit-cover-test-') as tmp:
            folder = Path(tmp)
            self.fixture(folder)
            result = module.verify(folder)
            self.assertTrue(result['source']['opaque'])
            self.assertEqual(result['beforeToCover']['changedPixels'], 1)
            self.assertTrue(result['visibleToHidden']['passed'])
            self.assertFalse(result['fullPresentationVerified'])

    def test_身份不一致不能进入像素比较(self):
        with tempfile.TemporaryDirectory(prefix='Augit-cover-test-') as tmp:
            folder = Path(tmp)
            self.fixture(folder)
            path = folder/'cover-visible-live.png.json'
            metadata = json.loads(path.read_text())
            metadata['windowHandle'] += 1
            path.write_text(json.dumps(metadata), encoding='utf-8')
            with self.assertRaises(ValueError):
                module.verify(folder)

    def test_透明源图不得误报不透明(self):
        with tempfile.TemporaryDirectory(prefix='Augit-cover-test-') as tmp:
            folder = Path(tmp)
            self.fixture(folder)
            image = Image.new('RGBA', (10, 8), (20, 30, 40, 255))
            image.putpixel((0, 0), (20, 30, 40, 254))
            image.save(folder/'cover-source.png')
            self.assertFalse(module.verify(folder)['source']['opaque'])

    def test_越界或伪来源明确拒绝(self):
        image = Image.new('RGB', (8, 6))
        with self.assertRaises(ValueError):
            module.crop_surface(image, {'capture': 'cdp'})
        with self.assertRaises(ValueError):
            module.crop_surface(image, {'capture': 'screen', 'method': 'System.Drawing.Graphics.CopyFromScreen',
                'regionPhysical': [0, 0, 8, 6], 'frameRectPhysical': [-1, 0, 5, 4]})

    def test_源图尺寸或截图哈希被改动须拒绝(self):
        with tempfile.TemporaryDirectory(prefix='Augit-cover-test-') as tmp:
            folder = Path(tmp)
            self.fixture(folder)
            Image.new('RGB', (8, 6), (0, 0, 0)).save(folder/'settings.png')
            with self.assertRaisesRegex(ValueError, '哈希'):
                module.verify(folder)
            self.fixture(folder)
            Image.new('RGB', (11, 8)).save(folder/'cover-source.png')
            with self.assertRaisesRegex(ValueError, '真实尺寸'):
                module.verify(folder)

    def test_复用遮盖必须显式记录不能默认首轮(self):
        with tempfile.TemporaryDirectory(prefix='Augit-cover-test-') as tmp:
            folder = Path(tmp)
            self.fixture(folder)
            self.assertIsNone(module.verify(folder)['priorCoverBytes'])
            (folder/'cover-state.json').write_text(json.dumps({'beforeCover': {'captureCoverBytes': 1234}}), encoding='utf-8')
            self.assertEqual(module.verify(folder)['priorCoverBytes'], 1234)


if __name__ == '__main__':
    unittest.main()
