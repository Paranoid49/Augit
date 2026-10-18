"""扫描设置开关录像中的近白整帧候选；不替代逐帧同态验收。"""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

import numpy as np


def read_exact(stream, size):
    chunks = bytearray()
    while len(chunks) < size:
        block = stream.read(size - len(chunks))
        if not block:
            return None
        chunks.extend(block)
    return bytes(chunks)


def verify(folder, threshold, output_name='white-frame-scan.json'):
    folder = Path(folder)
    manifest = json.loads((folder / 'cycle-screen.json').read_text(encoding='utf-8'))
    width, height = manifest['width'], manifest['height']
    movie = folder / 'cycle-screen.mkv'
    probe = subprocess.run(
        ['ffprobe', '-v', 'error', '-select_streams', 'v:0', '-show_frames',
         '-show_entries', 'frame=best_effort_timestamp_time', '-of', 'json', str(movie)],
        capture_output=True, check=True)
    times = [float(frame['best_effort_timestamp_time']) for frame in json.loads(probe.stdout)['frames']]
    decoder = subprocess.Popen(
        ['ffmpeg', '-v', 'error', '-i', str(movie), '-fps_mode', 'passthrough',
         '-pix_fmt', 'rgb24', '-f', 'rawvideo', 'pipe:1'], stdout=subprocess.PIPE)
    frame_size = width * height * 3
    rows = []
    index = 0
    maximum = -1
    try:
        while True:
            raw = read_exact(decoder.stdout, frame_size)
            if raw is None:
                break
            if index >= len(times):
                raise ValueError('解码帧多于时间戳')
            frame = np.frombuffer(raw, dtype=np.uint8).reshape(height, width, 3)
            near_white = int(np.all(frame >= 250, axis=2).sum())
            if near_white >= threshold:
                rows.append({'frame': index, 'seconds': times[index], 'nearWhitePixels': near_white})
            maximum = max(maximum, near_white)
            index += 1
    finally:
        decoder.stdout.close()
        decoder.wait()
    if index != len(times):
        raise ValueError('解码帧数与时间戳不一致')
    report = {
        'videoSha256': hashlib.sha256(movie.read_bytes()).hexdigest(),
        'frames': index,
        'width': width,
        'height': height,
        'pixelsPerFrame': width * height,
        'nearWhiteThreshold': threshold,
        'maximumNearWhitePixels': maximum,
        'framesAtOrAboveThreshold': len(rows),
        'candidates': rows,
        'limitation': '只统计近白像素，不区分合法浅色页面；整帧白闪需结合候选帧目视和逐帧同态复核。',
    }
    target = folder / output_name
    with target.open('x', encoding='utf-8', newline='\n') as handle:
        json.dump(report, handle, ensure_ascii=False, indent=2)
        handle.write('\n')
    return {key: value for key, value in report.items() if key != 'candidates'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder')
    parser.add_argument('--threshold', type=int, default=4500000)
    parser.add_argument('--output', default='white-frame-scan.json')
    args = parser.parse_args()
    print(json.dumps(verify(args.folder, args.threshold, args.output), ensure_ascii=False))
