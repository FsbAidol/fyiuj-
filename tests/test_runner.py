"""Orchestrator tests: stop rules, broken net -> trunk -> fish again, limits."""

from __future__ import annotations

import cv2
import pytest

import bot.runner as runner_mod
from bot.config import Config
from bot.runner import BotRunner, RunOptions
from bot.state import BotState
from bot.tasks import Phase
from conftest import blank_frame, draw_button


def _tpl(cfg, name, img):
    cv2.imwrite(str(cfg.template_dir() / f"{name}.png"), img)


@pytest.fixture
def runner(cfg, monkeypatch):
    """A runner wired to a fake capture and a recording input backend."""

    def build(frames, state: BotState | None = None):
        capture = None

        def fake_build_capture(config):
            nonlocal capture
            from conftest import FakeCapture

            capture = FakeCapture(frames, config)
            return capture

        monkeypatch.setattr(runner_mod, "build_capture", fake_build_capture)
        r = BotRunner(cfg, RunOptions(dry_run=True))
        if state is not None:
            r.state.total_kg = state.total_kg
            r.state.fish_count = state.fish_count
        r.actions.sleep = lambda s: None
        return r

    return build


class _Recorder:
    """Stand-in for the input backend that records everything."""

    name = "recorder"

    def __init__(self) -> None:
        self.taps = []

    def tap(self, point, hold_ms=None):  # noqa: D401 - test double
        self.taps.append(point)

    def swipe(self, start, end, duration_ms=250):
        self.taps.append(("swipe", tuple(start), tuple(end)))

    def hold(self, point, duration_ms):
        self.taps.append(("hold", point))


def test_weight_limit_stops_before_any_action(runner):
    r = runner([blank_frame()], state=BotState(total_kg=500.0))
    assert r._check_stop_rules() is True
    assert r.stop_reason == "weight_limit_reached"


def test_weight_limit_is_exclusive(runner):
    r = runner([blank_frame()], state=BotState(total_kg=499.99))
    assert r._check_stop_rules() is False
    r.state.add_catch(0.02)
    assert r._check_stop_rules() is True


def test_failure_limit_stops(runner):
    r = runner([blank_frame()])
    r.state.consecutive_failures = r.max_failures
    assert r._check_stop_rules() is True
    assert r.stop_reason == "too_many_failures"


def test_runtime_limit_stops(runner):
    r = runner([blank_frame()])
    r.max_runtime = 10
    r.state.started_at -= 11
    assert r._check_stop_rules() is True
    assert r.stop_reason == "runtime_limit"


def test_broken_net_restocks_then_resumes(runner, cfg):
    frame = blank_frame()
    broken = draw_button(frame, 8, 8, w=26, h=26, colour=(0, 0, 210))
    _tpl(cfg, "net_broken", broken[8:34, 8:34])
    script = cfg.base_dir / "scripts" / "trunk.yaml"
    script.write_text("name: trunk\nsteps:\n  - tap: [0.4, 0.4]\n")

    # the indicator is on screen for the first frames, then the net is replaced
    r = runner([broken, broken, blank_frame(), blank_frame()])
    r.touch = _Recorder()
    r.actions.touch = r.touch
    r.fishing.check_broken_net()
    assert r.fishing.phase is Phase.BROKEN_NET

    r.tick()  # should run the trunk routine and un-stick fishing

    assert r.state.nets_restocked == 1
    assert r.state.nets_broken == 1
    assert r.fishing.phase is Phase.IDLE
    assert r.touch.taps, "trunk routine should tap"


def test_broken_net_without_trunk_script_counts_failure(runner, cfg):
    (cfg.base_dir / "scripts" / "trunk.yaml").unlink(missing_ok=True)
    frame = blank_frame()
    broken = draw_button(frame, 8, 8, w=26, h=26, colour=(0, 0, 210))
    _tpl(cfg, "net_broken", broken[8:34, 8:34])
    r = runner([broken])
    r.fishing.net_broken = True
    r.fishing.phase = Phase.BROKEN_NET
    r.tick()
    assert r.state.consecutive_failures == 1
    assert r.state.nets_restocked == 0


def test_survival_takes_priority_over_fishing(runner, cfg):
    from bot.geometry import Region

    frame = blank_frame()
    tx, ty, tw, th = Region.from_value([0.02, 0.05, 0.26, 0.06]).to_pixels(640, 360)
    from conftest import draw_bar

    frame = draw_bar(frame, tx, ty, tw - tx, th - ty, 0.02, (110, 180, 220))
    (cfg.base_dir / "scripts" / "drink.yaml").write_text(
        "name: drink\nsteps:\n  - tap: [0.5, 0.5]\n")
    r = runner([frame])
    r.touch = _Recorder()
    r.actions.touch = r.touch
    r.tick()
    assert r.state.drinks == 1
    assert r.state.casts == 0  # fishing did not run first


def test_anti_afk_pokes_only_when_due(runner):
    r = runner([blank_frame()])
    r.touch = _Recorder()
    r.actions.touch = r.touch
    r.anti_afk.interval = 100
    r.anti_afk._next = 1e18
    assert r.anti_afk.maybe_poke() is False
    r.anti_afk._next = 0
    assert r.anti_afk.maybe_poke() is True
    assert r.state.anti_afk_actions == 1
    assert all(isinstance(t, tuple) for t in r.touch.taps)


def test_hooks_pause_and_resume_fishing(runner):
    r = runner([blank_frame()])
    r.fishing.phase = Phase.WAITING
    r._pause_fishing()
    assert r.fishing.phase is Phase.IDLE
    r._resume_fishing()
    assert r.fishing.phase is Phase.IDLE


def test_missing_templates_are_reported_not_fatal(runner, cfg):
    r = runner([blank_frame()])
    assert isinstance(r._missing_templates, list)


def test_run_loop_stops_on_weight_limit(cfg, monkeypatch):
    """End-to-end: the loop exits on its own once 500 kg is collected."""
    calls = {"n": 0}

    def fake_build_capture(config):
        from conftest import FakeCapture

        return FakeCapture([blank_frame()], config)

    monkeypatch.setattr(runner_mod, "build_capture", fake_build_capture)
    r = BotRunner(cfg, RunOptions(dry_run=True))
    r.actions.sleep = lambda s: None

    def fake_tick():
        calls["n"] += 1
        r.state.add_catch(250.0)   # two ticks = 500 kg
        if calls["n"] > 10:
            raise AssertionError("loop did not stop at the weight limit")

    r.tick = fake_tick
    state = r.run()
    assert state.stop_reason == "weight_limit_reached"
    assert state.total_kg == 500.0
    assert calls["n"] == 2
    assert state.running is False


def test_state_is_persisted_after_run(cfg, monkeypatch):
    def fake_build_capture(config):
        from conftest import FakeCapture

        return FakeCapture([blank_frame()], config)

    monkeypatch.setattr(runner_mod, "build_capture", fake_build_capture)
    r = BotRunner(cfg, RunOptions(dry_run=True))
    r.actions.sleep = lambda s: None
    r.tick = lambda: r.state.add_catch(12.5)
    r.max_kg = 20.0
    state = r.run()
    assert state.total_kg >= 20.0
    loaded = r.store.load()
    assert loaded is not None
    assert loaded.total_kg == state.total_kg
    assert loaded.running is False
