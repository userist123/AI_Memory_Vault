"""Background trading loop: multi-concurrent trades with multi-TP ladder,
partial closes, SL trailing, journal logging, audit, rolling history
buffers, and dashboard snapshots.

Supports paper-trading executor (drop-in) and live config restart.
"""
from __future__ import annotations

import threading
import time
from collections import deque
from dataclasses import dataclass, field
from datetime import datetime, time as dtime, timezone
from typing import Any, Deque, Dict, List, Optional

try:
    import MetaTrader5 as mt5
except ImportError:  # pragma: no cover
    mt5 = None  # type: ignore

import config
from core.audit import AuditLogger
from core.execution import (Executor, OrderPlan, build_levels,
                             build_tp_ladder, round_volume)
from core.journal import Journal
from core.mt5_client import MT5Client
from core.paper_executor import PaperExecutor
from core.risk_manager import RiskManager
from data.feed import DataFeed
from data.indicators import atr as calc_atr
from ml.features import build_features
from ml.model import OnlineLogReg
from ml.trainer import Trainer
from strategies.ensemble import Ensemble
from strategies.factory import StrategyFactory


@dataclass
class Snapshot:
    connected: bool = False
    balance: float = 0.0
    equity: float = 0.0
    daily_pnl: float = 0.0
    active_ticket: Optional[int] = None
    active_pnl: float = 0.0
    consensus: float = 0.0
    ml_prob: float = 0.5
    top_strategies: List[tuple] = field(default_factory=list)
    log: List[str] = field(default_factory=list)
    running: bool = False
    paper: bool = False
    consensus_history: List[float] = field(default_factory=list)
    ml_history: List[float] = field(default_factory=list)
    strategy_history: Dict[str, List[int]] = field(default_factory=dict)
    active_trades: List[Dict[str, Any]] = field(default_factory=list)
    today: Dict[str, Any] = field(default_factory=dict)


class StateMachine:
    def __init__(self,
                 client: MT5Client,
                 executor: Executor,
                 risk: RiskManager,
                 feed: DataFeed,
                 factory: StrategyFactory,
                 ensemble: Ensemble,
                 model: OnlineLogReg,
                 trainer: Trainer,
                 audit: Optional[AuditLogger] = None,
                 journal: Optional[Journal] = None) -> None:
        self.client = client
        self.executor: object = executor
        self.risk = risk
        self.feed = feed
        self.factory = factory
        self.ensemble = ensemble
        self.model = model
        self.trainer = trainer
        self.journal = journal or Journal()
        self.audit = audit or AuditLogger(journal=self.journal)
        if self.audit.journal is None:
            self.audit.journal = self.journal

        self._thread: Optional[threading.Thread] = None
        self._stop = threading.Event()
        self._auto = False
        self._active_ticket: Optional[int] = None
        self._active: Dict[int, Dict[str, Any]] = {}
        self._log: List[str] = []
        self.snapshot = Snapshot()
        self._lock = threading.Lock()

        self._consensus_hist: Deque[float] = deque(maxlen=config.HISTORY_MAX_POINTS)
        self._ml_hist: Deque[float] = deque(maxlen=config.HISTORY_MAX_POINTS)
        self._strategy_hist: Dict[str, Deque[int]] = {}
        self._last_skip_log: Dict[str, float] = {}

        self.cfg = config
        self.factory.set_status_listener(self._on_status_change)

    # ------------------------------------------------------------- control
    def start(self) -> None:
        if self._thread and self._thread.is_alive():
            return
        self._stop.clear()
        self._thread = threading.Thread(target=self._run, daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        if self._thread:
            self._thread.join(timeout=2.0)

    def set_auto(self, on: bool) -> None:
        self._auto = on
        self._log_event(f"Auto trade {'ENABLED' if on else 'DISABLED'}")

    def set_auto_trade(self, on: bool) -> None:
        self.set_auto(on)

    def kill_switch(self) -> None:
        self._auto = False
        tickets = list(self._active.keys())
        positions = self._open_positions_map()
        for tkt, pos in positions.items():
            try:
                self.executor.close(pos)  # type: ignore[union-attr]
            except Exception:
                pass
            self._finalise_trade(tkt, exit_reason="MANUAL_KILL")
        self._active.clear()
        self._active_ticket = None
        self.audit.log_breaker(event="kill_switch", tickets=tickets)
        self._log_event(f"KILL SWITCH executed ({len(tickets)} positions)")

    def kill_all(self) -> None:
        self.kill_switch()

    # ----------------------------------------------- paper / config / restart
    def set_paper_mode(self, on: bool) -> None:
        on = bool(on)
        if on and not getattr(self.executor, "is_paper", False):
            self.executor = PaperExecutor(self.client)
            config.PAPER_TRADING = True
            self._log_event("Switched to PAPER trading")
        elif not on and getattr(self.executor, "is_paper", False):
            self.executor = Executor(self.client)
            config.PAPER_TRADING = False
            self._log_event("Switched to LIVE trading")

    def set_paper_trading(self, on: bool) -> None:
        self.set_paper_mode(on)

    def apply_config(self, updates: Dict[str, object]) -> Dict[str, object]:
        applied = config.apply_overrides(updates)
        if "PAPER_TRADING" in applied:
            self.set_paper_mode(bool(applied["PAPER_TRADING"]))
        self._log_event(f"Config updated: {sorted(applied.keys())}")
        return applied

    def update_config(self, updates: Dict[str, object]) -> Dict[str, object]:
        return self.apply_config(updates)

    def export_daily_report(self):
        return self.audit.export_daily_report(factory=self.factory)

    def restart(self) -> None:
        was_auto = self._auto
        self._auto = False
        self.stop()
        self._stop = threading.Event()
        self._log_event("State machine restarted")
        self.start()
        self._auto = was_auto

    # ------------------------------------------------------------- logging
    def _log_event(self, msg: str) -> None:
        stamp = datetime.now(timezone.utc).strftime("%H:%M:%S")
        with self._lock:
            self._log.append(f"[{stamp}] {msg}")
            if len(self._log) > 200:
                self._log = self._log[-200:]

    def _skip_event(self, key: str, msg: str) -> None:
        now = time.time()
        last = self._last_skip_log.get(key, 0.0)
        if now - last >= config.SKIP_LOG_INTERVAL_SEC:
            self._last_skip_log[key] = now
            self._log_event(msg)

    def _on_status_change(self, sid: str, old: str, new: str) -> None:
        if new == "DISABLED":
            self.audit.log_breaker(event="strategy_disabled",
                                   strategy_id=sid, from_status=old)
            self._log_event(f"Strategy DISABLED (poor performance): {sid}")

    # ---------------------------------------------------------- snapshot
    def get_snapshot(self) -> Snapshot:
        with self._lock:
            top_ids = [sid for sid, _, _ in self.snapshot.top_strategies[:5]]
            strat_hist = {sid: list(self._strategy_hist.get(sid, []))
                          for sid in top_ids}
            active_trades = [self._active_trade_view(t, m)
                             for t, m in self._active.items()]
            today = {}
            try:
                today = self.audit.today_summary()
            except Exception:
                today = {}
            snap = Snapshot(
                connected=self.client.connected,
                balance=self.snapshot.balance,
                equity=self.snapshot.equity,
                daily_pnl=self.snapshot.daily_pnl,
                active_ticket=self._active_ticket,
                active_pnl=self.snapshot.active_pnl,
                consensus=self.snapshot.consensus,
                ml_prob=self.snapshot.ml_prob,
                top_strategies=list(self.snapshot.top_strategies),
                log=list(self._log[-30:]),
                running=self._auto,
                paper=bool(getattr(self.executor, "is_paper", False)),
                consensus_history=list(self._consensus_hist),
                ml_history=list(self._ml_hist),
                strategy_history=strat_hist,
                active_trades=active_trades,
                today=today,
            )
        return snap

    def _active_trade_view(self, ticket: int, meta: Dict[str, Any]) -> Dict[str, Any]:
        return {
            "ticket": int(ticket),
            "strategy_id": meta.get("strategy_id", ""),
            "side": meta.get("side", ""),
            "entry": float(meta.get("entry_price", 0.0)),
            "sl": float(meta.get("sl_price", 0.0)),
            "ladder": list(meta.get("ladder", [])),
            "hits": list(meta.get("hits", [])),
            "initial_volume": float(meta.get("initial_volume", 0.0)),
            "remaining_volume": float(meta.get("remaining_volume", 0.0)),
        }

    # ----------------------------------------------------------- main loop
    def _run(self) -> None:
        last_eval = 0.0
        last_ladder = 0.0
        while not self._stop.is_set():
            try:
                if getattr(self.executor, "is_paper", False):
                    self.executor.step()  # type: ignore[attr-defined]
                self._refresh_account()
                self._reconcile_active()
                now = time.time()
                if (now - last_ladder) >= config.LADDER_MONITOR_INTERVAL_SEC:
                    last_ladder = now
                    self._monitor_ladder()
                if self._auto and (now - last_eval) >= config.EVALUATION_INTERVAL_SEC:
                    last_eval = now
                    self._evaluate_and_trade()
            except Exception as exc:
                self._log_event(f"loop error: {exc}")
            time.sleep(0.5)

    # ---------------------------------------------------------- internals
    def _open_positions_map(self) -> Dict[int, Any]:
        if getattr(self.executor, "is_paper", False):
            return {int(p.ticket): p for p in
                    self.executor.positions_get()}  # type: ignore[attr-defined]
        return {int(p.ticket): p for p in
                (self.client.positions_get() or ())
                if getattr(p, "magic", 0) == config.MAGIC}

    def _refresh_account(self) -> None:
        info = self.client.account_info()
        paper = getattr(self.executor, "is_paper", False)
        if paper:
            balance = config.PAPER_START_BALANCE
            closed_total = sum(self.executor._closed_pnl.values())  # type: ignore[attr-defined]
            balance += closed_total
            open_profit = sum(p.profit for p in
                              self.executor.positions_get())  # type: ignore[attr-defined]
            equity = balance + open_profit
            self.risk.state.reset_day_if_needed(balance)
            with self._lock:
                self.snapshot.balance = float(balance)
                self.snapshot.equity = float(equity)
                today = datetime.now(timezone.utc).date()
                day_pnl = 0.0
                for tkt, p in self.executor._closed_meta.items():  # type: ignore[attr-defined]
                    if p.opened_at.date() == today:
                        day_pnl += self.executor._closed_pnl.get(tkt, 0.0)  # type: ignore[attr-defined]
                self.snapshot.daily_pnl = float(day_pnl)
                self.snapshot.top_strategies = self.factory.top_n(10)
            return
        if info is None:
            return
        self.risk.state.reset_day_if_needed(info.balance)
        midnight = datetime.combine(datetime.now(timezone.utc).date(),
                                    dtime(0, 0), tzinfo=timezone.utc)
        deals = self.client.history_deals_get(midnight, datetime.now(timezone.utc))
        realised = 0.0
        if mt5 is not None:
            realised = float(sum(
                d.profit + d.commission + d.swap
                for d in deals
                if getattr(d, "magic", 0) == config.MAGIC
            ))
        with self._lock:
            self.snapshot.balance = float(info.balance)
            self.snapshot.equity = float(info.equity)
            self.snapshot.daily_pnl = realised
            self.snapshot.top_strategies = self.factory.top_n(10)

    def _reconcile_active(self) -> None:
        positions = self._open_positions_map()
        # aggregate open PnL across our trades
        agg_pnl = sum(float(getattr(p, "profit", 0.0))
                      for tkt, p in positions.items()
                      if tkt in self._active)
        with self._lock:
            self.snapshot.active_pnl = float(agg_pnl)
        # update remaining volume from broker
        for tkt, meta in list(self._active.items()):
            if tkt in positions:
                meta["remaining_volume"] = float(
                    getattr(positions[tkt], "volume", 0.0))
        # detect closed trades
        for ticket in [t for t in list(self._active.keys())
                       if t not in positions]:
            self._finalise_trade(ticket, exit_reason="SL")
        self._active_ticket = (next(reversed(self._active))
                               if self._active else None)

    def _monitor_ladder(self) -> None:
        """Check open trades for TP-ladder progression and trail SL."""
        if not self._active:
            return
        positions = self._open_positions_map()
        for ticket, meta in list(self._active.items()):
            pos = positions.get(ticket)
            if pos is None:
                continue
            tick = self.client.tick(pos.symbol)
            info = self.client.symbol_info(pos.symbol)
            if tick is None or info is None:
                continue
            side = meta["side"]
            ladder = meta["ladder"]
            hits = meta["hits"]
            fractions = meta["fractions"]
            initial = float(meta["initial_volume"])
            for i, tp_price in enumerate(ladder):
                if hits[i]:
                    continue
                price_now = float(tick.bid) if side == "BUY" else float(tick.ask)
                reached = (price_now >= tp_price) if side == "BUY" else (price_now <= tp_price)
                if not reached:
                    break
                vol_to_close = initial * fractions[i]
                # On the last level, close everything remaining
                if i == len(ladder) - 1:
                    vol_to_close = float(getattr(pos, "volume", vol_to_close))
                vol_rounded = round_volume(vol_to_close, info)
                if vol_rounded <= 0:
                    hits[i] = True
                    continue
                ok = self.executor.partial_close(pos, vol_rounded)  # type: ignore[union-attr]
                if not ok:
                    break
                hits[i] = True
                self.journal.record_partial(ticket, i, price_now,
                                            vol_rounded, 0.0)
                self.audit.log_partial(ticket=ticket, level=i + 1,
                                       price=price_now, volume=vol_rounded,
                                       strategy_id=meta.get("strategy_id"))
                self._log_event(
                    f"TP{i+1} hit #{ticket} closed {vol_rounded:.2f} @ "
                    f"{price_now:.5f} ({meta.get('strategy_id')})"
                )
                # SL trailing
                new_sl = self._trail_sl(side, meta, i)
                if new_sl is not None and abs(new_sl - meta["sl_price"]) > 1e-9:
                    if self.executor.modify_sl(pos, new_sl):  # type: ignore[union-attr]
                        meta["sl_price"] = float(new_sl)
                        self._log_event(
                            f"SL trailed #{ticket} to {new_sl:.5f}"
                        )

    def _trail_sl(self, side: str, meta: Dict[str, Any],
                  level_hit: int) -> Optional[float]:
        mode = config.SL_TRAILING_MODE
        entry = float(meta["entry_price"])
        sl_dist = float(meta["sl_dist"])
        if mode == "TO_BREAK_EVEN_AT_TP1" and level_hit == 0:
            return entry
        if mode == "TO_BE_PLUS_AT_TP2":
            if level_hit == 0:
                return entry
            if level_hit == 1:
                return (entry + config.BE_PLUS_R * sl_dist) if side == "BUY" \
                    else (entry - config.BE_PLUS_R * sl_dist)
        return None

    def _finalise_trade(self, ticket: int, exit_reason: str) -> None:
        meta = self._active.pop(ticket, None)
        if meta is None:
            return
        paper = getattr(self.executor, "is_paper", False)
        if paper:
            pnl = float(self.executor.position_pnl(ticket))  # type: ignore[attr-defined]
            exit_price = float(meta.get("entry_price", 0.0))
            closed_meta = self.executor.closed_meta(ticket)  # type: ignore[attr-defined]
            if closed_meta is not None:
                exit_price = float(closed_meta.open_price)  # fallback
        else:
            midnight = datetime.combine(datetime.now(timezone.utc).date(),
                                        dtime(0, 0), tzinfo=timezone.utc)
            pnl = self.executor.position_pnl(ticket,  # type: ignore[union-attr]
                                             midnight,
                                             datetime.now(timezone.utc))
            exit_price = float(meta.get("entry_price", 0.0))
        # If any TP was hit and exit reason wasn't manual kill, label as ladder
        hits = meta.get("hits", [])
        if any(hits) and exit_reason == "SL":
            exit_reason = "TP_LADDER" if all(hits) else "SL_AFTER_PARTIAL"
        self.risk.update_after_trade(pnl)
        won = pnl > 0
        features = meta.get("features")
        if features is not None:
            self.trainer.update(features, won)
        sid = meta.get("strategy_id")
        if sid:
            self.factory.record_result(sid, won)
            hist = self._strategy_hist.setdefault(
                sid, deque(maxlen=config.STRATEGY_HISTORY_MAX))
            hist.append(1 if won else 0)
        self.journal.close_trade(ticket, exit_reason=exit_reason,
                                 exit_price=exit_price, gross_pnl=pnl)
        self.audit.log_pnl(
            ticket=ticket, strategy_id=sid,
            pnl=round(pnl, 2), won=won, exit_reason=exit_reason,
            ml_prob_win=round(float(meta.get("ml_prob_win", 0.5)), 4),
            paper=paper,
        )
        if self.risk.state.cooldown_until is not None:
            self.audit.log_breaker(
                event="cooldown",
                until=self.risk.state.cooldown_until.isoformat(),
                consecutive_losses=config.MAX_CONSECUTIVE_LOSSES,
            )
        self._log_event(f"Trade #{ticket} closed PnL={pnl:.2f} ({exit_reason})")

    # ----------------------------------------------------------- evaluate
    def _evaluate_and_trade(self) -> None:
        n_open = len(self._active)
        max_open = int(getattr(config, "MAX_CONCURRENT_POSITIONS", 1))
        if n_open >= max_open:
            self._skip_event("active",
                             f"No trade: {n_open}/{max_open} concurrent positions open")
            return
        if self.risk.in_cooldown():
            self._skip_event("cooldown",
                             f"No trade: cooldown until {self.risk.state.cooldown_until}")
            return
        if self.risk.order_limit_reached():
            self.audit.log_breaker(event="order_limit",
                                   orders_today=self.risk.state.orders_today)
            self._skip_event("order_limit", "No trade: max orders/day reached")
            return
        if not self.risk.in_session():
            now_hour = datetime.now(timezone.utc).hour
            self._skip_event("session",
                             f"No trade: outside UTC session now={now_hour}, "
                             f"allowed={config.SESSION_START_HOUR_UTC}-"
                             f"{config.SESSION_END_HOUR_UTC}")
            return
        if self.risk.daily_loss_breached(self.snapshot.balance,
                                        self.snapshot.daily_pnl):
            self._log_event("Daily loss limit hit — HALT")
            self.audit.log_breaker(event="daily_loss",
                                   daily_pnl=self.snapshot.daily_pnl,
                                   limit_pct=config.DAILY_LOSS_LIMIT_PCT)
            self._auto = False
            return

        symbol = config.SYMBOL
        info = self.client.symbol_info(symbol)
        tick = self.client.tick(symbol)
        if info is None or tick is None:
            self._skip_event("symbol_tick",
                             f"No trade: missing symbol/tick for {symbol}")
            return
        if not self.risk.spread_ok(tick, info):
            point = float(getattr(info, "point", 0.00001)) or 0.00001
            spread = (float(tick.ask) - float(tick.bid)) / (point * 10.0)
            self._skip_event("spread",
                             f"No trade: spread {spread:.2f} pips > "
                             f"max {config.MAX_SPREAD_PIPS:.2f}")
            return
        primary_tf = getattr(config, "PRIMARY_TF", "H1")
        fast_tf = getattr(config, "FAST_TF", "M15")
        rates_primary = self.feed.rates(symbol, primary_tf, 200)
        rates_fast = self.feed.rates(symbol, fast_tf, 200)
        if rates_primary is None or rates_fast is None or len(rates_primary) < 50:
            self._skip_event("rates", "No trade: insufficient candles")
            return
        atr_primary = calc_atr([r["high"] for r in rates_primary],
                          [r["low"] for r in rates_primary],
                          [r["close"] for r in rates_primary], 14)
        atr_fast_short = calc_atr([r["high"] for r in rates_fast],
                                 [r["low"] for r in rates_fast],
                                 [r["close"] for r in rates_fast], 14)
        atr_fast_long = calc_atr([r["high"] for r in rates_fast],
                                [r["low"] for r in rates_fast],
                                [r["close"] for r in rates_fast], 49)
        if atr_primary is None or atr_primary <= 0:
            self._skip_event("atr_primary", f"No trade: invalid {primary_tf} ATR")
            return
        if not self.risk.market_alive(atr_fast_short or 0, atr_fast_long or 0):
            self._skip_event("dead_market", "No trade: dead market")
            return

        ctx: Dict[str, object] = {"symbol": symbol, "feed": self.feed,
                                  "tick": tick, "info": info}
        features = build_features(rates_primary, rates_fast, tick, info)
        ml_prob_up = self.model.predict_proba(features)

        decision = self.ensemble.decide(self.factory.active(), ctx, ml_prob_up,
                                        trained_samples=self.model.trained_samples)
        with self._lock:
            self.snapshot.consensus = decision.consensus
            self.snapshot.ml_prob = decision.ml_prob
            self._consensus_hist.append(float(decision.consensus))
            self._ml_hist.append(float(decision.ml_prob))

        if decision.side is None:
            # ensemble already explains why (consensus or ML gate)
            tag = "ml_gate" if "ML" in decision.reason else "consensus"
            self._skip_event(tag, f"No trade: {decision.reason}")
            return


        price = tick.ask if decision.side == "BUY" else tick.bid
        sl, _legacy_tp, sl_dist = build_levels(decision.side, price,
                                               atr_primary, info.digits)
        # Build ladder
        if config.TP_LEVELS > 1:
            ladder = build_tp_ladder(decision.side, price, sl_dist, info.digits)
            fractions = list(config.TP_VOLUME_FRACTIONS[:config.TP_LEVELS])
        else:
            ladder = [_legacy_tp]
            fractions = [1.0]
        lot = self.risk.calc_lot(self.snapshot.balance, sl_dist, info)
        if lot <= 0:
            self._skip_event("lot",
                             f"No trade: invalid lot from balance={self.snapshot.balance:.2f}")
            return

        plan = OrderPlan(side=decision.side, price=price, sl=sl,
                         tp=ladder[-1], lot=lot,
                         strategy_id=decision.strategy_id,
                         tp_ladder=ladder, tp_fractions=fractions,
                         sl_dist=sl_dist)

        paper = getattr(self.executor, "is_paper", False)
        point = float(getattr(info, "point", 0.00001)) or 0.00001
        spread_pips = (float(tick.ask) - float(tick.bid)) / (point * 10.0)
        self.audit.log_order(
            symbol=symbol, side=decision.side, price=price,
            sl=sl, tp_ladder=ladder, fractions=fractions, lot=lot,
            strategy_id=decision.strategy_id,
            consensus=decision.consensus,
            ml_prob_win=round(prob_win, 4), paper=paper,
        )
        ticket = self.executor.send(symbol, plan)  # type: ignore[union-attr]
        if ticket is None:
            self.audit.log_fill(status="rejected",
                                strategy_id=decision.strategy_id,
                                side=decision.side, lot=lot, paper=paper)
            self._log_event(f"Order rejected ({decision.side} {lot})")
            return
        self.audit.log_fill(status="filled", ticket=ticket,
                            strategy_id=decision.strategy_id,
                            side=decision.side, lot=lot,
                            price=price, sl=sl, tp_ladder=ladder, paper=paper)
        self.risk.register_order()
        self.journal.open_trade(
            ticket, symbol=symbol, strategy_id=decision.strategy_id,
            side=decision.side, entry_price=price, sl_price=sl,
            tp_plan=ladder, tp_fractions=fractions,
            initial_volume=lot, sl_dist=sl_dist,
            ml_prob_win=prob_win, spread_entry=spread_pips, paper=paper,
        )
        self._active[int(ticket)] = {
            "features": features,
            "ml_prob_win": float(prob_win),
            "strategy_id": decision.strategy_id,
            "side": decision.side,
            "entry_price": float(price),
            "sl_price": float(sl),
            "sl_dist": float(sl_dist),
            "ladder": ladder,
            "fractions": fractions,
            "hits": [False] * len(ladder),
            "initial_volume": float(lot),
            "remaining_volume": float(lot),
        }
        self._active_ticket = int(ticket)
        self._log_event(
            f"OPEN {decision.side} lot={lot} @ {price:.5f} sl={sl:.5f} "
            f"ladder={[round(x,5) for x in ladder]} "
            f"strat={decision.strategy_id} {'[PAPER]' if paper else ''}"
        )
