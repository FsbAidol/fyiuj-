"""Logging setup: console + rotating file, optional debug frames."""

from __future__ import annotations

import atexit
import logging
import logging.handlers
import sys
from pathlib import Path
from typing import Any

from .config import Config


class _ColorFormatter(logging.Formatter):
    COLORS = {
        logging.DEBUG: "\033[38;5;245m",
        logging.INFO: "\033[38;5;39m",
        logging.WARNING: "\033[38;5;214m",
        logging.ERROR: "\033[38;5;196m",
        logging.CRITICAL: "\033[1;91m",
    }
    RESET = "\033[0m"

    def __init__(self, use_color: bool = True) -> None:
        super().__init__("%(asctime)s %(levelname)-7s %(message)s", "%H:%M:%S")
        self.use_color = use_color

    def format(self, record: logging.LogRecord) -> str:
        text = super().format(record)
        if not self.use_color:
            return text
        color = self.COLORS.get(record.levelno, "")
        return f"{color}{text}{self.RESET}" if color else text


def setup_logging(cfg: Config | None = None, verbose: bool = False,
                  level: int | None = None) -> logging.Logger:
    """Console + rotating file logging.

    Works with a full config *or* with nothing at all (``cfg=None``), so a crash
    while loading config.yaml still lands in a log file instead of vanishing.
    """
    if level is None:
        level_name = str(cfg.get("logging.level", "INFO")).upper() if cfg is not None else "INFO"
        level = getattr(logging, level_name, logging.INFO)
    if verbose:
        level = logging.DEBUG

    root = logging.getLogger()
    for handler in list(root.handlers):
        root.removeHandler(handler)

    console = logging.StreamHandler(sys.stdout)
    console.setFormatter(_ColorFormatter(use_color=sys.stdout.isatty()))
    root.addHandler(console)

    log_file = _resolve_log_file(cfg)
    if log_file is not None:
        try:
            log_file.parent.mkdir(parents=True, exist_ok=True)
            file_handler = logging.handlers.RotatingFileHandler(
                log_file, maxBytes=2 * 1024 * 1024, backupCount=3, encoding="utf-8"
            )
            file_handler.setFormatter(
                logging.Formatter("%(asctime)s %(levelname)-7s [%(name)s] %(message)s")
            )
            root.addHandler(file_handler)
            globals()["LAST_LOG_FILE"] = log_file
        except OSError as exc:  # read-only folder, no permission - keep going
            print(f"warning: cannot write log file {log_file}: {exc}")

    root.setLevel(level)
    atexit.register(_flush_logs)
    logger = logging.getLogger("bot")
    logger.debug("logging level %s, file %s", logging.getLevelName(level), log_file)
    return logger


#: path of the log file used by the last setup_logging() call
LAST_LOG_FILE: Path | None = None


def _resolve_log_file(cfg: Config | None) -> Path | None:
    """``logging.file`` from config, or ``<app dir>/logs/bot.log`` by default."""
    if cfg is not None:
        return cfg.path_of("logging.file", "logs/bot.log")
    from .paths import app_dir

    return app_dir() / "logs" / "bot.log"


def current_log_file() -> Path | None:
    return globals().get("LAST_LOG_FILE")


def log_startup_banner(logger: logging.Logger, command: str, cfg_path: str | None = None) -> None:
    """Every command writes at least this, so the log is never empty."""
    import platform
    import sys

    from . import __version__
    from .paths import app_dir, is_frozen

    logger.info("=" * 72)
    logger.info("AmazingBot %s · command=%s · %s", __version__, command, "exe" if is_frozen() else "source")
    logger.info("python %s · %s", sys.version.split()[0], platform.platform())
    logger.info("app dir: %s", app_dir())
    if cfg_path:
        logger.info("config: %s", cfg_path)
    log_file = current_log_file()
    if log_file is not None:
        logger.info("log file: %s", log_file)


def _flush_logs() -> None:
    """Flush + close file handlers so nothing is lost on a normal exit."""
    root = logging.getLogger()
    for handler in list(root.handlers):
        try:
            handler.flush()
        except Exception:  # noqa: BLE001 - never fail during shutdown
            pass
    logging.shutdown()
