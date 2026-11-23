"""Hotkeys and pause control.

A long unattended run still needs a human override.  ``F8`` pauses/resumes,
``F9`` resets the weight counter, ``F12`` stops immediately - the same keys the
prototype used, because muscle memory matters when a run goes wrong.

``pynput`` is optional: without it (headless boxes, CI) the watcher degrades to
a no-op and the CLI keeps working.
"""

from __future__ import annotations

import logging
from pathlib import Path
from typing import Callable

log = logging.getLogger("bot.watch")

HOTKEYS = {
    "f8": "toggle_pause",
    "f9": "reset_counter",
    "f12": "stop",
}


class HotkeyWatcher:
    """Global hotkeys -> callbacks.  Safe to construct when pynput is missing."""

    def __init__(self, callbacks: dict[str, Callable[[], None]]) -> None:
        self.callbacks = callbacks
        self._listener = None
        self.available = False
        try:
            from pynput import keyboard  # noqa: WPS433 - optional dependency
        except Exception:
            log.debug("pynput not available - hotkeys disabled")
            return

        def on_press(key) -> None:
            name = getattr(key, "name", "") or ""
            action = HOTKEYS.get(str(name).lower())
            if not action:
                return
            callback = self.callbacks.get(action)
            if not callback:
                return
            try:
                callback()
            except Exception:  # pragma: no cover - never let a hotkey crash the run
                log.exception("hotkey %s failed", action)

        try:
            self._listener = keyboard.Listener(on_press=on_press)
            self._listener.daemon = True
            self._listener.start()
            self.available = True
        except Exception:  # pragma: no cover - permissions on some OSes
            log.warning("could not install global hotkeys", exc_info=True)

    def stop(self) -> None:
        if self._listener is not None:
            try:
                self._listener.stop()
            except Exception:  # pragma: no cover
                pass


class PauseGate:
    """Cooperative pause: tasks call :meth:`checkpoint` in their waits."""

    def __init__(self) -> None:
        self.paused = False
        self.stopped = False

    def pause(self) -> None:
        self.paused = True

    def resume(self) -> None:
        self.paused = False

    def toggle(self) -> bool:
        self.paused = not self.paused
        return self.paused

    def stop(self) -> None:
        self.stopped = True

    def checkpoint(self) -> bool:
        """Return False when the run should stop."""
        return not self.stopped


def watch_pid_file(path: str | Path) -> Path:
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(str(__import__("os").getpid()), encoding="utf-8")
    return p
