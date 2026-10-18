# 验证每个像素恰属一个分区，四角与边界不遗漏。
import unittest
from collections import Counter
from refresh_frame_regions import classify_pixel, count_mask_regions, summarize_regions
from PIL import Image

class RegionTests(unittest.TestCase):
    def test_完整分区不重叠不遗漏(self):
        counts=Counter(classify_pixel(x,y,100,80) for y in range(80) for x in range(100))
        self.assertEqual(counts['corners16'],4*16*16)
        self.assertEqual(counts['interior'],68*48)
        self.assertEqual(sum(counts.values()),8000)
    def test_排他边界(self):
        self.assertEqual(classify_pixel(15,15,100,80),'corners16')
        self.assertEqual(classify_pixel(16,15,100,80),'edge16OutsideCorners')
        self.assertEqual(classify_pixel(16,16,100,80),'interior')
        self.assertEqual(classify_pixel(84,64,100,80),'corners16')
    def test_拒绝越界及过小图像(self):
        for args in [(-1,0,100,80),(100,0,100,80),(0,80,100,80),(0,0,20,20)]:
            with self.assertRaises(ValueError):classify_pixel(*args)
    def test_掩码计数与逐像素归属完全一致(self):
        for width,height in [(32,32),(33,48),(100,80)]:
            mask=Image.new('L',(width,height))
            expected=Counter(corners16=0,edge16OutsideCorners=0,interior=0)
            for y in range(height):
                for x in range(width):
                    if (x*13+y*7)%5==0:
                        mask.putpixel((x,y),255)
                        expected[classify_pixel(x,y,width,height)]+=1
            self.assertEqual(count_mask_regions(mask),dict(expected))
            self.assertEqual(sum(expected.values()),mask.histogram()[255])
    def test_拒绝非二值掩码(self):
        for mask in [Image.new('RGB',(40,40)),Image.new('L',(31,40)),Image.new('L',(40,40),3)]:
            with self.assertRaises(ValueError):count_mask_regions(mask)
    def test_连续分段保留缺帧及分区独立性(self):
        rows=[{'frame':frame,'seconds':frame/60,'regions':{'corners16':corner,'edge16OutsideCorners':edge,'interior':0}}
              for frame,corner,edge in [(3,2,0),(4,5,9),(6,2,9),(7,0,3)]]
        result=summarize_regions(rows)
        self.assertEqual([(r['firstFrame'],r['lastFrame'],r['maximumChangedPixels']) for r in result['corners16']['runs']],[(3,4,5),(6,6,2)])
        self.assertEqual([(r['firstFrame'],r['lastFrame']) for r in result['edge16OutsideCorners']['runs']],[(4,4),(6,7)])
        self.assertEqual(result['interior'],{'changedFrames':0,'runs':[]})
        self.assertTrue(all(not value['runs'] for value in summarize_regions([]).values()))

if __name__=='__main__':unittest.main()
