"""High level actions: "tap this template", "wait until it disappears", ...

The actions object owns the single cached frame per poll cycle, so a long chain
of lookups does not hammer the emulator with screenshots.
"""

from __future__ import annotations

import time
from pathlib import Path
from typing import Callable, Iterable

import numpy as np

from .config import Config
from .device.capture import CaptureBackend
from .device.input import InputBackend
from .geometry import Point, Region
from .vision import (
    Match,
    TemplateStore,
    count_template,
    find_template,
    motion_score,
)

FrameProvider = Callable[[], np.ndarray]


class ActionError(Exception):
    pass


class Actions:
    def __init__(
        self,
        cfg: Config,
        capture: CaptureBackend,
        touch: InputBackend,
        templates: TemplateStore,
        frame_provider: FrameProvider | None = None,
    ) -> None:
        self.cfg = cfg
        self.capture = capture
        self.touch = touch
        self.templates = templates
        self.threshold = cfg.num("vision.threshold", 0.82)
        self.scales = tuple(cfg.get("vision.scales", [1.0]) or (1.0,))
        self._frame: np.ndarray | None = None
        self._frame_ts = 0.0
        self._provider = frame_provider
        self._motion_prev: np.ndarray | None = None
        self.last_frame: np.ndarray | None = None
        # skip repeat searches for the same template within one tick: a long
        # script would otherwise re-scan the frame dozens of times
        self._lookup_cache: dict = {}

    # ------------------------------------------------------------------ frames
    @property
    def size(self) -> tuple[int, int]:
        return self.capture.size

    def frame(self, fresh: bool = False) -> np.ndarray:
        if self._provider is not None:
            return self._provider()
        if fresh or self._frame is None:
            self._frame = self.capture.grab()
            self._frame_ts = time.time()
            self.last_frame = self._frame
        return self._frame

    def invalidate(self) -> None:
        self._frame = None
        self._lookup_cache.clear()

    def begin_tick(self) -> None:
        """Start a new tick: fresh frame, empty lookup cache."""
        self.invalidate()

    def sleep(self, seconds: float) -> None:
        if seconds > 0:
            time.sleep(seconds)

    # ------------------------------------------------------------------ lookup
    def _cache_key(self, name, region, threshold, frame):
        return (name, None if region is None else tuple(region.as_list()),
                threshold, None if frame is None else id(frame))

    def find(
        self,
        name: str,
        region: Region | None = None,
        threshold: float | None = None,
        frame: np.ndarray | None = None,
    ) -> Match | None:
        key = self._cache_key(name, region, threshold, frame)
        if key in self._lookup_cache:
            return self._lookup_cache[key]
        result = self._find_uncached(name, region, threshold, frame)
        self._lookup_cache[key] = result
        return result

    def _find_uncached(
        self,
        name: str,
        region: Region | None = None,
        threshold: float | None = None,
        frame: np.ndarray | None = None,
    ) -> Match | None:
        tpl = self.templates.get(name, self.cfg.boolean("vision.grayscale", True))
        if tpl is None:
            return None
        img = frame if frame is not None else self.frame()
        match = find_template(
            img, tpl, threshold or self.threshold, region, self.scales
        )
        if match is not None:
            match = Match(name, match.score, match.x, match.y, match.w, match.h, match.scale)
        return match

    def find_any(self, names: Iterable[str], region: Region | None = None) -> Match | None:
        for name in names:
            m = self.find(name, region)
            if m is not None:
                return m
        return None

    def exists(self, name: str, region: Region | None = None, threshold: float | None = None) -> bool:
        return self.find(name, region, threshold) is not None

    def count(self, name: str, region: Region | None = None, limit: int = 20) -> int:
        tpl = self.templates.get(name, self.cfg.boolean("vision.grayscale", True))
        if tpl is None:
            return 0
        return len(count_template(self.frame(), tpl, self.threshold, region, limit))

    # ------------------------------------------------------------------ waiting
    def wait_for(
        self,
        name: str,
        timeout: float,
        region: Region | None = None,
        poll_hz: float = 4.0,
        threshold: float | None = None,
    ) -> Match | None:
        deadline = time.time() + max(0.0, timeout)
        interval = 1.0 / max(0.5, poll_hz)
        while True:
            self.invalidate()
            match = self.find(name, region, threshold)
            if match is not None:
                return match
            if time.time() >= deadline:
                return None
            self.sleep(interval)

    def wait_gone(self, name: str, timeout: float, region: Region | None = None, poll_hz: float = 3.0) -> bool:
        deadline = time.time() + max(0.0, timeout)
        interval = 1.0 / max(0.5, poll_hz)
        while True:
            self.invalidate()
            if self.find(name, region) is None:
                return True
            if time.time() >= deadline:
                return False
            self.sleep(interval)

    def wait_any(self, names: Iterable[str], timeout: float, poll_hz: float = 4.0) -> Match | None:
        deadline = time.time() + max(0.0, timeout)
        interval = 1.0 / max(0.5, poll_hz)
        names = list(names)
        while True:
            self.invalidate()
            match = self.find_any(names)
            if match is not None:
                return match
            if time.time() >= deadline:
                return None
            self.sleep(interval)

    # ------------------------------------------------------------------ tapping
    def invalidate_lookups(self) -> None:
        """After any input the screen may have changed - forget cached matches."""
        self._lookup_cache.clear()
        self._frame = None

    def tap(self, point: Point | Iterable[float] | Match) -> None:
        if isinstance(point, Match):
            x, y = point.center.x, point.center.y
            self.touch.tap(Point(x, y))
        elif isinstance(point, Point):
            # Point from a Match is in pixels, from config in relative units
            if point.x > 1 or point.y > 1:
                self.touch.tap(Point(point.x, point.y))
            else:
                self.touch.tap(point)
        else:
            self.touch.tap(list(point))
        self.invalidate_lookups()

    def tap_template(self, name: str, region: Region | None = None, timeout: float = 0.0) -> bool:
        match = self.find(name, region) or (timeout > 0 and self.wait_for(name, timeout, region))
        if not match:
            return False
        self.tap(match)
        return True

    def tap_center(self, region: Region | None = None) -> None:
        width, height = self.size
        if region is None:
            self.touch.tap(Point(0.5, 0.5))
        else:
            p = region.center(width, height)
            self.touch.tap(Point(p.x / width, p.y / height))
        self.invalidate_lookups()

    # ------------------------------------------------------------------ gestures
    def swipe(self, start: Iterable[float], end: Iterable[float], duration_ms: int = 250) -> None:
        self.touch.swipe(list(start), list(end), duration_ms)
        self.invalidate_lookups()

    def hold(self, point: Iterable[float] | Point, duration_ms: int) -> None:
        if isinstance(point, Point):
            self.touch.hold(Point(point.x, point.y), duration_ms)
        else:
            self.touch.hold(list(point), duration_ms)
        self.invalidate_lookups()

    # ------------------------------------------------------------------ motion
    def motion(self, region: Region | None = None) -> float:
        """Change since the previous call (per-region if given)."""
        from .vision import GrayFrame, crop

        frame = self.frame(fresh=True)
        patch, _, _ = crop(frame, region)
        gf = GrayFrame.from_frame(patch, 0.25)
        score = motion_score(self._motion_prev, gf.small)
        self._motion_prev = gf.small
        return score

    def brightness(self, region: Region | None = None) -> float:
        from .vision import mean_brightness

        return mean_brightness(self.frame(fresh=True), region)

    # ------------------------------------------------------------------ keyboard
    def press(self, key: str, hold_ms: int = 60) -> None:
        touch = self.touch
        if not hasattr(touch, "press"):
            raise ActionError("input backend cannot send keys")
        touch.press(key, hold_ms)
        self.invalidate_lookups()

    def hold_key(self, key: str, seconds: float) -> None:
        touch = self.touch
        if not hasattr(touch, "hold_key"):
            raise ActionError("input backend cannot hold keys")
        touch.hold_key(key, seconds)
        self.invalidate_lookups()

    # ------------------------------------------------------------------ prompts
    def prompt_key_detector(self):
        """Lazily built QTE detector (Tesseract is optional)."""
        detector = getattr(self, "_prompt_detector", None)
        if detector is None:
            from .prompt_key import PromptKeyConfig, PromptKeyDetector

            config = PromptKeyConfig.from_config(self.cfg)
            detector = PromptKeyDetector(config)
            self._prompt_detector = detector
        return detector

    def detect_prompt_key(self, frame=None):
        return self.prompt_key_detector().detect(frame if frame is not None else self.frame(fresh=True))

    def qte_hold(self, timeout: float = 8.0, hold_timeout: float = 28.0,
                 expect: str | None = None, on_tick=None) -> str:
        """Recognise the random prompt letter and hold it until it disappears.

        Returns ``"released"``, ``"timeout"``, ``"changed"`` or ``"none"`` (no
        letter recognised at all - the caller decides if that is fine).
        """
        from .prompt_key import hold_until_gone

        if not self.cfg.boolean("qte.enabled", True):
            return "none"
        detector = self.prompt_key_detector()
        letter, _elapsed = detector.wait(
            lambda: self.frame(fresh=True),
            timeout=timeout,
            stable_frames=int(self.cfg.num("qte.stable_frames", 2)),
            poll=self.cfg.num("qte.poll_seconds", 0.2),
            on_tick=on_tick,
        )
        if letter is None:
            return "none"
        hold_key = (expect or letter).lower()
        touch = self.touch
        result = hold_until_gone(
            hold_key,
            detector,
            lambda: self.frame(fresh=True),
            press_down=lambda k: getattr(touch, "key_down", lambda _k: None)(k),
            press_up=lambda k: getattr(touch, "key_up", lambda _k: None)(k),
            timeout=hold_timeout,
            disappear_frames=int(self.cfg.num("qte.disappear_frames", 3)),
            poll=self.cfg.num("qte.poll_seconds", 0.2),
            expect=letter,
        )
        self.invalidate()
        return result

    def clear_net(self, appear_timeout: float = 8.0, hold_timeout: float = 28.0) -> bool:
        """Clean the net: hold the recognised letter until the prompt is gone.

        Returns False only when a letter *was* recognised but the QTE failed -
        "no prompt" just means there was nothing to clean.
        """
        result = self.qte_hold(timeout=appear_timeout, hold_timeout=hold_timeout)
        return result in {"released", "none"}

    # ------------------------------------------------------------------ debug
    def save_debug(self, directory: Path, tag: str = "") -> Path | None:
        import cv2

        frame = self.frame()
        if frame is None:
            return None
        directory.mkdir(parents=True, exist_ok=True)
        stamp = time.strftime("%Y%m%d-%H%M%S")
        name = f"{stamp}-{tag or 'frame'}.png"
        path = directory / name
        cv2.imwrite(str(path), frame)
        return path
