"""比较同主题真实系统PNG的近边剖面，检出重复投影；不替代完整阴影验收。"""
import argparse
import json
from pathlib import Path


def verify(actual, reference, tolerance=3):
    if actual["scale"] != reference["scale"]:
        raise ValueError("缩放不一致")
    results = {}
    # 原始图目视确认右侧/底部近边中央背景为同主题纯色；远端文字与焦点变化另记。
    for side in ("right", "bottom"):
        first = actual["profiles"][side]["samples"][:16]
        second = reference["profiles"][side]["samples"][:16]
        if len(first) != 16 or len(second) != 16:
            raise ValueError("近边采样不足")
        if [row["distance_physical"] for row in first] != list(range(1, 17)) or [row["distance_physical"] for row in second] != list(range(1, 17)):
            raise ValueError("近边距离不连续")
        differences = [abs(a["median_darkening_rgb"]-b["median_darkening_rgb"]) for a, b in zip(first, second)]
        results[side] = {"maxMedianDifference": max(differences), "passed": max(differences) <= tolerance}
    return {"nearEdgePassed": all(row["passed"] for row in results.values()),
            "toleranceRgb": tolerance, "sides": results, "fullShadowVerified": False}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("actual")
    parser.add_argument("reference")
    parser.add_argument("--output", required=True)
    options = parser.parse_args()
    result = verify(json.loads(Path(options.actual).read_text(encoding="utf-8")),
                    json.loads(Path(options.reference).read_text(encoding="utf-8")))
    Path(options.output).write_text(json.dumps(result, ensure_ascii=False, indent=2)+"\n", encoding="utf-8", newline="\n")
    print(json.dumps(result))
    raise SystemExit(0 if result["nearEdgePassed"] else 1)
