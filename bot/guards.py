"""Safety guards: connection lost, loading screens, stuck screens.

The prototype stopped the whole bot when it saw the disconnect/loading screens.
That is too harsh for a night run: a 15-second loading screen is normal, a
10-minute one means the emulator died.  So the guard *waits* (pausing input)
for a bounded time and only then reports a fatal condition.

The same mechanism catches the "bot is stuck on an unknown popup" case by
watching how long the screen has been unchanged while no progress is made.
"""

from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Any, Callable

import numpy as np

from .geometry import Region


@dataclass
class GuardVerdict:
    state: str          # "ok" | "waiting" | "fatal"
    reason: str = ""

    @property
    def ok(self) -> bool:
        return self.state == "ok"


class ScreenGuard:
    def __init__(self, cfg: Any, actions: Any, logger: Any = None) -> None:
        self.cfg = cfg
        self.actions = actions
        self.log = logger
        self.enabled = bool(cfg.get("guards.lost_connection")) or bool(cfg.get("guards.loading_screen"))
        self.lost_template = str(cfg.get("guards.lost_connection", "") or "")
        self.loading_template = str(cfg.get("guards.loading_screen", "") or "")
        self.required_frames = max(1, int(cfg.num("guards.stable_frames", 3)))
        self.pause_seconds = max(1.0, cfg.num("guards.pause_seconds", 20.0))
        self.max_checks = max(1, int(cfg.num("guards.max_consecutive_checks", 5)))
        self._counts: dict[str, int] = {}

    # ------------------------------------------------------------------ helpers
    def _seen_stable(self, name: str) -> bool:
        """True when the template has been on screen for N frames in a row."""
        found = bool(name) and self.actions.find(name) is not None
        self._counts[name] = self._counts.get(name, 0) + 1 if found else 0
        return self._counts[name] >= self.required_frames

    def check(self) -> GuardVerdict:
        """Called once per tick, before anything else."""
        if not self.enabled:
            return GuardVerdict("ok")
        for name, label in ((self.loading_template, "loading screen"),
                            (self.lost_template, "connection lost")):
            if not name or not self.actions.templates.has(name):
                continue
            if not self._seen_stable(name):
                continue
            # a loading screen is normal; wait it out, but not forever
            for attempt in range(self.max_checks):
                if self.log:
                    self.log.warning("%s detected, waiting %.0fs (%s/%s)",
                                     label, self.pause_seconds, attempt + 1, self.max_checks)
                self.actions.sleep(self.pause_seconds)
                self.actions.invalidate()
                if not self._seen_stable(name):
                    return GuardVerdict("waiting", f"{label} cleared")
            return GuardVerdict("fatal", f"{label} did not clear")
        return GuardVerdict("ok")


class StuckDetector:
    """Detects "nothing changed for N seconds" while we expected progress."""

    def reset(self) -> None:
        """Progress happened - restart the clock."""
        self._last = None
        self._since = time.time()

    def __init__(self, threshold_seconds: float = 300.0) -> None:
        self.threshold = max(0.05, float(threshold_seconds))
        self._last: np.ndarray | None = None
        self._since = time.time()

    def feed(self, small_gray: np.ndarray) -> bool:
        """Return True when the screen has been frozen for too long."""
        if self._last is None or self._last.shape != small_gray.shape:
            self._last = small_gray
            self._since = time.time()
            return False
        diff = float(np.abs(small_gray.astype(np.int16) - self._last.astype(np.int16)).mean())
        self._last = small_gray
        if diff > 1.0:
            self._since = time.time()
            return False
        return (time.time() - self._since) >= self.threshold
