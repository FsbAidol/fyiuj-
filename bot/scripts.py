"""Tiny declarative step language for multi-tap routines.

Eating, drinking and restocking nets all share the shape "open inventory, find
the item, tap it, close the window".  Instead of writing that four times in
Python, the routines live in YAML files the user can edit without touching code:

.. code-block:: yaml

    name: eat
    steps:
      - tap_template: inventory_button
        timeout: 3
      - wait_gone: loading
      - repeat: 2
        steps:
          - tap_template: food_item
          - tap_region: [0.4, 0.4, 0.2, 0.2]
        until: absent:food_item
      - tap_template: close_button

Supported steps
---------------
``tap`` ``[x, y]``                     tap relative point
``tap_region`` ``[x, y, w, h]``        tap centre of a region
``tap_template`` ``name``              find & tap a template (``timeout`` optional)
``hold`` ``{point, ms}``               press and hold
``swipe`` ``{from, to, ms}``           drag
``wait`` ``seconds``                   sleep (randomised +/- 20%)
``wait_for`` / ``wait_gone`` ``name``  wait for a template to appear / vanish
``repeat`` ``n`` + ``steps``           nested block, optional ``until``
``if_exists`` ``name`` + ``steps``     conditional block
``assert_exists`` / ``assert_absent``  fail the script when the condition is false
``log`` ``text``                       log line
``note`` ``{kg, delta}``               bookkeeping only (ignored)
``key`` ``w``                          press a key (keyboard input)
``hold_key`` ``{key, seconds}``         hold a key, e.g. walk forward
``walk`` ``{direction, seconds}``       hold a movement key (see ``moving.keys``)
``type`` ``"text"``                    type text (chat commands: /fish, /car)
``chat_command`` ``"/fish"``           open chat (``moving.keys.chat``), type, Enter
``prompt_key`` ``{timeout, hold}``     recognise the random QTE letter and hold it
``clear_net`` ``{}``                   high level: clean the net QTE in one step
"""

from __future__ import annotations

import random
from pathlib import Path
from typing import Any, Callable, Iterable, Mapping

import yaml

from .actions import Actions
from .geometry import Region
from .paths import find_file

MAX_DEPTH = 6


class ScriptError(Exception):
    pass


class Script:
    def __init__(self, name: str, steps: list[dict[str, Any]], description: str = "") -> None:
        self.name = name
        self.description = description
        self.steps = steps

    def __repr__(self) -> str:  # pragma: no cover
        return f"<Script {self.name!r} steps={len(self.steps)}>"

    @classmethod
    def from_file(cls, path: str | Path) -> "Script":
        p = Path(path)
        if not p.exists() and not p.is_absolute():
            # a relative name may also come from the bundled exe data
            found = find_file(str(p))
            if found is not None:
                p = found
        if not p.exists():
            raise ScriptError(f"script not found: {p}")
        with p.open("r", encoding="utf-8") as fh:
            data = yaml.safe_load(fh) or {}
        if not isinstance(data, Mapping):
            raise ScriptError(f"{p}: script must be a mapping")
        steps = data.get("steps") or []
        if not isinstance(steps, list):
            raise ScriptError(f"{p}: 'steps' must be a list")
        return cls(str(data.get("name") or p.stem), steps, str(data.get("description", "")))

    @classmethod
    def from_steps(cls, name: str, steps: list[dict[str, Any]]) -> "Script":
        return cls(name, steps)


class ScriptRunner:
    """Executes a :class:`Script` against the live :class:`Actions`."""

    def __init__(self, actions: Actions, logger: Any = None, hooks: Mapping[str, Callable[[], None]] | None = None) -> None:
        self.actions = actions
        self.log = logger or _silent_logger()
        self.hooks = dict(hooks or {})
        self.executed: list[str] = []

    # ------------------------------------------------------------------ public
    def run(self, script: Script | str | Path, dry_ok: bool = True) -> bool:
        if isinstance(script, (str, Path)) and Path(str(script)).exists():
            script = Script.from_file(script)
        if isinstance(script, str):
            raise ScriptError(f"unknown script {script!r}")
        self.executed.clear()
        self.log.info("script start: %s", script.name)
        try:
            ok = self._run_steps(script.steps, script.name, depth=0)
        except ScriptError as exc:
            self.log.warning("script %s failed: %s", script.name, exc)
            return False
        if not ok:
            self.log.warning("script %s reported failure (see log)", script.name)
        else:
            self.log.debug("script %s done", script.name)
        return ok

    # ------------------------------------------------------------------ internals
    def _run_steps(self, steps: Iterable[Any], ctx: str, depth: int) -> bool:
        if depth > MAX_DEPTH:
            raise ScriptError(f"{ctx}: nesting too deep")
        for raw in steps:
            if not isinstance(raw, Mapping):
                raise ScriptError(f"{ctx}: step must be a mapping, got {raw!r}")
            ok = self._run_step(dict(raw), ctx, depth)
            if ok is False:
                return False
        return True

    def _run_step(self, step: dict[str, Any], ctx: str, depth: int) -> bool | None:
        if "hook" in step:
            name = str(step["hook"])
            hook = self.hooks.get(name)
            if hook is None:
                # a typo in a YAML routine must not kill a night run
                self.log.warning("%s: unknown hook %r - skipped", ctx, name)
                return True
            self.executed.append(f"hook:{name}")
            self.log.debug("hook %s", name)
            hook()
            return True

        if "log" in step:
            self.log.info("[script] %s", step["log"])
            return True
        if "note" in step:
            return True

        if "tap" in step:
            point = step["tap"]
            if len(point) != 2:
                raise ScriptError(f"{ctx}: 'tap' needs [x, y]")
            self.actions.tap([float(point[0]), float(point[1])])
            self.executed.append("tap")
            return True
        if "tap_region" in step:
            region = Region.from_value(step["tap_region"])
            self.actions.tap_center(region)
            self.executed.append("tap_region")
            return True
        if "tap_template" in step:
            name = str(step["tap_template"])
            timeout = float(step.get("timeout", 0.0))
            optional = bool(step.get("optional", False))
            region = step.get("region")
            hits = int(step.get("times", 1))
            for _ in range(max(1, hits)):
                ok = self.actions.tap_template(name, Region.from_value(region) if region else None, timeout)
                if not ok:
                    if optional:
                        return True
                    raise ScriptError(f"{ctx}: template {name!r} not found")
                self.executed.append(f"tap:{name}")
                self.actions.sleep(float(step.get("after", 0.35)))
            return True
        if "hold" in step:
            spec = step["hold"]
            point = spec.get("point", [0.5, 0.5])
            ms = int(spec.get("ms", 400))
            self.actions.hold(list(point), ms)
            return True
        if "swipe" in step:
            spec = step["swipe"]
            self.actions.swipe(spec.get("from", [0.5, 0.6]), spec.get("to", [0.5, 0.4]),
                               int(spec.get("ms", 250)))
            return True
        if "key" in step:
            if step.get("optional") and not hasattr(self.actions.touch, "press"):
                return True
            key = str(step["key"])
            self._keys().press(key, int(step.get("hold_ms", 60)))
            self.executed.append(f"key:{key}")
            self.actions.sleep(float(step.get("after", 0.25)))
            return True
        if "hold_key" in step:
            spec = step["hold_key"]
            key = str(spec.get("key", "w"))
            seconds = float(spec.get("seconds", 0.5))
            self._keys().hold_key(key, seconds)
            self.executed.append(f"hold:{key}")
            return True
        if "walk" in step:
            spec = step["walk"] if isinstance(step["walk"], Mapping) else {"direction": step["walk"]}
            direction = str(spec.get("direction", "forward"))
            seconds = float(spec.get("seconds", self.actions.cfg.num("moving.step_seconds", 0.6)))
            key = str(self.actions.cfg.get(f"moving.keys.{direction}", direction) or direction)
            self._keys().hold_key(key, seconds)
            self.executed.append(f"walk:{direction}")
            self.actions.sleep(float(spec.get("after", 0.2)))
            return True
        if "type" in step:
            self._keys().type_text(str(step["type"]))
            self.executed.append("type")
            return True
        if "chat_command" in step:
            command = str(step["chat_command"])
            chat_key = str(step.get("chat_key") or self.actions.cfg.get("moving.keys.chat", "t"))
            self._keys().press(chat_key)
            self.actions.sleep(0.35)
            self._keys().type_text(command)
            self.actions.sleep(0.15)
            self._keys().press("enter")
            self.executed.append(f"chat:{command}")
            self.actions.sleep(float(step.get("after", 0.8)))
            return True
        if "prompt_key" in step:
            spec = step["prompt_key"] if isinstance(step["prompt_key"], Mapping) else {}
            result = self.actions.qte_hold(
                timeout=float(spec.get("timeout", 8.0)),
                hold_timeout=float(spec.get("hold_timeout", 28.0)),
                expect=str(spec.get("expect")) if spec.get("expect") else None,
            )
            self.executed.append(f"prompt_key:{result}")
            if result == "none":
                if step.get("optional", True):
                    return True
                raise ScriptError(f"{ctx}: no prompt letter recognised")
            return result == "released"
        if "clear_net" in step:
            spec = step["clear_net"] if isinstance(step["clear_net"], Mapping) else {}
            ok = self.actions.clear_net(
                appear_timeout=float(spec.get("appear_timeout", 8.0)),
                hold_timeout=float(spec.get("hold_timeout", 28.0)),
            )
            self.executed.append("clear_net" if ok else "clear_net:failed")
            if not ok and not spec.get("optional", False):
                raise ScriptError(f"{ctx}: net cleaning failed")
            return True
        if "wait" in step:
            seconds = float(step["wait"])
            self.actions.sleep(seconds * random.uniform(0.85, 1.2))
            return True
        if "wait_for" in step:
            name = str(step["wait_for"])
            timeout = float(step.get("timeout", 5.0))
            match = self.actions.wait_for(name, timeout)
            if match is None and not step.get("optional", False):
                raise ScriptError(f"{ctx}: timed out waiting for {name!r}")
            return True
        if "wait_gone" in step:
            name = str(step["wait_gone"])
            timeout = float(step.get("timeout", 5.0))
            if not self.actions.wait_gone(name, timeout) and not step.get("optional", False):
                raise ScriptError(f"{ctx}: {name!r} never disappeared")
            return True
        if "assert_exists" in step:
            name = str(step["assert_exists"])
            if not self.actions.exists(name):
                raise ScriptError(f"{ctx}: expected {name!r} on screen")
            return True
        if "assert_absent" in step:
            name = str(step["assert_absent"])
            if self.actions.exists(name):
                raise ScriptError(f"{ctx}: {name!r} should not be on screen")
            return True
        if "if_exists" in step:
            name = str(step["if_exists"])
            if self.actions.exists(name):
                return self._run_steps(step.get("steps") or [], f"{ctx}>if({name})", depth + 1)
            if "else" in step:
                return self._run_steps(step["else"] or [], f"{ctx}>else({name})", depth + 1)
            return True
        if "repeat" in step:
            count = int(step["repeat"])
            sub = step.get("steps") or []
            until = step.get("until")
            for index in range(max(0, count)):
                ok = self._run_steps(sub, f"{ctx}>repeat#{index}", depth + 1)
                if ok is False:
                    return False
                if until and self._check_until(str(until)):
                    break
            return True

        raise ScriptError(f"{ctx}: unknown step keys {sorted(step)}")

    def _keys(self):
        touch = getattr(self.actions, "touch", None)
        if touch is None or not hasattr(touch, "press"):
            raise ScriptError("keyboard steps need an input backend with key support")
        return touch

    def _check_until(self, expr: str) -> bool:
        if expr.startswith("absent:"):
            return not self.actions.exists(expr.split(":", 1)[1])
        if expr.startswith("present:"):
            return self.actions.exists(expr.split(":", 1)[1])
        raise ScriptError(f"bad 'until' expression {expr!r} (use absent:name / present:name)")

    @staticmethod
    def _require(step: dict[str, Any], key: str) -> None:
        if key not in step:
            raise ScriptError(f"step needs '{key}'")


class _Silent:
    def debug(self, *a, **k): ...
    def info(self, *a, **k): ...
    def warning(self, *a, **k): ...
    def error(self, *a, **k): ...


def _silent_logger() -> _Silent:
    return _Silent()
