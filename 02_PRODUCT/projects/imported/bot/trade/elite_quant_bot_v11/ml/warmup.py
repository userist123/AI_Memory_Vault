"""Warm up the ML model from past trades stored in the SQLite journal.

We cannot recompute exact entry-time features after the fact (no historical
tick context), so warmup uses a degraded feature proxy derived from the
trade record itself: side, ml_prob_win (if previously stored), spread,
R-multiple, etc. This is sufficient to push weights away from zero and to
seed calibration; it is NOT a replacement for true online learning.

Strategy:
- pull up to `max_trades` most recent closed trades from the journal,
- build a simple feature proxy + label = (gross_pnl > 0),
- run `epochs` SGD passes.

Returns the number of updates applied.
"""
from __future__ import annotations

from typing import List

from core.journal import Journal
from ml.features import FEATURE_DIM
from ml.model import OnlineLogReg


def _proxy_features(trade: dict) -> List[float]:
    """Build a FEATURE_DIM-sized vector from journal columns.

    Layout mirrors `ml.features.build_features` slot-by-slot; unknown slots
    are filled with 0 so the bias term still carries information.
    """
    x = [0.0] * FEATURE_DIM
    x[0] = 1.0  # bias
    side = 1.0 if (trade.get("side") == "BUY") else -1.0
    x[6] = side                                              # regime_trend slot
    x[16] = side                                             # pattern slot
    x[21] = float(trade.get("spread_entry") or 0.0) / 5.0    # spread slot
    r = float(trade.get("r_multiple") or 0.0)
    x[10] = max(-2.0, min(2.0, r))                           # atr_ratio slot proxy
    return x


def warmup_from_journal(model: OnlineLogReg, journal: Journal,
                        max_trades: int = 500, epochs: int = 2) -> int:
    rows = journal.query_recent_days(5)[:max_trades]
    if not rows:
        return 0
    updates = 0
    for _ in range(max(1, epochs)):
        for tr in rows:
            pnl = tr.get("gross_pnl")
            if pnl is None:
                continue
            y = 1 if float(pnl) > 0 else 0
            model.update(_proxy_features(tr), y)
            updates += 1
    return updates
