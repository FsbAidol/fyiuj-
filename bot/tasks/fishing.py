"""The fishing loop: cast -> wait for bite -> reel -> collect -> repeat.

State machine, one poll per tick so the orchestrator stays responsive to hunger,
weight limit and anti-AFK:

``IDLE``      --(cast button)--> ``WAITING``
``WAITING``   --(bite marker / motion spike)--> ``REELING``
``REELING``   --(reel button held/tapped until catch)--> ``COLLECTING``
``COLLECTING``--(loot popup dismissed)--> ``IDLE``
any state     --(net broken icon)--> ``BROKEN_NET`` (orchestrator restocks)
"""

from __future__ import annotations

import random
import time
from enum import Enum
from typing import Any

from ..actions import Actions
from ..config import Config
from ..scripts import Script, ScriptRunner
from ..state import BotState
from ..vision import Match
from .base import BaseTask


class Phase(str, Enum):
    IDLE = "idle"
    WAITING = "waiting"
    REELING = "reeling"
    COLLECTING = "collecting"
    BROKEN_NET = "broken_net"
    FAILED = "failed"


class FishingTask(BaseTask):
    name = "fishing"

    def __init__(self, cfg: Config, actions: Actions, state: BotState, log: Any) -> None:
        super().__init__(cfg, actions, state, log)
        self.phase = Phase.IDLE
        self.phase_since = time.time()
        self.net_broken = False
        self._cast_attempts = 0
        self._reel_deadline = 0.0
        self._bite_prev: Any = None
        self._ocr: Any = None
        self._catches_since_read = 0
        self._bite_was_seen = False
        # set by the runner: clears junk / stray windows when casting is blocked
        self.blocked_handler: Any = None

    # ------------------------------------------------------------------ helpers
    def _enter(self, phase: Phase) -> None:
        if phase != self.phase:
            self.log.debug("fishing: %s -> %s", self.phase.value, phase.value)
        self.phase = phase
        self.phase_since = time.time()

    def _elapsed(self) -> float:
        return time.time() - self.phase_since

    def _cfg_list(self, dotted: str) -> list[str]:
        value = self.cfg.get(dotted) or []
        return [str(v) for v in value] if isinstance(value, (list, tuple)) else [str(value)]

    # ------------------------------------------------------------------ cast
    def _try_enter_fishing(self) -> bool:
        entered = self._cfg_list("fishing.enter_templates")
        if not entered:
            return True
        match = self.a.wait_any(entered, timeout=self.cfg.num("fishing.cast_timeout", 6.0))
        if match is None:
            return False
        self.a.tap(match)
        self.a.sleep(0.4)
        return True

    def cast(self) -> bool:
        cast_btn = self.template("fishing.cast_template", "cast_button")
        timeout = self.cfg.num("fishing.cast_timeout", 6.0)
        retries = int(self.cfg.num("fishing.cast_retries", 3))
        for attempt in range(1, retries + 1):
            if not self._try_enter_fishing():
                self.log.debug("cast: could not enter fishing screen (attempt %s)", attempt)
                self.a.sleep(0.6)
                continue
            match = self.a.wait_for(cast_btn, timeout=timeout)
            if match is None:
                self.log.debug("cast button %r not visible (attempt %s) trying recovery",
                               cast_btn, attempt)
                handler = getattr(self, "blocked_handler", None)
                if handler is not None and handler():
                    continue
                self.save_debug(f"cast-missing-{attempt}")
                # a leftover bag/trunk window blocks fishing: close it and retry
                self.fallback()
                self.a.sleep(0.5)
                continue
            self.a.tap(match)
            if self.cfg.boolean("fishing.cast_verify", True):
                timeout_verify = self.cfg.num("fishing.cast_verify_timeout", 1.5)
                if not self.a.wait_gone(cast_btn, timeout=timeout_verify, poll_hz=6.0):
                    self.log.info("cast did not start (button still visible) - recovery")
                    self._cast_attempts = attempt
                    handler = getattr(self, "blocked_handler", None)
                    if handler is not None:
                        handler()
                    continue
            self.state.casts += 1
            self.state.touch()
            self._cast_attempts = attempt
            self.log.info("cast #%s", self.state.casts)
            self._enter(Phase.WAITING)
            return True
        self._enter(Phase.FAILED)
        return False

    # ------------------------------------------------------------------ bite
    def _detect_bite(self) -> bool:
        strategy = str(self.cfg.get("fishing.bite.strategy", "template"))
        bite_tpl = self.cfg.get("fishing.bite.template", "bite")
        if strategy == "template" and bite_tpl:
            region = self.cfg.region("fishing.bite.region")
            if self.a.exists(str(bite_tpl), region):
                return True
            return False
        # motion strategy: water boils / float dips -> motion spike in region
        region = self.cfg.region("fishing.bite.region")
        score = self.a.motion(region)
        threshold = self.cfg.num("fishing.bite.diff_threshold", 0.006)
        self.log.debug("bite motion %.4f (thr %.4f)", score, threshold)
        return score >= threshold

    def wait_for_bite(self) -> bool:
        timeout = self.cfg.num("fishing.bite.timeout", 60.0)
        poll_hz = self.cfg.num("fishing.bite.poll_hz", 6.0)
        interval = 1.0 / max(1.0, poll_hz)
        deadline = time.time() + timeout
        self._bite_prev = None
        while time.time() < deadline:
            self.a.invalidate()
            if self._detect_bite():
                self.log.info("bite!")
                self._bite_was_seen = True
                self._enter(Phase.REELING)
                return True
            # a broken net while waiting ends the attempt immediately
            if self.check_broken_net():
                return False
            self.a.sleep(interval)
        self.log.info("no bite within %.0fs - recasting", timeout)
        return False

    # ------------------------------------------------------------------ reel
    def _bite_visible(self) -> bool:
        bite_tpl = self.template("fishing.bite.template", "bite")
        return bool(bite_tpl) and self.a.templates.has(bite_tpl) and self.a.exists(bite_tpl)

    def _catch_on_screen(self) -> bool:
        """True when the catch UI appeared (collect button / catch icon)."""
        collect = self.template("fishing.collect.template", "collect")
        if collect and self.a.templates.has(collect) and self.a.exists(collect):
            return True
        icon = self.template("fishing.indicators.catch_icon", "")
        if icon and self.a.templates.has(icon) and self.a.exists(icon):
            return True
        return False

    def _reel_finished(self) -> bool:
        """The bite marker vanished -> the fish is landed (or lost)."""
        if self._catch_on_screen():
            return True
        if self._bite_was_seen and not self._bite_visible():
            return True
        return False

    def reel(self) -> bool:
        mode = str(self.cfg.get("fishing.reel.mode", "hold"))
        region = self.cfg.region("fishing.reel.region") if self.cfg.get("fishing.reel.region") else None
        max_seconds = self.cfg.num("fishing.reel.max_seconds", 35.0)
        deadline = time.time() + max_seconds
        catch_icon = self.template("fishing.indicators.catch_icon", "")
        button = self.cfg.get("fishing.reel.button", "reel_button")
        indicator = None
        if button:
            indicator = self.a.find(str(button))

        self._reel_deadline = deadline
        if mode == "hold" and region is not None:
            # hold the reeling zone in chunks so we can watch for the catch
            chunk = int(self.cfg.num("fishing.reel.chunk_ms", 400))
            while time.time() < deadline:
                self.a.hold(region.center(*self.a.size), chunk)
                if self._reel_finished():
                    self.log.debug("reel finished after %.1fs", time.time() - (deadline - max_seconds))
                    break
                if self.check_broken_net():
                    return False
                # keep reeling only while the catch progress is still moving
        else:
            interval = max(0.05, self.cfg.num("fishing.reel.tap_interval_ms", 140) / 1000.0)
            while time.time() < deadline:
                if region is not None:
                    self.a.tap_center(region)
                elif indicator is not None:
                    self.a.tap(indicator)
                elif button:
                    self.a.tap_template(str(button))
                else:
                    self.a.tap([0.75, 0.75])
                if self._reel_finished():
                    break
                if self.check_broken_net():
                    return False
                self.a.sleep(interval)

        self._enter(Phase.COLLECTING)
        return self.collect()

    # ------------------------------------------------------------------ collect
    def collect(self) -> bool:
        collect_tpl = self.template("fishing.collect.template", "collect")
        timeout = self.cfg.num("fishing.collect.timeout", 5.0)
        optional = self.cfg.boolean("fishing.collect.optional", True)
        match = self.a.wait_for(collect_tpl, timeout=timeout)
        if match is None:
            if optional:
                self.log.debug("no collect button; assuming auto-loot")
                self._on_catch()
                return True
            return False
        self.a.tap(match)
        self.a.sleep(0.4)
        self._on_catch()
        return True

    def _on_catch(self) -> None:
        kg = self.estimate_catch_kg()
        self.state.add_catch(kg)
        self._catches_since_read += 1
        book = getattr(self, "book", None)
        if book is not None:
            try:
                book.note_catch(kg)
                every = int(self.cfg.num("weight.read_interval_catches", 5))
                if every and self._catches_since_read >= every:
                    total = self.read_weight_total()
                    if total is not None:
                        book.observe_total(total)
                    self._catches_since_read = 0
            except Exception:  # noqa: BLE001 - bookkeeping must never break fishing
                self.log.debug("weight book update failed", exc_info=True)
        self._bite_was_seen = False
        self.log.info("caught %s ~%.2f kg (total %.2f kg, %s fish)", "fish", kg,
                      self.state.total_kg, self.state.fish_count)
        self._enter(Phase.IDLE)
        self.a.sleep(self.rand("fishing.recast_delay", (1.0, 2.0)))

    def read_weight_total(self) -> float | None:
        """Current total weight, if the game exposes it (bar ratio * limit)."""
        limit = self.cfg.num("safety.weight_limit_kg", 500.0)
        region = self.cfg.region("hud.weight.region")
        hsv = self.cfg.get("hud.weight.fill_hsv")
        if region is not None and hsv:
            from ..vision import read_bar

            reading = read_bar(self.a.frame(fresh=True), region, hsv)
            if reading.found and reading.ratio > 0:
                return round(reading.ratio * limit, 2)
        if str(self.cfg.get("weight.strategy", "bar")) == "ocr" and self._ocr is not None:
            return self._ocr.read_number(self.a.frame(fresh=True), region)
        return None

    def estimate_catch_kg(self) -> float:
        """Best-effort weight of one catch.

        Priority: explicit OCR of the counter (if enabled & available) ->
        average weight from config -> 1 kg.  The 500 kg budget is conservative
        on purpose: the average is taken slightly low so we stop a catch early
        rather than overshoot.
        """
        strategy = str(self.cfg.get("weight.strategy", "bar"))
        if strategy == "ocr":
            ocr = getattr(self, "_ocr", None)
            if ocr is None:
                from ..vision import Ocr

                ocr = Ocr(str(self.cfg.get("weight.ocr_lang", "eng")))
                self._ocr = ocr
            total = ocr.read_number(self.a.frame(fresh=True), self.cfg.region("hud.weight.region"))
            if total is not None:
                delta = total - self.state.total_kg
                if 0 <= delta <= 50:
                    return delta
        return max(0.1, self.cfg.num("weight.avg_kg_per_fish", 1.0))

    # ------------------------------------------------------------------ net
    def check_broken_net(self) -> bool:
        tpl = self.template("fishing.indicators.net_broken", "net_broken")
        if not tpl:
            return False
        if self.a.exists(tpl):
            if not self.net_broken:
                self.log.warning("net broke!")
                self.state.nets_broken += 1
            self.net_broken = True
            self._enter(Phase.BROKEN_NET)
            return True
        return False

    def net_restocked(self) -> None:
        """Called by the orchestrator after the trunk routine succeeded."""
        self.net_broken = False
        self._enter(Phase.IDLE)
        self.a.sleep(self.rand("fishing.after_broken_delay", (1.5, 3.0)))

    # ------------------------------------------------------------------ tick
    def tick(self) -> Phase:
        if self.check_broken_net():
            return self.phase
        if self.phase in (Phase.IDLE, Phase.FAILED):
            if not self.cast():
                self.phase_since = time.time()
            return self.phase
        if self.phase is Phase.WAITING:
            if not self.wait_for_bite():
                self._enter(Phase.IDLE)
                self.a.sleep(0.5)
            return self.phase
        if self.phase is Phase.REELING:
            if not self.reel():
                self._enter(Phase.IDLE)
            return self.phase
        return self.phase

    def fallback(self) -> None:
        """Recovery when something unexpected covered the screen."""
        close = ["close_button", "close_popup", "ok_button"]
        for name in close:
            if self.a.tap_template(name, timeout=0.0):
                self.log.info("closed unexpected popup with %r", name)
                return
        self.log.debug("no known popup to close")
