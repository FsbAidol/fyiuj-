"""Persistent run state: how much fish we caught, why we stopped, etc.

The 500 kg auto-stop is a hard requirement, so the counter is written to disk
atomically after every catch.  If the bot is killed mid-run the next start
(resume mode) still knows how much was collected.
"""

from __future__ import annotations

import json
import os
import time
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any


@dataclass
class BotState:
    started_at: float = field(default_factory=time.time)
    updated_at: float = field(default_factory=time.time)
    total_kg: float = 0.0
    fish_count: int = 0
    casts: int = 0
    caught: int = 0
    missed: int = 0
    nets_broken: int = 0
    nets_restocked: int = 0
    trunk_trips: int = 0
    trash_cleared: int = 0
    snacks_eaten: int = 0
    drinks: int = 0
    anti_afk_actions: int = 0
    consecutive_failures: int = 0
    stop_reason: str = ""
    running: bool = False
    paused: bool = False
    weight_book: dict[str, Any] = field(default_factory=dict)

    # ------------------------------------------------------------------ time
    @property
    def uptime(self) -> float:
        return max(0.0, time.time() - self.started_at)

    def uptime_str(self) -> str:
        total = int(self.uptime)
        h, rem = divmod(total, 3600)
        m, s = divmod(rem, 60)
        return f"{h:02d}:{m:02d}:{s:02d}"

    # ------------------------------------------------------------------ counters
    def add_catch(self, kg: float) -> None:
        self.total_kg = round(self.total_kg + max(0.0, kg), 3)
        self.caught += 1
        self.fish_count += 1
        self.consecutive_failures = 0
        self.touch()

    def add_failure(self) -> int:
        self.consecutive_failures += 1
        self.missed += 1
        self.touch()
        return self.consecutive_failures

    def touch(self) -> None:
        self.updated_at = time.time()

    def summary(self) -> str:
        return (
            f"uptime={self.uptime_str()} fish={self.fish_count} kg={self.total_kg:.2f} "
            f"broken_nets={self.nets_broken} restocks={self.nets_restocked} "
            f"trash={self.trash_cleared} afk={self.anti_afk_actions}"
        )

    # ------------------------------------------------------------------ io
    def to_dict(self) -> dict[str, Any]:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "BotState":
        known = {f for f in cls.__dataclass_fields__}  # type: ignore[attr-defined]
        return cls(**{k: v for k, v in data.items() if k in known})


class StateStore:
    """Atomic JSON persistence for :class:`BotState`."""

    def __init__(self, path: str | Path) -> None:
        self.path = Path(path)

    def load(self) -> BotState | None:
        if not self.path.exists():
            return None
        try:
            with self.path.open("r", encoding="utf-8") as fh:
                return BotState.from_dict(json.load(fh))
        except (OSError, json.JSONDecodeError, TypeError):
            return None

    def save(self, state: BotState) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        state.touch()
        tmp = self.path.with_suffix(self.path.suffix + ".tmp")
        with tmp.open("w", encoding="utf-8") as fh:
            json.dump(state.to_dict(), fh, indent=2)
            fh.flush()
            os.fsync(fh.fileno())
        os.replace(tmp, self.path)

    def reset(self) -> BotState:
        state = BotState(running=True)
        self.save(state)
        return state
