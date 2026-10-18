"""整表面比较的负向测试，不作为实机证据。"""
import importlib.util
import io
from pathlib import Path
import unittest
from unittest.mock import patch
from PIL import Image

spec = importlib.util.spec_from_file_location('surface',Path(__file__).with_name('verify-refresh-surface.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class SurfaceTests(unittest.TestCase):
    def test_前景受干扰的录像拒绝归因为产品闪帧(self):
        source = {'method': 'ffmpeg-gdigrab-desktop-ffv1'}
        for evidence in [{}, {'qualified': False}, {'qualified': 1}, 'true']:
            with self.assertRaises(ValueError):
                module.validate_source({**source, 'foregroundEvidence': evidence})
        module.validate_source({**source, 'foregroundEvidence': {'qualified': True}})

    def test_桌面复制路径必须保留原始像素和坐标(self):
        source={'method':'ffmpeg-ddagrab-desktop-ffv1','width':100,'height':80,'frameRectPhysical':[10,20,110,100],
                'arguments':['-i','ddagrab=output_idx=0:draw_mouse=0:framerate=60:dup_frames=0:output_fmt=bgra:offset_x=10:offset_y=20:video_size=100x80,hwdownload,format=bgra']}
        module.validate_source(source)
        repeated={**source,'repeatStaticFrames':True,'arguments':['-i',source['arguments'][1].replace('dup_frames=0','dup_frames=1')]}
        module.validate_source(repeated)
        with self.assertRaises(ValueError):module.validate_source({**repeated,'repeatStaticFrames':False})
        with self.assertRaises(ValueError):module.validate_source({**repeated,'repeatStaticFrames':1})
        for patch_value in [{'width':99},{'method':'unknown'},{'arguments':['-i']},{'arguments':['-i','ddagrab,scale=100:80']}]:
            with self.assertRaises(ValueError):module.validate_source({**source,**patch_value})

    def test_整图一致与容差边界(self):
        reference = Image.new('RGB',(100,80),(20,30,40))
        self.assertTrue(module.difference(reference,reference)['passed'])
        current = reference.copy()
        current.putpixel((50,40),(22,30,40))
        self.assertTrue(module.difference(reference,current)['passed'])
        current.putpixel((50,40),(23,30,40))
        self.assertEqual(module.difference(reference,current)['changedPixels'],1)

    def test_远离探针的单像素及边界也检出(self):
        reference = Image.new('RGB',(100,80),(20,30,40))
        for point in [(0,0),(99,79),(79,27)]:
            current = reference.copy()
            current.putpixel(point,(255,255,255))
            result = module.difference(reference,current)
            self.assertFalse(result['passed'])
            self.assertEqual(result['changedBounds'],(*point,point[0]+1,point[1]+1))
            self.assertEqual(sum(result['regions'].values()),1)

    def test_不接受尺寸格式容差错误(self):
        frame = Image.new('RGB',(2,2))
        for other in [Image.new('RGB',(3,2)),Image.new('L',(2,2))]:
            with self.assertRaises(ValueError):module.difference(frame,other)
        with self.assertRaises(ValueError):module.difference(frame,frame,-1)

    def test_流截断与完整结束区分(self):
        self.assertEqual(module.read_frame(io.BytesIO(b'abc'),3),b'abc')
        self.assertIsNone(module.read_frame(io.BytesIO(),3))
        with self.assertRaises(ValueError):module.read_frame(io.BytesIO(b'ab'),3)

    def test_严格回归门禁失败返回非零但调查模式保留报告(self):
        for passed in [False, True]:
            with patch.object(module, 'verify', return_value={'sampledWholeSurfacePassed': passed}), patch('sys.stdout', io.StringIO()):
                self.assertEqual(module.main(['unused']), 0)
                self.assertEqual(module.main(['unused', '--require-stable']), 0 if passed else 1)

if __name__ == '__main__':unittest.main()
