"""完整开关录像逐帧诊断；三个已观察状态只定义像素包络，不代替同态参考验收。"""
import argparse
import hashlib
import importlib.util
import json
import subprocess
from pathlib import Path

import numpy as np
from PIL import Image

spec = importlib.util.spec_from_file_location('surface', Path(__file__).with_name('verify-refresh-surface.py'))
surface = importlib.util.module_from_spec(spec)
spec.loader.exec_module(surface)


def envelope_difference(frame, references, tolerance=2):
    if len(references) != 3 or any(r.shape != frame.shape for r in references):
        raise ValueError('需要同尺寸的打开前、已打开、关闭后三份参考')
    if frame.dtype != np.uint8 or any(r.dtype != np.uint8 for r in references):
        raise ValueError('必须使用原始8位RGB')
    if type(tolerance) is not int or not 0 <= tolerance <= 255:
        raise ValueError('容差无效')
    stack = np.stack(references).astype(np.int16)
    low, high = stack.min(axis=0), stack.max(axis=0)
    current = frame.astype(np.int16)
    outside = np.any((current < low-tolerance) | (current > high+tolerance), axis=2)
    return int(outside.sum())


def nearest_state_difference(frame, references, tolerance=2):
    # 必须整帧选择同一参考，不能逐像素挑选不同状态拼出一个不存在的画面。
    if len(references) != 3 or any(r.shape != frame.shape for r in references):
        raise ValueError('需要同尺寸的三个完整参考状态')
    if frame.dtype != np.uint8 or any(r.dtype != np.uint8 for r in references):
        raise ValueError('必须使用原始8位RGB')
    if type(tolerance) is not int or not 0 <= tolerance <= 255:
        raise ValueError('容差无效')
    counts = []
    current = frame.astype(np.int16)
    for reference in references:
        counts.append(int(np.any(np.abs(current-reference.astype(np.int16)) > tolerance, axis=2).sum()))
    nearest = min(range(len(counts)), key=counts.__getitem__)
    return {'nearestState': nearest, 'differentPixels': counts[nearest], 'stateDifferences': counts}


def analyze(folder, open_seconds, whole_state=False, output_directory=None):
    folder = Path(folder)
    output = Path(output_directory) if output_directory else folder
    output.mkdir(parents=True, exist_ok=True)
    filename = 'cycle-whole-state-analysis.json' if whole_state else 'cycle-frame-analysis.json'
    if (output/filename).exists():
        raise FileExistsError('不能覆盖已有逐帧分析证据')
    manifest = json.loads((folder/'cycle-screen.json').read_text(encoding='utf-8'))
    surface.validate_source({**manifest, 'foregroundEvidence': manifest['foreground']})
    movie = folder/'cycle-screen.mkv'
    metadata = json.loads(subprocess.run(['ffprobe','-v','error','-select_streams','v:0','-show_frames',
        '-show_streams','-show_entries','stream=codec_name,width,height:frame=best_effort_timestamp_time',
        '-of','json',str(movie)], capture_output=True, check=True).stdout)
    stream = metadata['streams'][0]
    w,h = manifest['width'],manifest['height']
    if stream['codec_name'] != 'ffv1' or (stream['width'],stream['height']) != (w,h):
        raise ValueError('尺寸或编码不符')
    times = [float(f['best_effort_timestamp_time']) for f in metadata['frames']]
    if len(times)<3 or any(b<=a for a,b in zip(times,times[1:])):
        raise ValueError('时间戳不足或非严格递增')
    if not times[0]<open_seconds<times[-1]:
        raise ValueError('已打开参考不在录像内部')
    middle = min(range(len(times)),key=lambda i:abs(times[i]-open_seconds))
    selected = [0,middle,len(times)-1]
    references=[]
    for index in selected:
        raw = subprocess.run(['ffmpeg','-v','error','-i',str(movie),'-vf',f'select=eq(n\,{index})',
            '-frames:v','1','-pix_fmt','rgb24','-f','rawvideo','pipe:1'], capture_output=True,check=True).stdout
        if len(raw)!=w*h*3:raise ValueError('参考帧不完整')
        references.append(np.frombuffer(raw,dtype=np.uint8).reshape(h,w,3))
        reference_path = output/f'cycle-reference-{index}.png'
        if not reference_path.exists():
            Image.fromarray(references[-1]).save(reference_path)
    low=np.minimum.reduce(references).astype(np.int16)-2
    high=np.maximum.reduce(references).astype(np.int16)+2
    decoder=subprocess.Popen(['ffmpeg','-v','error','-i',str(movie),'-fps_mode','passthrough',
        '-enc_time_base','1:1000000','-pix_fmt','rgb24','-f','rawvideo','pipe:1'],stdout=subprocess.PIPE)
    rows=[];maximum=-1;worst=None;maximum_state=-1;worst_state=None
    try:
        while (raw:=surface.read_frame(decoder.stdout,w*h*3)) is not None:
            frame=np.frombuffer(raw,dtype=np.uint8).reshape(h,w,3)
            out=np.any((frame<low)|(frame>high),axis=2)
            count=int(out.sum())
            y,x=np.nonzero(out)
            index=len(rows)
            rows.append({'frame':index,'seconds':times[index],'outsideEnvelopePixels':count,
                'outsideBounds':None if not count else [int(x.min()),int(y.min()),int(x.max()+1),int(y.max()+1)],
                'nearWhitePixels':int(np.all(frame>=250,axis=2).sum())})
            if whole_state:
                distance = nearest_state_difference(frame, references)
                rows[-1]['wholeState'] = distance
                if distance['differentPixels'] > maximum_state:
                    maximum_state = distance['differentPixels']; worst_state = frame.copy()
            if count>maximum:maximum=count;worst=frame.copy()
        if decoder.wait()!=0 or len(rows)!=len(times):raise ValueError('解码失败或帧数不符')
    finally:
        if decoder.poll() is None:decoder.kill()
        decoder.wait();decoder.stdout.close()
    if whole_state:
        Image.fromarray(worst_state).save(output/'cycle-worst-whole-state.png')
    else:
        Image.fromarray(worst).save(output/'cycle-worst-envelope.png')
    with movie.open('rb') as source:digest=hashlib.file_digest(source,'sha256').hexdigest()
    result={'videoSha256':digest,'width':w,'height':h,'frames':len(rows),'pixelsPerFrame':w*h,
        'excludedPixels':0,'toleranceRgb':2,'referenceFrames':selected,
        'referenceSeconds':[times[i] for i in selected],'maximumGapMilliseconds':max((b-a)*1000 for a,b in zip(times,times[1:])),
        'maximumOutsideEnvelopePixels':maximum,'framesOutsideEnvelope':sum(r['outsideEnvelopePixels']>0 for r in rows),
        'fullPresentationVerified':False,'rows':rows,
        'limitation':'三个完整状态的逐像素颜色包络只能发现异常候选；合法动画、提示及光标也可越界，包络内部仍可能是错误混合。所有原始帧均检查，不用墙钟请求时刻推断实际Present，不以此宣布全场景无闪。'}
    if whole_state:
        result['maximumNearestStateDifferentPixels'] = maximum_state
        result['limitation'] += '整帧距离拒绝逐像素拼接与淡化混合；实际合法悬停、光标、状态栏更新仍计差异，不自动判定产品通过。'
    with (output/filename).open('x', encoding='utf-8', newline=chr(10)) as result_file:
        result_file.write(json.dumps(result,ensure_ascii=False,indent=2)+chr(10))
    return {k:v for k,v in result.items() if k not in ('rows','limitation')}


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder');parser.add_argument('--open-seconds',type=float,required=True)
    parser.add_argument('--whole-state',action='store_true')
    parser.add_argument('--output-directory',help='纠正参考选择时另存分析，保留原始证据')
    args=parser.parse_args();print(json.dumps(analyze(args.folder,args.open_seconds,args.whole_state,args.output_directory)))
