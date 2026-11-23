"""A tiny fake "Amazing Online" screen for testing the bot without a phone.

The simulator draws the same UI elements the bot recognises (cast button, bite
marker, inventory, trunk, junk on the net, HUD bars) and reacts to taps, so the
whole automation loop - including the 500 kg stop - runs headless:

    python -m bot demo --kg 500 --speed 12

Frames are written to ``demo_frames/`` so they can be inspected afterwards.
"""

from __future__ import annotations

import random
from dataclasses import dataclass, field
from enum import Enum

import cv2
import numpy as np

from ...config import Config
from ...device.capture import CaptureBackend
from ...geometry import Point, Region


def hsv_bgr(h: int, s: int, v: int) -> tuple[int, int, int]:
    pixel = np.uint8([[[h, s, v]]])
    b, g, r = cv2.cvtColor(pixel, cv2.COLOR_HSV2BGR)[0][0]
    return int(b), int(g), int(r)


class Ui(str, Enum):
    WATER = "water"
    BAG = "bag"
    TRUNK = "trunk"


@dataclass
class SimState:
    ui: Ui = Ui.WATER
    cast: bool = False
    bite: bool = False
    bite_timer: float = 0.0
    net: bool = False
    trash: int = 0
    nets: int = 2
    net_torn: bool = False
    food_kg: int = 2
    water_kg: int = 2
    thirst: float = 0.7
    food: float = 0.7
    fish_kg: float = 0.0
    fish_count: int = 0
    action: str = ""
    action_timer: float = 0.0
    log: list[str] = field(default_factory=list)


class GameSimulator(CaptureBackend):
    """Acts as both ``CaptureBackend`` and ``InputBackend``."""

    name = "simulator"

    def __init__(self, cfg: Config, fish_kg: float = 4.0, speed: float = 10.0,
                 break_every: int = 0) -> None:
        super().__init__(cfg)
        self.break_every = max(0, int(break_every))
        # while the QTE prompt is up, the net is "in cleaning" and cannot be cast
        self.qte_until_clean = False
        self.cfg = cfg
        self.width, self.height = 1280, 720
        self.sim = SimState()
        self.fish_kg = max(0.2, fish_kg)
        self.speed = max(1.0, speed)
        self.rng = random.Random(2026)
        self.tap_log: list[tuple[str, int, int]] = []
        self.frames_drawn = 0

    # ---------------------------------------------------------------- capture
    def grab(self) -> np.ndarray:
        return self.render()

    # ---------------------------------------------------------------- input
    @property
    def size(self) -> tuple[int, int]:
        return self.width, self.height

    def jitter(self) -> None:  # pragma: no cover - compatibility shim
        return None

    def pixels(self, point) -> tuple[int, int]:
        if isinstance(point, Point):
            return point.x, point.y
        return int(point[0] * self.width), int(point[1] * self.height)

    def human_pause(self, scale: float = 1.0) -> None:
        return None

    def tap(self, point, hold_ms: int | None = None) -> None:
        x, y = self.pixels(point)
        self.tap_log.append(("tap", x, y))
        self._handle_tap(x, y)

    def swipe(self, start, end, duration_ms: int = 250) -> None:
        x1, y1 = self.pixels(start)
        x2, y2 = self.pixels(end)
        self.tap_log.append(("swipe", x1, y1))
        self._handle_swipe(x1, y1, x2, y2)

    def hold(self, point, duration_ms: int) -> None:
        x, y = self.pixels(point)
        self.tap_log.append(("hold", x, y))
        self._handle_hold(x, y, duration_ms)

    # keyboard: recorded, not simulated (the demo focuses on the fishing loop)
    def press(self, key: str, hold_ms: int = 60) -> None:
        self.tap_log.append(("key", str(key), hold_ms))
        if str(key).lower() in {"escape", "tab", "e"} and self.sim.ui is not Ui.WATER:
            self.sim.ui = Ui.WATER
            self.sim.log.append("closed window (key)")

    def key_down(self, key: str) -> None:
        self.tap_log.append(("key_down", str(key), 0))
        self._qte_cleaned()

    def key_up(self, key: str) -> None:
        self.tap_log.append(("key_up", str(key), 0))

    def hold_key(self, key: str, seconds: float) -> None:
        self.tap_log.append(("hold_key", str(key), int(seconds * 1000)))
        if str(key).lower() == "escape" and self.sim.ui is not Ui.WATER:
            self.sim.ui = Ui.WATER

    def type_text(self, text: str, interval: float = 0.04) -> None:
        self.tap_log.append(("type", text, 0))

    def release_all(self) -> None:
        return None

    # the net-cleaning QTE: holding a key (or a swipe) removes one junk item
    def _qte_cleaned(self) -> None:
        if self.sim.trash > 0:
            self.sim.trash -= 1
            self.sim.log.append("junk removed (QTE)")
            if self.sim.trash == 0:
                self.sim.net = False

    # ---------------------------------------------------------------- geometry
    def button(self, name: str) -> tuple[int, int, int, int]:
        """Absolute (x1, y1, x2, y2) of a UI element."""
        x1, y1, x2, y2 = Region.from_value(self.BUTTONS[name]).to_pixels(self.width, self.height)
        return x1, y1, x2, y2

    BUTTONS = {
        "cast_button": [0.79, 0.80, 0.14, 0.11],
        "bite": [0.46, 0.58, 0.06, 0.07],   # float splash on the water
        "collect": [0.44, 0.46, 0.12, 0.08],
        "bag_button": [0.92, 0.06, 0.05, 0.08],
        "close_button": [0.90, 0.08, 0.05, 0.07],
        "food_item": [0.24, 0.30, 0.10, 0.10],
        "water_item": [0.40, 0.30, 0.10, 0.10],
        "use_button": [0.44, 0.72, 0.12, 0.08],
        "net_item": [0.56, 0.30, 0.10, 0.10],
        "trunk_window": [0.20, 0.18, 0.50, 0.55],
        "trunk_button": [0.06, 0.82, 0.10, 0.08],
        "trash_on_net": [0.33, 0.30, 0.05, 0.06],
        "net_broken": [0.46, 0.10, 0.07, 0.09],
    }

    def _hit(self, name: str, x: int, y: int) -> bool:
        x1, y1, x2, y2 = self.button(name)
        return x1 <= x <= x2 and y1 <= y <= y2

    # ---------------------------------------------------------------- rules
    def _register_catch(self) -> None:
        """A fish landed: update counters and maybe tear the net."""
        sim = self.sim
        sim.cast = False
        sim.bite = False
        sim.net = False
        sim.fish_count += 1
        sim.fish_kg += self.fish_kg
        sim.thirst = max(0.0, sim.thirst - 0.006)
        sim.food = max(0.0, sim.food - 0.004)
        if sim.trash < 2 and self.rng.random() < 0.75:
            sim.trash += 1
        if self.break_every and sim.fish_count % self.break_every == 0:
            sim.net_torn = True
            sim.log.append("NET TORN")
        elif sim.trash >= 3 or self.rng.random() < 0.05:
            sim.net = False
            sim.log.append("net needs cleaning")

    def _handle_tap(self, x: int, y: int) -> None:
        sim = self.sim
        if self._hit("net_broken", x, y):
            return
        if sim.ui is Ui.WATER:
            if (self._hit("cast_button", x, y) and not sim.cast and not sim.net_torn
                    and sim.trash < 2):
                sim.cast = True
                sim.net = True
                sim.bite = False
                sim.bite_timer = self.rng.uniform(0.35, 0.9)
                sim.log.append("cast")
                return
            if sim.bite and self._hit("bite", x, y):
                self._register_catch()
                sim.log.append(f"catch #{sim.fish_count} ({sim.fish_kg:.1f} kg)")
                return
            if sim.bite and self._hit("collect", x, y):
                sim.bite = False
                return
            if self._hit("bag_button", x, y):
                sim.ui = Ui.BAG
                sim.log.append("open bag")
                return
            if self._hit("trunk_button", x, y):
                sim.ui = Ui.TRUNK
                sim.log.append("open trunk")
                return
            return

        if sim.ui is Ui.BAG:
            if self._hit("food_item", x, y) and sim.food_kg > 0:
                sim.food_kg -= 1
                sim.food = min(1.0, sim.food + 0.4)
                sim.log.append("ate food")
                return
            if self._hit("water_item", x, y) and sim.water_kg > 0:
                sim.water_kg -= 1
                sim.thirst = min(1.0, sim.thirst + 0.4)
                sim.log.append("drank water")
                return
            if self._hit("close_button", x, y):
                sim.ui = Ui.WATER
                sim.log.append("close bag")
            return

        if sim.ui is Ui.TRUNK:
            if self._hit("net_item", x, y) and sim.nets < 6:
                sim.nets += 1
                if sim.net_torn:
                    sim.net_torn = False
                    sim.log.append("new net rigged")
                else:
                    sim.log.append(f"took net ({sim.nets})")
                return
            if self._hit("close_button", x, y):
                sim.ui = Ui.WATER
                sim.log.append("close trunk")
            return

    def _handle_hold(self, x: int, y: int, duration_ms: int) -> None:
        sim = self.sim
        if sim.ui is not Ui.WATER:
            return
        # reeling with a broken net immediately loses the fish
        if sim.cast and sim.net is False and sim.nets > 0:
            sim.cast = False
            sim.bite = False
            sim.nets -= 1
            sim.log.append("NET BROKE - fish lost")
            return
        if sim.bite:
            self._register_catch()
            sim.log.append(f"catch #{sim.fish_count} ({sim.fish_kg:.1f} kg)")
            return
        if sim.cast:
            sim.cast = False
            sim.log.append("reel cancelled")

    def _handle_swipe(self, x1: int, y1: int, x2: int, y2: int) -> None:
        if not self._hit("trash_on_net", x2, y2):
            self._qte_cleaned()

    # ---------------------------------------------------------------- dynamics
    def step(self, dt: float) -> None:
        """Advance the world by ``dt`` * ``speed`` game seconds."""
        sim = self.sim
        game_dt = dt * self.speed
        if self.rng.random() < game_dt * 0.05:
            if self.rng.random() < 0.6:
                sim.thirst = max(0.0, sim.thirst - 0.03)
            else:
                sim.food = max(0.0, sim.food - 0.03)
        if sim.cast and not sim.bite:
            sim.bite_timer -= game_dt
            if sim.bite_timer <= 0:
                sim.bite = True

    # ---------------------------------------------------------------- drawing
    def render(self) -> np.ndarray:
        self.frames_drawn += 1
        sim = self.sim
        W, H = self.width, self.height
        frame = np.zeros((H, W, 3), np.uint8)
        # sky + water
        frame[: H // 3, :] = hsv_bgr(105, 60, 200)
        frame[H // 3:, :] = hsv_bgr(105, 170, 150)
        for i in range(0, H // 3, 24):
            cv2.line(frame, (0, i), (W, i), hsv_bgr(105, 40, 215), 6)
        cv2.putText(frame, "Amazing Online - SIMULATOR", (16, H - 18),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.6, (240, 240, 240), 1, cv2.LINE_AA)

        self._draw_hud(frame)

        if sim.ui is Ui.TRUNK:
            self._draw_panel(frame, "BAGAZHNIK")
            self._draw_button(frame, "net_item", "NET")
            self._draw_button(frame, "close_button", "X")
        elif sim.ui is Ui.BAG:
            self._draw_panel(frame, "INVENTORY")
            self._draw_button(frame, "food_item", "FOOD")
            self._draw_button(frame, "water_item", "WATER")
            self._draw_button(frame, "close_button", "X")
        else:
            self._draw_button(frame, "bag_button", "BAG")
            self._draw_button(frame, "trunk_button", "TRUNK")
            if not sim.cast and not sim.net_torn:
                self._draw_button(frame, "cast_button", "CAST")
            if sim.bite:
                self._draw_button(frame, "bite", "!")
            if sim.trash >= 2:
                self._draw_trash(frame)
            if sim.net_torn or sim.nets == 0:
                self._draw_warning(frame)
            if sim.trash > 0:
                self._draw_prompt(frame)

        self._draw_stats(frame)
        return frame

    def _draw_button(self, frame: np.ndarray, name: str, label: str) -> None:
        x1, y1, x2, y2 = self.button(name)
        colour = (40, 90, 200) if "close" not in name else (60, 60, 200)
        cv2.rectangle(frame, (x1, y1), (x2, y2), colour, -1)
        cv2.rectangle(frame, (x1, y1), (x2, y2), (245, 245, 245), 2)
        cv2.putText(frame, label, (x1 + 4, y2 - 6), cv2.FONT_HERSHEY_SIMPLEX,
                    0.45, (255, 255, 255), 1, cv2.LINE_AA)

    def _draw_panel(self, frame: np.ndarray, title: str) -> None:
        # panel window is inset from its title area so the title is NOT part of
        # the trunk_window template (otherwise every bag looks like a trunk)
        x1, y1, x2, y2 = self.button("trunk_window")
        y1 += 46
        trunk = title == "BAGAZHNIK"
        base = (105, 60, 20) if trunk else (55, 42, 35)
        cv2.rectangle(frame, (x1, y1), (x2, y2), base, -1)
        cv2.rectangle(frame, (x1, y1), (x2, y2), (235, 235, 235), 3)
        # coloured header strip makes the trunk unmistakable
        cv2.rectangle(frame, (x1, y1), (x2, y1 + 40), (150, 110, 30) if trunk else (80, 60, 45), -1)
        cv2.putText(frame, title, (x1 + 12, y1 + 28), cv2.FONT_HERSHEY_SIMPLEX,
                    0.6, (250, 250, 250), 1, cv2.LINE_AA)

    def _draw_hud(self, frame: np.ndarray) -> None:
        for key, ratio, hsv in (
            ("thirst", self.sim.thirst, (110, 180, 220)),
            ("food", self.sim.food, (20, 200, 220)),
        ):
            region = self.cfg.region(f"hud.{key}.region")
            x1, y1, x2, y2 = region.to_pixels(self.width, self.height)
            cv2.rectangle(frame, (x1, y1), (x2, y2), (60, 60, 60), -1)
            fill = int((x2 - x1) * ratio)
            if fill > 0:
                cv2.rectangle(frame, (x1, y1), (x1 + fill, y2), hsv_bgr(*hsv), -1)
            cv2.rectangle(frame, (x1, y1), (x2, y2), (200, 200, 200), 1)
        region = self.cfg.region("hud.weight.region")
        x1, y1, x2, y2 = region.to_pixels(self.width, self.height)
        ratio = min(1.0, self.sim.fish_kg / 500.0)
        cv2.rectangle(frame, (x1, y1), (x2, y2), (60, 60, 60), -1)
        fill = int((x2 - x1) * ratio)
        if fill > 0:
            cv2.rectangle(frame, (x1, y1), (x1 + fill, y2), hsv_bgr(50, 200, 230), -1)
        cv2.putText(frame, f"{self.sim.fish_kg:.0f} kg", (x1, y1 - 4),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.5, (250, 250, 250), 1, cv2.LINE_AA)

    def _draw_trash(self, frame: np.ndarray) -> None:
        x1, y1, x2, y2 = self.button("trash_on_net")
        colour = (30, 150, 30)
        cv2.rectangle(frame, (x1, y1), (x2, y2), colour, -1)
        cv2.line(frame, (x1, y1), (x2, y2), (240, 240, 240), 2)
        cv2.line(frame, (x2, y1), (x1, y2), (240, 240, 240), 2)

    def _draw_warning(self, frame: np.ndarray) -> None:
        x1, y1, x2, y2 = self.button("net_broken")
        cv2.rectangle(frame, (x1, y1), (x2, y2), (0, 0, 210), -1)
        cv2.putText(frame, "NET", (x1 + 2, y2 - 6), cv2.FONT_HERSHEY_SIMPLEX, 0.4,
                    (255, 255, 255), 1, cv2.LINE_AA)

    def _draw_prompt(self, frame: np.ndarray) -> None:
        """Random-looking Latin letter shown while junk is being cleaned."""
        letter = "PQXOK"[(self.sim.trash + self.sim.fish_count) % 5]
        h, w = frame.shape[:2]
        cv2.putText(frame, letter, (w // 2 - 40, h // 2 + 40), cv2.FONT_HERSHEY_SIMPLEX,
                    4.0, (250, 250, 250), 12, cv2.LINE_AA)

    def _draw_stats(self, frame: np.ndarray) -> None:
        lines = [
            f"nets: {self.sim.nets}",
            f"fish: {self.sim.fish_count}",
            f"junk: {self.sim.trash}",
        ]
        for i, text in enumerate(lines):
            cv2.putText(frame, text, (self.width - 170, 30 + i * 22),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.5, (250, 250, 250), 1, cv2.LINE_AA)
