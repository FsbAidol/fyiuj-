"""Task building blocks shared by the behaviours."""

from __future__ import annotations

import random
import time
from typing import Any, Callable, Mapping

from ..actions import Actions
from ..config import Config
from ..state import BotState
from ..vision import BarReading, read_bar


class BaseTask:
    name = "task"

    def __init__(
        self,
        cfg: Config,
        actions: Actions,
        state: BotState,
        log: Any,
        hooks: Mapping[str, Callable[[], None]] | None = None,
    ) -> None:
        self.cfg = cfg
        self.a = actions
        self.state = state
        self.log = log
        self.hooks: dict[str, Callable[[], None]] = dict(hooks or {})

    # ------------------------------------------------------------------ utils
    def rand(self, key: str, default: tuple[float, float] = (0.5, 1.2)) -> float:
        lo, hi = self.cfg.pair(key, default)
        return random.uniform(min(lo, hi), max(lo, hi))

    def region_of(self, key: str):
        return self.cfg.region(key)

    def read_bar(self, key: str, prefix: str = "hud") -> BarReading:
        region = self.cfg.region(f"{prefix}.{key}.region")
        hsv = self.cfg.get(f"{prefix}.{key}.fill_hsv")
        if region is None or not hsv:
            return BarReading(0.0, False)
        return read_bar(self.a.frame(fresh=True), region, hsv)

    def template(self, key: str, default: str = "") -> str:
        return str(self.cfg.get(key, default) or default)

    def save_debug(self, tag: str) -> None:
        directory = self.cfg.path_of("logging.debug_dir", "debug")
        try:
            self.a.save_debug(directory, tag)
        except Exception:  # pragma: no cover - debug must never crash a run
            self.log.debug("could not save debug frame %s", tag)


def now() -> float:
    return time.time()
