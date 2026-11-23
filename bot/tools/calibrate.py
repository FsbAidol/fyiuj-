"""Interactive calibration helper.

Rather than eyeballing normalised coordinates, take a snapshot, print an
annotated copy with a grid, and let the user type what they see:

    python -m bot snapshot -o samples/shot.png
    python -m bot calibrate

The result is printed as ready-to-paste YAML for ``hud.*.region``.
"""

from __future__ import annotations

import shutil
from pathlib import Path

import cv2

from ..config import Config
from ..vision import TemplateStore, crop, draw_matches, find_template

GUIDE = """
Калибровка HUD
--------------
1) Сначала сделайте снимок экрана игры:
     python -m bot snapshot -o samples/shot.png
2) Ниже будет создан samples/shot_grid.png - откройте его: клетка = 0.05 экрана.
3) Для каждой полоски введите координаты в пикселях как X1,Y1,X2,Y2
   (левый верхний и правый нижний угол ЗАПОЛНЕННОЙ полоски).
   Enter = оставить текущее значение.
"""


def _annotate_grid(frame):
    h, w = frame.shape[:2]
    out = frame.copy()
    for i in range(1, 20):
        x = int(w * i / 20)
        y = int(h * i / 20)
        cv2.line(out, (x, 0), (x, h), (60, 60, 60), 1)
        cv2.line(out, (0, y), (w, y), (60, 60, 60), 1)
        if i % 2 == 0:
            cv2.putText(out, f"{i/20:.2f}", (x + 2, 14), cv2.FONT_HERSHEY_SIMPLEX, 0.35, (200, 200, 200), 1)
            cv2.putText(out, f"{i/20:.2f}", (2, y - 2), cv2.FONT_HERSHEY_SIMPLEX, 0.35, (200, 200, 200), 1)
    return out


def _ask_region(label: str, current: list[float] | None, width: int, height: int):
    prompt = f"{label} [{current}] px x1,y1,x2,y2: "
    try:
        answer = input(prompt).strip()
    except EOFError:
        return None
    if not answer:
        return None
    try:
        x1, y1, x2, y2 = (float(v) for v in answer.replace(";", ",").split(","))
    except ValueError:
        print("  не понял, ожидаю четыре числа")
        return None
    x1, x2 = sorted((x1, x2))
    y1, y2 = sorted((y1, y2))
    rel = [round(x1 / width, 3), round(y1 / height, 3),
           round((x2 - x1) / width, 3), round((y2 - y1) / height, 3)]
    return rel


def interactive_calibration(cfg: Config) -> int:
    from ..device.capture import build_capture

    print(GUIDE)
    print("Снимаю кадр...")
    cap = build_capture(cfg)
    try:
        frame = cap.grab()
    finally:
        cap.close()
    h, w = frame.shape[:2]
    samples = cfg.base_dir / "samples"
    samples.mkdir(exist_ok=True)
    shot = samples / "shot.png"
    grid = samples / "shot_grid.png"
    cv2.imwrite(str(shot), frame)
    cv2.imwrite(str(grid), _annotate_grid(frame))
    print(f"  {shot}\n  {grid}\n")

    updates: dict[str, list[float]] = {}
    for kind in ("thirst", "food", "weight"):
        current = cfg.get(f"hud.{kind}.region")
        rel = _ask_region(f"hud.{kind}", current, w, h)
        if rel:
            updates[f"hud.{kind}.region"] = rel

    print("\nВставьте в config.yaml:\n")
    merged = dict(cfg.raw)
    for key, value in updates.items():
        parts = key.split(".")
        node = merged
        for part in parts[:-1]:
            node = node.setdefault(part, {})
        node[parts[-1]] = value
        print(f"hud:\n  {parts[1]}:\n    region: {value}")
    if not updates:
        print("  (изменений нет)")

    # optionally preview a template match so the user sees the score
    tpl_name = input("\nПроверить шаблон (Enter = пропустить): ").strip()
    if tpl_name:
        store = TemplateStore(cfg.template_dir(), cfg.get("vision.scales", [1.0]) or [1.0])
        tpl = store.get(tpl_name, True)
        if tpl is None:
            print(f"  шаблон {tpl_name!r} не найден в {store.directory}")
        else:
            match = find_template(frame, tpl, threshold=0.5)
            if match:
                print(f"  найден со score {match.score:.2f} в {match.center}")
                preview = samples / f"{tpl_name}_preview.png"
                cv2.imwrite(str(preview), draw_matches(frame, [match]))
                print(f"  превью: {preview}")
            else:
                print("  совпадений нет")
    return 0
