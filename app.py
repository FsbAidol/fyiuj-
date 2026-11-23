"""PyInstaller entry point.

* ``AmazingBot.exe``            -> friendly menu (bot/launcher.py)
* ``AmazingBot.exe run ...``    -> normal CLI (everything after the exe name)

Frozen builds put the log/state folders next to the exe, never inside the
temporary _MEIPASS folder.
"""

from __future__ import annotations

import os
import sys


def _prepare_frozen_env() -> None:
    if not getattr(sys, "frozen", False):
        return
    from bot.paths import app_dir

    root = app_dir()
    try:
        os.chdir(root)
    except OSError:  # pragma: no cover - read-only folder
        pass
    # keep matplotlib/OpenCV quiet in a windowed build
    os.environ.setdefault("OPENCV_LOG_LEVEL", "ERROR")


def main() -> int:
    _prepare_frozen_env()
    from bot import launcher
    from bot.cli import main as cli_main

    argv = sys.argv[1:]
    if not argv:
        return launcher.main()
    if argv[0] in {"menu", "--menu"}:
        return launcher.main()
    return cli_main(argv)


if __name__ == "__main__":
    raise SystemExit(main())
