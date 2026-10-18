"""检查刷新录像中除输入框光标外是否出现画面变化；不把光标闪烁误报成透明帧。"""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

import numpy as np
from PIL import Image


def read_exact(stream, size):
    chunks = bytearray()
    while len(chunks) < size:
        block = stream.read(size - len(chunks))
        if not block:
            return None
        chunks.extend(block)
    return bytes(chunks)


def verify(folder, caret):
    folder = Path(folder)
    movie = folder / 'refresh-screen.mkv'
    before = np.asarray(Image.open(folder / 'before.png').convert('RGB'))
    height, width, _ = before.shape
    analysis_path = folder / 'full-frame-analysis.json'
    if analysis_path.exists():
        analysis = json.loads(analysis_path.read_text(encoding='utf-8'))
        with movie.open('rb') as handle:
            digest = hashlib.file_digest(handle, 'sha256').hexdigest()
        if analysis['videoSha256'] != digest:
            raise ValueError('完整帧报告不属于本录像')
    probe = subprocess.run(
        ['ffprobe', '-v', 'error', '-select_streams', 'v:0', '-show_frames',
         '-show_entries', 'frame=best_effort_timestamp_time', '-of', 'json', str(movie)],
        capture_output=True, check=True)
    times = [float(frame['best_effort_timestamp_time']) for frame in json.loads(probe.stdout)['frames']]
    decoder = subprocess.Popen(
        ['ffmpeg', '-v', 'error', '-i', str(movie), '-fps_mode', 'passthrough',
         '-pix_fmt', 'rgb24', '-f', 'rawvideo', 'pipe:1'], stdout=subprocess.PIPE)
    x0, y0, x1, y1 = caret
    if not (0 <= x0 < x1 <= width and 0 <= y0 < y1 <= height):
        raise ValueError('光标排除区越界')
    changed, worst = [], None
    index = 0
    frame_size = width * height * 3
    try:
        while True:
            raw = read_exact(decoder.stdout, frame_size)
            if raw is None:
                break
            if index >= len(times):
                raise ValueError('解码帧多于时间戳')
            frame = np.frombuffer(raw, dtype=np.uint8).reshape(height, width, 3)
            delta = np.abs(frame.astype(np.int16) - before.astype(np.int16)).max(axis=2)
            delta[y0:y1, x0:x1] = 0
            ys, xs = np.nonzero(delta > 2)
            if len(xs):
                row = {'frame': index, 'seconds': times[index], 'changedPixels': int(len(xs)),
                       'maximumChannelDifference': int(delta.max()),
                       'bounds': [int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)]}
                changed.append(row)
                if worst is None or row['changedPixels'] > worst['changedPixels']:
                    worst = row
            index += 1
    finally:
        decoder.stdout.close()
        decoder.wait()
    if index != len(times):
        raise ValueError('解码帧数与时间戳不一致')
    result = {
        'videoSha256': hashlib.sha256(movie.read_bytes()).hexdigest(),
        'frames': index,
        'toleranceRgb': 2,
        'caretExcluded': list(caret),
        'changedFramesOutsideCaret': len(changed),
        'maximumChangedPixelsOutsideCaret': worst['changedPixels'] if worst else 0,
        'worst': worst,
        'failures': changed,
        'limitation': '只比较录制区域；光标矩形按已知位置排除。区域外或未被采样到的帧不在此结论内。',
    }
    target = folder / 'refresh-outside-caret.json'
    with target.open('x', encoding='utf-8', newline='\n') as handle:
        json.dump(result, handle, ensure_ascii=False, indent=2)
        handle.write('\n')
    return {key: value for key, value in result.items() if key != 'failures'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder')
    parser.add_argument('--caret', default='212,101,228,140', help='x0,y0,x1,y1')
    options = parser.parse_args()
    caret = tuple(int(value) for value in options.caret.split(','))
    if len(caret) != 4:
        raise SystemExit('光标排除区需要四个整数')
    print(json.dumps(verify(options.folder, caret), ensure_ascii=False))
