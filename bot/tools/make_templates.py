"""Generate template images so the bot can run against the simulator.

Real usage: cut templates from a screenshot of the real game
(``python -m bot.tools.crop_template``).  These synthetic ones match the
simulator's UI so ``python -m bot demo`` works out of the box, and they double
as a visual reference for what each template is supposed to be.

    python -m bot.tools.make_templates --out demo_templates
    python -m bot.tools.make_templates          # writes into templates/ if empty
"""

from __future__ import annotations

import argparse
from pathlib import Path

import cv2

from ..config import Config

# template name -> (button key, ui state, extra sim flags)
DEMO_TEMPLATES = {
    # in-world UI
    "cast_button": ("cast_button", "water", {}),
    "bite": ("bite", "water", {"bite": True}),
    "collect": ("collect", "water", {}),
    "bag_button": ("bag_button", "water", {}),
    "trunk_button": ("trunk_button", "water", {}),
    "car_near": ("trunk_button", "water", {}),
    "trash_on_net": ("trash_on_net", "water", {"trash": 2}),
    "net_broken": ("net_broken", "water", {"nets": 0}),
    # inventory window
    "food_item": ("food_item", "bag", {}),
    "water_item": ("water_item", "bag", {}),
    "food_tab": ("food_item", "bag", {}),
    "drink_tab": ("water_item", "bag", {}),
    "use_button": ("use_button", "bag", {}),
    "close_button": ("close_button", "bag", {}),
    # car trunk window
    "net_item": ("net_item", "trunk", {}),
    "trunk_window": ("trunk_window", "trunk", {}),
}


def build(cfg: Config, out_dir: Path) -> list[Path]:
    """Render one frame per UI state and cut every template from it.

    Templates must be cut from a frame where the element is actually drawn -
    cropping a menu button from the water screen would save a piece of
    background that then matches everywhere (classic false positive).
    """
    from .demo.simulator import GameSimulator, Ui

    out_dir.mkdir(parents=True, exist_ok=True)
    written: list[Path] = []
    for name, (key, state, flags) in DEMO_TEMPLATES.items():
        sim = GameSimulator(cfg)
        sim.sim.ui = {"water": Ui.WATER, "bag": Ui.BAG, "trunk": Ui.TRUNK}[state]
        for attr, value in flags.items():
            setattr(sim.sim, attr, value)
        frame = sim.render()
        x1, y1, x2, y2 = sim.button(key)
        path = out_dir / f"{name}.png"
        cv2.imwrite(str(path), frame[y1:y2, x1:x2])
        written.append(path)
    return written


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Generate demo templates")
    parser.add_argument("--config", default="config.yaml")
    parser.add_argument("--out", default="", help="output folder (default: vision.templates_dir)")
    args = parser.parse_args(argv)
    cfg = Config.load(args.config)
    out = Path(args.out) if args.out else cfg.template_dir()
    if out.exists() and any(out.glob("*.png")) and not args.out:
        print(f"{out} already has templates - use --out to write elsewhere")
        return 1
    files = build(cfg, out)
    for f in files:
        print(f"wrote {f}")
    print(f"\n{len(files)} templates in {out}")
    return 0


if __name__ == "__main__":  # pragma: no cover
    raise SystemExit(main())
