"""Friendly launcher for the packaged exe (double-click experience).

Running the exe with no arguments opens a small console menu instead of
argparse errors:

    1) Запустить бота
    2) Проверка (check)
    3) Диагностика (doctor)
    4) Прогон на симуляторе (demo)
    5) Открыть папку логов
    6) Открыть config.yaml
    7) Выход

Everything is still scriptable: ``AmazingBot.exe run --dry-run`` works exactly
like the module CLI.
"""

from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path


BANNER = r"""
   _                    _              ____        _
  / \   _ __ ___   __ _| | ____ _ _   _| __ )  ___ | |_
 / _ \ | '_ ` _ \ / _` | |/ / _` | | | |  _ \ / _ \| __|
/ ___ \| | | | | | (_| |   < (_| | |_| | |_) | (_) | |_
/_/   \_\_| |_| |_|\__,_|_|\_\__, |\__,_|____/ \___/ \__|
                             |___/   Amazing Online bot
"""


def _app_dir() -> Path:
    from .paths import app_dir

    return app_dir()


def _open_path(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)
    try:
        if sys.platform == "win32":
            os.startfile(str(path))  # type: ignore[attr-defined]
        elif sys.platform == "darwin":
            subprocess.Popen(["open", str(path)])
        else:
            subprocess.Popen(["xdg-open", str(path)])
    except Exception as exc:  # noqa: BLE001 - best effort
        print(f"Не удалось открыть {path}: {exc}")


def _first_run_help(exit_code: int) -> None:
    if exit_code != 2:
        return
    print("\nПохоже, рядом с ботом нет config.yaml.")
    print(f"Положите config.yaml, templates/ и scripts/ в папку:\n  {_app_dir()}")


def menu() -> int:
    from .cli import main as cli_main

    app = _app_dir()
    while True:
        print(BANNER)
        print(f"  папка бота: {app}")
        print("  ------------------------------------------------")
        print("  1) Запустить бота")
        print("  2) Проверка (check) — конфиг, шаблоны, устройство")
        print("  3) Диагностика (doctor)")
        print("  4) Прогон на симуляторе (demo, без игры)")
        print("  5) Открыть папку логов")
        print("  6) Открыть config.yaml")
        print("  0) Выход")
        try:
            choice = input("\n  выбор> ").strip()
        except (EOFError, KeyboardInterrupt):
            print()
            return 0

        if choice in {"1", "run"}:
            extra = input("  сухой прогон без нажатий? [y/N]> ").strip().lower()
            args = ["run"]
            if extra in {"y", "yes", "д", "да"}:
                args.append("--dry-run")
            _first_run_help(cli_main(args))
        elif choice in {"2", "check"}:
            _first_run_help(cli_main(["check"]))
        elif choice in {"3", "doctor"}:
            _first_run_help(cli_main(["doctor"]))
        elif choice in {"4", "demo"}:
            _first_run_help(cli_main(["demo", "--kg", "500", "--fish-kg", "5", "--speed", "120"]))
        elif choice in {"5", "logs"}:
            _open_path(app / "logs")
        elif choice in {"6", "config"}:
            target = app / "config.yaml"
            if target.exists():
                _open_path(target)
            else:
                print(f"  нет файла: {target}")
        elif choice in {"0", "q", "exit"}:
            return 0
        else:
            print("  не понял, введите цифру")
        try:
            input("\n  Enter — в меню...")
        except (EOFError, KeyboardInterrupt):
            return 0


def main() -> int:
    # scripts/ and templates/ next to the exe: make relative lookups work even
    # when Windows starts the process in another directory
    try:
        os.chdir(_app_dir())
    except OSError:  # pragma: no cover - read-only location
        pass
    return menu()


if __name__ == "__main__":  # pragma: no cover
    raise SystemExit(main())
