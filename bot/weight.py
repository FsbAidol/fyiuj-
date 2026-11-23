"""Keeping the 500 kg budget honest.

Two independent estimates are maintained and combined:

* **observed** - when the game shows the total (weight bar reading or OCR of
  the counter), we just read it; every observation updates the per-fish average.
* **extrapolated** - ``caught_count * avg_kg``, used between observations.

The hard stop always uses the *larger* of the two, and a safety margin is
subtracted from the remaining budget.  That way the bot stops slightly *before*
500 kg instead of a catch late: losing one fish is acceptable, breaking the
limit is not.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


@dataclass
class WeightBook:
    limit_kg: float = 500.0
    default_avg_kg: float = 1.0
    # weight of one fish is unknown for the first catch; keep a small floor
    min_avg_kg: float = 0.3
    margin_kg: float = 0.0

    catches: int = 0
    observed_kg: float | None = None
    avg_kg: float | None = None
    history: list[float] = field(default_factory=list)
    largest_catch: float = 0.0

    # ------------------------------------------------------------------ update
    def note_catch(self, kg: float | None = None) -> None:
        self.catches += 1
        if kg is not None and kg > 0:
            self.history.append(float(kg))
            self.largest_catch = max(self.largest_catch, float(kg))
            if len(self.history) > 200:
                self.history.pop(0)
            self._recompute()

    def observe_total(self, kg: float) -> None:
        """Feed a trusted reading of the total collected weight."""
        if kg < 0:
            return
        previous = self.observed_kg
        self.observed_kg = float(kg)
        if previous is not None and self.catches:
            # refine the average only when the total actually grew
            delta = self.observed_kg - previous
            if 0 < delta <= 50:
                self.history.append(delta)
                if len(self.history) > 200:
                    self.history.pop(0)
        self._recompute()

    def _recompute(self) -> None:
        # worst case (the biggest catch so far) is what can overshoot the limit
        if self.history:
            # conservative: the largest observed catch is what can overshoot us
            mean = sum(self.history) / len(self.history)
            self.avg_kg = max(self.min_avg_kg, max(mean, self.largest_catch))

    # ------------------------------------------------------------------ queries
    def effective_avg(self) -> float:
        """Worst-case weight per fish - used to decide if one more catch fits."""
        if self.avg_kg is not None:
            return max(self.avg_kg, self.largest_catch)
        return max(self.min_avg_kg, self.default_avg_kg, self.largest_catch)

    def estimate(self, fallback_total: float = 0.0) -> float:
        """Best estimate of the total collected weight."""
        extrapolated = self.catches * self.effective_avg()
        candidates = [max(0.0, fallback_total), extrapolated]
        if self.observed_kg is not None:
            candidates.append(self.observed_kg)
        return max(candidates)

    def remaining(self, fallback_total: float = 0.0) -> float:
        left = self.limit_kg - self.estimate(fallback_total) - max(0.0, self.margin_kg)
        return max(0.0, left)

    def would_exceed(self, fallback_total: float = 0.0) -> bool:
        return self.remaining(fallback_total) < self.effective_avg() * 0.5

    def catch_budget(self, fallback_total: float = 0.0) -> int:
        """How many more fish fit into the budget (at the worst case weight)."""
        worst = max(self.effective_avg(), self.largest_catch or 0.0, self.min_avg_kg)
        return int(self.remaining(fallback_total) // max(0.01, worst))

    def summary(self) -> str:
        return (f"{self.estimate():.1f}/{self.limit_kg:.0f} kg "
                f"(~{self.effective_avg():.2f} kg/fish, n={self.catches})")

    # ------------------------------------------------------------------ io
    def to_dict(self) -> dict[str, Any]:
        return {
            "limit_kg": self.limit_kg,
            "catches": self.catches,
            "observed_kg": self.observed_kg,
            "avg_kg": self.avg_kg,
            "history": self.history[-200:],
            "largest_catch": self.largest_catch,
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "WeightBook":
        book = cls(limit_kg=float(data.get("limit_kg", 500.0)))
        book.catches = int(data.get("catches", 0) or 0)
        observed = data.get("observed_kg")
        book.observed_kg = float(observed) if observed is not None else None
        avg = data.get("avg_kg")
        book.avg_kg = float(avg) if avg is not None else None
        book.history = [float(x) for x in (data.get("history") or [])][-200:]
        book.largest_catch = float(data.get("largest_catch", 0.0) or 0.0)
        return book
