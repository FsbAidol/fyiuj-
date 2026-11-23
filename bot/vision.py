"""Computer vision helpers: template matching, colour bars, OCR.

Everything here is pure functions over numpy frames so it can be unit tested
with synthetic images (no emulator needed).
"""

from __future__ import annotations

import math
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

import cv2
import numpy as np

from .geometry import Point, Region

# --------------------------------------------------------------------- results


@dataclass(frozen=True)
class Match:
    template: str
    score: float
    x: int
    y: int
    w: int
    h: int
    scale: float = 1.0

    @property
    def center(self) -> Point:
        return Point(self.x + self.w // 2, self.y + self.h // 2)

    @property
    def relative_center(self) -> Point:
        """Kept as a Point but divided by frame size by the caller if needed."""
        return self.center

    def __str__(self) -> str:  # pragma: no cover - trivial
        return f"{self.template}@{self.score:.2f} {self.center}"


@dataclass(frozen=True)
class BarReading:
    ratio: float
    found: bool

    def __str__(self) -> str:  # pragma: no cover
        return f"{self.ratio:.0%}"


@dataclass
class GrayFrame:
    """Cached grayscale copy of a frame plus a downscaled version for motion."""

    frame: np.ndarray
    gray: np.ndarray
    small: np.ndarray
    width: int
    height: int

    @classmethod
    def from_frame(cls, frame: np.ndarray, small_factor: float = 0.25) -> "GrayFrame":
        gray = frame if frame.ndim == 2 else cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        sw = max(8, int(frame.shape[1] * small_factor))
        sh = max(8, int(frame.shape[0] * small_factor))
        small = cv2.resize(gray, (sw, sh), interpolation=cv2.INTER_AREA)
        return cls(frame=frame, gray=gray, small=small, width=frame.shape[1], height=frame.shape[0])


# -------------------------------------------------------------------- templates


class TemplateStore:
    """Loads template images once and caches grayscale/scaled variants."""

    def __init__(self, directory: Path, scales: Iterable[float] | None = None) -> None:
        self.directory = Path(directory)
        self.scales = tuple(sorted({float(s) for s in (scales or [1.0]) if 0.2 <= float(s) <= 3.0}))
        self._cache: dict[tuple[str, bool], np.ndarray] = {}
        self._missing: set[str] = set()

    def path(self, name: str) -> Path:
        for suffix in (".png", ".jpg", ".jpeg", ".bmp", "_dark.png", "_light.png"):
            candidate = self.directory / f"{name}{suffix}"
            if candidate.exists():
                return candidate
        return self.directory / f"{name}.png"

    def has(self, name: str) -> bool:
        return self.path(name).exists()

    def available(self) -> list[str]:
        if not self.directory.exists():
            return []
        names: set[str] = set()
        for p in self.directory.iterdir():
            if p.suffix.lower() in {".png", ".jpg", ".jpeg", ".bmp"}:
                stem = p.stem
                for tail in ("_dark", "_light"):
                    if stem.endswith(tail):
                        stem = stem[: -len(tail)]
                names.add(stem)
        return sorted(names)

    def get(self, name: str, grayscale: bool = True) -> np.ndarray | None:
        key = (name, grayscale)
        if key in self._cache:
            return self._cache[key]
        p = self.path(name)
        if not p.exists():
            self._missing.add(name)
            return None
        img = cv2.imread(str(p), cv2.IMREAD_COLOR)
        if img is None:  # pragma: no cover - corrupt file
            self._missing.add(name)
            return None
        if grayscale:
            img = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        self._cache[key] = img
        return img

    @property
    def missing(self) -> list[str]:
        return sorted(self._missing)


def crop(frame: np.ndarray, region: Region | None) -> tuple[np.ndarray, int, int]:
    if region is None:
        return frame, 0, 0
    x1, y1, x2, y2 = region.to_pixels(frame.shape[1], frame.shape[0])
    return frame[y1:y2, x1:x2], x1, y1


def find_template(
    frame: np.ndarray,
    template: np.ndarray,
    threshold: float = 0.82,
    region: Region | None = None,
    scales: Iterable[float] = (1.0,),
) -> Match | None:
    """Best multi-scale ``matchTemplate`` hit above ``threshold``.

    ``template`` may be colour or grayscale; it is converted to match the frame.
    Returns coordinates in the *full frame* space even when a region is given.
    """
    if template is None or template.size == 0:
        return None
    search, ox, oy = crop(frame, region)
    if search.shape[0] < 4 or search.shape[1] < 4:
        return None
    if search.ndim == 3 and template.ndim == 2:
        search_gray = cv2.cvtColor(search, cv2.COLOR_BGR2GRAY)
    else:
        search_gray = search
    best: Match | None = None
    for scale in scales:
        tpl = template
        if abs(scale - 1.0) > 1e-3:
            tpl = cv2.resize(template, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)
        th, tw = tpl.shape[:2]
        if th < 3 or tw < 3 or th > search_gray.shape[0] or tw > search_gray.shape[1]:
            continue
        res = cv2.matchTemplate(search_gray, tpl, cv2.TM_CCOEFF_NORMED)
        _, max_val, _, max_loc = cv2.minMaxLoc(res)
        if max_val < threshold:
            continue
        candidate = Match(
            template="",
            score=float(max_val),
            x=ox + int(max_loc[0]),
            y=oy + int(max_loc[1]),
            w=tw,
            h=th,
            scale=float(scale),
        )
        if best is None or candidate.score > best.score:
            best = candidate
    return best


def count_template(
    frame: np.ndarray,
    template: np.ndarray,
    threshold: float = 0.82,
    region: Region | None = None,
    limit: int = 20,
) -> list[Match]:
    """Find up to ``limit`` non-overlapping instances (used for inventory counts)."""
    if template is None:
        return []
    search, ox, oy = crop(frame, region)
    if search.ndim == 3 and template.ndim == 2:
        search_gray = cv2.cvtColor(search, cv2.COLOR_BGR2GRAY)
    else:
        search_gray = search
    th, tw = template.shape[:2]
    if th > search_gray.shape[0] or tw > search_gray.shape[1]:
        return []
    res = cv2.matchTemplate(search_gray, template, cv2.TM_CCOEFF_NORMED)
    found: list[Match] = []
    work = res.copy()
    for _ in range(limit):
        _, max_val, _, max_loc = cv2.minMaxLoc(work)
        if max_val < threshold:
            break
        x, y = int(max_loc[0]), int(max_loc[1])
        found.append(Match("", float(max_val), ox + x, oy + y, tw, th))
        # suppress the neighbourhood
        x0, y0 = max(0, x - tw // 2), max(0, y - th // 2)
        x1, y1 = min(work.shape[1], x + tw // 2 + 1), min(work.shape[0], y + th // 2 + 1)
        work[y0:y1, x0:x1] = -1.0
    return found


# ------------------------------------------------------------------- colour bars


def hsv_mask(frame: np.ndarray, hsv_range: Iterable[Iterable[int]]) -> np.ndarray:
    lo, hi = (list(map(int, pair)) for pair in hsv_range)
    hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
    return cv2.inRange(hsv, np.array(lo, np.uint8), np.array(hi, np.uint8))


def read_bar(
    frame: np.ndarray,
    region: Region,
    fill_hsv: Iterable[Iterable[int]],
    min_coverage: float = 0.01,
) -> BarReading:
    """Estimate how full a status bar is by the horizontal extent of a colour.

    A bar is "how far right does the fill colour reach", so we look at columns
    with enough fill pixels and take the right-most contiguous run from the
    left edge.  That copes with rounded ends and icon overlays.
    """
    patch, _, _ = crop(frame, region)
    if patch.size == 0:
        return BarReading(0.0, False)
    mask = hsv_mask(patch, fill_hsv)
    h, w = mask.shape[:2]
    col = (mask > 127).sum(axis=0) / max(1, h)
    covered = col > 0.25
    total = float(np.count_nonzero(mask)) / float(mask.size)
    if not covered.any():
        return BarReading(0.0, False)
    # first column that has fill, then walk right while the fill continues
    start = int(np.argmax(covered))
    end = start
    while end + 1 < w and covered[end + 1]:
        end += 1
    ratio = (end - start + 1) / float(w)
    return BarReading(float(np.clip(ratio, 0.0, 1.0)), found=total >= min_coverage or ratio > 0.02)


# ----------------------------------------------------------------------- motion


def motion_score(prev_small: np.ndarray, cur_small: np.ndarray) -> float:
    """0..1 fraction of pixels that changed noticeably."""
    if prev_small is None or cur_small.shape != prev_small.shape:
        return 0.0
    diff = cv2.absdiff(prev_small, cur_small)
    changed = int(np.count_nonzero(diff > 18))
    return changed / float(diff.size)


def mean_brightness(frame: np.ndarray, region: Region | None = None) -> float:
    patch, _, _ = crop(frame, region)
    if patch.size == 0:
        return 0.0
    gray = patch if patch.ndim == 2 else cv2.cvtColor(patch, cv2.COLOR_BGR2GRAY)
    return float(gray.mean())


# -------------------------------------------------------------------------- OCR


class Ocr:
    """Optional number reader for the 'fish weight' counter.

    Tesseract is not required: if the binary/language data is missing every call
    returns ``None`` and the caller falls back to the weight-bar strategy.
    """

    def __init__(self, lang: str = "eng") -> None:
        self.lang = lang
        self._backend = None
        try:  # pragma: no cover - depends on host packages
            import pytesseract  # type: ignore

            pytesseract.get_tesseract_version()
            self._backend = pytesseract
        except Exception:
            self._backend = None

    @property
    def available(self) -> bool:
        return self._backend is not None

    def read_number(self, frame: np.ndarray, region: Region | None = None) -> float | None:  # pragma: no cover
        if self._backend is None:
            return None
        patch, _, _ = crop(frame, region)
        if patch.size == 0:
            return None
        gray = patch if patch.ndim == 2 else cv2.cvtColor(patch, cv2.COLOR_BGR2GRAY)
        gray = cv2.resize(gray, None, fx=3.0, fy=3.0, interpolation=cv2.INTER_CUBIC)
        _, thresh = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
        try:
            text = self._backend.image_to_string(
                thresh, config="--psm 7 -c tessedit_char_whitelist=0123456789.,"
            )
        except Exception:
            return None
        return parse_number(text)

    def read_text(self, frame: np.ndarray, region: Region | None = None) -> str:  # pragma: no cover
        if self._backend is None:
            return ""
        patch, _, _ = crop(frame, region)
        if patch.size == 0:
            return ""
        try:
            return str(self._backend.image_to_string(patch, lang=self.lang)).strip()
        except Exception:
            return ""


def parse_number(text: str) -> float | None:
    """Read the first number out of OCR text, handling "1 234,5 кг".

    Thousand separators written as spaces (common when the game prints Russian
    or uses a narrow font) are joined, and both ``,`` and ``.`` work as the
    decimal mark.
    """
    if not text:
        return None
    cleaned = "".join(ch if (ch.isdigit() or ch in ".,") else " " for ch in text)
    cleaned = " ".join(cleaned.split())          # collapse whitespace runs
    cleaned = re.sub(r"(?<=\d)\s(?=\d{3}(?!\d))", "", cleaned)  # 1 234 -> 1234
    for token in cleaned.split():
        token = token.strip(".,")
        if not token:
            continue
        if "," in token and "." in token:
            # the right-most separator is the decimal mark
            if token.rfind(",") > token.rfind("."):
                token = token.replace(".", "").replace(",", ".")
            else:
                token = token.replace(",", "")
        else:
            token = token.replace(",", ".")
        try:
            return float(token)
        except ValueError:
            continue
    return None


# -------------------------------------------------------------------- utilities


def scaled_point(point: Point, width: int, height: int) -> Point:
    """Relative (0..1 stored as float point) -> pixels.  Convenience wrapper."""
    return Point(int(round(point.x * width)), int(round(point.y * height)))


def distance(a: Point, b: Point) -> float:
    return math.hypot(a.x - b.x, a.y - b.y)


def draw_matches(frame: np.ndarray, matches: Iterable[Match], color=(0, 255, 0)) -> np.ndarray:
    out = frame.copy()
    for m in matches:
        cv2.rectangle(out, (m.x, m.y), (m.x + m.w, m.y + m.h), color, 2)
        cv2.putText(out, f"{m.template}:{m.score:.2f}", (m.x, max(12, m.y - 6)),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.5, color, 1, cv2.LINE_AA)
    return out
