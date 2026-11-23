"""Touch input backends: tap, swipe, hold.

Coordinates are relative (0..1) all the way from the config to the device, with
a little random jitter so automated taps do not line up pixel-perfect (some
games flag perfectly repeated touches).
"""

from __future__ import annotations

import random
import shutil
import subprocess
import time
from abc import ABC, abstractmethod
from pathlib import Path
from typing import Sequence

from ..config import Config
from ..geometry import Point


class InputError(Exception):
    pass


class InputBackend(ABC):
    name = "base"

    def __init__(self, cfg: Config, width: int, height: int) -> None:
        self.cfg = cfg
        self.width = width
        self.height = height
        self.jitter = int(cfg.num("input.jitter_px", 3))
        self.hold_ms = int(cfg.num("input.tap_hold_ms", 60))
        lo, hi = cfg.pair("input.human_delay", (0.05, 0.15))
        self.delay_range = (min(lo, hi), max(lo, hi))

    # ---------------------------------------------------------------- helpers
    def pixels(self, point: Point | Sequence[float]) -> tuple[int, int]:
        if isinstance(point, Point):
            rx, ry = point.x / self.width, point.y / self.height
        else:
            rx, ry = float(point[0]), float(point[1])
        if not (0.0 <= rx <= 1.0 and 0.0 <= ry <= 1.0):
            raise InputError(f"point outside screen: {point}")
        x = int(round(rx * self.width)) + random.randint(-self.jitter, self.jitter)
        y = int(round(ry * self.height)) + random.randint(-self.jitter, self.jitter)
        return max(0, min(self.width - 1, x)), max(0, min(self.height - 1, y))

    def human_pause(self, scale: float = 1.0) -> None:
        lo, hi = self.delay_range
        time.sleep(random.uniform(lo, hi) * scale)

    @abstractmethod
    def _tap_px(self, x: int, y: int, hold_ms: int) -> None: ...

    @abstractmethod
    def _swipe_px(self, x1: int, y1: int, x2: int, y2: int, duration_ms: int) -> None: ...

    # ---------------------------------------------------------------- public
    def tap(self, point: Point | Sequence[float], hold_ms: int | None = None) -> None:
        x, y = self.pixels(point)
        self._tap_px(x, y, self.hold_ms if hold_ms is None else int(hold_ms))
        self.human_pause()

    def swipe(
        self,
        start: Point | Sequence[float],
        end: Point | Sequence[float],
        duration_ms: int = 250,
    ) -> None:
        x1, y1 = self.pixels(start)
        x2, y2 = self.pixels(end)
        self._swipe_px(x1, y1, x2, y2, int(duration_ms))
        self.human_pause()

    def hold(self, point: Point | Sequence[float], duration_ms: int) -> None:
        """Press and hold - used for the reeling step."""
        x, y = self.pixels(point)
        self._swipe_px(x, y, x, y, int(max(1, duration_ms)))
        self.human_pause(0.3)


# ------------------------------------------------------------------- keyboard
class KeyboardMixin:
    """Keyboard primitives shared by every backend that can send keys.

    Amazing Online uses WASD to walk, E/Tab/Enter for menus and occasional
    single-key prompts (the net-cleaning QTE).  Taps are not enough there: some
    actions need a *held* key, and a released-key left stuck is the worst bug a
    bot can have, so :meth:`release_all` is always safe to call.
    """

    # keys currently held (defensive: release_all on stop/exception)
    _held: set[str]

    def _init_keyboard(self) -> None:
        self._held = set()

    def key_down(self, key: str) -> None:  # pragma: no cover - abstract
        raise NotImplementedError

    def key_up(self, key: str) -> None:  # pragma: no cover - abstract
        raise NotImplementedError

    def press(self, key: str, hold_ms: int = 40) -> None:
        key = str(key).lower()
        self.key_down(key)
        time.sleep(max(0.0, hold_ms / 1000.0))
        self.key_up(key)

    def hold_key(self, key: str, seconds: float) -> None:
        """Hold a key for N seconds, guaranteeing release on exceptions."""
        key = str(key).lower()
        self.key_down(key)
        try:
            time.sleep(max(0.0, float(seconds)))
        finally:
            self.key_up(key)

    def type_text(self, text: str, interval: float = 0.04) -> None:
        for ch in text:
            self.press(ch, hold_ms=int(interval * 1000) + 10)

    def release_all(self) -> None:
        for key in tuple(getattr(self, "_held", ())):
            try:
                self.key_up(key)
            except Exception:  # pragma: no cover - best effort
                pass

    # usable by the tasks: is anything currently pressed?
    def holding(self, key: str | None = None) -> bool:
        held = getattr(self, "_held", set())
        return bool(held) if key is None else str(key).lower() in held


# --------------------------------------------------------------------- no-op
class NullInput(KeyboardMixin, InputBackend):
    name = "null"

    def __init__(self, cfg: Config, width: int, height: int) -> None:
        super().__init__(cfg, width, height)
        self._init_keyboard()

    def _tap_px(self, x: int, y: int, hold_ms: int) -> None:
        print(f"[dry-run] tap {x},{y} ({hold_ms} ms)")

    def _swipe_px(self, x1: int, y1: int, x2: int, y2: int, duration_ms: int) -> None:
        print(f"[dry-run] swipe {x1},{y1} -> {x2},{y2} ({duration_ms} ms)")

    def key_down(self, key: str) -> None:
        self._held.add(str(key).lower())
        print(f"[dry-run] key down {key}")

    def key_up(self, key: str) -> None:
        self._held.discard(str(key).lower())
        print(f"[dry-run] key up {key}")


# ------------------------------------------------------------------------- adb
# Android key codes for the letters we send (A-Z) and a few special keys
_ADB_KEYCODES = {
    **{chr(code): code for code in range(ord("a"), ord("z") + 1)},
    **{chr(code): code - 68 for code in range(ord("A"), ord("Z") + 1)},
    "space": 62, "enter": 66, "tab": 61, "escape": 111,
    "up": 19, "down": 20, "left": 21, "right": 22,
}


class AdbInput(KeyboardMixin, InputBackend):
    name = "adb"

    def __init__(self, cfg: Config, width: int, height: int) -> None:
        super().__init__(cfg, width, height)
        self._init_keyboard()
        self.adb = str(cfg.get("device.adb_path", "adb"))
        self.serial = str(cfg.get("device.serial", "") or "")
        if shutil.which(self.adb) is None and not Path(self.adb).exists():
            raise InputError(f"adb not found ({self.adb!r})")

    def _run(self, args: list[str]) -> None:
        cmd = [self.adb]
        if self.serial:
            cmd += ["-s", self.serial]
        cmd += ["shell", *args]
        try:
            proc = subprocess.run(cmd, capture_output=True, timeout=20, check=False)
        except (subprocess.TimeoutExpired, OSError) as exc:
            raise InputError(f"android input failed: {exc}") from exc
        if proc.returncode != 0:
            raise InputError(proc.stderr.decode("utf-8", "replace").strip() or "input failed")

    def _tap_px(self, x: int, y: int, hold_ms: int) -> None:
        self._run(["input", "tap", str(x), str(y)])

    def _swipe_px(self, x1: int, y1: int, x2: int, y2: int, duration_ms: int) -> None:
        self._run(["input", "swipe", str(x1), str(y1), str(x2), str(y2), str(max(1, duration_ms))])

    # adb cannot hold a key, so a "hold" is repeated keyevents while it lasts
    def key_down(self, key: str) -> None:
        key = str(key).lower()
        self._held.add(key)
        code = _ADB_KEYCODES.get(key)
        if code is None:
            self._run(["input", "text", key])
        else:
            self._run(["input", "keyevent", str(code)])

    def key_up(self, key: str) -> None:
        self._held.discard(str(key).lower())

    def hold_key(self, key: str, seconds: float) -> None:
        key = str(key).lower()
        code = _ADB_KEYCODES.get(key)
        deadline = time.time() + max(0.0, float(seconds))
        self._held.add(key)
        try:
            while time.time() < deadline:
                if code is not None:
                    self._run(["input", "keyevent", str(code)])
                time.sleep(0.08)
        finally:
            self._held.discard(key)


# --------------------------------------------------------------------- desktop
class DesktopInput(KeyboardMixin, InputBackend):
    name = "desktop"

    def __init__(self, cfg: Config, width: int, height: int) -> None:
        super().__init__(cfg, width, height)
        self._init_keyboard()
        try:
            import pyautogui  # noqa: WPS433
        except ImportError as exc:  # pragma: no cover
            raise InputError("desktop input needs pyautogui") from exc
        pyautogui.FAILSAFE = False
        self._gui = pyautogui

    def _tap_px(self, x: int, y: int, hold_ms: int) -> None:
        self._gui.mouseDown(x, y)
        time.sleep(hold_ms / 1000.0)
        self._gui.mouseUp(x, y)

    def _swipe_px(self, x1: int, y1: int, x2: int, y2: int, duration_ms: int) -> None:
        self._gui.moveTo(x1, y1)
        self._gui.mouseDown()
        steps = max(6, duration_ms // 12)
        self._gui.moveTo(x2, y2, duration=max(0.05, duration_ms / 1000.0), tween=None)
        self._gui.mouseUp()
        _ = steps

    def key_down(self, key: str) -> None:
        key = str(key).lower()
        if key not in self._held:
            self._gui.keyDown(key)
            self._held.add(key)

    def key_up(self, key: str) -> None:
        key = str(key).lower()
        if key in self._held:
            self._gui.keyUp(key)
            self._held.discard(key)

    def type_text(self, text: str, interval: float = 0.04) -> None:
        self._gui.write(text, interval=interval)


def build_input(cfg: Config, width: int, height: int, dry_run: bool = False) -> InputBackend:
    if dry_run:
        return NullInput(cfg, width, height)
    backend = str(cfg.get("device.backend", "adb")).lower()
    if backend in {"adb", "images", "video"}:
        # replaying screenshots but tapping a real device is a valid workflow
        # (tune on saved frames, replay the taps on the emulator)
        if backend != "adb" and cfg.boolean("device.replay_input", False):
            return AdbInput(cfg, width, height)
        return AdbInput(cfg, width, height) if backend == "adb" else NullInput(cfg, width, height)
    if backend == "desktop":
        return DesktopInput(cfg, width, height)
    raise InputError(f"no input backend for device.backend={backend!r}")

