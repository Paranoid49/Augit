"""全客户区逐帧比较；只允许另行登记的细光标变化，不用局部探针宣称全窗稳定。"""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path
import numpy as np
from PIL import Image


def compare_frame(frame, baseline, tolerance=2):
    if frame.shape != baseline.shape or frame.dtype != np.uint8 or baseline.dtype != np.uint8:
        raise ValueError('帧与基线须为同尺寸原始RGB')
    delta = np.abs(frame.astype(np.int16)-baseline.astype(np.int16)).max(axis=2)
    ys, xs = np.nonzero(delta > tolerance)
    return {'differentPixels': len(xs), 'maximumChannelDifference': int(delta.max()),
            'bounds': [int(xs.min()), int(ys.min()), int(xs.max()+1), int(ys.max()+1)] if len(xs) else None}


def analyze(folder):
    folder = Path(folder)
    if (folder/'full-frame-analysis.json').exists() or (folder/'worst-frame.png').exists():
        raise ValueError('拒绝覆盖已有完整帧分析证据')
    movie = folder/'refresh-screen.mkv'
    baseline = np.asarray(Image.open(folder/'before.png').convert('RGB'))
    height, width, _ = baseline.shape
    probe = subprocess.run(['ffprobe', '-v', 'error', '-select_streams', 'v:0', '-show_frames',
                            '-show_entries', 'frame=best_effort_timestamp_time', '-of', 'json', str(movie)], capture_output=True, check=True)
    times = [float(f['best_effort_timestamp_time']) for f in json.loads(probe.stdout)['frames']]
    command = ['ffmpeg', '-v', 'error', '-i', str(movie), '-fps_mode', 'passthrough', '-enc_time_base', '1:1000000',
               '-pix_fmt', 'rgb24', '-f', 'rawvideo', 'pipe:1']
    decoded = subprocess.run(command, capture_output=True, check=True)
    frames = np.frombuffer(decoded.stdout, dtype=np.uint8).reshape(-1, height, width, 3)
    if len(times) != len(frames) or len(times) < 2 or any(b <= a for a, b in zip(times, times[1:])):
        raise ValueError('帧数或原始时间戳不符')
    rows = [dict(frame=i, seconds=times[i], **compare_frame(frame, baseline)) for i, frame in enumerate(frames)]
    worst = max(rows, key=lambda row: row['differentPixels'])
    Image.fromarray(frames[worst['frame']]).save(folder/'worst-frame.png')
    with movie.open('rb') as source:
        digest = hashlib.file_digest(source, 'sha256').hexdigest()
    result = {'videoSha256': digest, 'width': width, 'height': height, 'frameCount': len(frames),
              'maximumFrameGapMilliseconds': max(b-a for a, b in zip(times, times[1:]))*1000,
              'toleranceRgb': 2, 'resampled': False, 'worst': worst, 'frames': rows,
              'decoderDiagnostics': decoded.stderr.decode('utf-8', errors='replace'),
              'fullPresentationVerified': False,
              'limitation': '完整矩形与初始系统PNG比较，含边框圆角、光标及其后的真实变化；须目视区分，不能自动排除异常像素。'}
    with (folder/'full-frame-analysis.json').open('x', encoding='utf-8', newline='\n') as file:
        json.dump(result, file, ensure_ascii=False, indent=2)
        file.write('\n')
    return {k: v for k, v in result.items() if k not in ('frames', 'decoderDiagnostics')}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder')
    print(json.dumps(analyze(parser.parse_args().folder), ensure_ascii=False))
