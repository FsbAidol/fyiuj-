"""Command line interface.

    python -m bot run                 # start the automation
    python -m bot run --dry-run       # see decisions, click nothing
    python -m bot check               # verify config, templates, device
    python -m bot doctor              # detailed diagnostics
    python -m bot snapshot -o shot.png
    python -m bot calibrate --interactive
    python -m bot state               # show saved progress
    python -m bot reset               # zero the 500 kg counter
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from . import __version__
from .config import Config, ConfigError
from .logging_setup import setup_logging
from .state import StateStore


def _add_common(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("-c", "--config", default="config.yaml", help="path to config.yaml")
    parser.add_argument("-v", "--verbose", action="store_true", help="debug logging")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="bot",
        description="Vision-based fishing automation for Amazing Online",
    )
    parser.add_argument("--version", action="version", version=f"amazing-bot {__version__}")
    sub = parser.add_subparsers(dest="command", required=True)

    p_run = sub.add_parser("run", help="run the automation")
    _add_common(p_run)
    p_run.add_argument("--dry-run", action="store_true", help="log actions, send no taps")
    p_run.add_argument("--resume", action="store_true", help="continue the saved kg counter")
    p_run.add_argument("--max-kg", type=float, default=0.0, help="override the 500 kg stop")
    p_run.add_argument("--max-minutes", type=float, default=0.0, help="stop after N minutes")
    p_run.add_argument("--yes", "-y", action="store_true", help="skip the safety prompt")

    p_check = sub.add_parser("check", help="validate config and assets")
    _add_common(p_check)

    p_doctor = sub.add_parser("doctor", help="diagnostics report")
    _add_common(p_doctor)

    p_snap = sub.add_parser("snapshot", help="save one screenshot")
    _add_common(p_snap)
    p_snap.add_argument("-o", "--output", default="samples/snapshot.png")

    p_cal = sub.add_parser("calibrate", help="help pick HUD regions")
    _add_common(p_cal)

    p_demo = sub.add_parser("demo", help="run against the built-in simulator (no game)")
    _add_common(p_demo)
    p_demo.add_argument("--kg", type=float, default=500.0, help="stop after N kg (default 500)")
    p_demo.add_argument("--fish-kg", type=float, default=4.0, help="kg per simulated fish")
    p_demo.add_argument("--speed", type=float, default=10.0, help="simulation speed multiplier")
    p_demo.add_argument("--max-minutes", type=float, default=0.0, help="optional wall-clock cap")
    p_demo.add_argument("--templates", default="", help="folder with demo templates")
    p_demo.add_argument("--break-every", type=int, default=0,
                        help="force a broken net every N catches (tests the trunk path)")
    p_demo.add_argument("--save-frames", action="store_true", help="dump frames to demo_frames/")

    p_state = sub.add_parser("state", help="print saved state")
    _add_common(p_state)

    p_reset = sub.add_parser("reset", help="reset the kg counter and stop flag")
    _add_common(p_reset)

    return parser


# ---------------------------------------------------------------------- commands
def cmd_run(args: argparse.Namespace) -> int:
    from .runner import BotRunner, RunOptions

    cfg = Config.load(args.config)
    cfg.ensure_dirs()
    log = setup_logging(cfg, verbose=args.verbose)

    state_store = StateStore(cfg.path_of("state.path"))
    previous = state_store.load()
    if args.resume and previous and previous.total_kg > 0:
        log.info("resuming: %.2f kg already collected", previous.total_kg)
    elif previous and previous.total_kg > 0 and not args.yes:
        print(
            f"\nSaved progress: {previous.total_kg:.2f} kg / "
            f"{cfg.num('safety.weight_limit_kg', 500):.0f} kg limit "
            f"(last stop: {previous.stop_reason or 'n/a'}).\n"
            "Start with a fresh counter? [Y/n] ",
            end="",
        )
        answer = input().strip().lower()
        if answer in {"n", "no", "н", "нет"}:
            args.resume = True

    options = RunOptions(
        resume=args.resume,
        dry_run=args.dry_run,
        max_seconds=args.max_minutes * 60.0,
        max_kg=args.max_kg,
    )
    runner = BotRunner(cfg, options)
    if args.dry_run:
        log.warning("DRY RUN: no taps will be sent to the game")
    state = runner.run()
    print(f"\nDone. {state.summary()}\nStop reason: {state.stop_reason or 'finished'}")
    return 0 if state.stop_reason in {"weight_limit_reached", "runtime_limit", "finished"} else 1


def cmd_check(args: argparse.Namespace) -> int:
    from .tools.check import run_checks

    cfg = Config.load(args.config)
    setup_logging(cfg, verbose=args.verbose)
    return 0 if run_checks(cfg) else 1


def cmd_doctor(args: argparse.Namespace) -> int:
    from .tools.check import doctor_report

    cfg = Config.load(args.config)
    setup_logging(cfg, verbose=args.verbose)
    return 0 if doctor_report(cfg) else 1


def cmd_snapshot(args: argparse.Namespace) -> int:
    import cv2

    from .device.capture import build_capture

    cfg = Config.load(args.config)
    setup_logging(cfg, verbose=args.verbose)
    cap = build_capture(cfg)
    try:
        frame = cap.grab()
    finally:
        cap.close()
    out = Path(args.output)
    out.parent.mkdir(parents=True, exist_ok=True)
    cv2.imwrite(str(out), frame)
    h, w = frame.shape[:2]
    print(f"saved {out} ({w}x{h})")
    return 0


def cmd_calibrate(args: argparse.Namespace) -> int:
    from .tools.calibrate import interactive_calibration

    cfg = Config.load(args.config)
    setup_logging(cfg, verbose=args.verbose)
    return interactive_calibration(cfg)


def cmd_demo(args: argparse.Namespace) -> int:
    from .tools.demo_run import run_demo

    cfg = Config.load(args.config)
    return run_demo(cfg, args)


def cmd_state(args: argparse.Namespace) -> int:
    cfg = Config.load(args.config)
    store = StateStore(cfg.path_of("state.path"))
    state = store.load()
    if state is None:
        print("no saved state")
        return 0
    print(json.dumps(state.to_dict(), indent=2))
    print(state.summary())
    return 0


def cmd_reset(args: argparse.Namespace) -> int:
    cfg = Config.load(args.config)
    store = StateStore(cfg.path_of("state.path"))
    state = store.reset()
    print(f"state reset: {state.summary()}")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    handlers = {
        "demo": cmd_demo,
        "run": cmd_run,
        "check": cmd_check,
        "doctor": cmd_doctor,
        "snapshot": cmd_snapshot,
        "calibrate": cmd_calibrate,
        "state": cmd_state,
        "reset": cmd_reset,
    }
    try:
        return handlers[args.command](args)
    except ConfigError as exc:
        print(f"config error: {exc}", file=sys.stderr)
        return 2
    except KeyboardInterrupt:
        print("\ninterrupted", file=sys.stderr)
        return 130
