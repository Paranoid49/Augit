"""以内存PNG中的三色标记核对真实客户区；正式证据必须移除标记后另存。"""
import io
import json
import sys
from math import ceil, floor

from PIL import Image


COLORS = ((233, 17, 89), (13, 227, 173), (71, 43, 241))


def verify(png, geometry):
    image = Image.open(io.BytesIO(png)).convert("RGB")
    dpr = geometry["dpr"]
    assert 0.5 <= dpr <= 4, "客户区缩放必须有限且在有效范围内"
    x, y = geometry["x"], geometry["y"]
    found = []
    for index, color in enumerate(COLORS):
        # 只检查色块内部，避开分数DPI的边缘栅格化，不扫描全图寻找碰巧同色像素。
        left, top = ceil(x + (index * 6 + 1) * dpr), ceil(y + dpr)
        right, bottom = floor(x + (index * 6 + 5) * dpr), floor(y + 5 * dpr)
        assert 0 <= left < right <= image.width and 0 <= top < bottom <= image.height, "标记须进入PNG客户区"
        pixels = [image.getpixel((px, py)) for py in range(top, bottom) for px in range(left, right)]
        assert all(pixel == color for pixel in pixels), "PNG未包含实际模态三色标记，可能捕获了旧主窗surface"
        found.append({"color": list(color), "interior": [left, top, right, bottom]})
    return {"image_size": list(image.size), "markers": found, "method": "客户区三色标记内部原始像素，正式图移除标记后重采"}


if __name__ == "__main__":
    result = verify(sys.stdin.buffer.read(), json.loads(sys.argv[1]))
    print(json.dumps(result, ensure_ascii=False))
