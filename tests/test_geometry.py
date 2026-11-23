from bot.geometry import Point, Region


def test_region_from_various_forms():
    assert Region.from_value([0.1, 0.2, 0.3, 0.4]) == Region(0.1, 0.2, 0.3, 0.4)
    assert Region.from_value("0.1, 0.2, 0.3, 0.4") == Region(0.1, 0.2, 0.3, 0.4)
    assert Region.from_value({"x": 0.1, "y": 0.2, "w": 0.3, "h": 0.4}) == Region(0.1, 0.2, 0.3, 0.4)


def test_region_scales_with_resolution():
    region = Region(0.25, 0.5, 0.5, 0.25)
    assert region.to_pixels(1280, 720) == (320, 360, 960, 540)
    assert region.to_pixels(640, 360) == (160, 180, 480, 270)
    assert region.center(1000, 500) == Point(500, 312)


def test_region_validation_rejects_garbage():
    for bad in ([0.5, 0.5, 0.0, 0.1], [0.9, 0.1, 0.5, 0.1], [-0.2, 0.1, 0.1, 0.1]):
        try:
            Region.from_value(bad).validate("test")
        except ValueError:
            continue
        raise AssertionError(f"{bad} should be rejected")


def test_with_margin_clips_to_screen():
    grown = Region(0.98, 0.01, 0.02, 0.02).with_margin(0.05)
    grown.validate("grown")
    assert grown.x >= 0 and grown.y >= 0
    assert grown.x + grown.w <= 1.0001
