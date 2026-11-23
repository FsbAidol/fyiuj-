"""Restock nets from the car trunk when the current net breaks.

Flow (all steps overridable in ``config.yaml`` / ``scripts/trunk_restock.yaml``):

1. walk to the car (``trunk.walk_script``: taps/teleport routine)
2. open the trunk (``trunk.open_template``)
3. take a net (``trunk.take_steps`` - usually ``tap_template: net_item``)
4. close the trunk / get back to the water
5. report success so the fishing task can cast again

``trunk.min_nets`` guards against trying to fish with an empty trunk: if the
inventory count of nets is below it, the bot stops instead of flailing.
"""

from __future__ import annotations

from typing import Any

from ..actions import Actions
from ..config import Config
from ..scripts import Script, ScriptRunner
from ..state import BotState
from .base import BaseTask


class TrunkTask(BaseTask):
    name = "trunk"

    def __init__(self, cfg: Config, actions: Actions, state: BotState, log: Any,
                 hooks: dict[str, Any] | None = None) -> None:
        super().__init__(cfg, actions, state, log, hooks)
        self.runner = ScriptRunner(actions, log, hooks)
        self._script: Script | None | bool = False

    def enabled(self) -> bool:
        return self.cfg.boolean("trunk.enabled", True)

    # ------------------------------------------------------------------ count
    def nets_in_pockets(self) -> int | None:
        """How many spare nets are on the character (``None`` = unknown)."""
        tpl = self.template("trunk.net_template", "net_item")
        region = self.cfg.region("trunk.nets_region")
        if not tpl or region is None:
            return None
        try:
            return self.a.count(tpl, region)
        except Exception:  # pragma: no cover - defensive
            return None

    # ------------------------------------------------------------------ restock
    def _load_script(self) -> Script | None:
        if self._script is not False:
            return self._script  # type: ignore[return-value]
        path = self.cfg.path_of("trunk.script", "scripts/trunk_restock.yaml")
        script = None
        if path.exists():
            try:
                script = Script.from_file(path)
            except Exception as exc:  # noqa: BLE001
                self.log.error("cannot load trunk script %s: %s", path, exc)
        else:
            self.log.warning("trunk script missing: %s", path)
        self._script = script
        return script

    def _close_windows_before(self) -> None:
        """Leftover windows must not block the trunk routine."""
        for name in ("trunk_window", "close_button"):
            if self.a.exists(name):
                self.a.tap_template(name)
                self.a.sleep(0.3)

    def net_is_broken(self) -> bool:
        """Is the "net torn" indicator actually on screen right now?"""
        tpl = self.template("fishing.indicators.net_broken", "net_broken")
        if not tpl:
            return True
        return self.a.exists(tpl)

    def restock(self, reason: str = "net broke") -> bool:
        if not self.enabled():
            self.log.error("%s but trunk restocking is disabled - stopping would be safer", reason)
            return False
        limit = int(self.cfg.num("trunk.max_per_session", 12))
        if self.state.trunk_trips >= limit:
            self.log.error("trunk trip limit reached (%s) - stopping restocks", limit)
            return False

        before = self.nets_in_pockets()
        min_nets = int(self.cfg.num("trunk.min_nets", 1))
        if before is not None and before < min_nets:
            self.log.error("trunk restock: only %s spare net(s) visible (min %s)", before, min_nets)
            # still try once - the count may be visual noise

        script = self._load_script()
        if script is None:
            return False

        self.log.info("restocking nets from the trunk (%s)", reason)
        # the trunk routine may leave a window open (screenshot scripts); make
        # sure we are back on the water screen before fishing again
        self._close_windows_before()
        ok = self.runner.run(script)
        self.state.trunk_trips += 1
        self.a.invalidate()
        if not ok:
            self.log.error("trunk restock routine failed")
            self.save_debug("trunk-failed")
            return False

        after = self.nets_in_pockets()
        if before is not None and after is not None and after <= before:
            self.log.warning("no new net detected in pockets (before=%s after=%s)", before, after)

        # the only trustworthy success signal: the broken-net indicator is gone
        if not self.net_is_broken():
            self.log.info("net indicator cleared - net is usable again")
        else:
            self.log.warning("net still shows as broken after restocking - "
                             "check the trunk routine/templates")
        self.state.nets_restocked += 1
        self.state.touch()
        self.log.info("new net ready (restocks=%s)", self.state.nets_restocked)
        return True
