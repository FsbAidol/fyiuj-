"""Loading / disconnect guards wait instead of killing the run outright."""

from __future__ import annotations

import cv2
import numpy as np
import pytest

from bot.guards import ScreenGuard, StuckDetector
from conftest import blank_frame, draw_button


def _tpl(cfg, name, img):
    cv2.imwrite(str(cfg.template_dir() / f"{name}.png"), img)


def _guard(cfg, actions, **overrides):
    cfg.raw["guards"].update({"pause_seconds": 0.01, "max_consecutive_checks": 2,
                              "stable_frames": 1, **overrides})
    return ScreenGuard(cfg, actions)


def test_loading_screen_waits_then_reports_fatal(cfg, make_actions):
    frame = blank_frame()
    loading = draw_button(frame, 300, 150, w=80, h=30, colour=(90, 90, 90))
    _tpl(cfg, "loading_screen", loading[150:180, 300:380])
    actions, _ = make_actions([loading])
    guard = _guard(cfg, actions)
    verdict = guard.check()
    assert verdict.state == "fatal"
    assert "loading" in verdict.reason


def test_wait_clears_when_screen_goes(cfg, make_actions):
    frame = blank_frame()
    loading = draw_button(frame, 300, 150, w=80, h=30, colour=(90, 90, 90))
    _tpl(cfg, "loading_screen", loading[150:180, 300:380])
    # first frames are the loading screen, then the game appears
    frames = [loading] * 2 + [blank_frame()] * 20
    actions, _ = make_actions(frames)
    guard = _guard(cfg, actions, max_consecutive_checks=3)
    verdict = guard.check()
    assert verdict.state in {"waiting", "ok"}


def test_guard_ok_without_templates(cfg, make_actions):
    actions, _ = make_actions([blank_frame()])
    assert _guard(cfg, actions).check().state == "ok"


def test_stuck_detector_fires_on_frozen_screen():
    det = StuckDetector(threshold_seconds=0.05)
    frame = np.zeros((40, 40), np.uint8)
    det.feed(frame)
    import time

    time.sleep(0.08)
    assert det.feed(frame) is True


def test_stuck_detector_resets_on_change():
    det = StuckDetector(threshold_seconds=0.05)
    a = np.zeros((40, 40), np.uint8)
    b = a.copy()
    b[5:15, 5:15] = 120
    det.feed(a)
    import time

    time.sleep(0.08)
    det.feed(b)                 # change resets the timer
    assert det.feed(b) is False
