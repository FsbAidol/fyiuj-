"""Random-key prompt recognition (the "clean the net" QTE).

Amazing Online sometimes asks for a **single random Latin letter** and wants it
*held* until the prompt disappears.  Clicking blindly fails the QTE, so we only
ever press a key we are confident about.

Pipeline (all optional - without Tesseract the geometric fallback still works):

1. crop the configured region, threshold, split both polarities
2. take connected components that look like one big glyph near screen centre
3. classify with Tesseract (two page-segmentation modes = two votes) and/or by
   shape: P/D, Q/O and X are the confusable pairs, each has a geometric test
4. require agreement (2 votes) or one very confident read, plus shape checks
5. only then press

The shape heuristics are the valuable part of the original prototype: OCR
misreads «P» as «D», «Q» as «O» and drops confidence on «X», and a wrong key is
worse than no key at all.
"""

from __future__ import annotations

import re
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable, Iterable, Sequence

import cv2
import numpy as np

from .geometry import Region

ALLOWED_KEYS = set("QWERTYUIOPASDFGHJKLZXCVBNM")


@dataclass
class PromptKeyConfig:
    region: Region = field(default_factory=lambda: Region(0.30, 0.22, 0.40, 0.56))
    glyph_scale: float = 5.0
    min_confidence: float = 25.0
    single_read_min_confidence: float = 65.0
    min_key_height_fraction: float = 0.028
    max_center_distance_sq: float = 0.075
    recognition_timeout: float = 1.2
    tesseract_cmd: str = ""

    @classmethod
    def from_config(cls, cfg: Any) -> "PromptKeyConfig":
        out = cls()
        raw = cfg.get("qte.region") if hasattr(cfg, "get") else None
        if raw is not None:
            try:
                out.region = Region.from_value(raw)
            except ValueError:
                pass
        if hasattr(cfg, "get"):
            out.glyph_scale = float(cfg.get("qte.glyph_scale", out.glyph_scale) or out.glyph_scale)
            out.min_confidence = float(cfg.get("qte.min_confidence", out.min_confidence))
            out.single_read_min_confidence = float(
                cfg.get("qte.single_read_min_confidence", out.single_read_min_confidence)
            )
            out.min_key_height_fraction = float(
                cfg.get("qte.min_key_height_fraction", out.min_key_height_fraction)
            )
            out.max_center_distance_sq = float(
                cfg.get("qte.max_center_distance_sq", out.max_center_distance_sq)
            )
            out.recognition_timeout = float(
                cfg.get("qte.recognition_timeout_seconds", out.recognition_timeout)
            )
            out.tesseract_cmd = str(cfg.get("qte.tesseract_cmd", "") or "")
        return out


# --------------------------------------------------------------------- tesseract
class GlyphClassifier:
    """Optional Tesseract wrapper with vote counting and a stub for tests."""

    OCR_CONFIGS = (
        "--oem 3 --psm 7 -c tessedit_char_whitelist=ABCDEFGHIJKLMNOPQRSTUVWXYZ",
        "--oem 3 --psm 13 -c tessedit_char_whitelist=ABCDEFGHIJKLMNOPQRSTUVWXYZ",
    )

    def __init__(self, cfg: PromptKeyConfig, fake: Callable[[np.ndarray], Sequence[tuple[str, float]]] | None = None) -> None:
        self.cfg = cfg
        self._fake = fake
        self._pytesseract: Any = None
        if fake is None:
            self._pytesseract = self._load()

    def _load(self) -> Any:  # pragma: no cover - depends on host packages
        try:
            import pytesseract  # type: ignore
        except Exception:
            return None
        candidates = [self.cfg.tesseract_cmd] if self.cfg.tesseract_cmd else []
        candidates += [
            "tesseract",
            r"C:\Program Files\Tesseract-OCR\tesseract.exe",
            r"C:\Program Files (x86)\Tesseract-OCR\tesseract.exe",
        ]
        for candidate in candidates:
            if not candidate:
                continue
            path = Path(candidate)
            if path.is_file():
                pytesseract.pytesseract.tesseract_cmd = str(path)
                break
        try:
            pytesseract.get_tesseract_version()
        except Exception:
            return None
        return pytesseract

    @property
    def available(self) -> bool:
        return self._fake is not None or self._pytesseract is not None

    def read(self, glyph: np.ndarray) -> list[tuple[str, float]]:
        """Return [(letter, confidence), ...] for one prepared glyph image."""
        if self._fake is not None:
            try:
                return list(self._fake(glyph))
            except Exception:
                return []
        if self._pytesseract is None:
            return []
        votes: list[tuple[str, float]] = []
        for config in self.OCR_CONFIGS:
            try:
                data = self._pytesseract.image_to_data(
                    glyph,
                    lang="eng",
                    config=config,
                    output_type=self._pytesseract.Output.DICT,
                    timeout=self.cfg.recognition_timeout,
                )
            except Exception:
                continue
            for raw, conf in zip(data.get("text", []), data.get("conf", [])):
                token = re.sub(r"[^A-Z]", "", str(raw).upper())
                if len(token) != 1 or token not in ALLOWED_KEYS:
                    continue
                try:
                    confidence = float(conf)
                except (TypeError, ValueError):
                    continue
                if confidence >= self.cfg.min_confidence:
                    votes.append((token, confidence))
        return votes


# ------------------------------------------------------------------- heuristics
def _quadrant_means(mask: np.ndarray) -> tuple[float, float, float, float]:
    h, w = mask.shape[:2]
    mid_y, mid_x = max(1, h // 2), max(1, w // 2)
    return (
        float(mask[:mid_y, :mid_x].mean()),
        float(mask[:mid_y, mid_x:].mean()),
        float(mask[mid_y:, :mid_x].mean()),
        float(mask[mid_y:, mid_x:].mean()),
    )


def shape_correct(letter: str, mask: np.ndarray) -> tuple[str, float, bool]:
    """Fix the confusable pairs using the glyph silhouette.

    Returns ``(letter, bonus, confirmed)``.  Bonus ranks shape-confirmed reads
    above plain OCR ones.
    """
    h, w = mask.shape[:2]
    aspect = w / max(1, h)
    top_left, top_right, bottom_left, bottom_right = _quadrant_means(mask)

    # P is read as D: P has an open lower-right quadrant.
    if letter == "D" and aspect < 0.80 and bottom_right < bottom_left * 0.45:
        return "P", 300.0, True

    # Q is read as O: Q has a tail at the lower right.
    if letter in {"O", "Q"} and aspect < 0.94 and (bottom_right - bottom_left) > 0.08:
        return "Q", 300.0, True

    # X often gets low confidence despite a clear crossed shape.
    if letter == "X":
        quadrants = (top_left, top_right, bottom_left, bottom_right)
        region = mask[int(h * 0.40): max(int(h * 0.41), int(h * 0.60)),
                      int(w * 0.40): max(int(w * 0.41), int(w * 0.60))]
        central = float(region.mean()) if region.size else 0.0
        if 0.70 <= aspect <= 1.30 and central > 0.70 and (max(quadrants) - min(quadrants)) < 0.12:
            return "X", 300.0, True

    return letter, 0.0, False


# ------------------------------------------------------------------- recogniser
@dataclass
class Candidate:
    letter: str
    score: float
    center_distance: float


class PromptKeyDetector:
    """Finds the single big prompt letter on screen, if there is one."""

    def __init__(self, cfg: PromptKeyConfig, classifier: GlyphClassifier | None = None) -> None:
        self.cfg = cfg
        self.classifier = classifier or GlyphClassifier(cfg)

    # ------------------------------------------------------------------ helpers
    def _glyphs(self, frame: np.ndarray) -> Iterable[tuple[np.ndarray, np.ndarray, tuple[int, int, int, int]]]:
        """Yield (prepared_glyph_image, binary_mask, bbox) for plausible letters."""
        h, w = frame.shape[:2]
        x1, y1, x2, y2 = self.cfg.region.to_pixels(w, h)
        patch = frame[y1:y2, x1:x2]
        if patch.size == 0:
            return
        gray = cv2.cvtColor(patch, cv2.COLOR_BGR2GRAY) if patch.ndim == 3 else patch
        gray = cv2.GaussianBlur(gray, (3, 3), 0)
        _, binary = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)

        min_height = max(18, int(h * self.cfg.min_key_height_fraction))
        max_height = max(min_height + 1, int(h * 0.28))
        max_width = max(40, int(w * 0.20))
        min_area = max(80, int(h * w * 0.00002))

        for polarity in (binary, 255 - binary):
            count, _labels, stats, centroids = cv2.connectedComponentsWithStats(polarity, 8)
            for idx in range(1, count):
                gx, gy, gw, gh, area = (int(v) for v in stats[idx])
                if gh < min_height or gh > max_height or gw < 5 or gw > max_width or area < min_area:
                    continue
                cx = x1 + float(centroids[idx][0])
                cy = y1 + float(centroids[idx][1])
                center_distance = ((cx - w / 2) / max(1, w)) ** 2 + ((cy - h / 2) / max(1, h)) ** 2
                if center_distance > self.cfg.max_center_distance_sq:
                    continue
                pad = max(12, int(gh * 0.30))
                px1, py1 = max(0, gx - pad), max(0, gy - pad)
                px2 = min(polarity.shape[1], gx + gw + pad)
                py2 = min(polarity.shape[0], gy + gh + pad)
                glyph = polarity[py1:py2, px1:px2]
                mask = polarity[gy:gy + gh, gx:gx + gw] > 0
                # connected component is white on black; OCR wants dark on light
                prepared = 255 - glyph
                prepared = cv2.resize(prepared, None, fx=self.cfg.glyph_scale,
                                      fy=self.cfg.glyph_scale, interpolation=cv2.INTER_CUBIC)
                yield prepared, mask, (gx, gy, gw, gh)

    # ------------------------------------------------------------------ public
    def detect(self, frame: np.ndarray | None) -> str | None:
        """Return the prompt letter (uppercase) or ``None`` when unsure."""
        if frame is None or frame.size == 0:
            return None
        candidates: list[Candidate] = []
        for prepared, mask, (gx, gy, gw, gh) in self._glyphs(frame):
            h, w = mask.shape[:2]
            votes: dict[str, list[float]] = {}
            for letter, confidence in self.classifier.read(prepared):
                votes.setdefault(letter, []).append(confidence)
            if not votes:
                continue
            ranked = sorted(
                ((sum(scores) + 15 * (len(scores) - 1), key) for key, scores in votes.items()),
                reverse=True,
            )
            best_score, best_letter = ranked[0]
            best_votes = votes[best_letter]
            best_letter, bonus, confirmed = shape_correct(best_letter, mask)
            aspect = gw / max(1, gh)
            narrow_i = best_letter == "I" and aspect < 0.30 and max(best_votes) >= 52
            reliable = len(best_votes) >= 2 or max(best_votes) >= self.cfg.single_read_min_confidence
            if reliable or confirmed or narrow_i:
                candidates.append(Candidate(best_letter, best_score + bonus, 0.0))
        if not candidates:
            return None
        candidates.sort(key=lambda c: -c.score)
        return candidates[0].letter

    def wait(self, grab: Callable[[], np.ndarray], timeout: float,
             stable_frames: int = 2, poll: float = 0.2,
             on_tick: Callable[[], None] | None = None) -> tuple[str | None, float]:
        """Poll until the same letter is read ``stable_frames`` times.

        Returns ``(letter or None, elapsed)``.  ``None`` means "no prompt seen
        within the timeout" - the caller decides whether that is fine (no junk)
        or a failure.
        """
        started = time.time()
        previous: str | None = None
        stable = 0
        while time.time() - started < timeout:
            if on_tick:
                on_tick()
            letter = self.detect(grab())
            if letter and letter == previous:
                stable += 1
            elif letter:
                previous, stable = letter, 1
            else:
                previous, stable = None, 0
            if letter and stable >= max(1, stable_frames):
                return letter, time.time() - started
            time.sleep(max(0.05, poll))
        return None, time.time() - started


def hold_until_gone(
    key: str,
    detector: PromptKeyDetector,
    grab: Callable[[], np.ndarray],
    press_down: Callable[[str], None],
    press_up: Callable[[str], None],
    timeout: float = 28.0,
    disappear_frames: int = 3,
    poll: float = 0.2,
    expect: str | None = None,
) -> str:
    """Hold ``key`` until the prompt disappears.

    Returns ``"released"`` (prompt gone - success), ``"timeout"`` (still there
    after ``timeout``) or ``"changed"`` (a *different* letter appeared: release
    immediately instead of guessing, a wrong key can fail the whole QTE).
    """
    key = key.upper()
    expect = (expect or key).upper()
    started = time.time()
    missing = 0
    press_down(key.lower())
    try:
        while time.time() - started < timeout:
            press_down(key.lower())          # adb has no real key-down: re-send
            detected = detector.detect(grab())
            if detected == expect:
                missing = 0
            elif detected is None:
                missing += 1
                if missing >= max(1, disappear_frames):
                    return "released"
            else:
                return "changed"
            time.sleep(max(0.05, poll))
        return "timeout"
    finally:
        press_up(key.lower())
