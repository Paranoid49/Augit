# 固定首帧与报告指定失败帧比较，分区仅用于定位，全部差异仍计入失败。
import json
import subprocess
import sys
from pathlib import Path
from PIL import Image, ImageChops
from refresh_frame_regions import classify_pixel

folder=Path(sys.argv[1])
report=json.loads((folder/'refresh-surface-analysis.json').read_text(encoding='utf-8'))
manifest=json.loads((folder/'refresh-screen.json').read_text(encoding='utf-8'))
worst=max(report['failures'],key=lambda item:item['changedPixels'])
w,h=manifest['width'],manifest['height']
indices=[0,worst['frame']]
images=[]
for index in indices:
    result=subprocess.run(['ffmpeg','-v','error','-i',str(folder/'refresh-screen.mkv'),'-vf','select=eq(n'+chr(92)+','+str(index)+')','-frames:v','1','-pix_fmt','rgb24','-f','rawvideo','pipe:1'],capture_output=True,check=True)
    if len(result.stdout)!=w*h*3:raise ValueError('指定帧未完整解码')
    images.append(Image.frombytes('RGB',(w,h),result.stdout))
bands=ImageChops.difference(*images).split()
delta=ImageChops.lighter(ImageChops.lighter(bands[0],bands[1]),bands[2])
mask=delta.point([255 if value>report['toleranceRgb'] else 0 for value in range(256)])
groups={'corners16':0,'edge16OutsideCorners':0,'interior':0}
points=[]
for y in range(h):
    for x in range(w):
        if mask.getpixel((x,y)):
            key=classify_pixel(x,y,w,h)
            groups[key]+=1
            if len(points)<150:points.append([x,y,delta.getpixel((x,y))])
if sum(groups.values())!=worst['changedPixels']:raise ValueError('分区计数与整表面报告不符')
result={'frame':worst['frame'],'changedPixels':worst['changedPixels'],'groups':groups,'first150Points':points,'diagnosticOnly':True}
(folder/'worst-frame-location.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+chr(10),encoding='utf-8',newline=chr(10))
images[1].save(folder/('worst-frame-'+str(worst['frame'])+'.png'))
print(json.dumps({key:value for key,value in result.items() if key!='first150Points'}))
