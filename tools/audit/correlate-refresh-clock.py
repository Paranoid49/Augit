"""关联同机录屏原始时钟与桥接时间；不把采样时刻当作Present或根因。"""
import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path


def frame_clocks(log, video_times, duration=None):
    # 输入微秒时间戳经归零、滤镜和编码量化；逐项核对后才按输出序号关联。
    source = [int(v) for v in re.findall(r'demuxer -> .*?pkt_pts:([0-9]+)', log)]
    normalized = [int(v) for v in re.findall(r'demuxer[+]tsfixup -> .*?pkt_pts:([0-9]+)', log)]
    filtered = [int(v) for v in re.findall(r'filter_raw -> pts:([0-9]+)', log)]
    encoded = [(int(p), int(n), int(d)) for p,n,d in re.findall(
        r'encoder <- type:video frame_pts:([0-9]+).*?time_base:([0-9]+)/([0-9]+)', log)]
    muxed = [float(v) for v in re.findall(r'muxer <- pts:[0-9]+ pts_time:([0-9.]+)', log)]
    if not source or len(source) != len(normalized):
        raise ValueError('输入时钟记录不完整')
    if any(b <= a for a,b in zip(source,source[1:])):
        raise ValueError('输入时钟重复或倒退')
    if len({a-b for a,b in zip(source,normalized)}) != 1:
        raise ValueError('输入时钟偏移发生变化')
    if not video_times or not (len(encoded) == len(muxed) == len(video_times)) or len(filtered) < len(encoded):
        raise ValueError('滤镜、编码、封装和录像帧数不一致')
    if len(filtered) > len(encoded):
        # -t截止前读取的末帧可能量化到截止点；只允许这种已证实的尾部裁剪。
        num,den=encoded[-1][1:]
        if duration is None or num <= 0 or den <= 0 or any(
                int(raw/1000000*den/num+0.5)*num/den < duration for raw in filtered[len(encoded):]):
            raise ValueError('未编码滤镜帧不在录制截止边界')
        filtered=filtered[:len(encoded)]
    lookup = dict(zip(normalized,source))
    if len(set(filtered)) != len(filtered) or any(v not in lookup for v in filtered):
        raise ValueError('滤镜帧不能唯一关联输入')
    if any(b <= a for a,b in zip(filtered,filtered[1:])):
        raise ValueError('滤镜时间戳倒退')
    for raw,(pts,num,den),mux,video in zip(filtered,encoded,muxed,video_times):
        if num <= 0 or den <= 0 or abs(pts*num/den-raw/1000000) > num/den/2+0.000001:
            raise ValueError('编码时间戳超出半刻度量化范围')
        if abs(mux-pts*num/den) > 0.001 or abs(mux-video) > 0.001:
            raise ValueError('封装或录像时间戳不匹配')
    return [lookup[v]/1000 for v in filtered]


def correlate(clocks, events, failures):
    phases = []
    for event in events:
        anchors=[]
        for phase in event['bridgePhases']:
            start,end = phase['startedUtcMilliseconds'],phase['endedUtcMilliseconds']
            if not isinstance(start,(int,float)) or not isinstance(end,(int,float)) or end < start:
                raise ValueError('桥接时钟无效')
            if abs((end-start)-phase['roundTripMilliseconds']) > 3:
                raise ValueError('墙钟与单调时钟不一致')
            anchors.extend([start-phase['startAfterMutation'],end-phase['endAfterMutation']])
            phases.append({**phase,'sample':event['sample']})
        if anchors and max(anchors)-min(anchors) > 3:
            raise ValueError('同轮桥接阶段间发生墙钟漂移')
    if not phases:
        raise ValueError('缺少桥接事件')
    result=[]
    for failure in failures:
        index=failure['frame']
        if index < 0 or index >= len(clocks):
            raise ValueError('失败帧编号越界')
        stamp=clocks[index]
        nearest=min(phases,key=lambda p:min(abs(stamp-p['startedUtcMilliseconds']),abs(stamp-p['endedUtcMilliseconds'])))
        result.append({**failure,'captureUtcMilliseconds':stamp,
            'nearestPhase':{'sample':nearest['sample'],'method':nearest['method'],
                'millisecondsAfterRequest':round(stamp-nearest['startedUtcMilliseconds'],3),
                'millisecondsAfterResponse':round(stamp-nearest['endedUtcMilliseconds'],3)},
            'overlappingBridgePhases':[{'sample':p['sample'],'method':p['method']} for p in phases
                if p['startedUtcMilliseconds'] <= stamp <= p['endedUtcMilliseconds']]})
    return result


def require_gdi_clock(manifest):
    if manifest.get('method') != 'ffmpeg-gdigrab-desktop-ffv1':
        raise ValueError('此关联器仅支持已验证的GDI墙钟；桌面复制相对时间不可冒充UTC')


def verify(folder):
    folder=Path(folder)
    manifest=json.loads((folder/'refresh-screen.json').read_text(encoding='utf-8'))
    require_gdi_clock(manifest)
    surface=json.loads((folder/'refresh-surface-analysis.json').read_text(encoding='utf-8'))
    movie=folder/'refresh-screen.mkv'
    with movie.open('rb') as handle:
        digest=hashlib.file_digest(handle,'sha256').hexdigest()
    if digest != surface['videoSha256']:
        raise ValueError('整表面分析来自其他录像')
    probe=subprocess.run(['ffprobe','-v','error','-select_streams','v:0','-show_frames',
        '-show_entries','frame=best_effort_timestamp_time','-of','json',str(movie)],capture_output=True,check=True)
    times=[float(f['best_effort_timestamp_time']) for f in json.loads(probe.stdout)['frames']]
    if len(times) != surface['frames']:
        raise ValueError('整表面分析帧数不匹配')
    arguments=manifest['arguments']
    duration=float(arguments[arguments.index('-t')+1])
    clocks=frame_clocks((folder/'refresh-screen.log').read_text(encoding='utf-8'),times,duration)
    failures=correlate(clocks,manifest['events'],surface['failures'])
    report={'videoSha256':digest,'frames':len(clocks),'changedFrames':len(failures),'failures':failures,
        'fullPresentationVerified':False,
        'limitation':'同机墙钟关联；检查桥接墙钟/单调时钟漂移。GDI采样、毫秒墙钟精度及异步合成仍有限制，不能据时间先后断定根因或证明全部Present。'}
    (folder/'refresh-clock-analysis.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+chr(10),encoding='utf-8',newline=chr(10))
    return report


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder')
    report=verify(parser.parse_args().folder)
    print(json.dumps({k:v for k,v in report.items() if k not in ('failures','limitation')}))
