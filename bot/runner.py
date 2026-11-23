"""The orchestrator.

Priority order on every tick (higher wins):

1. **weight limit** - 500 kg collected -> stop everything
2. **broken net**   - restock from the car trunk, then fish again
3. **hunger/thirst**- eat/drink, then resume
4. **trash on net**  - clean it, then resume
5. **anti-AFK**      - heartbeat when idle for a while
6. **fishing**       - the actual loop

Everything is bounded: consecutive failures, runtime and stops requested with
Ctrl+C all end the run and are written to the state file, so a night session
never burns resources silently.
"""

from __future__ import annotations

import logging
import signal
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .actions import Actions
from .config import Config
from .device.capture import CaptureError, build_capture
from .device.input import build_input
from .guards import ScreenGuard, StuckDetector
from .logging_setup import setup_logging
from .notify import Notifier
from .state import BotState, StateStore
from .tasks import AntiAfkTask, FishingTask, Phase, SurvivalTask, TrashTask, TrunkTask
from .vision import GrayFrame, TemplateStore
from .weight import WeightBook

STOP_WEIGHT = "weight_limit_reached"
STOP_RUNTIME = "runtime_limit"
STOP_FAILURES = "too_many_failures"
STOP_SIGNAL = "interrupted"
STOP_GUARD = "screen_guard_fatal"
STOP_STUCK = "stuck_no_progress"


@dataclass
class RunOptions:
    resume: bool = False
    dry_run: bool = False
    max_seconds: float = 0.0
    max_kg: float = 0.0
    hotkeys: bool = True


class BotRunner:
    def __init__(
        self,
        cfg: Config,
        options: RunOptions | None = None,
        capture: Any = None,
        touch: Any = None,
    ) -> None:
        self.cfg = cfg
        self.options = options or RunOptions()
        self.log = logging.getLogger("bot")
        self.stop_requested = False
        self.stop_reason = ""
        self.store = StateStore(cfg.path_of("state.path", "state/state.json"))

        self.capture = capture or build_capture(cfg)
        width, height = self.capture.size
        self.touch = touch or build_input(cfg, width, height, dry_run=self.options.dry_run)
        self.templates = TemplateStore(
            cfg.template_dir(), cfg.get("vision.scales", [1.0]) or [1.0]
        )
        self.actions = Actions(cfg, self.capture, self.touch, self.templates)
        self.notifier = Notifier(cfg)

        state = self.store.load() if self.options.resume else None
        if state is None or not self.options.resume:
            state = BotState()
        self.state = state
        self.state.running = True
        self.state.stop_reason = ""

        # weight book survives restarts together with the counters
        self.book = WeightBook(
            limit_kg=float(cfg.num("safety.weight_limit_kg", 500.0)),
            default_avg_kg=float(cfg.num("weight.avg_kg_per_fish", 1.0)),
            margin_kg=float(cfg.num("weight.safety_margin_kg", 0.0)),
        )
        if state.weight_book:
            self.book = WeightBook.from_dict({**state.weight_book,
                                              "limit_kg": self.book.limit_kg})
        self.fishing = FishingTask(cfg, self.actions, self.state, self.log)
        self.fishing.book = self.book
        self.fishing.blocked_handler = self._recover_blocked_cast
        self._resume_phase = Phase.IDLE
        hooks = {"pause_fishing": self._pause_fishing, "resume_fishing": self._resume_fishing}
        self.survival = SurvivalTask(cfg, self.actions, self.state, self.log, hooks)
        self.trunk = TrunkTask(cfg, self.actions, self.state, self.log, hooks)
        self.trash = TrashTask(cfg, self.actions, self.state, self.log, hooks)
        self.anti_afk = AntiAfkTask(cfg, self.actions, self.state, self.log)

        self.weight_limit = float(self.options.max_kg or cfg.num("safety.weight_limit_kg", 500.0))
        self.max_failures = int(cfg.num("safety.max_consecutive_failures", 12))
        self.max_runtime = float(self.options.max_seconds or cfg.num("safety.max_runtime_hours", 0) * 3600)
        self.guard = ScreenGuard(cfg, self.actions, self.log)
        self.stuck = StuckDetector(cfg.num("safety.stuck_seconds", 180.0))
        self.paused = False
        self.hotkeys = None
        self._last_trash_check = 0.0
        self._stuck_last_fish = 0
        self._missing_templates = [t for t in cfg.template_names() if not self.templates.has(t)]
        if self._missing_templates:
            self.log.warning("templates not found (template matching will be skipped): %s",
                             ", ".join(self._missing_templates))

    # ------------------------------------------------------------------ signals
    def install_signal_handlers(self) -> None:
        def handler(signum, _frame):  # pragma: no cover - signal path
            self.log.warning("signal %s received - stopping after this tick", signum)
            self.request_stop(STOP_SIGNAL)

        for sig in (signal.SIGINT, signal.SIGTERM):
            try:
                signal.signal(sig, handler)
            except (ValueError, OSError):  # pragma: no cover - not main thread
                pass

    def request_stop(self, reason: str) -> None:
        if not self.stop_requested:
            self.stop_reason = reason
        self.stop_requested = True

    # ------------------------------------------------------------------ stop rules
    def _check_stop_rules(self) -> bool:
        if self.stop_requested:
            return True
        estimated = self.book.estimate(self.state.total_kg)
        if estimated > self.state.total_kg:
            self.state.total_kg = round(estimated, 2)
        if estimated >= self.weight_limit:
            self.state.total_kg = max(self.state.total_kg, round(estimated, 2))
            self.log.info("weight limit reached: %.2f / %.0f kg (%s) - stopping automation",
                          self.state.total_kg, self.weight_limit, self.book.summary())
            self.request_stop(STOP_WEIGHT)
            return True
        if self.max_runtime and self.state.uptime >= self.max_runtime:
            self.log.info("runtime limit %.0f min reached - stopping", self.max_runtime / 60)
            self.request_stop(STOP_RUNTIME)
            return True
        if self.state.consecutive_failures >= self.max_failures:
            self.log.error("%s consecutive failures - stopping", self.state.consecutive_failures)
            self.request_stop(STOP_FAILURES)
            return True
        return False

    def kg_left(self) -> float:
        return max(0.0, self.weight_limit - self.state.total_kg)

    # ------------------------------------------------------------------ main loop
    def run(self) -> BotState:
        self.install_signal_handlers()
        hz = max(0.5, self.cfg.num("loop.hz", 4.0))
        interval = 1.0 / hz
        debug_every = self.cfg.num("loop.debug_every", 0)
        last_debug = time.time()

        self.log.info("bot start: backend=%s device=%s limit=%.0f kg",
                      self.capture.describe(), self.touch.name, self.weight_limit)
        self.store.save(self.state)

        self.install_hotkeys()
        try:
            while True:
                tick_start = time.time()
                while self.paused and not self.stop_requested:
                    time.sleep(0.2)
                self._check_stop_rules()
                if self.stop_requested:
                    break
                try:
                    self.tick()
                except CaptureError as exc:
                    self.log.error("capture failed: %s", exc)
                    self.state.consecutive_failures += 1
                    self.state.touch()
                    time.sleep(1.0)
                except Exception as exc:  # noqa: BLE001 - keep the night run alive
                    self.log.exception("unexpected error in tick: %s", exc)
                    self.state.consecutive_failures += 1
                    self._safe_debug("tick-error")
                    time.sleep(1.0)
                self.state.weight_book = self.book.to_dict()
                self.store.save(self.state)
                if debug_every and (time.time() - last_debug) >= debug_every:
                    self._safe_debug("periodic")
                    last_debug = time.time()
                elapsed = time.time() - tick_start
                self.actions.sleep(max(0.0, interval - elapsed))
        finally:
            self.shutdown()
        return self.state

    def install_hotkeys(self) -> None:
        if not self.options.hotkeys:
            return
        from .watch import HotkeyWatcher

        def toggle_pause() -> None:
            self.paused = not self.paused
            self.state.paused = self.paused
            self.log.warning("PAUSED by hotkey" if self.paused else "resumed by hotkey")

        def reset_counter() -> None:
            self.log.warning("counter reset by hotkey")
            self.state.total_kg = 0.0
            self.state.fish_count = 0
            self.book = WeightBook(limit_kg=self.weight_limit,
                                   default_avg_kg=self.fishing.cfg.num("weight.avg_kg_per_fish", 1.0),
                                   margin_kg=self.fishing.cfg.num("weight.safety_margin_kg", 0.0))
            self.fishing.book = self.book
        self.fishing.blocked_handler = self._recover_blocked_cast

        self.hotkeys = HotkeyWatcher({"toggle_pause": toggle_pause,
                                      "reset_counter": reset_counter,
                                      "stop": lambda: self.request_stop(STOP_SIGNAL)})
        if self.hotkeys.available:
            self.log.info("hotkeys: F8 pause/resume, F9 reset counter, F12 stop")

    def tick(self) -> None:
        self.actions.begin_tick()
        # 0) hard guards: loading/disconnect screens, frozen screen
        verdict = self.guard.check()
        if verdict.state == "fatal":
            self.log.error("guard: %s", verdict.reason)
            self.request_stop(STOP_GUARD)
            return
        if verdict.state == "waiting":
            self.log.info("guard: %s", verdict.reason)
            return
        if self.cfg.boolean("safety.detect_stuck", True):
            self._check_stuck()

        # 1) net broken -> restock from the trunk (highest priority, fishing is stuck)
        if self.fishing.phase is Phase.BROKEN_NET or self.fishing.net_broken:
            self._handle_broken_net()
            return

        # 2) survival
        if self.survival.handle():
            return

        # 3) trash
        if self._trash_due() and self.trash.needed():
            if self.trash.clear():
                self.fishing.phase = Phase.IDLE  # recast after cleaning
                return

        # 4) anti-AFK heartbeat (never while a bite is on screen)
        if self.anti_afk.maybe_poke():
            return

        # 5) fishing
        phase = self.fishing.tick()
        if phase is Phase.FAILED:
            count = self.state.add_failure()
            self.log.warning("cast failed (%s/%s consecutive)", count, self.max_failures)
            self.fishing.phase = Phase.IDLE
            if self.cfg.boolean("safety.pause_on_unknown_screen", True):
                self.fishing.fallback()

    # ------------------------------------------------------------------ recovery
    def _recover_blocked_cast(self) -> bool:
        """Casting is blocked: junk on the net? stray window?  Returns True if fixed."""
        if self.cfg.boolean("trash.enabled", True) and self.trash.counts() > 0:
            self.log.info("cast blocked by junk on the net - cleaning")
            if self.trash.clear():
                return True
        before = self.fishing.phase
        self.fishing.fallback()
        return self.actions.exists(self.cfg.get("fishing.cast_template", "cast_button")) or before is not None

    # ------------------------------------------------------------------ hooks
    def _pause_fishing(self) -> None:
        """Remember the fishing phase so a routine can restore it afterwards."""
        self._resume_phase = self.fishing.phase
        if self.fishing.phase in (Phase.WAITING, Phase.REELING):
            self.fishing.phase = Phase.IDLE
            self.log.debug("fishing paused (was %s)", self._resume_phase.value)

    def _resume_fishing(self) -> None:
        self.fishing.phase = Phase.IDLE
        self.log.debug("fishing resumed; casting again")

    # ------------------------------------------------------------------ handlers
    def _handle_broken_net(self) -> None:
        self.log.info("handling broken net (kg %.2f/%.0f)", self.state.total_kg, self.weight_limit)
        restocks_before = self.state.nets_restocked
        if self.trunk.restock("net broke while fishing"):
            if self.trunk.net_is_broken():
                count = self.state.add_failure()
                self.log.error("net still broken after restock (%s/%s)", count, self.max_failures)
                if count >= self.max_failures:
                    self.request_stop(STOP_FAILURES)
                return
            self.log.info("restock #%s done, resuming fishing", self.state.nets_restocked)
            self.fishing.net_restocked()
        else:
            count = self.state.add_failure()
            self.log.error("restock failed (%s/%s)", count, self.max_failures)
            if count < self.max_failures:
                self.fishing.net_restocked()
                # retry the trunk walk on the next tick
            else:
                self.request_stop(STOP_FAILURES)

    def _check_stuck(self) -> None:
        """Only watch for a frozen screen when nothing is supposed to happen.

        A calm lake with no bite looks static for minutes, so fishing phases
        that legitimately wait are skipped - the bite timeout handles those.
        """
        if self.fishing.phase in (Phase.WAITING, Phase.REELING):
            self.stuck.reset()
            return
        if self.state.fish_count != self._stuck_last_fish:
            self._stuck_last_fish = self.state.fish_count
            self.stuck.reset()
            return
        small = GrayFrame.from_frame(self.actions.frame(fresh=True), 0.15).small
        if self.stuck.feed(small):
            self.log.error("no visible progress for %.0fs - stopping", self.stuck.threshold)
            self._safe_debug("stuck")
            self.request_stop(STOP_STUCK)

    def _trash_due(self) -> bool:
        interval = self.cfg.num("trash.check_interval", 3.0)
        now = time.time()
        if now - self._last_trash_check < interval:
            return False
        self._last_trash_check = now
        return True

    def _safe_debug(self, tag: str) -> None:
        try:
            self.actions.save_debug(self.cfg.path_of("logging.debug_dir", "debug"), tag)
        except Exception:  # pragma: no cover
            pass

    # ------------------------------------------------------------------ shutdown
    def shutdown(self) -> None:
        self.state.running = False
        if not self.state.stop_reason:
            self.state.stop_reason = self.stop_reason or "finished"
        try:
            self.store.save(self.state)
        finally:
            try:
                self.capture.close()
            except Exception:  # pragma: no cover
                pass
        if self.hotkeys is not None:
            self.hotkeys.stop()
        try:
            self.touch.release_all()
        except Exception:  # pragma: no cover - backend may not support keys
            pass
        summary = self.state.summary()
        self.log.info("bot stopped (%s): %s", self.state.stop_reason, summary)
        self.notifier.finished(self.state)


def run_from_config(config_path: str | Path, options: RunOptions | None = None) -> BotState:
    cfg = Config.load(config_path)
    cfg.ensure_dirs()
    setup_logging(cfg)
    runner = BotRunner(cfg, options)
    return runner.run()
