"""Weighted ensemble voting + ML probability gate with cold-start ramp.

Decision flow:
1. Each enabled strategy emits (signal, confidence).
2. Per-strategy weight = confidence * max(win_rate, 0.05) * ml_alignment.
3. Aggregate consensus must clear ENSEMBLE_CONSENSUS_THRESHOLD.
4. ML gate: ml_prob_win for the chosen side must clear an adaptive
   threshold that relaxes during cold start (few trained samples) and
   tightens once the model has seen ML_MIN_TRAINED_SAMPLES updates.
5. Risk rules (checked by RiskManager elsewhere) can always veto.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable, Optional

import config
from strategies.base import SIGNAL_BUY, SIGNAL_SELL, Strategy


@dataclass
class Decision:
    side: Optional[str]
    consensus: float
    ml_prob: float
    ml_prob_win: float            # prob of WIN for the chosen side
    ml_threshold: float           # adaptive threshold actually used
    strategy_id: str
    reason: str = ""              # human-readable why-not when side is None


def _adaptive_ml_threshold(trained_samples: int) -> float:
    """Cold-start ramp: 0.50 with no data → ML_PROB_THRESHOLD once warm."""
    target = float(config.ML_PROB_THRESHOLD)
    warm = max(1, int(config.ML_MIN_TRAINED_SAMPLES))
    if trained_samples >= warm:
        return target
    # linear ramp from 0.50 (no data) to target (fully warm)
    frac = trained_samples / warm
    return 0.50 + (target - 0.50) * frac


class Ensemble:
    def decide(self, strategies: Iterable[Strategy],
               ctx: dict, ml_prob_up: float,
               trained_samples: int = 0) -> Decision:
        total_w = 0.0
        score = 0.0
        best_contrib = 0.0
        best_id = ""
        for s in strategies:
            sig, conf = s.evaluate(ctx)
            if sig not in (SIGNAL_BUY, SIGNAL_SELL) or conf <= 0:
                continue
            wr = s.stats.win_rate
            ml_align = ml_prob_up if sig == SIGNAL_BUY else (1.0 - ml_prob_up)
            w = conf * max(wr, 0.05) * max(ml_align, 0.05)
            total_w += w
            direction = 1.0 if sig == SIGNAL_BUY else -1.0
            score += direction * w
            if w > best_contrib:
                best_contrib = w
                best_id = s.id

        thr_ml = _adaptive_ml_threshold(trained_samples)

        if total_w == 0:
            return Decision(None, 0.0, ml_prob_up, 0.5, thr_ml, "",
                            reason="no contributing strategies")

        consensus = score / total_w
        thr_cons = float(config.ENSEMBLE_CONSENSUS_THRESHOLD)
        if abs(consensus) < thr_cons:
            return Decision(None, consensus, ml_prob_up, 0.5, thr_ml,
                            best_id or "",
                            reason=f"consensus {consensus:+.2f} < {thr_cons:.2f}")

        side = SIGNAL_BUY if consensus > 0 else SIGNAL_SELL
        ml_win = ml_prob_up if side == SIGNAL_BUY else (1.0 - ml_prob_up)
        if ml_win < thr_ml:
            return Decision(None, consensus, ml_prob_up, ml_win, thr_ml,
                            best_id or "",
                            reason=f"ML p(win)={ml_win:.2f} < {thr_ml:.2f}")

        return Decision(side=side, consensus=consensus, ml_prob=ml_prob_up,
                        ml_prob_win=ml_win, ml_threshold=thr_ml,
                        strategy_id=best_id or "ensemble", reason="OK")
