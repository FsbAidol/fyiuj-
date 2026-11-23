import cv2
import numpy as np

from bot.geometry import Region
from bot.vision import (
    BarReading,
    count_template,
    find_template,
    hsv_mask,
    mean_brightness,
    motion_score,
    parse_number,
    read_bar,
)
from conftest import blank_frame, draw_bar, draw_button


def _save(tmp_path, name, img):
    path = tmp_path / f"{name}.png"
    cv2.imwrite(str(path), img)
    return cv2.imread(str(path), cv2.IMREAD_GRAYSCALE)


def test_find_template_locates_button(tmp_path):
    frame = blank_frame()
    frame = draw_button(frame, 400, 250, label="CAST")
    tpl = _save(tmp_path, "cast", frame[250:278, 400:460])
    match = find_template(frame, tpl, threshold=0.9)
    assert match is not None
    assert abs(match.center.x - 430) <= 2
    assert abs(match.center.y - 264) <= 2


def test_find_template_respects_threshold(tmp_path):
    frame = blank_frame()
    tpl = _save(tmp_path, "t", draw_button(blank_frame(), 0, 0))
    assert find_template(frame, tpl, threshold=0.9) is None


def test_find_template_region_offset(tmp_path):
    frame = draw_button(blank_frame(), 300, 150)
    tpl = _save(tmp_path, "b", frame[150:178, 300:360])
    region = Region(0.0, 0.0, 0.6, 0.6)
    match = find_template(frame, tpl, 0.9, region=region)
    assert match is not None
    # coordinates are in full-frame space even though the search was limited
    assert match.center.x > 280


def test_find_template_multi_scale(tmp_path):
    frame = blank_frame()
    frame = draw_button(frame, 100, 100, w=80, h=40)
    tpl_big = _save(tmp_path, "big", frame[100:140, 100:180])
    tpl_small = cv2.resize(tpl_big, (40, 20), interpolation=cv2.INTER_AREA)
    match = find_template(frame, tpl_small, 0.7, scales=(1.6, 2.0))
    assert match is not None
    assert match.scale > 1.0


def test_template_survives_noise(tmp_path):
    frame = blank_frame()
    frame = draw_button(frame, 200, 120, noise=6)
    tpl = _save(tmp_path, "noisy", frame[120:148, 200:260])
    assert find_template(draw_button(blank_frame(), 200, 120, noise=6), tpl, 0.85) is not None


def test_count_template_finds_two(tmp_path):
    frame = draw_button(draw_button(blank_frame(), 50, 50), 400, 200)
    tpl = _save(tmp_path, "icon", frame[50:78, 50:110])
    hits = count_template(frame, tpl, 0.9)
    assert len(hits) == 2


def test_read_bar_full_partial_empty():
    frame = blank_frame()
    region = Region(0.1, 0.1, 0.6, 0.05)
    hsv = [[90, 60, 90], [130, 255, 255]]
    x, y, w, h = region.to_pixels(640, 360)

    half = draw_bar(frame, x, y, w - x, h - y, 0.5)
    reading = read_bar(half, region, hsv)
    assert reading.found
    assert 0.45 <= reading.ratio <= 0.6

    full = draw_bar(frame, x, y, w - x, h - y, 1.0)
    assert read_bar(full, region, hsv).ratio > 0.95

    empty = draw_bar(frame, x, y, w - x, h - y, 0.0)
    assert read_bar(empty, region, hsv).ratio < 0.05


def test_read_bar_ignores_other_colours():
    frame = blank_frame()
    region = Region(0.1, 0.1, 0.4, 0.05)
    x, y, w, h = region.to_pixels(640, 360)
    # orange bar, but we look for blue
    orange = draw_bar(frame, x, y, w - x, h - y, 0.8, hsv_colour=(20, 200, 220))
    assert read_bar(orange, region, [[90, 60, 90], [130, 255, 255]]).ratio < 0.1


def test_hsv_mask_and_brightness():
    frame = np.zeros((50, 50, 3), np.uint8)
    # black has S=0, so a saturated range must not match it
    assert hsv_mask(frame, [[0, 60, 60], [10, 255, 255]]).max() == 0
    assert mean_brightness(np.full((10, 10, 3), 200, np.uint8)) == 200


def test_motion_score_detects_change():
    a = np.zeros((40, 40), np.uint8)
    b = a.copy()
    b[10:20, 10:20] = 255
    assert motion_score(a, b) > 0.01
    assert motion_score(a, a.copy()) == 0.0
    assert motion_score(None, b) == 0.0


def test_parse_number():
    assert parse_number("1 234,5 кг") == 1234.5
    assert parse_number("500") == 500.0
    assert parse_number("1.234,5") == 1234.5
    assert parse_number("12.5 kg") == 12.5
    assert parse_number("нет числа") is None
