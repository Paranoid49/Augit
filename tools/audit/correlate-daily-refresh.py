"""关联正常入口GDI原始输入时钟与刷新桥接阶段；拒绝相对时钟和不完整证据。"""
import argparse
import hashlib
import importlib.util
import json
import math
import subprocess
from pathlib import Path

spec = importlib.util.spec_from_file_location('refresh_clock', Path(__file__).with_name('correlate-refresh-clock.py'))
clock = importlib.util.module_from_spec(spec)
spec.loader.exec_module(clock)


def bridge_events(requests):
    phases = []
    for request in requests:
        if request['method'] not in ('window/modal-cover', 'window/modal-refresh', 'window/modal-uncover'):
            continue
        required = ('started', 'ended', 'startedUtcMilliseconds', 'endedUtcMilliseconds', 'milliseconds')
        if any(type(request.get(key)) not in (int, float) or not math.isfinite(request[key]) for key in required):
            raise ValueError('模态请求缺少已完成的双时钟记录')
        if request['ended'] < request['started'] or request['milliseconds'] < 0:
            raise ValueError('模态单调时钟倒退')
        phases.append({'method': request['method'],
                       'startedUtcMilliseconds': request['startedUtcMilliseconds'],
                       'endedUtcMilliseconds': request['endedUtcMilliseconds'],
                       'roundTripMilliseconds': request['milliseconds'],
                       'startAfterMutation': request['started'], 'endAfterMutation': request['ended']})
    if not phases:
        raise ValueError('没有模态阶段')
    return [{'sample': 0, 'bridgePhases': phases}]


def verify(folder):
    folder = Path(folder)
    recording = json.loads((folder/'recording.json').read_text(encoding='utf-8'))
    clock.require_gdi_clock(recording)
    if recording['foreground'].get('qualified') is not True:
        raise ValueError('前景身份未通过')
    surface = json.loads((folder/'full-frame-analysis.json').read_text(encoding='utf-8'))
    movie = folder/'refresh-screen.mkv'
    with movie.open('rb') as source:
        digest = hashlib.file_digest(source, 'sha256').hexdigest()
    if digest != surface['videoSha256']:
        raise ValueError('完整帧分析不属于本录像')
    probe = subprocess.run(['ffprobe', '-v', 'error', '-select_streams', 'v:0', '-show_frames',
                            '-show_entries', 'frame=best_effort_timestamp_time', '-of', 'json', str(movie)],
                           capture_output=True, check=True)
    times = [float(frame['best_effort_timestamp_time']) for frame in json.loads(probe.stdout)['frames']]
    if len(times) != surface['frameCount']:
        raise ValueError('完整帧分析帧数不符')
    args = recording['args']
    if '-f' not in args or args[args.index('-f')+1] != 'gdigrab':
        raise ValueError('实际参数不是GDI采集')
    clocks = clock.frame_clocks((folder/'recording.log').read_text(encoding='utf-8'), times, float(args[args.index('-t')+1]))
    requests = json.loads((folder/'complete-requests.json').read_text(encoding='utf-8'))
    failures = clock.correlate(clocks, bridge_events(requests), [row for row in surface['frames'] if row['differentPixels']])
    report = {'videoSha256': digest, 'frameCount': len(clocks), 'failures': failures,
              'worst': max(failures, key=lambda row: row['differentPixels']) if failures else None,
              'fullPresentationVerified': False,
              'limitation': 'GDI输入墙钟与桥接双时钟校验；只证明采样时序，不证明完整Present或根因。'}
    with (folder/'daily-refresh-clock.json').open('x', encoding='utf-8', newline=chr(10)) as output:
        json.dump(report, output, ensure_ascii=False, indent=2)
        output.write(chr(10))
    return {key: value for key, value in report.items() if key != 'failures'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder')
    print(json.dumps(verify(parser.parse_args().folder), ensure_ascii=False))
