"""YAML configuration with dotted lookups and validation.

The config is intentionally dict based: behaviours, scripts and the calibration
tool all read the same file, and unknown keys never crash an unattended run.
Missing optional values fall back to ``DEFAULTS``.
"""

from __future__ import annotations

import copy
from pathlib import Path
from typing import Any, Iterable, Mapping

import yaml

from .geometry import Point, Region

DEFAULTS: dict[str, Any] = {
    "device": {
        "backend": "adb",          # adb | desktop | images | video
        "adb_path": "adb",
        "serial": "",              # empty -> first device reported by adb
        "monitor": 1,              # desktop backend: mss monitor index
        "video_path": "",
        "images_dir": "",
        "image_loop": True,
        "width": 1280,             # logical resolution of the emulator window
        "height": 720,
        "orientation": "landscape",
    },
    "loop": {"hz": 4.0, "max_seconds": 0, "debug_every": 0},
    "input": {"jitter_px": 3, "tap_hold_ms": 60, "human_delay": [0.05, 0.15]},
    "vision": {"threshold": 0.82, "scales": [1.0], "grayscale": True, "templates_dir": "templates"},
    "hud": {
        "thirst": {
            "region": [0.02, 0.05, 0.18, 0.025],
            "fill_hsv": [[90, 60, 90], [130, 255, 255]],
            "low": 0.25,
            "critical": 0.12,
        },
        "food": {
            "region": [0.02, 0.09, 0.18, 0.025],
            "fill_hsv": [[5, 80, 90], [35, 255, 255]],
            "low": 0.25,
            "critical": 0.12,
        },
        "weight": {
            "region": [0.72, 0.05, 0.24, 0.02],
            "fill_hsv": [[35, 70, 120], [65, 255, 255]],
        },
    },
    "safety": {
        "weight_limit_kg": 500.0,
        "max_consecutive_failures": 12,
        "max_runtime_hours": 0,
        "pause_on_unknown_screen": True,
    },
    "weight": {"strategy": "bar", "avg_kg_per_fish": 1.0, "ocr_lang": "eng",
               "safety_margin_kg": 2.0, "read_interval_catches": 1},
    "fishing": {
        "cast_template": "cast_button",
        "enter_templates": [],
        "cast_timeout": 6.0,
        "cast_retries": 3,
        "cast_verify": True       # кнопка заброса должна исчезнуть/смениться
        ,"cast_verify_timeout": 1.5,
        "bite": {"strategy": "template", "template": "bite", "region": [0.0, 0.0, 1.0, 1.0],
                 "diff_threshold": 0.006, "timeout": 60.0, "poll_hz": 6.0},
        "reel": {"mode": "hold", "button": "reel_button", "max_seconds": 35.0,
                 "tap_interval_ms": 140, "chunk_ms": 400},
        "collect": {"template": "collect", "timeout": 5.0, "optional": True},
        "indicators": {"net_broken": "net_broken", "no_bait": "no_bait", "catch_icon": "catch_icon"},
        "recast_delay": [1.0, 2.0],
        "after_broken_delay": [1.5, 3.0],
    },
    "survival": {"enabled": True, "eat_script": "scripts/eat.yaml", "drink_script": "scripts/drink.yaml",
                 "cooldown_seconds": 20.0},
    "trunk": {
        "enabled": True,
        "script": "scripts/trunk_restock.yaml",
        "min_nets": 1,
        "nets_region": [0.80, 0.86, 0.18, 0.10],
        "net_template": "net_item",
        "max_per_session": 12,
    },
    "trash": {
        "enabled": True,
        "script": "scripts/clear_trash.yaml",
        "template": "trash_on_net",
        "region": [0.18, 0.25, 0.44, 0.20],
        "trigger_count": 2,
        "check_interval": 3.0,
    },
    "moving": {
        "enabled": False,
        "keys": {"forward": "w", "backward": "s", "left": "a", "right": "d",
                 "enter": "enter", "handbrake": "space", "chat": "t",
                 "menu": "e", "inventory": "tab", "escape": "escape"},
        "step_seconds": 0.6,
        "pause_seconds": 0.4,
        "recover_after_failures": 3,
    },
    "qte": {
        "enabled": True,
        "region": [0.30, 0.22, 0.40, 0.56],
        "glyph_scale": 5.0,
        "min_confidence": 25,
        "single_read_min_confidence": 65,
        "min_key_height_fraction": 0.028,
        "max_center_distance_sq": 0.075,
        "recognition_timeout_seconds": 1.2,
        "stable_frames": 2,
        "poll_seconds": 0.2,
        "hold_timeout_seconds": 28.0,
        "disappear_frames": 3,
        "tesseract_cmd": "",
    },
    "guards": {
        "lost_connection": "connection_lost",
        "loading_screen": "loading_screen",
        "stable_frames": 3,
        "pause_seconds": 20.0,
        "max_consecutive_checks": 5,
    },
    "anti_afk": {
        "enabled": True,
        "interval": 45.0,
        "jitter": 8.0,
        "mode": "swipe",           # swipe | tap
        "point": [0.5, 0.62],
        "swipe": [[0.48, 0.60], [0.52, 0.60]],
        "duration_ms": 220,
    },
    "notify": {"beep_on_stop": True, "webhook_url": ""},
    "state": {"path": "state/state.json"},
    "logging": {"level": "INFO", "file": "logs/bot.log", "debug_dir": "debug"},
}


def _deep_merge(base: dict[str, Any], override: Mapping[str, Any]) -> dict[str, Any]:
    result = copy.deepcopy(base)
    for key, value in override.items():
        if isinstance(value, Mapping) and isinstance(result.get(key), Mapping):
            result[key] = _deep_merge(dict(result[key]), value)
        else:
            result[key] = copy.deepcopy(value)
    return result


class ConfigError(Exception):
    pass


class Config:
    """Thin wrapper around the merged YAML tree."""

    def __init__(self, data: Mapping[str, Any], path: Path | None = None) -> None:
        self.raw: dict[str, Any] = _deep_merge(DEFAULTS, data or {})
        self.path = path
        self.base_dir = (path.parent if path else Path.cwd()).resolve()

    # ------------------------------------------------------------------ load
    @classmethod
    def load(cls, path: str | Path) -> "Config":
        p = Path(path)
        if not p.exists():
            raise ConfigError(f"config file not found: {p}")
        with p.open("r", encoding="utf-8") as fh:
            data = yaml.safe_load(fh) or {}
        if not isinstance(data, Mapping):
            raise ConfigError(f"{p}: top level must be a mapping")
        cfg = cls(data, p.resolve())
        cfg.validate()
        return cfg

    # ------------------------------------------------------------------ get
    def get(self, dotted: str, default: Any = None) -> Any:
        node: Any = self.raw
        for part in dotted.split("."):
            if not isinstance(node, Mapping) or part not in node:
                return default
            node = node[part]
        return node

    def num(self, dotted: str, default: float = 0.0) -> float:
        try:
            return float(self.get(dotted, default))
        except (TypeError, ValueError):
            return default

    def boolean(self, dotted: str, default: bool = False) -> bool:
        value = self.get(dotted, default)
        if isinstance(value, str):
            return value.strip().lower() in {"1", "true", "yes", "on"}
        return bool(value)

    def region(self, dotted: str, required: bool = False) -> Region | None:
        value = self.get(dotted)
        if value is None:
            if required:
                raise ConfigError(f"missing required region '{dotted}'")
            return None
        region = Region.from_value(value)
        region.validate(dotted)
        return region

    def point(self, dotted: str, required: bool = False) -> Point | None:
        value = self.get(dotted)
        if value is None:
            if required:
                raise ConfigError(f"missing required point '{dotted}'")
            return None
        if isinstance(value, Mapping):
            return Point(int(value["x"]), int(value["y"]))
        if isinstance(value, str):
            value = [p.strip() for p in value.split(",")]
        if len(value) != 2:
            raise ConfigError(f"{dotted} must be [x, y] relative coordinates")
        return Point(int(float(value[0])), int(float(value[1])))

    def pair(self, dotted: str, default: tuple[float, float] = (0.0, 0.0)) -> tuple[float, float]:
        value = self.get(dotted, list(default))
        try:
            return float(value[0]), float(value[1])
        except (TypeError, ValueError, IndexError) as exc:
            raise ConfigError(f"{dotted} must be a pair, got {value!r}") from exc

    def path_of(self, dotted: str, default: str = "") -> Path:
        """Absolute path for a config value.

        Relative values are resolved against the config file's folder (not the
        process CWD), so a double-clicked exe and ``python -m bot`` behave the
        same.  Missing keys fall back to ``default``.
        """
        value = str(self.get(dotted, default) or "")
        if not value:
            value = default
        p = Path(value)
        if p.is_absolute():
            return p
        base = self.base_dir if self.path is not None else Path.cwd()
        return (base / p).resolve()

    def template_dir(self) -> Path:
        return self.path_of("vision.templates_dir", "templates")

    # ------------------------------------------------------------------ validate
    def validate(self) -> None:
        problems: list[str] = []
        backend = str(self.get("device.backend", "adb"))
        if backend not in {"adb", "desktop", "images", "video"}:
            problems.append(f"device.backend must be adb|desktop|images|video, got {backend!r}")
        width, height = int(self.num("device.width", 1280)), int(self.num("device.height", 720))
        if width < 240 or height < 240:
            problems.append(f"device.width/height look wrong: {width}x{height}")
        hz = self.num("loop.hz", 4.0)
        if hz <= 0 or hz > 30:
            problems.append(f"loop.hz must be in (0, 30], got {hz}")
        for key in ("hud.thirst.region", "hud.food.region", "hud.weight.region",
                    "fishing.bite.region", "trash.region", "trunk.nets_region", "qte.region"):
            try:
                self.region(key)
            except (ValueError, ConfigError) as exc:
                problems.append(str(exc))
        if self.num("safety.weight_limit_kg", 0) <= 0:
            problems.append("safety.weight_limit_kg must be > 0")
        if problems:
            raise ConfigError("invalid config:\n  - " + "\n  - ".join(problems))

    # ------------------------------------------------------------------ helpers
    def template_names(self, include_scripts: bool = True) -> list[str]:
        """Every template name referenced by the config (and its scripts)."""
        names: set[str] = set()
        key_names = {"template", "cast_template", "net_template", "bite_template"}

        def walk(node: Any, key: str = "") -> None:
            if isinstance(node, Mapping):
                for k, v in node.items():
                    walk(v, str(k))
            elif isinstance(node, (list, tuple)):
                if key in {"enter_templates", "templates", "required"}:
                    for item in node:
                        if isinstance(item, str):
                            names.add(item)
                else:
                    for item in node:
                        walk(item, key)
            elif isinstance(node, str) and key in key_names:
                if node and not node.startswith("scripts/"):
                    names.add(node)

        walk(self.raw)

        if include_scripts:
            import re

            import yaml as _yaml

            pattern = re.compile(r"(?:tap_template|wait_for|wait_gone|assert_exists|assert_absent"
                                 r"|if_exists|template)\s*:\s*([A-Za-z0-9_\-]+)")
            for path in self.script_paths():
                if not path.exists():
                    continue
                try:
                    text = path.read_text(encoding="utf-8")
                    _yaml.safe_load(text)
                except Exception:
                    continue
                names.update(pattern.findall(text))
                # 'until: absent:name' / 'until: present:name'
                for target in re.findall(r"until:\s*(?:absent|present):([A-Za-z0-9_\-]+)", text):
                    names.add(target)
        return sorted(n for n in names if n and n != "templates")

    def script_paths(self) -> list[Path]:
        found: list[Path] = []
        for node in self.raw.values():
            if isinstance(node, Mapping):
                script = node.get("script")
                if isinstance(script, str):
                    found.append(self.path_of(f"x", script))
        for key in ("survival.eat_script", "survival.drink_script"):
            found.append(self.path_of(key))
        return [p for p in dict.fromkeys(found) if p.suffix in {".yaml", ".yml"}]

    def ensure_dirs(self, keys: Iterable[str] = ("logging.file", "state.path")) -> None:
        for key in keys:
            p = self.path_of(key)
            p.parent.mkdir(parents=True, exist_ok=True)
