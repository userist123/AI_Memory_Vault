"""Trading-specific memory helpers built on the existing proposal queue.

Does not bypass MemoryController; it only shapes trading decisions into
well-formed RAW candidates with strategy/symbol/backtest metadata, ready
for the standard extract -> review -> approve -> promote-approved flow.
"""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional

from .extraction import MemoryCandidate


@dataclass
class TradingDecision:
    symbol: str
    strategy: str
    action: str
    rationale: str
    backtest_ref: Optional[str] = None
    confidence: str = "medium"


class TradingDecisionLogger:
    """Converts TradingDecision entries into MemoryCandidate objects for the queue."""

    def to_candidate(self, decision: TradingDecision, source_ref: str) -> MemoryCandidate:
        content = (
            f"[{decision.symbol}] {decision.strategy} -> {decision.action}. "
            f"Rationale: {decision.rationale}"
        )
        if decision.backtest_ref:
            content += f" (backtest: {decision.backtest_ref})"
        return MemoryCandidate(
            candidate_id=f"trading-{decision.symbol}-{datetime.now(timezone.utc).timestamp()}",
            type="decision",
            category="trading",
            content=content,
            confidence=decision.confidence,
            lifecycle="RAW",
            verification="unverified",
            tags=["trading", decision.symbol.lower(), decision.strategy.lower()],
            provenance={"source_type": "execution", "source_ref": source_ref,
                        "extractor": "trading-decision-logger"},
            source_event_ids=[],
            created_at=datetime.now(timezone.utc).isoformat(),
            content_hash="",
        )

    def batch(self, decisions: List[TradingDecision], source_ref: str) -> List[MemoryCandidate]:
        return [self.to_candidate(d, source_ref) for d in decisions]
