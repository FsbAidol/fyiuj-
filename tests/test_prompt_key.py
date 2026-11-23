"""Tests for the random-key QTE recogniser.

OCR is stubbed (no Tesseract in CI), so these tests pin down the part that
actually matters: the geometric corrections P/D, Q/O, X and the "do not press
anything when unsure" rule.
"""

from __future__ import annotations

import cv2
import numpy as np

from bot.prompt_key import (
    GlyphClassifier,
    PromptKeyConfig,
    PromptKeyDetector,
    hold_until_gone,
    shape_correct,
)


def _mask(rows) -> np.ndarray:
    if isinstance(rows, np.ndarray):
        return rows.astype(bool)
    # "0", "." and space mean empty; "1" or any other char means filled
    data = [[0 if str(ch) in ". 0" else 1 for ch in row] for row in rows]
    return np.array(data, dtype=bool)


# --------------------------------------------------------------------------- geometry
P_GLYPH = _mask([
    "11110",
    "10001",
    "10001",
    "11110",
    "10000",
    "10000",
    "10000",
])  # closed upper bowl, open lower right -> looks like D to OCR

O_GLYPH = _mask([
    ".111.",
    "1...1",
    "1...1",
    "1...1",
    "1...1",
    "1...1",
    ".111.",
])

Q_GLYPH = _mask([
    ".111.",
    "1...1",
    "1...1",
    "1...1",
    "1...1",
    "1..11",
    ".1111",
])

X_GLYPH = _mask([
    "1...1",
    ".1.1.",
    "..1..",
    "..1..",
    ".1.1.",
    "1...1",
])


def test_p_misread_as_d_is_corrected():
    letter, bonus, confirmed = shape_correct("D", P_GLYPH)
    assert letter == "P"
    assert confirmed and bonus > 0


def test_q_misread_as_o_is_corrected():
    letter, _bonus, confirmed = shape_correct("O", Q_GLYPH)
    assert letter == "Q"
    assert confirmed


def test_clean_o_stays_o():
    letter, _bonus, confirmed = shape_correct("O", O_GLYPH)
    assert letter == "O"
    assert confirmed is False


def test_x_with_good_shape_is_confirmed():
    letter, bonus, confirmed = shape_correct("X", X_GLYPH)
    assert letter == "X"
    assert confirmed and bonus == 300.0


def test_unrelated_letter_passes_through():
    letter, bonus, confirmed = shape_correct("K", P_GLYPH)
    assert letter == "K" and bonus == 0.0 and confirmed is False


# --------------------------------------------------------------------------- detector
def _frame_with_glyph(rows: list[str], width: int = 1280, height: int = 720,
                      letters: list[tuple[str, float]] | None = None,
                      draw: bool = True) -> tuple[np.ndarray, list[tuple[str, float]]]:
    """Put a big bright glyph in the middle of a dark frame."""
    frame = np.full((height, width, 3), 25, np.uint8)
    mask = _mask(rows)
    cell = 18
    glyph = np.kron(mask, np.ones((cell, cell), np.uint8)) * 255
    gh, gw = glyph.shape
    y0, x0 = height // 2 - gh // 2, width // 2 - gw // 2
    if draw:
        frame[y0:y0 + gh, x0:x0 + gw] = cv2.cvtColor(glyph, cv2.COLOR_GRAY2BGR)
    return frame, letters or []


def _detector(ocr_letter: str, conf: float = 90.0) -> PromptKeyDetector:
    cfg = PromptKeyConfig()
    classifier = GlyphClassifier(cfg, fake=lambda _glyph: [(ocr_letter, conf)])
    return PromptKeyDetector(cfg, classifier)


def test_detector_reads_prompt_letter():
    frame, _ = _frame_with_glyph(P_GLYPH)
    detector = _detector("D")          # OCR misreads the real «P» as «D»
    assert detector.detect(frame) == "P"


def test_detector_handles_q():
    frame, _ = _frame_with_glyph(Q_GLYPH)
    detector = _detector("O")
    assert detector.detect(frame) == "Q"


def test_detector_returns_none_on_plain_screen():
    frame = np.full((720, 1280, 3), 30, np.uint8)
    assert _detector("A").detect(frame) is None


def test_low_confidence_single_vote_is_rejected():
    frame, _ = _frame_with_glyph(P_GLYPH)
    detector = _detector("K", conf=40.0)   # below single_read_min_confidence
    assert detector.detect(frame) is None


def test_two_votes_are_trusted():
    frame, _ = _frame_with_glyph(O_GLYPH)
    cfg = PromptKeyConfig()
    classifier = GlyphClassifier(cfg, fake=lambda _g: [("O", 40.0), ("O", 45.0)])
    assert PromptKeyDetector(cfg, classifier).detect(frame) == "O"


def test_wait_requires_stable_frames():
    frame, _ = _frame_with_glyph(P_GLYPH)
    detector = _detector("D")
    calls = {"n": 0}

    def grab():
        calls["n"] += 1
        # first frame empty, then the glyph -> must not return on frame 1
        return frame if calls["n"] > 1 else np.full_like(frame, 30)

    letter, _elapsed = detector.wait(grab, timeout=3, stable_frames=2, poll=0)
    assert letter == "P"
    assert calls["n"] >= 3


def test_hold_releases_when_prompt_disappears():
    frame, _ = _frame_with_glyph(P_GLYPH)
    detector = _detector("D")
    events: list[str] = []
    frames = {"n": 0}

    def grab():
        frames["n"] += 1
        return frame if frames["n"] < 3 else np.full_like(frame, 30)

    result = hold_until_gone(
        "p", detector, grab,
        press_down=lambda k: events.append(f"down:{k}"),
        press_up=lambda k: events.append(f"up:{k}"),
        timeout=5, disappear_frames=2, poll=0, expect="P",
    )
    assert result == "released"
    assert events[0] == "down:p"
    assert events[-1] == "up:p"


def test_hold_aborts_when_letter_changes():
    a, _ = _frame_with_glyph(P_GLYPH)
    b, _ = _frame_with_glyph(Q_GLYPH)
    detector = _detector("D")
    frames = {"n": 0}

    def grab():
        frames["n"] += 1
        return a if frames["n"] < 3 else b

    released: list[str] = []
    result = hold_until_gone(
        "p", detector, grab,
        press_down=lambda _k: None,
        press_up=released.append,
        timeout=5, disappear_frames=1, poll=0, expect="P",
    )
    assert result == "changed"
    assert released == ["p"]


def test_hold_times_out_and_always_releases():
    frame, _ = _frame_with_glyph(P_GLYPH)
    detector = _detector("D")
    released: list[str] = []
    result = hold_until_gone(
        "p", detector, lambda: frame,
        press_down=lambda _k: None,
        press_up=released.append,
        timeout=0.2, disappear_frames=3, poll=0, expect="P",
    )
    assert result == "timeout"
    assert released == ["p"]      # never leave a key stuck
