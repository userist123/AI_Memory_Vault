"""Glue between model + persistent store + calibration tracker."""
from __future__ import annotations

from collections import deque
from typing import Deque, List, Tuple

from ml.model import OnlineLogReg
from ml.store import MLStore


class Trainer:
    """Wraps `OnlineLogReg` with rolling loss + calibration tracking.

    - rolling_loss: mean BCE over recent updates
    - calibration_high: win rate for samples where predicted prob >= 0.6
      (only meaningful once ~30 high-prob predictions are recorded)
    """

    def __init__(self, model: OnlineLogReg, store: MLStore,
                 window: int = 200) -> None:
        self.model = model
        self.store = store
        self._recent: Deque[Tuple[float, int]] = deque(maxlen=window)

    # ----------------------------------------------------------- updates
    def update(self, features: List[float], won: bool) -> None:
        import math
        p = self.model.predict_proba(features)
        y = 1 if won else 0
        # BCE before the SGD step (predictive performance)
        bce = -(y * math.log(max(p, 1e-9))
                + (1 - y) * math.log(max(1 - p, 1e-9)))
        self._recent.append((p, y))
        self.model.update(features, y)
        self.store.save_model(self.model)
        return bce  # noqa: returned for callers that care; ignored otherwise

    # ----------------------------------------------------------- stats
    def rolling_loss(self) -> float:
        if not self._recent:
            return 0.0
        import math
        s = 0.0
        for p, y in self._recent:
            s += -(y * math.log(max(p, 1e-9))
                   + (1 - y) * math.log(max(1 - p, 1e-9)))
        return s / len(self._recent)

    def calibration_high(self, threshold: float = 0.6) -> Tuple[int, float]:
        """Return (n, win_rate) for predictions with p>=threshold."""
        bucket = [y for p, y in self._recent if p >= threshold]
        if not bucket:
            return 0, 0.0
        return len(bucket), sum(bucket) / len(bucket)

    def trained_samples(self) -> int:
        return self.model.trained_samples
