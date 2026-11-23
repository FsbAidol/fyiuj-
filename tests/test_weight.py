"""The 500 kg budget must never be overshot by much - that is the promise."""

from bot.weight import WeightBook


def test_fresh_book_uses_default_average():
    book = WeightBook(limit_kg=500, default_avg_kg=2.0)
    assert book.effective_avg() == 2.0
    assert book.estimate() == 0
    assert book.remaining() == 500


def test_estimate_follows_catch_count():
    book = WeightBook(limit_kg=100, default_avg_kg=2.0)
    for _ in range(10):
        book.note_catch(2.0)
    assert book.estimate() == 20.0
    assert book.remaining() == 80.0


def test_observation_wins_when_higher():
    book = WeightBook(limit_kg=100, default_avg_kg=1.0)
    book.note_catch(1.0)
    book.observe_total(37.0)
    assert book.estimate(fallback_total=2.0) == 37.0


def test_observation_lower_than_count_is_ignored():
    book = WeightBook(limit_kg=100, default_avg_kg=5.0)
    for _ in range(10):
        book.note_catch(5.0)
    book.observe_total(10.0)          # e.g. HUD not updated yet
    assert book.estimate() == 50.0    # extrapolation stays authoritative


def test_largest_catch_drives_the_average_up():
    book = WeightBook(limit_kg=500, default_avg_kg=1.0)
    for kg in (1, 1, 1, 12):
        book.note_catch(kg)
    assert book.effective_avg() >= 9.0   # worst case: the 12 kg fish


def test_would_exceed_blocks_the_last_half_fish():
    book = WeightBook(limit_kg=10, default_avg_kg=2.0)
    for _ in range(4):
        book.note_catch(2.0)            # 8 kg
    assert book.would_exceed() is False
    for _ in range(2):
        book.note_catch(2.0)            # 12 kg -> over
    assert book.would_exceed() is True


def test_margin_reserves_space():
    book = WeightBook(limit_kg=10, default_avg_kg=1.0, margin_kg=2.0)
    assert book.remaining() == 8.0


def test_remaining_never_negative():
    book = WeightBook(limit_kg=10, default_avg_kg=5.0)
    for _ in range(10):
        book.note_catch(5.0)
    assert book.remaining() == 0.0


def test_catch_budget_uses_worst_case():
    book = WeightBook(limit_kg=100, default_avg_kg=1.0)
    book.note_catch(10)                 # one big fish seen
    assert book.catch_budget() <= 10


def test_roundtrip_serialisation():
    book = WeightBook(limit_kg=500, default_avg_kg=1.5)
    for _ in range(3):
        book.note_catch(1.7)
    book.observe_total(5.1)
    restored = WeightBook.from_dict(book.to_dict())
    assert restored.catches == 3
    assert restored.observed_kg == 5.1
    assert restored.estimate() == book.estimate()


def test_history_is_bounded():
    book = WeightBook()
    for _ in range(500):
        book.note_catch(1.0)
    assert len(book.history) <= 200
    assert len(book.to_dict()["history"]) <= 200
