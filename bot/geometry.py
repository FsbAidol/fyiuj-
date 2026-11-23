"""Resolution independent screen geometry.

Every region in the config is stored in *relative* coordinates (0..1), so the
bot keeps working when the emulator resolution changes.  Pixels are only
computed at the moment of a click.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Mapping, Sequence

_MAX_ERROR = 8.0  # relative units; guards against fat-fingered configs


@dataclass(frozen=True)
class Point:
    x: int
    y: int

    def as_tuple(self) -> tuple[int, int]:
        return (self.x, self.y)

    def __str__(self) -> str:  # pragma: no cover - trivial
        return f"({self.x}, {self.y})"


@dataclass(frozen=True)
class Region:
    """Axis aligned rectangle in relative screen coordinates."""

    x: float
    y: float
    w: float
    h: float

    # ------------------------------------------------------------------ parse
    @classmethod
    def from_value(cls, value: Any) -> "Region":
        """Accept [x, y, w, h], {"x":..}, "x,y,w,h" or a Region."""
        if isinstance(value, Region):
            return value
        if isinstance(value, Mapping):
            try:
                return cls(float(value["x"]), float(value["y"]), float(value["w"]), float(value["h"]))
            except KeyError as exc:  # pragma: no cover - defensive
                raise ValueError(f"region is missing key {exc}") from exc
        if isinstance(value, str):
            parts = [p.strip() for p in value.replace(";", ",").split(",")]
            if len(parts) != 4:
                raise ValueError(f"region string must have 4 numbers: {value!r}")
            return cls(*(float(p) for p in parts))
        if isinstance(value, Sequence) and not isinstance(value, (bytes, bytearray)):
            if len(value) != 4:
                raise ValueError(f"region sequence must have 4 numbers: {value!r}")
            return cls(*(float(p) for p in value))
        raise ValueError(f"cannot build Region from {value!r}")

    # ------------------------------------------------------------------ validate
    def validate(self, name: str = "region") -> None:
        if self.w <= 0 or self.h <= 0:
            raise ValueError(f"{name}: width/height must be positive, got {self.w}x{self.h}")
        if not (-_MAX_ERROR <= self.x <= _MAX_ERROR and -_MAX_ERROR <= self.y <= _MAX_ERROR):
            raise ValueError(f"{name}: x/y must be relative 0..1, got {self.x},{self.y}")
        if self.w > _MAX_ERROR or self.h > _MAX_ERROR:
            raise ValueError(f"{name}: width/height must be relative 0..1, got {self.w}x{self.h}")
        if self.x < 0 or self.y < 0 or self.x + self.w > 1.0001 or self.y + self.h > 1.0001:
            raise ValueError(f"{name}: region leaves the screen: {self.as_list()}")

    # ------------------------------------------------------------------ convert
    def to_pixels(self, width: int, height: int) -> tuple[int, int, int, int]:
        """Return (x1, y1, x2, y2) clipped to the frame."""
        x1 = max(0, min(width, int(round(self.x * width))))
        y1 = max(0, min(height, int(round(self.y * height))))
        x2 = max(x1 + 1, min(width, int(round((self.x + self.w) * width))))
        y2 = max(y1 + 1, min(height, int(round((self.y + self.h) * height))))
        return x1, y1, x2, y2

    def center(self, width: int, height: int) -> Point:
        x1, y1, x2, y2 = self.to_pixels(width, height)
        return Point((x1 + x2) // 2, (y1 + y2) // 2)

    def contains_point(self, point: Point, width: int, height: int) -> bool:
        x1, y1, x2, y2 = self.to_pixels(width, height)
        return x1 <= point.x < x2 and y1 <= point.y < y2

    # ------------------------------------------------------------------ misc
    def as_list(self) -> list[float]:
        return [round(self.x, 4), round(self.y, 4), round(self.w, 4), round(self.h, 4)]

    def with_margin(self, margin: float) -> "Region":
        """Grow the region, clipping to the screen - handy for search windows."""
        x = max(0.0, self.x - margin)
        y = max(0.0, self.y - margin)
        w = min(1.0 - x, self.w + 2 * margin)
        h = min(1.0 - y, self.h + 2 * margin)
        return Region(x, y, w, h)


def clamp_point(point: Point, width: int, height: int) -> Point:
    return Point(max(0, min(width - 1, point.x)), max(0, min(height - 1, point.y)))
