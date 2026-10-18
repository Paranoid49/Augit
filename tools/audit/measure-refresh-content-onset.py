"""从正常入口全窗录像中量测首个可见新内容帧；不把撤盖应答当作屏幕内容时刻。

口径：
- 只分析指定裁切区域（默认编辑正文左侧，避开 Go to Line 模态和底部工具窗）；
- 以写入前最后一帧为旧内容、以本轮静置帧为新内容；
- “首个变化帧”指相对旧内容变化像素超过阈值的第一帧；
- “首个新内容帧”指相对静置新内容差异像素不超过阈值的第一帧；
- 两者使用同一条 GDI 墙钟和事件写入时间，保留录屏负载影响，不宣称全窗无闪。
"""
import argparse
import hashlib
import importlib.util
import json
import subprocess
from pathlib import Path

import numpy as np
from PIL import Image

DEFAULT_CROP = (760, 200, 380, 500)
DEFAULT_TOLERANCE = 16
DEFAULT_CHANGE_THRESHOLD = 100
DEFAULT_FINAL_THRESHOLD = 0


def load_clock_module():
    spec = importlib.util.spec_from_file_location(
        'refresh_clock', Path(__file__).with_name('correlate-refresh-clock.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def validate_crop(crop, width, height):
    x, y, w, h = crop
    if len(crop) != 4 or min(crop) < 0:
        raise ValueError('裁切区域必须是非负四元组')
    if w == 0 or h == 0 or x + w > width or y + h > height:
        raise ValueError('裁切区域越界')


def onset_indices(frames, pre, final, start_index, end_index,
                  tolerance=DEFAULT_TOLERANCE, change_threshold=DEFAULT_CHANGE_THRESHOLD,
                  final_threshold=DEFAULT_FINAL_THRESHOLD):
    """返回首个变化帧和首个新内容帧；索引失败返回 None。"""
    if not (0 <= start_index <= end_index < len(frames)):
        raise ValueError('帧范围越界')
    first_change = None
    first_final = None
    pre = pre.astype(np.int16)
    final = final.astype(np.int16)
    for index in range(start_index, end_index + 1):
        frame = frames[index].astype(np.int16)
        if first_change is None:
            changed = np.abs(frame - pre)
            if int((changed > tolerance).sum()) > change_threshold:
                first_change = index
        if first_final is None:
            changed = np.abs(frame - final)
            if int((changed > tolerance).sum()) <= final_threshold:
                first_final = index
        if first_change is not None and first_final is not None:
            break
    return first_change, first_final


def decode_frames(movie, crop):
    x, y, w, h = crop
    decoder = subprocess.Popen(
        ['ffmpeg', '-v', 'error', '-i', str(movie), '-vf', f'crop={w}:{h}:{x}:{y}',
         '-fps_mode', 'passthrough', '-pix_fmt', 'gray', '-f', 'rawvideo', 'pipe:1'],
        stdout=subprocess.PIPE)
    size = w * h
    frames = []
    try:
        while True:
            raw = decoder.stdout.read(size)
            if len(raw) < size:
                break
            frames.append(np.frombuffer(raw, dtype=np.uint8).reshape(h, w).copy())
    finally:
        decoder.stdout.close()
        decoder.wait()
    return frames


def measure(folder, crop=DEFAULT_CROP, tolerance=DEFAULT_TOLERANCE,
            change_threshold=DEFAULT_CHANGE_THRESHOLD,
            final_threshold=DEFAULT_FINAL_THRESHOLD, save_frames=True):
    folder = Path(folder)
    manifest = json.loads((folder / 'recording.json').read_text(encoding='utf-8'))
    events = json.loads((folder / 'events.json').read_text(encoding='utf-8'))
    if not events:
        raise ValueError('没有刷新轮次')
    width, height = manifest['source']['imageSize']
    validate_crop(crop, width, height)
    movie = folder / 'refresh-screen.mkv'
    digest = hashlib.sha256(movie.read_bytes()).hexdigest()
    probe = subprocess.run(
        ['ffprobe', '-v', 'error', '-select_streams', 'v:0', '-show_frames',
         '-show_entries', 'frame=best_effort_timestamp_time', '-of', 'json', str(movie)],
        capture_output=True, check=True)
    video_times = [float(frame['best_effort_timestamp_time'])
                   for frame in json.loads(probe.stdout)['frames']]
    arguments = manifest['args']
    duration = float(arguments[arguments.index('-t') + 1])
    clock = load_clock_module()
    clocks = clock.frame_clocks((folder / 'recording.log').read_text(encoding='utf-8'),
                                video_times, duration)
    frames = decode_frames(movie, crop)
    if len(frames) != len(clocks):
        raise ValueError(f'解码帧数与时间戳不一致：{len(frames)} != {len(clocks)}')
    rounds = []
    for event in events:
        started = event['started']
        settled = event['settledObservedMilliseconds']
        pre_index = max(index for index, stamp in enumerate(clocks) if stamp <= started)
        final_index = max(index for index, stamp in enumerate(clocks)
                          if stamp <= started + settled)
        pre = frames[pre_index]
        final = frames[final_index]
        pre_vs_final = int((np.abs(pre.astype(np.int16) - final.astype(np.int16)) > tolerance).sum())
        if pre_vs_final <= change_threshold:
            raise ValueError('本轮新旧内容在裁切区域内不可区分')
        first_change, first_new = onset_indices(
            frames, pre, final, pre_index, min(len(frames) - 1, final_index + 15),
            tolerance, change_threshold, final_threshold)
        if first_new is None:
            raise ValueError('未找到新内容帧')
        if save_frames:
            Image.fromarray(pre).save(folder / f'content-onset-{event["generation"]}-pre.png')
            Image.fromarray(final).save(folder / f'content-onset-{event["generation"]}-final.png')
            Image.fromarray(frames[first_new]).save(folder / f'content-onset-{event["generation"]}-first.png')
        rounds.append({
            'round': event['generation'],
            'strictPresentationMilliseconds': round(event['presentationCompletion']['milliseconds'], 3),
            'preFrame': {'index': pre_index, 'utcMilliseconds': clocks[pre_index]},
            'finalFrame': {'index': final_index, 'utcMilliseconds': clocks[final_index]},
            'firstChangeFrame': None if first_change is None else {
                'index': first_change,
                'millisecondsAfterWrite': round(clocks[first_change] - started, 3)
            },
            'firstNewContentFrame': {
                'index': first_new,
                'millisecondsAfterWrite': round(clocks[first_new] - started, 3),
                'pixelsDifferentFromFinal': int((np.abs(
                    frames[first_new].astype(np.int16) - final.astype(np.int16)) > tolerance).sum())
            },
            'oldVsFinalChangedPixels': pre_vs_final,
            'recordedAfterStrictPresentationMilliseconds': round(
                clocks[first_new] - (started + event['presentationCompletion']['milliseconds']), 3)
        })
    result = {
        'videoSha256': digest,
        'crop': {'x': crop[0], 'y': crop[1], 'width': crop[2], 'height': crop[3]},
        'toleranceRgb': tolerance,
        'changeThresholdPixels': change_threshold,
        'finalDifferentPixels': final_threshold,
        'rounds': rounds,
        'fullPresentationVerified': False,
        'limitation': '首个新内容帧是录像裁切区域内的屏幕可见证据；GDI 录屏负载会抬高首轮，裁切区域外和未采样帧不在结论内。'
    }
    with (folder / 'refresh-content-onset.json').open('w', encoding='utf-8', newline='\n') as handle:
        json.dump(result, handle, ensure_ascii=False, indent=2)
        handle.write('\n')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder')
    parser.add_argument('--crop', default=','.join(str(value) for value in DEFAULT_CROP),
                        help='x,y,width,height；默认编辑正文左侧、避开模态')
    parser.add_argument('--tolerance', type=int, default=DEFAULT_TOLERANCE)
    parser.add_argument('--change-threshold', type=int, default=DEFAULT_CHANGE_THRESHOLD)
    parser.add_argument('--final-threshold', type=int, default=DEFAULT_FINAL_THRESHOLD)
    options = parser.parse_args()
    crop = tuple(int(value) for value in options.crop.split(','))
    if len(crop) != 4:
        raise SystemExit('裁切区域需要四个整数')
    report = measure(Path(options.folder), crop, options.tolerance,
                     options.change_threshold, options.final_threshold)
    print(json.dumps({
        'videoSha256': report['videoSha256'],
        'rounds': [{
            'round': row['round'],
            'strictPresentationMilliseconds': row['strictPresentationMilliseconds'],
            'firstChangeMilliseconds': row['firstChangeFrame'] and row['firstChangeFrame']['millisecondsAfterWrite'],
            'firstNewContentMilliseconds': row['firstNewContentFrame']['millisecondsAfterWrite']
        } for row in report['rounds']]
    }, ensure_ascii=False))
