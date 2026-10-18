# 设备像素一对一截取源图作诊断，不缩放图像，不作为双端同态配对。
import json
import sys
from PIL import Image, ImageChops

with Image.open(sys.argv[1]) as source, Image.open(sys.argv[2]) as actual:
    x, y = map(int, sys.argv[3:5])
    if x < 0 or y < 0 or x + actual.width > source.width or y + actual.height > source.height:
        raise ValueError('诊断截图越过源图范围')
    expected = source.convert('RGB').crop((x, y, x + actual.width, y + actual.height))
    delta = ImageChops.difference(expected, actual.convert('RGB'))
    maximum = max(high for low, high in delta.getextrema())
    channels = delta.split()
    mask = ImageChops.lighter(ImageChops.lighter(channels[0], channels[1]), channels[2])
    histogram = mask.histogram()
    print(json.dumps({'changedPixels':sum(histogram[1:]), 'overTolerancePixels':sum(histogram[3:]),
                      'maximumChannelDifference':maximum, 'changedBounds':mask.getbbox()}))
