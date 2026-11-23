"""Logging setup: console + rotating file, optional debug frames."""

from __future__ import annotations

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


def setup_logging(cfg: Config, verbose: bool = False) -> logging.Logger:
    level_name = str(cfg.get("logging.level", "INFO")).upper()
    level = getattr(logging, level_name, logging.INFO)
    if verbose:
        level = logging.DEBUG

    root = logging.getLogger()
    for handler in list(root.handlers):
        root.removeHandler(handler)

    console = logging.StreamHandler(sys.stdout)
    console.setFormatter(_ColorFormatter(use_color=sys.stdout.isatty()))
    root.addHandler(console)

    log_file = cfg.path_of("logging.file", "logs/bot.log")
    if str(log_file):
        log_file.parent.mkdir(parents=True, exist_ok=True)
        file_handler = logging.handlers.RotatingFileHandler(
            log_file, maxBytes=2 * 1024 * 1024, backupCount=3, encoding="utf-8"
        )
        file_handler.setFormatter(
            logging.Formatter("%(asctime)s %(levelname)-7s [%(name)s] %(message)s")
        )
        root.addHandler(file_handler)

    root.setLevel(level)
    logging.getLogger("bot").debug("logging level %s", logging.getLevelName(level))
    return logging.getLogger("bot")
