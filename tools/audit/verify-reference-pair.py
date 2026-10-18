"""验证双端截图可比性；通过只代表允许量测，不代表像素复原完成。"""
import hashlib
import json
from pathlib import Path
import sys

from PIL import Image


CONDITIONS = (
    "scene", "state", "theme", "fixtureSha256", "focus", "scroll",
    "uiFamily", "uiSize", "codeFamily", "codeSize", "fontFallback",
    "dpr", "viewport", "panels",
)


def verify_pair(pair, root):
    errors = []
    sides = [pair.get("reference", {}), pair.get("actual", {})]
    # 应用名称是登记字段，不能证明来源不同；同文件或同一原图拒绝充当双端证据。
    files = [side.get("file") for side in sides]
    if all(files) and (root / files[0]).resolve() == (root / files[1]).resolve():
        errors.append("双端引用同一截图文件")
    hashes = [str(side.get("sha256", "")).lower() for side in sides]
    if hashes[0] and hashes[0] == hashes[1]:
        errors.append("双端截图内容相同，不能证明独立来源")
    for key in CONDITIONS:
        if any(key not in side or side[key] is None for side in sides):
            errors.append(f"缺少同态条件：{key}")
        elif sides[0][key] != sides[1][key]:
            errors.append(f"两端条件不同：{key}")
    if sides[0].get("application") != "PyCharm" or sides[0].get("version") != "2026.2.1":
        errors.append("参考必须为PyCharm 2026.2.1")
    if sides[1].get("application") != "Augit":
        errors.append("实际侧必须为Augit真实外壳")
    for name, side in zip(("参考", "实际"), sides):
        if side.get("capture") != "screen" or side.get("unoccluded") is not True:
            errors.append(f"{name}缺少无遮挡屏幕取证")
        if not side.get("contentEvidence"):
            errors.append(f"{name}缺少实际画面内容核验记录")
        if side.get("resampled") is not False:
            errors.append(f"{name}未确认原图未重采样")
        try:
            file = root / side["file"]
            data = file.read_bytes()
            if hashlib.sha256(data).hexdigest() != side.get("sha256", "").lower():
                errors.append(f"{name}SHA256不符")
            with Image.open(file) as image:
                if image.format != "PNG":
                    errors.append(f"{name}不是原始无损PNG")
                if list(image.size) != side.get("imageSize"):
                    errors.append(f"{name}图片尺寸与登记不符")
        except (KeyError, OSError, ValueError) as error:
            errors.append(f"{name}图片不可核验：{type(error).__name__}")
    if sides[0].get("imageSize") != sides[1].get("imageSize"):
        errors.append("两端原始图尺寸不同，须先完成窗口配准")
    return {"comparable": not errors, "pixelRestorationVerified": False, "errors": errors}


if __name__ == "__main__":
    source = Path(sys.argv[1]).resolve()
    result = verify_pair(json.loads(source.read_text(encoding="utf-8")), source.parent)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    sys.exit(0 if result["comparable"] else 1)
