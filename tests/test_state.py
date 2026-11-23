import json
import os

from bot.state import BotState, StateStore


def test_add_catch_accumulates_and_resets_failures():
    state = BotState(total_kg=10.0, consecutive_failures=3)
    state.add_catch(2.5)
    assert state.total_kg == 12.5
    assert state.fish_count == 1
    assert state.consecutive_failures == 0


def test_negative_or_zero_weights_are_safe():
    state = BotState()
    state.add_catch(-5)
    state.add_catch(0)
    assert state.total_kg == 0.0
    assert state.fish_count == 2


def test_store_roundtrip(tmp_path):
    store = StateStore(tmp_path / "state.json")
    state = BotState(total_kg=123.456, fish_count=99, stop_reason="test")
    store.save(state)
    loaded = store.load()
    assert loaded is not None
    assert loaded.total_kg == 123.456
    assert loaded.fish_count == 99
    assert loaded.stop_reason == "test"
    # atomic write leaves no temp files behind
    assert not (tmp_path / "state.json.tmp").exists()


def test_store_survives_corruption(tmp_path):
    path = tmp_path / "state.json"
    path.write_text("{not json")
    assert StateStore(path).load() is None


def test_reset_zeroes_counter(tmp_path):
    store = StateStore(tmp_path / "state.json")
    store.save(BotState(total_kg=499.0))
    fresh = store.reset()
    assert fresh.total_kg == 0.0
    assert store.load().total_kg == 0.0


def test_uptime_string_format():
    state = BotState()
    state.started_at -= 3725
    assert state.uptime_str().startswith("01:02:")
    assert "fish=" in state.summary()
