"""Scenario tests for the fishing state machine and survival/trunk/trash logic."""

from __future__ import annotations

import cv2
import pytest

from bot.scripts import Script
from bot.state import BotState
from bot.tasks import FishingTask, Phase, SurvivalTask, TrashTask, TrunkTask
from conftest import blank_frame, draw_bar, draw_button


def _tpl(cfg, name, img):
    cv2.imwrite(str(cfg.template_dir() / f"{name}.png"), img)


def _logger():
    import logging

    return logging.getLogger("test")


@pytest.fixture
def cast_frame(cfg):
    frame = draw_button(blank_frame(), 540, 300, w=80, h=40)
    _tpl(cfg, "cast_button", frame[300:340, 540:620])
    return frame


def test_cast_then_bite_then_catch(cfg, make_actions, cast_frame):
    bite_frame = draw_button(cast_frame.copy(), 400, 280, w=40, h=30, colour=(20, 20, 220))
    _tpl(cfg, "bite", bite_frame[280:310, 400:440])
    # the cast button disappears once the line is out, then the bite appears
    no_button = blank_frame()
    actions, _ = make_actions([cast_frame, no_button, bite_frame, bite_frame])
    actions.sleep = lambda s: None  # keep the test fast
    cfg.raw["fishing"]["cast_verify"] = True
    task = FishingTask(cfg, actions, BotState(), _logger())
    task.cast()
    assert task.phase is Phase.WAITING
    assert task.wait_for_bite() is True
    assert task.phase is Phase.REELING
    assert task.collect() is True
    assert task.state.fish_count == 1
    assert task.state.total_kg > 0


def test_cast_is_not_counted_when_button_stays(cfg, make_actions, cast_frame):
    """The button must go away; if taps do nothing we must not believe the cast."""
    actions, _ = make_actions([cast_frame] * 20)
    actions.sleep = lambda s: None
    calls = {"n": 0}

    def blocked():
        calls["n"] += 1
        return True

    cfg.raw["fishing"]["cast_retries"] = 2
    cfg.raw["fishing"]["cast_verify"] = True
    cfg.raw["fishing"]["cast_verify_timeout"] = 0.05
    task = FishingTask(cfg, actions, BotState(), _logger())
    task.blocked_handler = blocked
    assert task.cast() is False
    assert calls["n"] >= 1            # recovery was attempted
    assert task.state.casts == 0      # the cast is not counted
    assert task.phase is Phase.FAILED


def test_cast_fails_without_button(cfg, make_actions):
    actions, _ = make_actions([blank_frame()])
    actions.sleep = lambda s: None
    task = FishingTask(cfg, actions, BotState(), _logger())
    assert task.cast() is False
    assert task.phase is Phase.FAILED


def test_broken_net_detected(cfg, make_actions):
    frame = blank_frame()
    broken = draw_button(frame, 10, 10, w=30, h=30, colour=(0, 0, 200))
    _tpl(cfg, "net_broken", broken[10:40, 10:40])
    actions, _ = make_actions([broken])
    state = BotState()
    task = FishingTask(cfg, actions, state, _logger())
    assert task.check_broken_net() is True
    assert task.phase is Phase.BROKEN_NET
    assert state.nets_broken == 1
    # repeated checks do not double count
    task.check_broken_net()
    assert state.nets_broken == 1


def test_net_restocked_resumes(cfg, make_actions):
    actions, _ = make_actions([blank_frame()])
    actions.sleep = lambda s: None
    task = FishingTask(cfg, actions, BotState(), _logger())
    task.net_broken = True
    task.net_restocked()
    assert task.net_broken is False
    assert task.phase is Phase.IDLE


def test_bite_by_motion(cfg, make_actions):
    still = blank_frame()
    splash = draw_button(still.copy(), 400, 280, w=60, h=40, colour=(220, 220, 220))
    cfg.raw["fishing"]["bite"]["strategy"] = "motion"
    cfg.raw["fishing"]["bite"]["diff_threshold"] = 0.005
    actions, _ = make_actions([still, still, splash, splash])
    actions.sleep = lambda s: None
    task = FishingTask(cfg, actions, BotState(), _logger())
    assert task.wait_for_bite() is True


def test_estimate_weight_uses_average(cfg, make_actions):
    actions, _ = make_actions([blank_frame()])
    task = FishingTask(cfg, actions, BotState(), _logger())
    cfg.raw["weight"]["avg_kg_per_fish"] = 2.5
    assert task.estimate_catch_kg() == 2.5


# --------------------------------------------------------------------- survival
def _hud_frame(thirst=0.8, food=0.8):
    from bot.geometry import Region

    frame = blank_frame()
    tx, ty, tw, th = Region.from_value([0.02, 0.05, 0.26, 0.06]).to_pixels(640, 360)
    frame = draw_bar(frame, tx, ty, tw - tx, th - ty, thirst, (110, 180, 220))
    fx, fy, fw, fh = Region.from_value([0.02, 0.14, 0.26, 0.06]).to_pixels(640, 360)
    frame = draw_bar(frame, fx, fy, fw - fx, fh - fy, food, (20, 200, 220))
    return frame


def _write_use_script(cfg, name, item):
    path = cfg.base_dir / "scripts" / f"{name}.yaml"
    path.write_text(
        "name: %s\nsteps:\n  - tap: [0.5, 0.5]\n  - tap_template: %s\n    optional: true\n"
        "    timeout: 0.05\n  - hook: resume_fishing\n" % (name, item)
    )
    return path


def test_survival_reads_bars(cfg, make_actions):
    actions, _ = make_actions([_hud_frame(0.8, 0.8)])
    task = SurvivalTask(cfg, actions, BotState(), _logger())
    assert task.needs() == {}
    actions, _ = make_actions([_hud_frame(0.1, 0.8)])
    task = SurvivalTask(cfg, actions, BotState(), _logger())
    assert "thirst" in task.needs()
    assert "food" not in task.needs()


def test_survival_eats_when_hungry(cfg, make_actions, fake_input):
    _write_use_script(cfg, "eat", "food_item")
    _write_use_script(cfg, "drink", "water_item")
    actions, _ = make_actions([_hud_frame(0.8, 0.05)])
    actions.sleep = lambda s: None
    state = BotState()
    task = SurvivalTask(cfg, actions, state, _logger())
    assert task.handle() is True
    assert state.snacks_eaten == 1
    assert fake_input.taps, "eating routine should tap"


def test_survival_drinks_when_thirsty(cfg, make_actions):
    _write_use_script(cfg, "eat", "food_item")
    _write_use_script(cfg, "drink", "water_item")
    actions, _ = make_actions([_hud_frame(0.04, 0.9)])
    actions.sleep = lambda s: None
    state = BotState()
    task = SurvivalTask(cfg, actions, state, _logger())
    assert task.handle() is True
    assert state.drinks == 1


def test_survival_cooldown_blocks_spam(cfg, make_actions):
    _write_use_script(cfg, "eat", "food_item")
    _write_use_script(cfg, "drink", "water_item")
    cfg.raw["survival"]["cooldown_seconds"] = 60
    actions, _ = make_actions([_hud_frame(0.05, 0.05)])
    actions.sleep = lambda s: None
    state = BotState()
    task = SurvivalTask(cfg, actions, state, _logger())
    assert task.handle() is True
    assert task.handle() is False  # cooldown
    assert (state.drinks + state.snacks_eaten) == 1


def test_survival_prioritises_most_urgent(cfg, make_actions):
    _write_use_script(cfg, "eat", "food_item")
    _write_use_script(cfg, "drink", "water_item")
    actions, _ = make_actions([_hud_frame(0.12, 0.02)])  # food is lower
    actions.sleep = lambda s: None
    state = BotState()
    task = SurvivalTask(cfg, actions, state, _logger())
    task.handle()
    assert state.snacks_eaten == 1  # food first, not drink


def test_survival_disabled(cfg, make_actions):
    cfg.raw["survival"]["enabled"] = False
    actions, _ = make_actions([_hud_frame(0.01, 0.01)])
    task = SurvivalTask(cfg, actions, BotState(), _logger())
    assert task.handle() is False


# ------------------------------------------------------------------------ trunk
def _trunk_script(cfg, taps=1):
    path = cfg.base_dir / "scripts" / "trunk.yaml"
    steps = "".join("  - tap: [0.3, 0.3]\n" for _ in range(taps))
    path.write_text(f"name: trunk\nsteps:\n{steps}  - log: restocked\n")
    return path


def test_trunk_restock_runs_script(cfg, make_actions, fake_input):
    _trunk_script(cfg, taps=2)
    actions, _ = make_actions([blank_frame()])
    actions.sleep = lambda s: None
    state = BotState()
    task = TrunkTask(cfg, actions, state, _logger())
    assert task.restock("test") is True
    assert len(fake_input.taps) == 2
    assert state.trunk_trips == 1
    assert state.nets_restocked == 1


def test_trunk_disabled_refuses(cfg, make_actions):
    cfg.raw["trunk"]["enabled"] = False
    actions, _ = make_actions([blank_frame()])
    task = TrunkTask(cfg, actions, BotState(), _logger())
    assert task.restock("test") is False


def test_trunk_trip_limit(cfg, make_actions):
    _trunk_script(cfg)
    cfg.raw["trunk"]["max_per_session"] = 2
    actions, _ = make_actions([blank_frame()])
    actions.sleep = lambda s: None
    state = BotState()
    task = TrunkTask(cfg, actions, state, _logger())
    assert task.restock() is True
    assert task.restock() is True
    assert task.restock() is False


def test_trunk_counts_nets_in_pockets(cfg, make_actions):
    frame = blank_frame()
    frame = draw_button(frame, 560, 320, w=20, h=20)
    _tpl(cfg, "net_item", frame[320:340, 560:580])
    actions, _ = make_actions([frame])
    task = TrunkTask(cfg, actions, BotState(), _logger())
    assert task.nets_in_pockets() == 1


# ------------------------------------------------------------------------ trash
def test_trash_needed_by_count(cfg, make_actions):
    frame = blank_frame()
    frame = draw_button(frame, 200, 120, w=24, h=24, colour=(0, 140, 0))
    frame = draw_button(frame, 300, 132, w=24, h=24, colour=(0, 140, 0))
    _tpl(cfg, "trash_on_net", frame[120:144, 200:224])
    actions, _ = make_actions([frame])
    task = TrashTask(cfg, actions, BotState(), _logger())
    cfg.raw["trash"]["trigger_count"] = 2
    assert task.counts() >= 2
    assert task.needed() is True


def test_trash_below_trigger(cfg, make_actions):
    actions, _ = make_actions([blank_frame()])
    task = TrashTask(cfg, actions, BotState(), _logger())
    assert task.needed() is False


def test_trash_clear_uses_swipe(cfg, make_actions, fake_input):
    actions, _ = make_actions([blank_frame()])
    actions.sleep = lambda s: None
    state = BotState()
    task = TrashTask(cfg, actions, state, _logger())
    assert task.clear() is True
    assert fake_input.swipes
    assert state.trash_cleared == 1
