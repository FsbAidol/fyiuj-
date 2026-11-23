"""Pre-flight checks: does the config make sense and are the assets there?

``check`` is fast and answers "can I press start?".  ``doctor`` additionally
probes the device and prints a full diagnostic report.
"""

from __future__ import annotations

import shutil
import sys
from pathlib import Path

from ..config import Config
from ..scripts import Script
from ..state import StateStore
from ..vision import TemplateStore

OK = "  ok  "
WARN = " warn "
FAIL = " fail "

# templates that are nice to have but not fatal when missing
OPTIONAL_TEMPLATES = {
    "collect", "catch_icon", "no_bait", "trash_on_net", "food_tab", "drink_tab",
    "use_button", "take_button", "close_popup", "ok_button", "car_map_marker",
    "car_near", "connection_lost", "loading_screen",
}


class Reporter:
    def __init__(self) -> None:
        self.lines: list[str] = []
        self.failed = False
        self.warned = False

    def add(self, status: str, message: str) -> None:
        if status == FAIL:
            self.failed = True
        elif status == WARN:
            self.warned = True
        self.lines.append(f"[{status}] {message}")
        print(self.lines[-1])

    def ok(self, message: str) -> None:
        self.add(OK, message)

    def warn(self, message: str) -> None:
        self.add(WARN, message)

    def fail(self, message: str) -> None:
        self.add(FAIL, message)

    def result(self) -> bool:
        return not self.failed


def _check_templates(cfg: Config, rep: Reporter) -> list[str]:
    store = TemplateStore(cfg.template_dir(), cfg.get("vision.scales", [1.0]) or [1.0])
    names = cfg.template_names()
    missing: list[str] = []
    if not cfg.template_dir().exists():
        rep.warn(f"templates folder missing: {cfg.template_dir()}")
    for name in names:
        if store.has(name):
            rep.ok(f"template {name}")
        elif name in OPTIONAL_TEMPLATES:
            missing.append(name)
            rep.warn(f"optional template {name} missing (feature degrades gracefully)")
        else:
            missing.append(name)
            rep.fail(f"template {name} missing -> {store.path(name)}")
    if not names:
        rep.warn("no templates referenced in config")
    return missing


def _check_scripts(cfg: Config, rep: Reporter) -> None:
    for path in cfg.script_paths():
        if not path.exists():
            rep.fail(f"script missing: {path}")
            continue
        try:
            script = Script.from_file(path)
        except Exception as exc:  # noqa: BLE001
            rep.fail(f"script {path.name} invalid: {exc}")
            continue
        rep.ok(f"script {script.name} ({len(script.steps)} steps) from {path.name}")


def _check_paths(cfg: Config, rep: Reporter) -> None:
    state_path = cfg.path_of("state.path")
    try:
        state_path.parent.mkdir(parents=True, exist_ok=True)
    except OSError as exc:
        rep.fail(f"cannot create state dir {state_path.parent}: {exc}")
    else:
        rep.ok(f"state file: {state_path}")
    log_path = cfg.path_of("logging.file")
    try:
        log_path.parent.mkdir(parents=True, exist_ok=True)
    except OSError as exc:
        rep.fail(f"cannot create log dir {log_path.parent}: {exc}")
        return
    store = StateStore(state_path)
    state = store.load()
    if state is None:
        rep.ok("no previous run (fresh 500 kg counter)")
    else:
        limit = cfg.num("safety.weight_limit_kg", 500)
        status = WARN if state.total_kg >= limit else OK
        rep.add(status, f"previous run: {state.total_kg:.2f}/{limit:.0f} kg, "
                        f"stop={state.stop_reason or 'n/a'}, running={state.running}")
        if state.running:
            rep.warn("state says a run is still marked running (was it killed?)")


def _check_features(cfg: Config, rep: Reporter) -> None:
    """Report optional capabilities that change what the bot can do."""
    from ..prompt_key import GlyphClassifier, PromptKeyConfig

    if cfg.boolean("qte.enabled", True):
        cfg_qte = PromptKeyConfig.from_config(cfg)
        classifier = GlyphClassifier(cfg_qte)
        if classifier.available:
            rep.ok("tesseract OCR available - net-cleaning QTE can read letters")
        else:
            rep.warn("tesseract OCR not found: QTE letters fall back to shape heuristics only "
                     "(install tesseract-ocr and/or set qte.tesseract_cmd)")
        rep.ok(f"QTE region: {cfg_qte.region.as_list()}")
    if cfg.boolean("trash.qte", True) or cfg.boolean("qte.enabled", True):
        rep.ok("net cleaning: QTE hold enabled (scripted swipe used as fallback)")
    if cfg.boolean("trash.enabled", True):
        rep.ok("trash cleaning enabled")
    if cfg.boolean("survival.enabled", True):
        rep.ok("auto eat/drink enabled")
    if cfg.boolean("anti_afk.enabled", True):
        rep.ok(f"anti-AFK every ~{cfg.num('anti_afk.interval', 45):.0f}s")
    limit = cfg.num("safety.weight_limit_kg", 500)
    rep.ok(f"auto-stop at {limit:.0f} kg (margin {cfg.num('weight.safety_margin_kg', 0):.1f} kg)")
    try:
        import pynput  # noqa: F401

        rep.ok("hotkeys F8 pause / F9 reset / F12 stop available")
    except Exception:
        rep.warn("pynput not installed - global hotkeys disabled")
    if str(cfg.get("weight.strategy", "bar")) == "ocr":
        from ..vision import Ocr

        if not Ocr(str(cfg.get("weight.ocr_lang", "eng"))).available:
            rep.warn("weight.strategy=ocr but tesseract is unavailable; using the bar estimate")


def _check_device(cfg: Config, rep: Reporter, probe: bool) -> None:
    backend = str(cfg.get("device.backend", "adb"))
    if backend == "adb":
        adb = str(cfg.get("device.adb_path", "adb"))
        if shutil.which(adb) is None and not Path(adb).exists():
            rep.fail(f"adb not found ({adb!r}) - install platform-tools")
            return
        rep.ok(f"adb binary: {adb}")
        if probe:
            import subprocess

            try:
                out = subprocess.run([adb, "devices"], capture_output=True, text=True, timeout=10)
                lines = [l for l in out.stdout.splitlines()[1:] if l.strip()]
                if not lines:
                    rep.fail("adb: no devices connected (start the emulator, enable USB debugging)")
                else:
                    for line in lines:
                        rep.ok(f"adb device: {line.strip()}")
            except Exception as exc:  # noqa: BLE001
                rep.fail(f"adb devices failed: {exc}")
    elif backend == "desktop":
        try:
            import mss
        except ImportError:
            rep.fail("backend desktop needs 'mss' (pip install mss)")
        else:
            rep.ok(f"mss available, monitor {cfg.num('device.monitor', 1):.0f}")
        try:
            import pyautogui  # noqa: F401
        except ImportError:
            rep.fail("backend desktop needs 'pyautogui' for input")
        else:
            rep.ok("pyautogui available")
    elif backend in {"images", "video"}:
        target = cfg.path_of("device.images_dir", "samples") if backend == "images" else Path(str(cfg.get("device.video_path", "")))
        if backend == "images" and not target.exists():
            rep.fail(f"screenshot folder missing: {target}")
        elif backend == "video" and not target.exists():
            rep.fail(f"video missing: {target}")
        else:
            rep.ok(f"{backend} source: {target}")
    else:
        rep.fail(f"unknown backend {backend!r}")


def run_checks(cfg: Config, probe: bool = True) -> bool:
    rep = Reporter()
    print("== config ==")
    rep.ok(f"config loaded from {cfg.path}")
    _check_paths(cfg, rep)
    print("== device ==")
    _check_device(cfg, rep, probe=probe)
    print("== features ==")
    _check_features(cfg, rep)
    print("== templates ==")
    _check_templates(cfg, rep)
    print("== scripts ==")
    _check_scripts(cfg, rep)
    print()
    if rep.failed:
        print("RESULT: problems found - fix the [ fail ] lines above")
        return False
    if rep.warned:
        print("RESULT: usable, with warnings")
    else:
        print("RESULT: all good")
    return True


def doctor_report(cfg: Config) -> bool:
    rep = Reporter()
    print(f"python: {sys.version.split()[0]}")
    for module in ("cv2", "numpy", "yaml", "mss", "pyautogui", "pytesseract"):
        try:
            mod = __import__(module)
            version = getattr(mod, "__version__", "?")
            rep.ok(f"{module} {version}")
        except Exception:  # noqa: BLE001
            rep.warn(f"{module} not installed")
    print("== capture test ==")
    try:
        from ..device.capture import build_capture

        cap = build_capture(cfg)
        try:
            frame = cap.grab()
            h, w = frame.shape[:2]
            rep.ok(f"capture ok via {cap.name}: {w}x{h}")
        finally:
            cap.close()
    except Exception as exc:  # noqa: BLE001
        rep.fail(f"capture failed: {exc}")
    print("== hud bars ==")
    try:
        from ..vision import read_bar

        cap = build_capture(cfg)
        try:
            frame = cap.grab()
        finally:
            cap.close()
        for kind in ("thirst", "food", "weight"):
            region = cfg.region(f"hud.{kind}.region")
            hsv = cfg.get(f"hud.{kind}.fill_hsv")
            if region is None or not hsv:
                rep.warn(f"{kind}: not configured")
                continue
            reading = read_bar(frame, region, hsv)
            status = OK if reading.found else WARN
            rep.add(status, f"{kind}: {reading.ratio:.0%} (found={reading.found})")
    except Exception as exc:  # noqa: BLE001
        rep.warn(f"hud check skipped: {exc}")
    print("== full checks ==")
    ok = run_checks(cfg, probe=True)
    return ok and rep.result()


def main(argv: list[str] | None = None) -> int:  # pragma: no cover - thin wrapper
    from ..cli import main as cli_main

    args = list(argv or [])
    return cli_main(["check", *args] if not args or args[0].startswith("-") else args)
