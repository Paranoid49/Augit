"""将正常入口完整帧与表单隐藏区间关联；不把DOM恢复当作桌面呈现。"""
import argparse
import importlib.util
import json
import math
from pathlib import Path

spec = importlib.util.spec_from_file_location('daily_clock', Path(__file__).with_name('correlate-daily-refresh.py'))
daily = importlib.util.module_from_spec(spec)
spec.loader.exec_module(daily)


def hidden_intervals(trace):
    if not trace or trace[0].get('kind') != 'installed':
        raise ValueError('表面记录缺失起点，可能已截断')
    anchors, previous, intervals, pending, cover = [], -math.inf, [], None, None
    for row in trace:
        utc, mono = row.get('utcMilliseconds'), row.get('monotonicMilliseconds')
        if any(type(value) not in (int, float) or not math.isfinite(value) for value in (utc, mono)):
            raise ValueError('表面时钟无效')
        if mono < previous:
            raise ValueError('表面单调时钟倒退')
        previous = mono
        anchors.append(utc-mono)
        if row.get('kind') == 'response' and row.get('method') == 'window/modal-cover':
            native = row.get('native', {})
            if row.get('failed') or not all(native.get(key) is True for key in
                    ('active', 'captureCoverReady', 'captureCoverWindowVisible', 'captureCoverTopmostChild')):
                raise ValueError('遮盖应答未确认就绪、可见及层级')
            cover = row
        if row.get('kind') != 'dom-attribute' or row.get('attribute') != 'data-native-background-capture':
            continue
        if row.get('isHidden') is True:
            if pending is not None or cover is None or row.get('hiddenDialogs') != 1:
                raise ValueError('单层隐藏缺少独立遮盖或发生重入')
            pending = row
        elif row.get('isHidden') is False:
            if pending is None or row.get('hiddenDialogs') != 0:
                raise ValueError('恢复没有对应隐藏')
            intervals.append({'hiddenUtcMilliseconds': pending['utcMilliseconds'],
                              'restoredUtcMilliseconds': utc,
                              'coverResponseUtcMilliseconds': cover['utcMilliseconds']})
            pending, cover = None, None
        else:
            raise ValueError('隐藏状态不是布尔值')
    if pending is not None or not intervals:
        raise ValueError('隐藏区间不完整')
    if max(anchors)-min(anchors) > 3:
        raise ValueError('表面墙钟发生漂移')
    return intervals


def summarize(intervals, frames):
    results = []
    for interval in intervals:
        # 排除边界三毫秒仅用于归属不确定性，不从完整帧报告删除任何像素或帧。
        inside = [row for row in frames if interval['hiddenUtcMilliseconds']+3 < row['captureUtcMilliseconds'] < interval['restoredUtcMilliseconds']-3]
        results.append({**interval, 'unambiguousChangedFrames': len(inside),
                        'worst': max(inside, key=lambda row: row['differentPixels']) if inside else None})
    return results


def validate_trace_requests(trace, requests):
    methods = {'window/modal-cover', 'window/modal-refresh', 'window/modal-uncover'}
    daily.bridge_events(requests)
    for kind, wall, mono in [('request', 'startedUtcMilliseconds', 'started'),
                             ('response', 'endedUtcMilliseconds', 'ended')]:
        actual = [row for row in trace if row.get('kind') == kind and row.get('method') in methods]
        expected = sorted([row for row in requests if row.get('method') in methods], key=lambda row: row[mono])
        if len(actual) != len(expected):
            raise ValueError('表面记录与桥接记录数量不符')
        for left, right in zip(actual, expected):
            if left['method'] != right['method'] or abs(left['utcMilliseconds']-right[wall]) > 3 or abs(left['monotonicMilliseconds']-right[mono]) > 3:
                raise ValueError('表面记录不属于同批桥接时间')


def verify(folder):
    folder = Path(folder)
    # 复用已经严格核验GDI输入/编码/录像及桥接双时钟的结果，不重写已有证据。
    correlated = json.loads((folder/'daily-refresh-clock.json').read_text(encoding='utf-8'))
    surface = json.loads((folder/'full-frame-analysis.json').read_text(encoding='utf-8'))
    import hashlib
    with (folder/'refresh-screen.mkv').open('rb') as handle:
        digest = hashlib.file_digest(handle, 'sha256').hexdigest()
    if correlated['videoSha256'] != digest or surface['videoSha256'] != digest:
        raise ValueError('录像身份不一致')
    trace = json.loads((folder/'surface-trace.json').read_text(encoding='utf-8'))
    intervals = hidden_intervals(trace)
    validate_trace_requests(trace, json.loads((folder/'complete-requests.json').read_text(encoding='utf-8')))
    # 此处只统计已有完整帧报告中发生像素变化的帧；零变化帧不能由缺失记录臆造。
    results = summarize(intervals, correlated['failures'])
    report = {'videoSha256': digest, 'intervals': results, 'fullPresentationVerified': False,
              'limitation': '只关联已变化帧；三毫秒边界标为不确定。原生应答只证明当时状态，不能证明整个区间的合成可见性或根因。'}
    with (folder/'surface-clock-analysis.json').open('x', encoding='utf-8', newline=chr(10)) as handle:
        json.dump(report, handle, ensure_ascii=False, indent=2)
        handle.write(chr(10))
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder')
    print(json.dumps(verify(parser.parse_args().folder), ensure_ascii=False))
