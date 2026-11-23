"""Where the bot lives, frozen (exe) or from source.

A packaged build must not depend on the current working directory: when the
user double-clicks the exe Windows may start it anywhere.  Everything the user
edits (``config.yaml``, ``templates/``, ``scripts/``, ``logs/``) sits next to
the executable.
"""

from __future__ import annotations

import sys
from pathlib import Path


def is_frozen() -> bool:
    """True inside a PyInstaller build."""
    return bool(getattr(sys, "frozen", False))


def app_dir() -> Path:
    """Folder that holds config.yaml / templates / scripts."""
    if is_frozen():
        return Path(sys.executable).resolve().parent
    return Path.cwd().resolve()


def bundled_dir() -> Path | None:
    """Folder with read-only files extracted by PyInstaller (``_MEIPASS``)."""
    meipass = getattr(sys, "_MEIPASS", None)
    return Path(meipass) if meipass else None


def default_config() -> Path:
    candidate = app_dir() / "config.yaml"
    if candidate.exists():
        return candidate
    bundle = bundled_dir()
    if bundle is not None and (bundle / "config.yaml").exists():
        return bundle / "config.yaml"
    return candidate


def find_file(*relative: str) -> Path | None:
    """First existing path among app dir and the bundle."""
    for root in (app_dir(), bundled_dir()):
        if root is None:
            continue
        candidate = root.joinpath(*relative)
        if candidate.exists():
            return candidate
    return None
