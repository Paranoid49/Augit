# 源图到系统图的诊断校验，错误裁取不得静默补黑。
import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
from PIL import Image

spec=importlib.util.spec_from_file_location('source_screen',Path(__file__).with_name('compare-cover-source-screen.py'))
module=importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class SourceScreenTests(unittest.TestCase):
    def fixture(self, folder):
        Image.new('RGB',(100,100),(20,30,40)).save(folder/'cover-source.png')
        (folder/'cover-source.json').write_text(json.dumps({'method':'cdp-loaded-resource-content','width':100,'height':100,'dpr':1,'rect':{'x':-10,'y':-10}}),encoding='utf-8')
        for name in ['settings','cover-visible-live','cover-hidden-live']:
            image=Image.new('RGB',(64,64),(20,30,40))
            image.putpixel((0,0),(25,30,40))
            file=folder/(name+'.png');image.save(file)
            info={'capture':'screen','method':'System.Drawing.Graphics.CopyFromScreen','resampled':False,
                  'sha256':hashlib.sha256(file.read_bytes()).hexdigest(),'stableActivation':True,
                  'stableProcessFrameAndDpi':True,'dpi':96,'frameRectPhysical':[0,0,64,64],
                  'regionPhysical':[0,0,64,64],'processId':1,'windowHandle':2,'activationState':'active'}
            (folder/(name+'.png.json')).write_text(json.dumps(info),encoding='utf-8')

    def test_边缘差异仍报告_内部相同不代表全图通过(self):
        with tempfile.TemporaryDirectory(prefix='Augit-source-screen-') as tmp:
            folder=Path(tmp);self.fixture(folder)
            result=module.analyze(folder)
            self.assertEqual(result['images']['settings']['full']['overTolerancePixels'],1)
            self.assertEqual(result['images']['settings']['interior16']['changedPixels'],0)
            self.assertTrue(result['diagnosticOnly'])

    def test_拒绝身份尺寸哈希和来源错误(self):
        for patch in [{'processId':4},{'regionPhysical':[0,0,65,64]},{'sha256':'bad'},{'resampled':True},{'dpi':120}]:
            with self.subTest(patch=patch),tempfile.TemporaryDirectory(prefix='Augit-source-screen-') as tmp:
                folder=Path(tmp);self.fixture(folder)
                file=folder/'cover-hidden-live.png.json';info=json.loads(file.read_text());info.update(patch);file.write_text(json.dumps(info))
                with self.assertRaises(ValueError):module.analyze(folder)

    def test_拒绝非整数物理偏移与越界(self):
        for x in [-10.4,-50]:
            with self.subTest(x=x),tempfile.TemporaryDirectory(prefix='Augit-source-screen-') as tmp:
                folder=Path(tmp);self.fixture(folder)
                file=folder/'cover-source.json';info=json.loads(file.read_text());info['rect']['x']=x;file.write_text(json.dumps(info))
                with self.assertRaises(ValueError):module.analyze(folder)

if __name__=='__main__':unittest.main()
