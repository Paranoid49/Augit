"""检查桌面无损录像中的三处探针；不把采样无异常等同于没有漏帧。"""
import argparse
import json
import subprocess
from pathlib import Path


def inspect_pixels(raw, colors, tolerance=2):
    if len(raw) != 15 * 5 * 3:
        raise ValueError('探针帧必须为15×5 RGB像素')
    maxima = []
    for index, color in enumerate(colors):
        target = bytes.fromhex(color.lstrip('#'))
        if len(target) != 3:
            raise ValueError('颜色必须为RGB')
        maxima.append(max(abs(raw[(y*15+index*5+x)*3+c]-target[c])
                          for y in range(5) for x in range(5) for c in range(3)))
    return {'maximumChannelDifferences': maxima, 'passed': all(value <= tolerance for value in maxima)}


def timing_summary(times):
    if len(times) < 2 or any(b < a for a, b in zip(times, times[1:])):
        raise ValueError('必须具有至少两个不倒退的实际帧时间戳')
    gaps = [(b-a)*1000 for a, b in zip(times, times[1:])]
    return {'frames': len(times), 'durationSeconds': times[-1]-times[0],
            'duplicateTimestamps': sum(gap == 0 for gap in gaps),
            'strictTimingPassed': all(gap > 0 for gap in gaps),
            'maximumGapMilliseconds': max(gaps),
            'gapsAbove25Milliseconds': sum(gap > 25 for gap in gaps)}


def verify(folder):
    folder = Path(folder)
    manifest = json.loads((folder/'refresh-screen.json').read_text(encoding='utf-8'))
    movie = str(folder/'refresh-screen.mkv')
    result = subprocess.run(['ffprobe','-v','error','-select_streams','v:0','-show_frames',
        '-show_entries','frame=best_effort_timestamp_time','-of','json',movie],
        capture_output=True, check=True)
    times = [float(frame['best_effort_timestamp_time']) for frame in json.loads(result.stdout)['frames']]
    filters = ['[0:v]split=3[p0][p1][p2]']
    for index, probe in enumerate(manifest['probes']):
        filters.append(f'[p{index}]crop=5:5:{probe["x"]-2}:{probe["y"]-2}[c{index}]')
    filters.append('[c0][c1][c2]hstack=inputs=3,format=rgb24[out]')
    decoded = subprocess.run(['ffmpeg','-v','error','-i',movie,'-filter_complex',';'.join(filters),
        '-map','[out]','-fps_mode','passthrough','-f','rawvideo','pipe:1'],capture_output=True,check=True).stdout
    frame_size = 225
    if len(decoded) != len(times)*frame_size:
        raise ValueError('解码帧数与时间戳不一致')
    failures = []
    for index, time in enumerate(times):
        sampled = inspect_pixels(decoded[index*frame_size:(index+1)*frame_size], [p['color'] for p in manifest['probes']])
        if not sampled['passed']:
            failures.append({'frame':index,'seconds':time,**sampled})
    report = {**timing_summary(times),'probeFailureFrames':len(failures),'failures':failures,
              'sampledProbeFramesPassed':not failures,'fullPresentationVerified':False,
              'limitation':'仅检查三处5×5探针；GDI采样可能漏掉显示帧，不能证明整个表单从无透明帧。'}
    (folder/'refresh-video-analysis.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
    return report


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder')
    args=parser.parse_args()
    report=verify(args.folder)
    print(json.dumps({key:value for key,value in report.items() if key not in ('failures','limitation')}))
