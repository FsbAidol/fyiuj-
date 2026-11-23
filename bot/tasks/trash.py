"""Pull trash / junk off the net while fishing in an undefined territory.

The junk appears on the net as a small sprite (``trash.template``).  When enough
junk accumulates (``trash.trigger_count``) the bot drags it off using the
configured routine - by default a short drag towards the water - then re-casts.

Keeping junk off the net matters because a loaded net snaps, and a snapped net
means a trip to the trunk: doing it proactively saves time and nets.
"""

from __future__ import annotations

from typing import Any

from ..actions import Actions
from ..config import Config
from ..scripts import Script, ScriptRunner
from ..state import BotState
from .base import BaseTask

# default gesture: flick the junk off the net towards open water
DEFAULT_STEPS = [
    {"swipe": {"from": [0.40, 0.35], "to": [0.44, 0.62], "ms": 260}},
    {"wait": 0.6},
]


class TrashTask(BaseTask):
    name = "trash"

    def __init__(self, cfg: Config, actions: Actions, state: BotState, log: Any,
                 hooks: dict[str, Any] | None = None) -> None:
        super().__init__(cfg, actions, state, log, hooks)
        self.runner = ScriptRunner(actions, log, hooks)
        self._script: Script | None | bool = False
        steps = cfg.get("trash.steps")
        if isinstance(steps, list) and steps:
            self._inline = Script.from_steps("clear_trash", steps)
        else:
            self._inline = None

    def counts(self) -> int:
        tpl = self.template("trash.template", "trash_on_net")
        if not tpl:
            return 0
        region = self.cfg.region("trash.region")
        try:
            return self.a.count(tpl, region)
        except Exception:  # pragma: no cover
            return 0

    def needed(self) -> bool:
        if not self.cfg.boolean("trash.enabled", True):
            return False
        trigger = int(self.cfg.num("trash.trigger_count", 2))
        return self.counts() >= trigger

    def _load_script(self) -> Script | None:
        if self._inline is not None:
            return self._inline
        if self._script is not False:
            return self._script  # type: ignore[return-value]
        path = self.cfg.path_of("trash.script", "scripts/clear_trash.yaml")
        script = None
        if path.exists():
            script = Script.from_file(path)
        else:
            self.log.debug("no trash script at %s, using built-in swipe", path)
            script = Script.from_steps("clear_trash", DEFAULT_STEPS)
        self._script = script
        return script

    def clear_net_qte(self) -> bool:
        """Preferred path: interactive net cleaning (hold the shown letter).

        Modern Amazing Online asks for a random Latin letter while junk is
        removed; dragging with a swipe does nothing then.  We try the QTE first
        and fall back to the scripted swipe when no prompt appears.
        """
        if not self.cfg.boolean("trash.qte", True):
            return False
        if not self.cfg.boolean("qte.enabled", True):
            return False
        self.log.info("cleaning the net: looking for the prompt letter...")
        result = self.a.qte_hold(
            timeout=self.cfg.num("qte.appear_timeout_seconds", 8.0),
            hold_timeout=self.cfg.num("qte.hold_timeout_seconds", 28.0),
        )
        self.log.info("net cleaning result: %s", result)
        if result == "released":
            self.state.trash_cleared += 1
            self.state.touch()
            return True
        if result == "none":
            return False  # no prompt -> caller may fall back to the swipe script
        self.save_debug(f"qte-{result}")
        return False

    def clear(self) -> bool:
        if self.clear_net_qte():
            return True
        script = self._load_script()
        if script is None:
            return False
        self.log.info("clearing trash off the net (%s item(s))", self.counts())
        ok = self.runner.run(script)
        self.a.invalidate()
        if ok:
            self.state.trash_cleared += 1
            self.state.touch()
            self.a.sleep(self.rand("trash.after_delay", (0.5, 1.2)))
        else:
            self.save_debug("trash-failed")
        return ok
