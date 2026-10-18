# 分区只用于定位，计数仍覆盖整张表面，不改变像素验收范围。
def classify_pixel(x, y, width, height):
    if min(width, height) < 32 or not (0 <= x < width and 0 <= y < height):
        raise ValueError('坐标越界或分区尺寸不足')
    horizontal = x < 16 or x >= width - 16
    vertical = y < 16 or y >= height - 16
    return 'corners16' if horizontal and vertical else 'edge16OutsideCorners' if horizontal or vertical else 'interior'


def count_mask_regions(mask):
    """按互斥矩形统计二值差异掩码，整表面计数不因诊断分区减少。"""
    width, height = mask.size
    if mask.mode != 'L' or min(width, height) < 32:
        raise ValueError('分区需要至少32像素的灰度二值掩码')
    histogram = mask.histogram()
    if sum(histogram[1:255]):
        raise ValueError('差异掩码只能包含0或255')
    def count(rect):
        with mask.crop(rect) as part:
            return part.histogram()[255]
    corners = sum(count(rect) for rect in [
        (0, 0, 16, 16), (width-16, 0, width, 16),
        (0, height-16, 16, height), (width-16, height-16, width, height)])
    interior = count((16, 16, width-16, height-16))
    return {'corners16': corners, 'edge16OutsideCorners': histogram[255]-corners-interior,
            'interior': interior}


def summarize_regions(failures):
    """只按连续帧编号分段，不把录像时钟推断成宿主呈现时刻。"""
    result = {}
    for region in ['corners16', 'edge16OutsideCorners', 'interior']:
        runs = []
        selected = [frame for frame in failures if frame['regions'][region] > 0]
        for frame in selected:
            if not runs or frame['frame'] != runs[-1]['lastFrame'] + 1:
                runs.append({'firstFrame': frame['frame'], 'lastFrame': frame['frame'],
                    'firstSeconds': frame['seconds'], 'lastSeconds': frame['seconds'],
                    'maximumChangedPixels': frame['regions'][region]})
            else:
                runs[-1]['lastFrame'] = frame['frame']
                runs[-1]['lastSeconds'] = frame['seconds']
                runs[-1]['maximumChangedPixels'] = max(runs[-1]['maximumChangedPixels'], frame['regions'][region])
        result[region] = {'changedFrames': len(selected), 'runs': runs}
    return result
