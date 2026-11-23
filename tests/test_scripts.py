from pathlib import Path

import cv2
import pytest

from bot.scripts import Script, ScriptRunner, ScriptError
from conftest import blank_frame, draw_button


def _write_template(cfg, name, img):
    cv2.imwrite(str(cfg.template_dir() / f"{name}.png"), img)


def test_script_from_file_and_inline(tmp_path):
    path = tmp_path / "s.yaml"
    path.write_text("name: demo\nsteps:\n  - wait: 0\n")
    script = Script.from_file(path)
    assert script.name == "demo"
    assert Script.from_steps("inline", [{"log": "hi"}]).name == "inline"


def test_missing_script_raises(tmp_path):
    with pytest.raises(ScriptError):
        Script.from_file(tmp_path / "nope.yaml")


def test_runner_executes_tap_and_wait_steps(cfg, make_actions, fake_input):
    actions, _ = make_actions([blank_frame()])
    runner = ScriptRunner(actions)
    script = Script.from_steps("basic", [
        {"log": "starting"},
        {"wait": 0},
        {"tap": [0.5, 0.5]},
        {"tap_region": [0.1, 0.1, 0.2, 0.2]},
    ])
    assert runner.run(script) is True
    assert len(fake_input.taps) == 2
    assert runner.executed == ["tap", "tap_region"]


def test_runner_fails_when_template_missing(cfg, make_actions):
    actions, _ = make_actions([blank_frame()])
    runner = ScriptRunner(actions)
    assert runner.run(Script.from_steps("x", [{"tap_template": "ghost", "timeout": 0.1}])) is False


def test_runner_optional_template_continues(cfg, make_actions):
    actions, _ = make_actions([blank_frame()])
    runner = ScriptRunner(actions)
    ok = runner.run(Script.from_steps("x", [
        {"tap_template": "ghost", "optional": True, "timeout": 0.05},
        {"log": "continued"},
    ]))
    assert ok is True


def test_runner_taps_real_template(cfg, make_actions, fake_input):
    frame = draw_button(blank_frame(), 300, 200)
    _write_template(cfg, "bag", frame[200:228, 300:360])
    actions, _ = make_actions([frame])
    ok = ScriptRunner(actions).run(Script.from_steps("x", [{"tap_template": "bag", "timeout": 0.2}]))
    assert ok is True
    assert fake_input.taps
    x, y = fake_input.taps[0]
    assert abs(x - 330) < 8 and abs(y - 214) < 8


def test_repeat_until_absent(cfg, make_actions):
    frame = blank_frame()
    with_item = draw_button(frame, 100, 100)
    _write_template(cfg, "item", frame[100:128, 100:160])
    # first frame has the item, then it is gone
    actions, _ = make_actions([with_item, with_item, blank_frame()])
    runner = ScriptRunner(actions)
    ok = runner.run(Script.from_steps("x", [
        {"repeat": 5, "steps": [{"tap_template": "item", "optional": True, "timeout": 0.05}],
         "until": "absent:item"},
    ]))
    assert ok is True


def test_if_exists_else_branch(cfg, make_actions, fake_input):
    actions, _ = make_actions([blank_frame()])
    runner = ScriptRunner(actions)
    ok = runner.run(Script.from_steps("x", [
        {"if_exists": "ghost", "steps": [{"tap": [0.1, 0.1]}], "else": [{"tap": [0.9, 0.9]}]},
    ]))
    assert ok is True
    assert len(fake_input.taps) == 1
    assert fake_input.taps[0][0] > fake_input.taps[0][1]


def test_hooks_are_called(cfg, make_actions):
    actions, _ = make_actions([blank_frame()])
    calls = []
    runner = ScriptRunner(actions, hooks={"pause_fishing": lambda: calls.append("pause"),
                                          "resume_fishing": lambda: calls.append("resume")})
    runner.run(Script.from_steps("x", [{"hook": "pause_fishing"}, {"hook": "resume_fishing"}]))
    assert calls == ["pause", "resume"]


def test_unknown_step_and_hook_fail(cfg, make_actions):
    actions, _ = make_actions([blank_frame()])
    runner = ScriptRunner(actions)
    assert runner.run(Script.from_steps("x", [{"teleport": "home"}])) is False
    # unknown hooks are non-fatal (typos must not kill an unattended run)
    assert runner.run(Script.from_steps("x", [{"hook": "nope"}])) is True


def test_nesting_guard(cfg, make_actions):
    actions, _ = make_actions([blank_frame()])
    step = {"repeat": 1, "steps": []}
    inner = {"repeat": 1, "steps": [step]}
    for _ in range(10):
        inner = {"repeat": 1, "steps": [inner]}
    ok = ScriptRunner(actions).run(Script.from_steps("deep", [inner]))
    assert ok is False


def test_assertions(cfg, make_actions):
    frame = draw_button(blank_frame(), 50, 50)
    _write_template(cfg, "here", frame[50:78, 50:110])
    actions, _ = make_actions([frame])
    runner = ScriptRunner(actions)
    assert runner.run(Script.from_steps("x", [{"assert_exists": "here"}])) is True
    assert runner.run(Script.from_steps("x", [{"assert_absent": "here"}])) is False


def test_wait_for_times_out(cfg, make_actions):
    actions, _ = make_actions([blank_frame()])
    ok = ScriptRunner(actions).run(Script.from_steps("x", [{"wait_for": "ghost", "timeout": 0.1}]))
    assert ok is False
