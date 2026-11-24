"""The log file must never be empty: every command writes at least a banner."""

from __future__ import annotations

import logging
from pathlib import Path

import pytest

from bot import logging_setup
from bot.config import Config
from bot.logging_setup import log_startup_banner, setup_logging


def _read(path: Path) -> str:
    logging_setup._flush_logs()
    return path.read_text(encoding="utf-8")


@pytest.fixture(autouse=True)
def _restore_logging():
    yield
    logging_setup._flush_logs()
    root = logging.getLogger()
    for handler in list(root.handlers):
        root.removeHandler(handler)
        handler.close()


def test_check_writes_startup_banner(cfg: Config):
    logging_setup.LAST_LOG_FILE = None
    log = setup_logging(cfg)
    log_startup_banner(log, "check", str(cfg.path))
    text = _read(cfg.path_of("logging.file"))
    assert "AmazingBot" in text
    assert "command=check" in text
    assert "config:" in text
    assert str(cfg.path_of("logging.file")) in text
    assert logging_setup.current_log_file() == cfg.path_of("logging.file")


def test_every_run_appends_without_losing_lines(cfg: Config):
    for _ in range(3):
        log = setup_logging(cfg)
        log_startup_banner(log, "run", str(cfg.path))
    text = _read(cfg.path_of("logging.file"))
    assert text.count("command=run") == 3
    assert text.count("=" * 72) == 3


def test_setup_logging_without_config_writes_to_app_logs(tmp_path, monkeypatch):
    monkeypatch.chdir(tmp_path)
    logging_setup.LAST_LOG_FILE = None
    log = setup_logging(None)
    log_startup_banner(log, "check", None)
    path = tmp_path / "logs" / "bot.log"
    assert path.exists() and path.stat().st_size > 0
    assert "command=check" in _read(path)


def test_unwritable_log_dir_does_not_crash(cfg: Config, tmp_path):
    locked = tmp_path / "locked"
    locked.mkdir()
    cfg.raw["logging"]["file"] = str(locked / "bot.log")
    locked.chmod(0o500)
    try:
        log = setup_logging(cfg)  # must not raise
        log.info("hello")
    finally:
        locked.chmod(0o700)


def test_cli_check_logs_even_when_config_is_broken(tmp_path, monkeypatch):
    from bot.cli import main

    monkeypatch.chdir(tmp_path)
    logging_setup.LAST_LOG_FILE = None
    (tmp_path / "bad.yaml").write_text("not: [valid\n", encoding="utf-8")
    assert main(["check", "-c", "bad.yaml"]) == 1
    text = _read(tmp_path / "logs" / "bot.log")
    assert "config could not be loaded" in text
    crashes = list((tmp_path / "logs").glob("crash-*.txt"))
    assert crashes and "Traceback" in crashes[0].read_text(encoding="utf-8")
