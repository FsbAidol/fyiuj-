"""End-of-run notification: beep, desktop toast (optional) and webhook.

A night run must wake you up or ping your phone when it stops - whether it hit
500 kg or crashed.  Everything here is best-effort and never raises.
"""

from __future__ import annotations

import json
import sys
from typing import Any

from .config import Config
from .state import BotState


class Notifier:
    def __init__(self, cfg: Config) -> None:
        self.cfg = cfg
        self.beep = cfg.boolean("notify.beep_on_stop", True)
        self.webhook = str(cfg.get("notify.webhook_url", "") or "")

    def finished(self, state: BotState) -> None:
        message = f"Amazing bot stopped: {state.stop_reason} | {state.summary()}"
        if self.beep:
            self._beep()
        if self.webhook:
            self._post(message)
        try:
            from .logging_setup import logging  # local import keeps deps optional

            logging.getLogger("bot").info("notification: %s", message)
        except Exception:  # pragma: no cover
            pass

    # ------------------------------------------------------------------ internals
    def _beep(self) -> None:
        try:
            if sys.platform.startswith("win"):
                import winsound

                for _ in range(3):
                    winsound.Beep(880, 180)  # type: ignore[attr-defined]
            else:
                print("\a", end="", flush=True)
        except Exception:  # pragma: no cover
            pass

    def _post(self, message: str) -> None:
        try:
            import urllib.request

            payload = json.dumps({"content": message, "text": message}).encode()
            req = urllib.request.Request(
                self.webhook, data=payload, headers={"Content-Type": "application/json"}
            )
            urllib.request.urlopen(req, timeout=8).close()
        except Exception:  # pragma: no cover - network optional
            pass
