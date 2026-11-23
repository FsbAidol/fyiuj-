"""Hunger / thirst management.

The HUD bars are read by colour (see :func:`bot.vision.read_bar`).  When a bar
drops below ``low`` the configured YAML routine runs: open the bag, eat the
right item, close the bag.  A cooldown prevents the bot from fighting a bar that
is already refilling, and the routine is skipped while the cast/reel animation
is on screen (the game ignores taps then and we would waste food).
"""

from __future__ import annotations

from typing import Any

from ..actions import Actions
from ..config import Config
from ..scripts import Script, ScriptRunner
from ..state import BotState
from .base import BaseTask


class SurvivalTask(BaseTask):
    name = "survival"

    def __init__(self, cfg: Config, actions: Actions, state: BotState, log: Any,
                 hooks: dict[str, Any] | None = None) -> None:
        super().__init__(cfg, actions, state, log, hooks)
        self.enabled = cfg.boolean("survival.enabled", True)
        self.runner = ScriptRunner(actions, log, hooks)
        self._scripts: dict[str, Script | None] = {}
        self._last_action: dict[str, float] = {"thirst": 0.0, "food": 0.0}
        self.cooldown = cfg.num("survival.cooldown_seconds", 20.0)
        self._critical_logged = False

    # ------------------------------------------------------------------ need?
    def needs(self) -> dict[str, float]:
        """Return {kind: ratio} for every bar under its low threshold."""
        out: dict[str, float] = {}
        for kind in ("thirst", "food"):
            reading = self.read_bar(kind)
            if not reading.found:
                continue
            low = self.cfg.num(f"hud.{kind}.low", 0.25)
            if reading.ratio <= low:
                out[kind] = reading.ratio
        return out

    def critical(self) -> bool:
        for kind in ("thirst", "food"):
            reading = self.read_bar(kind)
            if reading.found and reading.ratio <= self.cfg.num(f"hud.{kind}.critical", 0.12):
                return True
        return False

    def bar_snapshot(self) -> dict[str, str]:
        out: dict[str, str] = {}
        for kind in ("thirst", "food"):
            reading = self.read_bar(kind)
            out[kind] = f"{reading.ratio:.0%}" if reading.found else "n/a"
        return out

    # ------------------------------------------------------------------ run
    def _script_for(self, kind: str) -> Script | None:
        if kind in self._scripts:
            return self._scripts[kind]
        key = "survival.eat_script" if kind == "food" else "survival.drink_script"
        path = self.cfg.path_of(key)
        script = None
        if path.exists():
            try:
                script = Script.from_file(path)
            except Exception as exc:  # noqa: BLE001 - config path, report not crash
                self.log.error("cannot load %s (%s): %s", kind, path, exc)
        else:
            self.log.warning("script for %s not found: %s", kind, path)
        self._scripts[kind] = script
        return script

    def handle(self) -> bool:
        """Run one recovery routine if needed.  Returns True when it acted."""
        if not self.enabled or not self.cfg.boolean("survival.enabled", True):
            return False
        needed = self.needs()
        if not needed:
            self._critical_logged = False
            return False
        # most urgent first
        kind = min(needed, key=lambda k: needed[k])
        if self._on_cooldown(kind):
            return False
        script = self._script_for(kind)
        if script is None:
            return False
        self.log.info("HUD %s at %s -> running %s routine", kind, f"{needed[kind]:.0%}", kind)
        ok = self.runner.run(script)
        self._last_action[kind] = self.a_clock()
        if ok:
            if kind == "food" and self.cfg.boolean("survival.notify_on_act", False):
                pass
            if kind == "food":
                self.state.snacks_eaten += 1
            else:
                self.state.drinks += 1
            self.state.touch()
        else:
            self.log.warning("%s routine failed; will retry after cooldown", kind)
            self.save_debug(f"survival-{kind}-failed")
        self.a.invalidate()
        return True

    def _on_cooldown(self, kind: str) -> bool:
        return (self.a_clock() - self._last_action.get(kind, 0.0)) < self.cooldown

    @staticmethod
    def a_clock() -> float:
        import time

        return time.time()
