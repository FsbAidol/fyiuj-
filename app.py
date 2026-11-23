"""PyInstaller entry point.

* ``AmazingBot.exe``            -> friendly menu (bot/launcher.py)
* ``AmazingBot.exe run ...``    -> normal CLI (everything after the exe name)

Frozen builds put the log/state folders next to the exe, never inside the
temporary _MEIPASS folder.
"""

from __future__ import annotations

import os
import sys


def _force_utf8_streams() -> None:
    """Windows consoles default to cp1251/cp866: never crash on Cyrillic output."""
    for stream_name in ("stdout", "stderr"):
        stream = getattr(sys, stream_name, None)
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is None:
            continue
        try:
            reconfigure(encoding="utf-8", errors="replace")
        except (ValueError, OSError):  # pragma: no cover - redirected stream
            pass


def _prepare_frozen_env() -> None:
    _force_utf8_streams()
    if getattr(sys, "frozen", False) and sys.platform == "win32":
        try:  # nicer window title
            import ctypes

            ctypes.windll.kernel32.SetConsoleTitleW("AmazingBot - Amazing Online")
        except Exception:  # pragma: no cover - console may be redirected
            pass
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
