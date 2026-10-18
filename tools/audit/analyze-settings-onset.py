"""无CDP录像局部时序诊断；不把探针变化计作完整画面或完整开关验收。"""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

import numpy as np


def transitions(frames, times, tolerance=2):
    if len(frames) != len(times) or len(frames) < 2:
        raise ValueError('帧数与时间戳不符')
    if frames.dtype != np.uint8 or any(b <= a for a, b in zip(times, times[1:])):
        raise ValueError('需要原始8位像素与严格递增时间戳')
    changed = np.any(np.abs(frames.astype(np.int16)-frames[0].astype(np.int16)) > tolerance, axis=3).sum(axis=(1, 2))
    indices = np.flatnonzero(changed > 0)
    return {'firstChangedFrame': int(indices[0]) if len(indices) else None,
            'firstChangedSeconds': times[indices[0]] if len(indices) else None,
            'changedPixels': changed.tolist()}


def decoder_command(movie, rect):
    x, y, width, height = rect
    return ['ffmpeg', '-v', 'error', '-i', str(movie), '-vf', f'crop={width}:{height}:{x}:{y}',
            '-fps_mode', 'passthrough', '-enc_time_base', '1:1000000', '-pix_fmt', 'rgb24', '-f', 'rawvideo', 'pipe:1']


def analyze(folder, rect):
    folder = Path(folder)
    movie = folder/'cycle-screen.mkv'
    probe = subprocess.run(['ffprobe', '-v', 'error', '-select_streams', 'v:0', '-show_frames',
                            '-show_entries', 'frame=best_effort_timestamp_time', '-of', 'json', str(movie)], capture_output=True, check=True)
    times = [float(f['best_effort_timestamp_time']) for f in json.loads(probe.stdout)['frames']]
    _, _, width, height = rect
    decoded = subprocess.run(decoder_command(movie, rect), capture_output=True, check=True)
    frames = np.frombuffer(decoded.stdout, dtype=np.uint8).reshape(-1, height, width, 3)
    with movie.open('rb') as source:
        digest = hashlib.file_digest(source, 'sha256').hexdigest()
    result = {'videoSha256': digest,
              'rect': rect, 'resampled': False, 'tolerance': 2, 'frameCount': len(frames),
              'maximumFrameGapMilliseconds': max(b-a for a, b in zip(times, times[1:]))*1000,
              'decoderDiagnostics': decoded.stderr.decode('utf-8', errors='replace'),
              'probeDiagnostics': probe.stderr.decode('utf-8', errors='replace'),
              'fullPresentationVerified': False, 'completeCycleVerified': False,
              'limitation': '局部探针只能定位变化；不等于窗口首次完整绘制，也不直接给出输入到呈现耗时。',
              **transitions(frames, times)}
    with (folder/'onset-diagnostic.json').open('x', encoding='utf-8', newline='\n') as output:
        json.dump(result, output, ensure_ascii=False, indent=2)
        output.write('\n')
    return {key: value for key, value in result.items() if key not in ('changedPixels', 'decoderDiagnostics', 'probeDiagnostics')}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder')
    parser.add_argument('--rect', type=int, nargs=4, required=True)
    args = parser.parse_args()
    if args.rect[0] < 0 or args.rect[1] < 0 or min(args.rect[2:]) <= 0:
        parser.error('区域须为非负原点与正整数宽高')
    print(json.dumps(analyze(args.folder, args.rect), ensure_ascii=False))
