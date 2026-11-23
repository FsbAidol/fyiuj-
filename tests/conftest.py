"""Shared fixtures: synthetic frames, a fake device and a test config."""

from __future__ import annotations

from pathlib import Path

import cv2
import numpy as np
import pytest

from bot.config import Config
from bot.device.capture import CaptureBackend
from bot.device.input import InputBackend
from bot.geometry import Point

W, H = 640, 360


# --------------------------------------------------------------------- drawing
def blank_frame(width: int = W, height: int = H, colour=(40, 44, 48)) -> np.ndarray:
    return np.full((height, width, 3), colour, dtype=np.uint8)


def draw_button(
    frame: np.ndarray,
    x: int,
    y: int,
    w: int = 60,
    h: int = 28,
    colour=(60, 120, 240),
    label: str = "",
    noise: float = 0.0,
) -> np.ndarray:
    out = frame.copy()
    cv2.rectangle(out, (x, y), (x + w, y + h), colour, -1)
    cv2.rectangle(out, (x, y), (x + w, y + h), (255, 255, 255), 2)
    cv2.circle(out, (x + w // 2, y + h // 2), min(w, h) // 4, (10, 10, 10), -1)
    if label:
        cv2.putText(out, label, (x + 4, y + h - 6), cv2.FONT_HERSHEY_SIMPLEX, 0.35, (255, 255, 255), 1)
    if noise:
        rng = np.random.default_rng(7)
        patch = out[y:y + h, x:x + w].astype(np.int16)
        patch += rng.normal(0, noise, patch.shape).astype(np.int16)
        out[y:y + h, x:x + w] = np.clip(patch, 0, 255).astype(np.uint8)
    return out


def draw_bar(
    frame: np.ndarray,
    x: int,
    y: int,
    w: int,
    h: int,
    ratio: float,
    hsv_colour=(110, 180, 220),
) -> np.ndarray:
    out = frame.copy()
    cv2.rectangle(out, (x, y), (x + w, y + h), (70, 70, 70), -1)
    fill = int(w * max(0.0, min(1.0, ratio)))
    if fill > 0:
        rgb = cv2.cvtColor(np.uint8([[[*hsv_colour]]]), cv2.COLOR_HSV2BGR)[0][0]
        cv2.rectangle(out, (x, y), (x + fill, y + h), tuple(int(c) for c in rgb), -1)
    return out


# ---------------------------------------------------------------------- config
@pytest.fixture
def cfg(tmp_path: Path) -> Config:
    templates = tmp_path / "templates"
    templates.mkdir()
    (tmp_path / "scripts").mkdir()
    data = {
        "device": {"backend": "images", "width": W, "height": H, "images_dir": str(tmp_path / "shots")},
        "vision": {"threshold": 0.8, "templates_dir": str(templates)},
        "logging": {"file": str(tmp_path / "logs" / "bot.log"), "debug_dir": str(tmp_path / "debug")},
        "state": {"path": str(tmp_path / "state" / "state.json")},
        "anti_afk": {"enabled": True, "interval": 0.15, "jitter": 0},
        "trunk": {"enabled": True, "script": str(tmp_path / "scripts" / "trunk.yaml")},
        "survival": {
            "enabled": True,
            "eat_script": str(tmp_path / "scripts" / "eat.yaml"),
            "drink_script": str(tmp_path / "scripts" / "drink.yaml"),
            "cooldown_seconds": 0,
        },
        "trash": {"enabled": True, "script": str(tmp_path / "scripts" / "trash.yaml"),
                  "trigger_count": 2, "check_interval": 0},
        "hud": {
            "thirst": {"region": [0.02, 0.05, 0.26, 0.06], "fill_hsv": [[90, 60, 90], [130, 255, 255]],
                       "low": 0.25, "critical": 0.1},
            "food": {"region": [0.02, 0.14, 0.26, 0.06], "fill_hsv": [[5, 80, 90], [35, 255, 255]],
                     "low": 0.25, "critical": 0.1},
            "weight": {"region": [0.60, 0.05, 0.3, 0.05], "fill_hsv": [[35, 70, 120], [65, 255, 255]]},
        },
        "safety": {"weight_limit_kg": 500, "max_consecutive_failures": 5},
    }
    return Config(data, tmp_path / "config.yaml")


# ------------------------------------------------------------------ fake device
class FakeInput(InputBackend):
    name = "fake"

    def __init__(self, cfg: Config, width: int, height: int) -> None:
        super().__init__(cfg, width, height)
        self.taps: list[tuple[int, int]] = []
        self.swipes: list[tuple[tuple[int, int], tuple[int, int], int]] = []
        self.holds: list[tuple[int, int, int]] = []

    def _tap_px(self, x: int, y: int, hold_ms: int) -> None:
        self.taps.append((x, y))

    def _swipe_px(self, x1: int, y1: int, x2: int, y2: int, duration_ms: int) -> None:
        self.swipes.append(((x1, y1), (x2, y2), duration_ms))

    def hold(self, point, duration_ms: int) -> None:  # type: ignore[override]
        x, y = self.pixels(point)
        self.holds.append((x, y, int(duration_ms)))


class FakeCapture(CaptureBackend):
    name = "fake"

    def __init__(self, frames: list[np.ndarray], cfg: Config) -> None:
        super().__init__(cfg)
        self.frames = frames
        self.index = 0
        self.height, self.width = frames[0].shape[:2]

    def grab(self) -> np.ndarray:
        if self.index >= len(self.frames):
            if self.cfg.boolean("device.image_loop", True):
                self.index = 0
            else:
                return self.frames[-1]
        frame = self.frames[self.index]
        self.index += 1
        return frame.copy()


@pytest.fixture
def fake_input(cfg: Config) -> FakeInput:
    return FakeInput(cfg, W, H)


@pytest.fixture
def make_actions(cfg: Config, fake_input: FakeInput):
    from bot.actions import Actions
    from bot.vision import TemplateStore

    def factory(frames: list[np.ndarray]):
        capture = FakeCapture(frames, cfg)
        store = TemplateStore(cfg.template_dir(), [1.0])
        return Actions(cfg, capture, fake_input, store), capture

    return factory
