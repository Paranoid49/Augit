"""关联正常入口无CDP设置原生阶段与GDI录像；不以原生presented代替画面验收。"""
import bisect
import datetime
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys

spec = importlib.util.spec_from_file_location('refresh_clock', Path(__file__).with_name('correlate-refresh-clock.py'))
clock = importlib.util.module_from_spec(spec)
spec.loader.exec_module(clock)


def correlate(trace, clocks, expected_pid):
    if trace['processId'] != expected_pid or trace['frequency'] <= 0:
        raise ValueError('原生阶段进程或时钟频率不符')
    if not clocks or any(b <= a for a, b in zip(clocks, clocks[1:])):
        raise ValueError('录像时钟缺失或倒退')
    origin = trace['originTimestamp']
    anchor = datetime.datetime.fromisoformat(trace['utc'])
    if anchor.tzinfo is None:
        raise ValueError('原生UTC锚点缺少时区')
    anchor = anchor.timestamp() * 1000
    previous = origin
    rows = []
    for sample in trace['samples']:
        stamp = sample['Timestamp']
        if stamp < previous:
            raise ValueError('原生阶段时钟倒退')
        elapsed = (stamp-origin)*1000/trace['frequency']
        utc = anchor+elapsed
        index = bisect.bisect_left(clocks, utc)
        rows.append({'stage':sample['Stage'], 'elapsedMilliseconds':elapsed,
                     'utcMilliseconds':utc, 'thread':sample['Thread'],
                     'beforeFrame':index-1 if index else None,
                     'afterFrame':index if index < len(clocks) else None,
                     'beforeFrameUtcMilliseconds':clocks[index-1] if index else None,
                     'afterFrameUtcMilliseconds':clocks[index] if index < len(clocks) else None})
        previous = stamp
    if not rows or rows[0]['stage'] != 'created' or rows[-1]['stage'] != 'presented':
        raise ValueError('缺少完整创建至presented阶段')
    if any(row['beforeFrame'] is None or row['afterFrame'] is None for row in rows):
        raise ValueError('录像未覆盖完整原生开窗阶段')
    return rows


def verify(folder):
    folder = Path(folder)
    read = lambda name: json.loads((folder/name).read_text(encoding='utf8'))
    launch, manifest = read('launch.json'), read('cycle-screen.json')
    clock.require_gdi_clock(manifest)
    if launch['remoteDebugging'] or launch['cdpConnections'] != 0:
        raise ValueError('本分析要求无CDP正常入口')
    video = folder/'cycle-screen.mkv'
    probe = subprocess.run(['ffprobe','-v','error','-select_streams','v:0','-show_frames',
        '-show_entries','frame=best_effort_timestamp_time','-of','json',str(video)],capture_output=True,check=True)
    times = [float(frame['best_effort_timestamp_time']) for frame in json.loads(probe.stdout)['frames']]
    args = manifest['arguments']
    clocks = clock.frame_clocks(read('recording-log.json')['log'], times, float(args[args.index('-t')+1]))
    files = list((folder/'native-trace').glob('*.json'))
    if len(files) != 1:
        raise ValueError('需要唯一完整开窗原生阶段文件')
    stages = correlate(json.loads(files[0].read_text(encoding='utf8')), clocks, launch['pid'])
    with video.open('rb') as handle:
        digest = hashlib.file_digest(handle,'sha256').hexdigest()
    report = {'pid':launch['pid'],'videoSha256':digest,'frames':len(clocks),
        'firstFrameUtcMilliseconds':clocks[0],'lastFrameUtcMilliseconds':clocks[-1],
        'maxFrameGapMilliseconds':max(b-a for a,b in zip(clocks,clocks[1:])),
        'foregroundQualified':manifest['foreground']['qualified'],'stages':stages,
        'fullPresentationVerified':False,
        'limitation':'原生UTC锚点存在调用间误差；GDI为采样而非Present，阶段邻帧需目视核对，不证明采样间无闪或字体。'}
    (folder/'settings-clock-analysis.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+chr(10),encoding='utf8',newline=chr(10))
    return report


if __name__ == '__main__':
    result = verify(sys.argv[1])
    print(json.dumps(result,ensure_ascii=False))
