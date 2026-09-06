"""
Trading Bot — Strategy Engine
Automated trading strategies with risk management.
"""
import logging
from dataclasses import dataclass, field
from typing import Optional, List, Callable
from datetime import datetime
from enum import Enum

log = logging.getLogger("tradingbot.strategy")


class StrategyStatus(Enum):
    IDLE = "idle"
    RUNNING = "running"
    PAUSED = "paused"
    STOPPED = "stopped"


@dataclass
class TradeRecord:
    timestamp: str
    symbol: str
    side: str
    price: float
    amount: float
    pnl: float = 0.0
    reason: str = ""


@dataclass
class StrategyConfig:
    name: str = "Default"
    enabled: bool = False
    symbols: list = field(default_factory=lambda: ["BTC-USD"])
    timeframe: str = "1D"
    # Entry conditions
    min_score: float = 70.0           # minimum AI score to enter
    min_confidence: float = 60.0
    required_strength: str = "MODERATE"  # WEAK / MODERATE / STRONG
    # Risk management
    max_risk_pct: float = 2.0        # max % of portfolio per trade
    max_positions: int = 3
    stop_loss_atr_mult: float = 2.0
    take_profit_atr_mult: float = 3.0
    trailing_stop_pct: float = 0.0   # 0 = disabled
    # Filters
    min_volume_ratio: float = 0.8
    min_adx: float = 15.0
    avoid_overbought: bool = True    # skip if RSI > 75
    avoid_oversold_shorts: bool = True  # skip shorts if RSI < 25
    # Auto-trading
    auto_execute: bool = False       # if True, places orders automatically


class StrategyEngine:
    """
    Evaluates market conditions against strategy rules and produces trade decisions.
    Does NOT execute trades directly — returns decisions for the user to confirm
    (unless auto_execute is enabled in config).
    """

    def __init__(self):
        self.strategies: List[StrategyConfig] = []
        self.trade_log: List[TradeRecord] = []
        self.active_signals = {}

    def add_strategy(self, config: StrategyConfig):
        self.strategies.append(config)
        log.info(f"Strategie adaugata: {config.name}")

    def remove_strategy(self, name: str):
        self.strategies = [s for s in self.strategies if s.name != name]

    def evaluate(self, report, strategy: StrategyConfig = None) -> dict:
        """
        Evaluate an AdviceReport against strategy rules.
        Returns a decision dict.
        """
        cfg = strategy or (self.strategies[0] if self.strategies else StrategyConfig())

        decision = {
            "action": "NONE",
            "reason": "",
            "passes_filters": True,
            "score": report.score,
            "symbol": report.symbol,
        }

        sig = report.signal

        # Filter checks
        if report.score < cfg.min_score and sig.direction != "HOLD":
            if sig.direction == "BUY" and report.score < cfg.min_score:
                decision["passes_filters"] = False
                decision["reason"] = f"Scor {report.score:.0f} < minim {cfg.min_score}"
                return decision
            if sig.direction == "SELL" and (100 - report.score) < cfg.min_score:
                decision["passes_filters"] = False
                decision["reason"] = f"Scor bearish insuficient"
                return decision

        if sig.confidence < cfg.min_confidence:
            decision["passes_filters"] = False
            decision["reason"] = f"Confidenta {sig.confidence:.0f}% < {cfg.min_confidence}%"
            return decision

        strength_rank = {"WEAK": 0, "MODERATE": 1, "STRONG": 2}
        if strength_rank.get(sig.strength, 0) < strength_rank.get(cfg.required_strength, 1):
            decision["passes_filters"] = False
            decision["reason"] = f"Putere {sig.strength} < {cfg.required_strength}"
            return decision

        indicators = report.key_indicators
        vol_ratio = indicators.get("Vol_Ratio", 1.0)
        if vol_ratio < cfg.min_volume_ratio:
            decision["passes_filters"] = False
            decision["reason"] = f"Volum ratio {vol_ratio:.2f} < {cfg.min_volume_ratio}"
            return decision

        adx = indicators.get("ADX", 20)
        if adx < cfg.min_adx:
            decision["passes_filters"] = False
            decision["reason"] = f"ADX {adx:.1f} < {cfg.min_adx} (piata laterala)"
            return decision

        rsi = indicators.get("RSI", 50)
        if cfg.avoid_overbought and rsi > 75 and sig.direction == "BUY":
            decision["passes_filters"] = False
            decision["reason"] = f"RSI {rsi:.1f} > 75 — supracumparare, evit BUY"
            return decision

        if cfg.avoid_oversold_shorts and rsi < 25 and sig.direction == "SELL":
            decision["passes_filters"] = False
            decision["reason"] = f"RSI {rsi:.1f} < 25 — supravanzare, evit SELL"
            return decision

        # All filters passed
        if sig.direction in ("BUY", "SELL"):
            decision["action"] = sig.direction
            decision["entry"] = sig.entry
            decision["stop_loss"] = sig.stop_loss
            decision["take_profit"] = sig.take_profit_2
            decision["position_size_pct"] = sig.position_size_pct
            decision["risk_reward"] = sig.risk_reward
            decision["reason"] = f"Toate filtrele trec. {sig.reason}"
        else:
            decision["action"] = "HOLD"
            decision["reason"] = sig.reason

        return decision

    def log_trade(self, symbol: str, side: str, price: float,
                  amount: float, pnl: float = 0, reason: str = ""):
        record = TradeRecord(
            timestamp=datetime.now().isoformat(),
            symbol=symbol, side=side, price=price,
            amount=amount, pnl=pnl, reason=reason,
        )
        self.trade_log.append(record)

    def get_stats(self) -> dict:
        if not self.trade_log:
            return {"total": 0, "wins": 0, "losses": 0, "win_rate": 0, "total_pnl": 0}
        wins = sum(1 for t in self.trade_log if t.pnl > 0)
        losses = sum(1 for t in self.trade_log if t.pnl < 0)
        total_pnl = sum(t.pnl for t in self.trade_log)
        return {
            "total": len(self.trade_log),
            "wins": wins, "losses": losses,
            "win_rate": round(wins / len(self.trade_log) * 100, 1) if self.trade_log else 0,
            "total_pnl": round(total_pnl, 2),
        }
