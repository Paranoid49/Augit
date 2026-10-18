"""扫描刷新录像的全窗近白帧与固定边缘带变化；不把一次采样当作完整 Present 证明。"""
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


def verify(folder, threshold, top, bottom, side):
    folder = Path(folder)
    manifest = json.loads((folder / 'recording.json').read_text(encoding='utf-8'))
    width, height = manifest['source']['imageSize']
    movie = folder / 'refresh-screen.mkv'
    probe = subprocess.run(
        ['ffprobe', '-v', 'error', '-select_streams', 'v:0', '-show_frames',
         '-show_entries', 'frame=best_effort_timestamp_time', '-of', 'json', str(movie)],
        capture_output=True, check=True)
    times = [float(frame['best_effort_timestamp_time']) for frame in json.loads(probe.stdout)['frames']]
    decoder = subprocess.Popen(
        ['ffmpeg', '-v', 'error', '-i', str(movie), '-fps_mode', 'passthrough',
         '-pix_fmt', 'rgb24', '-f', 'rawvideo', 'pipe:1'], stdout=subprocess.PIPE)
    frame_size = width * height * 3
    edge_top = np.zeros((height, width), dtype=bool)
    edge_top[:top, :] = True
    edge_top[height - bottom:, :] = True
    edge_top[:, :side] = True
    edge_top[:, width - side:] = True
    reference = None
    maximum_white = -1
    white_candidates = []
    changed_edges = []
    worst = None
    index = 0
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
                white_candidates.append({'frame': index, 'seconds': times[index], 'nearWhitePixels': near_white})
            maximum_white = max(maximum_white, near_white)
            if reference is None:
                reference = frame.copy()
            else:
                delta = np.abs(frame.astype(np.int16) - reference.astype(np.int16)).max(axis=2)
                delta[~edge_top] = 0
                ys, xs = np.nonzero(delta > 2)
                if len(xs):
                    row = {'frame': index, 'seconds': times[index], 'changedPixels': int(len(xs)),
                           'bounds': [int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)]}
                    changed_edges.append(row)
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
        'width': width,
        'height': height,
        'nearWhiteThreshold': threshold,
        'maximumNearWhitePixels': maximum_white,
        'whiteCandidateFrames': len(white_candidates),
        'whiteCandidates': white_candidates,
        'edgeBands': {'top': top, 'bottom': bottom, 'side': side},
        'changedEdgeFrames': len(changed_edges),
        'worstEdge': worst,
        'edgeChanges': changed_edges,
        'limitation': '固定边缘带与首帧比较；内容区、光标和合法标题栏更新不计，GDI 采样可能漏帧。不证明每个 Present。',
    }
    target = folder / 'refresh-white-edge-scan.json'
    with target.open('x', encoding='utf-8', newline='\n') as handle:
        json.dump(result, handle, ensure_ascii=False, indent=2)
        handle.write('\n')
    return {key: value for key, value in result.items() if key not in ('whiteCandidates', 'edgeChanges')}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder')
    parser.add_argument('--threshold', type=int, default=4500000)
    parser.add_argument('--top', type=int, default=120)
    parser.add_argument('--bottom', type=int, default=60)
    parser.add_argument('--side', type=int, default=16)
    args = parser.parse_args()
    print(json.dumps(verify(args.folder, args.threshold, args.top, args.bottom, args.side), ensure_ascii=False))
