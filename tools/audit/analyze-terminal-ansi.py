#!/usr/bin/env python3
"""Analyze terminal ANSI color screenshots captured by terminal-ansi-colors.cjs.

For each C30..C97 row, crop the text area (CSS row rect * 1.75 DPR), take the most
common exact palette color, and compare with the expected light/dark value. This
checks the implementation's core glyph pixels, not a reference screenshot.
"""
import collections
import json
import sys
from pathlib import Path

from PIL import Image

LIGHT = {
    "C30": "000000", "C31": "CE0505", "C32": "067D17", "C33": "B28C00",
    "C34": "063FDB", "C35": "B309B3", "C36": "028E8E", "C37": "929292",
    "C90": "656565", "C91": "FF1616", "C92": "16B42C", "C93": "ECC32C",
    "C94": "0060AF", "C95": "E617E6", "C96": "15C1C1", "C97": "C9C9C9",
}
DARK = {
    "C30": "000000", "C31": "F0524F", "C32": "5C962C", "C33": "A68A0D",
    "C34": "3993D4", "C35": "A771BF", "C36": "00A3A3", "C37": "808080",
    "C90": "595959", "C91": "FF4050", "C92": "4FC414", "C93": "E5BF00",
    "C94": "1FB0FF", "C95": "ED7EED", "C96": "00E5E5", "C97": "FFFFFF",
}
SCALE = 1.75


def analyze(directory: Path, expected: dict) -> list:
    data = json.loads((directory / "terminal-ansi-colors.json").read_text(encoding="utf-8"))
    result = []
    for batch in data["batches"]:
        image = Image.open(batch["file"]).convert("RGB")
        for row in batch["rows"]:
            text = row["text"].strip()
            wanted = expected.get(text)
            if wanted is None:
                continue
            x, y, w, h = row["x"], row["y"], row["width"], row["height"]
            crop = image.crop((int(x * SCALE), int(y * SCALE), int((x + w) * SCALE), int((y + h) * SCALE)))
            counts = collections.Counter(crop.getdata())
            top = [
                {"hex": "#%02X%02X%02X" % rgb, "count": count}
                for rgb, count in counts.most_common(6)
            ]
            wanted_hex = "#" + wanted
            match = next((item for item in top if item["hex"] == wanted_hex), None)
            result.append({
                "batch": batch["name"],
                "row": text,
                "expected": wanted_hex,
                "observed": match,
                "dominant": top[0] if top else None,
                "match": bool(match),
            })
    return result


def main() -> int:
    if len(sys.argv) != 4:
        print("usage: analyze-terminal-ansi.py <light-dir> <dark-dir> <out.json>", file=sys.stderr)
        return 2
    light = analyze(Path(sys.argv[1]), LIGHT)
    dark = analyze(Path(sys.argv[2]), DARK)
    payload = {
        "dpr": SCALE,
        "light": light,
        "dark": dark,
        "lightMatches": sum(1 for item in light if item["match"]),
        "darkMatches": sum(1 for item in dark if item["match"]),
    }
    Path(sys.argv[3]).write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({
        "light": f"{payload['lightMatches']}/{len(light)}",
        "dark": f"{payload['darkMatches']}/{len(dark)}",
        "out": sys.argv[3],
    }, ensure_ascii=False))
    return 0 if payload["lightMatches"] == len(light) and payload["darkMatches"] == len(dark) else 1


if __name__ == "__main__":
    raise SystemExit(main())
