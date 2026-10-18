"""角部算法定向测试；合成图不计实机验收。"""
import importlib.util
import hashlib
import json
import tempfile
from pathlib import Path
import unittest
from PIL import Image

spec = importlib.util.spec_from_file_location('shadow_corners',Path(__file__).with_name('compare-shadow-corners.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

def pair():
    return (Image.new('RGB',(48,48),'white'),Image.new('RGB',(48,48),'white'),[12,12,36,36],1.75)

class CornerTests(unittest.TestCase):
    def test_活动和非活动必须分别比较且元数据不能矛盾(self):
        source={'activationState':'inactive','stableActivation':True,'foregroundBeforeAndAfter':False,
                'foregroundWindow':20,'foregroundProcessId':30,'windowHandle':10}
        self.assertEqual(module.activation(source),'inactive')
        self.assertEqual(module.activation({'foregroundBeforeAndAfter':True}),'active')
        for patch in [{'stableActivation':False},{'foregroundWindow':10},{'foregroundBeforeAndAfter':True},
                      {'activationState':'unknown'},{'foregroundProcessId':0}]:
            with self.assertRaises(ValueError):module.activation({**source,**patch})
        with self.assertRaises(ValueError):module.compare((*pair(),'inactive'),(*pair(),'active'),8)
        self.assertEqual(module.compare((*pair(),'inactive'),(*pair(),'inactive'),8)['activationState'],'inactive')

    def test_同图通过但不等于全阴影(self):
        result=module.compare(pair(),pair(),8)
        self.assertTrue(result['allExternalCornersPassed'])
        self.assertFalse(result['fullShadowVerified'])
        self.assertEqual(result['corners']['topLeft']['totalPixels'],192)

    def test_四角外侧缺陷均能检出且内侧排除(self):
        for point,key in [((11,11),'topLeft'),((36,11),'topRight'),((11,36),'bottomLeft'),((36,36),'bottomRight')]:
            actual=pair();actual[0].putpixel(point,(251,251,251))
            self.assertFalse(module.compare(actual,pair(),8)['corners'][key]['passed'])
        actual=pair();actual[0].putpixel((12,12),(0,0,0))
        self.assertTrue(module.compare(actual,pair(),8)['allExternalCornersPassed'])

    def test_背景不一致不可筛选后判通过(self):
        actual=pair();actual[0].putpixel((11,11),(254,254,254));actual[1].putpixel((11,11),(254,254,254))
        result=module.compare(actual,pair(),8)['corners']['topLeft']
        self.assertEqual(result['maxDifferenceAll'],0)
        self.assertEqual(result['unmatchedBackgroundPixels'],1)
        self.assertFalse(result['passed'])

    def test_容差边界与输入拒绝(self):
        actual=pair();actual[0].putpixel((11,11),(252,252,252))
        self.assertTrue(module.compare(actual,pair(),8)['allExternalCornersPassed'])
        for radius,tolerance in [(0,3),(13,3),(8,float('nan'))]:
            with self.assertRaises(ValueError):module.compare(pair(),pair(),radius,tolerance)
        with self.assertRaises(ValueError):module.compare((*pair()[:3],1),pair(),8)

    def test_来源校验拒绝失效哈希和缺稳定性(self):
        with tempfile.TemporaryDirectory(prefix='Augit-shadow-corner-test-') as directory:
            paths=[Path(directory)/name for name in ('front.png','back.png')]
            sources=[]
            for path in paths:
                Image.new('RGB',(48,48),'white').save(path)
                source={'capture':'screen','method':'System.Drawing.Graphics.CopyFromScreen',
                        'resampled':False,'foregroundBeforeAndAfter':True,'stableProcessFrameAndDpi':True,
                        'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'imageSize':[48,48],
                        'regionPhysical':[0,0,48,48],'scale':1.75,'processId':1,'frameRectInImage':[12,12,36,36]}
                sources.append(source)
                Path(str(path)+'.json').write_text(json.dumps(source),encoding='utf-8')
            self.assertEqual(module.load_pair(*paths)[3],1.75)
            for field,value in [('sha256','invalid'),('stableProcessFrameAndDpi',None),('processId',2),('method','jpeg-conversion')]:
                changed={**sources[1],field:value}
                Path(str(paths[1])+'.json').write_text(json.dumps(changed),encoding='utf-8')
                with self.subTest(field=field),self.assertRaises(ValueError):module.load_pair(*paths)

if __name__ == '__main__':unittest.main()
