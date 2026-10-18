"""逐采样帧比较整张模态表面；保留光标和边框差异，不以三处探针代表全表单。"""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

from PIL import Image, ImageChops
from refresh_frame_regions import count_mask_regions, summarize_regions


def difference(reference, current, tolerance=2):
    if reference.size != current.size or reference.mode != 'RGB' or current.mode != 'RGB':
        raise ValueError('两帧必须为同尺寸RGB图像')
    if type(tolerance) is not int or not 0 <= tolerance <= 255:
        raise ValueError('容差须为0至255的整数')
    channels = ImageChops.difference(reference, current).split()
    maximum = ImageChops.lighter(ImageChops.lighter(channels[0], channels[1]), channels[2])
    mask = maximum.point([255 if value > tolerance else 0 for value in range(256)])
    count = mask.histogram()[255]
    result = {'changedPixels': count, 'maximumChannelDifference': maximum.getextrema()[1],
              'changedBounds': mask.getbbox(), 'passed': count == 0}
    if min(mask.size) >= 32:
        result['regions'] = count_mask_regions(mask)
        if sum(result['regions'].values()) != count:
            raise ValueError('诊断分区与整表面计数不符')
    return result


def read_frame(stream, size):
    chunks = bytearray()
    while len(chunks) < size:
        block = stream.read(size-len(chunks))
        if not block:
            if chunks:
                raise ValueError('解码帧截断')
            return None
        chunks.extend(block)
    return bytes(chunks)


def validate_source(source):
    # 历史缺失记录仍可分析，但已知前景受干扰的录像不能归为产品闪帧。
    evidence = source.get('foregroundEvidence')
    if evidence is not None and (not isinstance(evidence, dict) or evidence.get('qualified') is not True):
        raise ValueError('录像期间前景证据未通过，禁止作为产品整表面验收')
    method = source.get('method')
    if method == 'ffmpeg-gdigrab-desktop-ffv1':
        return
    if method != 'ffmpeg-ddagrab-desktop-ffv1':
        raise ValueError('只接受已登记的原始无损系统录像')
    args = source.get('arguments', [])
    l, t, r, b = source['frameRectPhysical']
    repeat = source.get('repeatStaticFrames', False)
    if type(repeat) is not bool:
        raise ValueError('静止重复帧标记必须为布尔值')
    expected = (f'ddagrab=output_idx=0:draw_mouse=0:framerate=60:dup_frames={int(repeat)}:output_fmt=bgra:'
                f'offset_x={l}:offset_y={t}:video_size={r-l}x{b-t},hwdownload,format=bgra')
    if (not all(type(v) is int for v in (l,t,r,b)) or l < 0 or t < 0 or r <= l or b <= t
            or (source['width'],source['height']) != (r-l,b-t)
            or '-i' not in args or args.index('-i')+1 >= len(args) or args[args.index('-i')+1] != expected):
        raise ValueError('桌面复制采集坐标或原始像素路径不符')


def verify(folder):
    folder = Path(folder)
    source = json.loads((folder/'refresh-screen.json').read_text(encoding='utf-8'))
    validate_source(source)
    movie = folder/'refresh-screen.mkv'
    probe = subprocess.run(['ffprobe','-v','error','-select_streams','v:0',
        '-show_streams','-show_frames','-show_entries',
        'stream=codec_name,width,height:frame=best_effort_timestamp_time','-of','json',str(movie)],
        capture_output=True,check=True)
    metadata = json.loads(probe.stdout)
    stream = metadata['streams'][0]
    width,height = source['width'],source['height']
    if stream['codec_name'] != 'ffv1' or (stream['width'],stream['height']) != (width,height):
        raise ValueError('录像编码或原始尺寸与清单不同')
    times = [float(frame['best_effort_timestamp_time']) for frame in metadata['frames']]
    if len(times) < 2 or any(b < a for a,b in zip(times,times[1:])):
        raise ValueError('时间戳不足或倒退')
    # 首帧须由采集驱动在变化前取得；不寻找最有利帧，不排除光标或边缘。
    decoder = subprocess.Popen(['ffmpeg','-v','error','-i',str(movie),'-map','0:v:0',
        '-fps_mode','passthrough','-pix_fmt','rgb24','-f','rawvideo','pipe:1'],stdout=subprocess.PIPE)
    baseline = None
    failures = []
    index = 0
    try:
        while (raw := read_frame(decoder.stdout,width*height*3)) is not None:
            if index >= len(times):
                raise ValueError('解码帧多于时间戳')
            current = Image.frombytes('RGB',(width,height),raw)
            if baseline is None:
                baseline = current.copy()
            result = difference(baseline,current)
            if not result['passed']:
                failures.append({'frame':index,'seconds':times[index],**result})
            current.close()
            index += 1
        if decoder.wait() != 0 or index != len(times):
            raise ValueError('解码失败或帧数不符')
    finally:
        if decoder.poll() is None:
            decoder.kill()
        decoder.wait()
        decoder.stdout.close()
        if baseline is not None:
            baseline.close()
    gaps = [(b-a)*1000 for a,b in zip(times,times[1:])]
    with movie.open('rb') as handle:
        digest = hashlib.file_digest(handle,'sha256').hexdigest()
    report = {'captureMethod':source['method'],'videoSha256':digest,'frames':index,'pixelsPerFrame':width*height,
        'referenceFrame':0,'toleranceRgb':2,'excludedPixels':0,
        'changedFrames':len(failures),'failures':failures,
        'diagnosticRegions':summarize_regions(failures) if min(width,height)>=32 else None,
        'maximumGapMilliseconds':max(gaps),'duplicateTimestamps':sum(gap==0 for gap in gaps),
        'sampledWholeSurfacePassed':not failures,'fullPresentationVerified':False,
        'limitation':'仅比较已采样帧与首帧；动画、光标、边框或遮挡变化均报告。GDI仍可能漏显示帧，静态相同也不能证明完整呈现。'}
    (folder/'refresh-surface-analysis.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
    return report


def main(arguments=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder')
    parser.add_argument('--require-stable', action='store_true', help='任一采样帧变化即返回失败，用于真实外壳回归')
    options = parser.parse_args(arguments)
    result = verify(options.folder)
    print(json.dumps({key:value for key,value in result.items() if key not in ('failures','limitation','diagnosticRegions')}))
    return 1 if options.require_stable and not result['sampledWholeSurfacePassed'] else 0


if __name__ == '__main__':
    raise SystemExit(main())
