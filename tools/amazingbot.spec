# -*- mode: python ; coding: utf-8 -*-
"""PyInstaller spec for AmazingBot.exe.

One-folder build: fast start, and the user can see/replace templates and
scripts right next to the exe.

    pip install pyinstaller
    pyinstaller tools/amazingbot.spec --noconfirm
"""

import importlib.util
import os
from pathlib import Path

# SPECPATH is the folder that holds this spec file (tools/)
ROOT = Path(os.environ.get("AMAZINGBOT_ROOT", str(Path(SPECPATH).parent))).resolve()

datas = [
    (str(ROOT / "config.yaml"), "."),
    (str(ROOT / "demo_templates"), "demo_templates"),
    (str(ROOT / "scripts"), "scripts"),
]
# user templates are shipped as a starting point, never overwritten at runtime
if (ROOT / "templates").exists():
    datas.append((str(ROOT / "templates"), "templates"))

_candidates = [
    # our own package (imported lazily in places, so list it explicitly)
    "bot", "bot.cli", "bot.launcher", "bot.runner", "bot.prompt_key", "bot.paths",
    "bot.tasks", "bot.tasks.fishing", "bot.tasks.survival", "bot.tasks.trunk",
    "bot.tasks.trash", "bot.tasks.anti_afk", "bot.tools", "bot.tools.check",
    "bot.tools.calibrate", "bot.tools.demo", "bot.tools.demo_run",
    "bot.tools.crop_template", "bot.tools.make_templates", "bot.device",
    "bot.device.capture", "bot.device.input", "bot.watch", "bot.guards",
    "bot.notify", "bot.weight", "bot.logging_setup",
    # optional third-party modules: keep only the ones installed, otherwise
    # PyInstaller fails the whole build on a missing package
    "pynput", "pynput.keyboard._win32", "pynput.mouse._win32",
    "pytesseract", "yaml", "mss", "pyautogui",
]


def _available(module: str) -> bool:
    try:
        return importlib.util.find_spec(module) is not None
    except (ImportError, ValueError, ModuleNotFoundError):
        return False


hiddenimports = [m for m in _candidates if m.startswith("bot") or _available(m)]

a = Analysis(
    [str(ROOT / "app.py")],
    pathex=[str(ROOT)],
    binaries=[],
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    runtime_hooks=[],
    excludes=["tkinter", "matplotlib", "PyQt5", "PySide2", "IPython", "pytest"],
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data)

# --- single-file build (AmazingBot-onefile.exe) -----------------------------
# Handy for "just download one file and run it". Slower to start (it unpacks
# to %TEMP% once) and config.yaml/templates still live next to the exe.
onefile = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name="AmazingBot-onefile",
    debug=False,
    strip=False,
    upx=False,
    console=True,
    runtime_tmpdir=None,
    disable_windowed_traceback=False,
)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name="AmazingBot",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,                 # UPX trips Windows Defender heuristics
    console=True,              # a console window shows the log - intended
    disable_windowed_traceback=False,
    icon=str(ROOT / "tools" / "amazingbot.ico") if (ROOT / "tools" / "amazingbot.ico").exists() else None,
)

coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=False,
    name="AmazingBot",
)
