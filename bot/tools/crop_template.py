"""Tiny template cropper: cut template PNGs straight from a screenshot.

    python -m bot.tools.crop_template samples/shot.png 900,600,1100,700 templates/cast_button.png
"""

from __future__ import annotations

import sys
from pathlib import Path

import cv2


def main(argv: list[str]) -> int:
    if len(argv) < 4:
        print(__doc__)
        return 2
    src = Path(argv[1])
    x1, y1, x2, y2 = (int(float(v)) for v in argv[2].replace(";", ",").split(","))
    dst = Path(argv[3])
    frame = cv2.imread(str(src))
    if frame is None:
        print(f"cannot read {src}")
        return 1
    h, w = frame.shape[:2]
    x1, x2 = sorted((max(0, x1), min(w, x2)))
    y1, y2 = sorted((max(0, y1), min(h, y2)))
    if x2 - x1 < 4 or y2 - y1 < 4:
        print("crop too small")
        return 1
    dst.parent.mkdir(parents=True, exist_ok=True)
    cv2.imwrite(str(dst), frame[y1:y2, x1:x2])
    print(f"saved {dst} ({x2-x1}x{y2-y1})")
    return 0


if __name__ == "__main__":  # pragma: no cover
    raise SystemExit(main(sys.argv))
