"""Anti-AFK keep-alive.

While the bot fishes it is *already* sending input, but games usually kick
players for "no meaningful activity" or for a static camera.  This task makes a
harmless camera swipe every ``anti_afk.interval`` seconds (+/- jitter) so the
player is never flagged, and it also serves as a heartbeat between long bite
waits.

The swipe is deliberately small and always returns the camera to the starting
position, so fishing continues seamlessly.
"""

from __future__ import annotations

import random
import time
from typing import Any

from ..actions import Actions
from ..config import Config
from ..state import BotState
from .base import BaseTask


class AntiAfkTask(BaseTask):
    name = "anti_afk"

    def __init__(self, cfg: Config, actions: Actions, state: BotState, log: Any) -> None:
        super().__init__(cfg, actions, state, log)
        self.enabled = cfg.boolean("anti_afk.enabled", True)
        self.interval = max(5.0, cfg.num("anti_afk.interval", 45.0))
        jitter = cfg.num("anti_afk.jitter", 8.0)
        self.jitter = max(0.0, jitter)
        self.last_action = time.time()
        self._next = self._schedule()

    # ------------------------------------------------------------------ timing
    def _schedule(self) -> float:
        return time.time() + max(2.0, self.interval + random.uniform(-self.jitter, self.jitter))

    def due(self) -> bool:
        return self.enabled and time.time() >= self._next

    def seconds_left(self) -> float:
        return max(0.0, self._next - time.time())

    # ------------------------------------------------------------------ action
    def poke(self) -> None:
        mode = str(self.cfg.get("anti_afk.mode", "swipe"))
        try:
            if mode == "tap":
                self.a.tap(self.cfg.point("anti_afk.point") or [0.5, 0.62])
            else:
                swipe = self.cfg.get("anti_afk.swipe") or [[0.48, 0.60], [0.52, 0.60]]
                start, end = swipe[0], swipe[1]
                duration = int(self.cfg.num("anti_afk.duration_ms", 220))
                self.a.swipe(start, end, duration)
                self.a.sleep(0.15)
                self.a.swipe(end, start, duration)  # put the camera back
        except Exception as exc:  # noqa: BLE001 - heartbeats must never kill a run
            self.log.debug("anti-afk gesture failed: %s", exc)
            return
        self.state.anti_afk_actions += 1
        self.state.touch()
        self.last_action = time.time()
        self._next = self._schedule()
        self.log.debug("anti-afk poke #%s (next in ~%.0fs)", self.state.anti_afk_actions,
                       self._next - time.time())

    def maybe_poke(self) -> bool:
        if self.due():
            self.poke()
            return True
        return False
