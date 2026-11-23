"""Screen capture backends.

All backends return BGR ``numpy`` frames and expose ``size`` (width, height).
Four flavours cover every way people run Amazing Online in an emulator:

``adb``      - emulator/phone through ``adb exec-out screencap`` (no extra deps)
``desktop``  - the emulator window on the same PC (fast, mss)
``images``   - a folder of screenshots (offline testing / tuning)
``video``    - a recorded clip (offline replay)
"""

from __future__ import annotations

import glob
import shutil
import subprocess
import time
from abc import ABC, abstractmethod
from pathlib import Path
from typing import Sequence

import cv2
import numpy as np

from ..config import Config


class CaptureError(Exception):
    pass


class CaptureBackend(ABC):
    name = "base"

    def __init__(self, cfg: Config) -> None:
        self.cfg = cfg
        self.width = int(cfg.num("device.width", 1280))
        self.height = int(cfg.num("device.height", 720))

    @property
    def size(self) -> tuple[int, int]:
        return self.width, self.height

    @abstractmethod
    def grab(self) -> np.ndarray:
        """Return the next frame as BGR uint8."""

    def close(self) -> None:  # pragma: no cover - trivial
        return None

    def describe(self) -> str:
        return f"{self.name} {self.width}x{self.height}"


# --------------------------------------------------------------------------- adb
class AdbBackend(CaptureBackend):
    name = "adb"

    def __init__(self, cfg: Config) -> None:
        super().__init__(cfg)
        self.adb = str(cfg.get("device.adb_path", "adb"))
        self.serial = str(cfg.get("device.serial", "") or "")
        if shutil.which(self.adb) is None and not Path(self.adb).exists():
            raise CaptureError(
                f"adb not found ({self.adb!r}). Install platform-tools and put adb on PATH, "
                "or set device.adb_path / use backend: desktop"
            )
        self._warned = False

    def _base_cmd(self) -> list[str]:
        cmd = [self.adb]
        if self.serial:
            cmd += ["-s", self.serial]
        return cmd

    def grab(self) -> np.ndarray:
        cmd = self._base_cmd() + ["exec-out", "screencap", "-p"]
        try:
            proc = subprocess.run(cmd, capture_output=True, timeout=15, check=False)
        except (subprocess.TimeoutExpired, OSError) as exc:
            raise CaptureError(f"adb screencap failed: {exc}") from exc
        if proc.returncode != 0 or not proc.stdout:
            msg = proc.stderr.decode("utf-8", "replace").strip()
            raise CaptureError(f"adb screencap error: {msg or proc.returncode}")
        data = np.frombuffer(proc.stdout, dtype=np.uint8)
        frame = cv2.imdecode(data, cv2.IMREAD_COLOR)
        if frame is None:
            raise CaptureError("adb returned data that is not a PNG")
        return self._fit(frame)

    def _fit(self, frame: np.ndarray) -> np.ndarray:
        h, w = frame.shape[:2]
        self.width, self.height = w, h
        return frame


# ----------------------------------------------------------------------- desktop
class DesktopBackend(CaptureBackend):
    name = "desktop"

    def __init__(self, cfg: Config) -> None:
        super().__init__(cfg)
        try:
            import mss  # noqa: WPS433 - optional dependency
        except ImportError as exc:  # pragma: no cover
            raise CaptureError("desktop backend needs 'mss' (pip install mss)") from exc
        self._mss = mss.mss()
        self.monitor = int(cfg.num("device.monitor", 1))
        try:
            mon = self._mss.monitors[self.monitor]
        except IndexError as exc:
            raise CaptureError(f"monitor {self.monitor} does not exist") from exc
        self._mon = mon
        self.width, self.height = mon["width"], mon["height"]

    def grab(self) -> np.ndarray:
        raw = self._mss.grab(self._mon)
        frame = np.asarray(raw, dtype=np.uint8)[:, :, :3]
        return np.ascontiguousarray(frame)

    def close(self) -> None:
        try:
            self._mss.close()
        except Exception:  # pragma: no cover
            pass


# ------------------------------------------------------------------------ images
class ImagesBackend(CaptureBackend):
    name = "images"

    def __init__(self, cfg: Config) -> None:
        super().__init__(cfg)
        directory = cfg.path_of("device.images_dir", "samples")
        patterns = ("*.png", "*.jpg", "*.jpeg", "*.bmp")
        files: list[str] = []
        for pattern in patterns:
            files.extend(glob.glob(str(directory / pattern)))
        if not files:
            raise CaptureError(f"no screenshots found in {directory}")
        self.files = sorted(files)
        self.loop = cfg.boolean("device.image_loop", True)
        self.index = 0
        first = cv2.imread(self.files[0], cv2.IMREAD_COLOR)
        if first is None:
            raise CaptureError(f"cannot read {self.files[0]}")
        self.height, self.width = first.shape[:2]

    def grab(self) -> np.ndarray:
        if self.index >= len(self.files):
            if not self.loop:
                raise CaptureError("screenshot list exhausted")
            self.index = 0
        path = self.files[self.index]
        self.index += 1
        frame = cv2.imread(path, cv2.IMREAD_COLOR)
        if frame is None:
            raise CaptureError(f"cannot read {path}")
        self.height, self.width = frame.shape[:2]
        return frame


# ------------------------------------------------------------------------- video
class VideoBackend(CaptureBackend):
    name = "video"

    def __init__(self, cfg: Config) -> None:
        super().__init__(cfg)
        path = str(cfg.get("device.video_path", "") or "")
        if not path or not Path(path).exists():
            raise CaptureError(f"device.video_path not found: {path!r}")
        self.cap = cv2.VideoCapture(path)
        if not self.cap.isOpened():  # pragma: no cover - env dependent
            raise CaptureError(f"cannot open video {path}")
        fps = self.cap.get(cv2.CAP_PROP_FPS) or 30.0
        self.frame_delay = 1.0 / max(1.0, float(fps))
        self.loop = cfg.boolean("device.image_loop", True)
        ok, frame = self.cap.read()
        if not ok:  # pragma: no cover
            raise CaptureError("video has no frames")
        self.height, self.width = frame.shape[:2]
        self._pending = frame

    def grab(self) -> np.ndarray:
        if self._pending is not None:
            frame, self._pending = self._pending, None
            return frame
        ok, frame = self.cap.read()
        if not ok:
            if not self.loop:
                raise CaptureError("video finished")
            self.cap.set(cv2.CAP_PROP_POS_FRAMES, 0)
            ok, frame = self.cap.read()
            if not ok:
                raise CaptureError("video finished and cannot restart")
        self.height, self.width = frame.shape[:2]
        return frame

    def close(self) -> None:  # pragma: no cover
        self.cap.release()


_BACKENDS = {
    "adb": AdbBackend,
    "desktop": DesktopBackend,
    "images": ImagesBackend,
    "video": VideoBackend,
}


def build_capture(cfg: Config) -> CaptureBackend:
    backend = str(cfg.get("device.backend", "adb")).lower()
    try:
        cls = _BACKENDS[backend]
    except KeyError as exc:
        raise CaptureError(f"unknown device.backend {backend!r}") from exc
    return cls(cfg)
