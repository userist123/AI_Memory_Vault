"""MetaTrader 5–style Tkinter dashboard for the Elite Quant Bot.

Layout (mirrors the MT5 terminal):

    ┌──────────────────────────── Account status bar ──────────────────────────┐
    │ Conn · Mode · Login · Balance · Equity · Margin · Free · Level · Profit │
    ├──────────────────────────── Controls toolbar ────────────────────────────┤
    │ Start · Stop · Kill · Paper · Export · Config                            │
    ├────────────┬─────────────────────────────────────┬───────────────────────┤
    │ Market     │  Candlestick chart (selected sym.)  │  Signals & Strategies │
    │ Watch      │  Consensus / ML rolling charts      │  Top strategies       │
    │            │                                     │  W/L history bars     │
    ├────────────┴─────────────────────────────────────┴───────────────────────┤
    │ Toolbox: Trade · History · Journal · Experts                             │
    └──────────────────────────────────────────────────────────────────────────┘

All widget updates run on the Tk thread via `root.after`; the state machine
worker is never touched from here.
"""
from __future__ import annotations

import tkinter as tk
from tkinter import messagebox, ttk
from datetime import datetime, time as dtime, timezone
from typing import List, Optional

import config
from core.audit import AuditLogger
from core.health import run_checks
from core.state_machine import StateMachine
from strategies.factory import StrategyFactory
from ui.charts import CandlestickChart, LineChart, MultiSeriesBars
from ui.widgets import (
    ActiveTradesTable, DailyReportsPanel, HealthChecksDialog,
    HistoryTable, LabeledValue, LogPanel, MarketWatch, PositionsTable,
    TodayCard, TopStrategiesTable, apply_mt5_style,
    MT5_BG, MT5_BUY, MT5_SELL, MT5_FG, MT5_MUTED, MT5_ACCENT,
)


# Default Market-Watch symbols if none can be discovered live.
_DEFAULT_WATCH = (
    "EURUSD", "GBPUSD", "USDJPY", "USDCHF", "AUDUSD", "USDCAD",
    "NZDUSD", "XAUUSD", "XAGUSD", "BTCUSD",
)


class App:
    def __init__(self, sm: StateMachine,
                 audit: Optional[AuditLogger] = None,
                 factory: Optional[StrategyFactory] = None) -> None:
        self.sm = sm
        self.audit = audit or sm.audit
        self.factory = factory or getattr(sm, "factory", None)
        self.root = tk.Tk()
        self.root.title("Elite Quant Bot — MT5 Terminal")
        self.root.geometry("1480x900")
        self.root.minsize(1200, 760)
        apply_mt5_style(self.root)

        self._paper_var = tk.BooleanVar(
            value=bool(getattr(sm.executor, "is_paper", False)))
        self._tf_var = tk.StringVar(value="M15")
        self._selected_symbol: str = getattr(config, "SYMBOL", "EURUSD")
        self._watch_symbols: List[str] = list(dict.fromkeys(
            [self._selected_symbol, *_DEFAULT_WATCH]))

        self._build()
        self._poll()

    # ===================================================== layout
    def _build(self) -> None:
        self._build_status_bar()
        self._build_toolbar()

        body = ttk.Frame(self.root, padding=(6, 4))
        body.pack(fill="both", expand=True)
        body.columnconfigure(0, weight=0, minsize=320)
        body.columnconfigure(1, weight=3)
        body.columnconfigure(2, weight=2, minsize=380)
        body.rowconfigure(0, weight=3)
        body.rowconfigure(1, weight=2)

        # ---------- left: Market Watch
        watch_frame = ttk.LabelFrame(body, text="MARKET WATCH")
        watch_frame.grid(row=0, column=0, rowspan=2, sticky="nsew",
                         padx=(0, 6), pady=(0, 0))
        self.market = MarketWatch(watch_frame, on_select=self._on_pick_symbol)
        self.market.pack(fill="both", expand=True, padx=2, pady=2)

        # ---------- center top: candlestick chart
        chart_frame = ttk.LabelFrame(body, text="CHART")
        chart_frame.grid(row=0, column=1, sticky="nsew", padx=(0, 6))
        tf_bar = ttk.Frame(chart_frame)
        tf_bar.pack(fill="x", padx=4, pady=2)
        ttk.Label(tf_bar, text="Timeframe:", foreground=MT5_MUTED).pack(side="left")
        for tf in ("M1", "M5", "M15", "M30", "H1", "H4", "D1"):
            ttk.Radiobutton(tf_bar, text=tf, value=tf,
                            variable=self._tf_var).pack(side="left", padx=1)
        self.candles = CandlestickChart(chart_frame, height=360)
        self.candles.pack(fill="both", expand=True, padx=4, pady=(0, 4))

        # ---------- right column: signals + strategies + W/L
        right = ttk.Frame(body)
        right.grid(row=0, column=2, rowspan=2, sticky="nsew")
        right.columnconfigure(0, weight=1)

        sig = ttk.LabelFrame(right, text="SIGNALS")
        sig.pack(fill="x", pady=(0, 6))
        self.chart_consensus = LineChart(sig, title="Ensemble consensus",
                                         y_min=-1.0, y_max=1.0, baseline=0.0,
                                         fg="#60a5fa", height=110)
        self.chart_consensus.pack(fill="x", padx=4, pady=2)
        self.chart_ml = LineChart(sig, title="ML probability (UP)",
                                  y_min=0.0, y_max=1.0, baseline=0.5,
                                  fg="#facc15", height=110)
        self.chart_ml.pack(fill="x", padx=4, pady=2)

        wl = ttk.LabelFrame(right, text="STRATEGY W/L (rolling)")
        wl.pack(fill="both", expand=True, pady=(0, 6))
        self.chart_strategies = MultiSeriesBars(wl, height=150)
        self.chart_strategies.pack(fill="both", expand=True, padx=4, pady=2)

        top_lf = ttk.LabelFrame(right, text="TOP STRATEGIES")
        top_lf.pack(fill="both", expand=True)
        self.top = TopStrategiesTable(top_lf)
        self.top.pack(fill="both", expand=True, padx=2, pady=2)

        worst_lf = ttk.LabelFrame(right, text="WORST STRATEGIES")
        worst_lf.pack(fill="both", expand=True, pady=(6, 0))
        self.worst = TopStrategiesTable(worst_lf)
        self.worst.pack(fill="both", expand=True, padx=2, pady=2)

        # ---------- TODAY card sits above the toolbox
        self.today_card = TodayCard(body)
        self.today_card.grid(row=1, column=1, sticky="ew", padx=(0, 6),
                             pady=(6, 0))

        toolbox = ttk.Notebook(body)
        toolbox.grid(row=2, column=1, sticky="nsew", padx=(0, 6),
                     pady=(4, 0))
        body.rowconfigure(2, weight=2)

        trade_tab = ttk.Frame(toolbox)
        self.positions = PositionsTable(trade_tab)
        self.positions.pack(fill="both", expand=True, padx=2, pady=2)
        toolbox.add(trade_tab, text="Trade")

        active_tab = ttk.Frame(toolbox)
        self.active_trades = ActiveTradesTable(active_tab)
        self.active_trades.pack(fill="both", expand=True, padx=2, pady=2)
        toolbox.add(active_tab, text="Active TP Ladder")

        history_tab = ttk.Frame(toolbox)
        self.history = HistoryTable(history_tab)
        self.history.pack(fill="both", expand=True, padx=2, pady=2)
        toolbox.add(history_tab, text="History")

        reports_tab = ttk.Frame(toolbox)
        self.reports_panel = DailyReportsPanel(reports_tab, config.REPORT_DIR)
        self.reports_panel.pack(fill="both", expand=True, padx=2, pady=2)
        toolbox.add(reports_tab, text="Daily Reports")

        journal_tab = ttk.Frame(toolbox)
        self.log = LogPanel(journal_tab)
        self.log.pack(fill="both", expand=True, padx=2, pady=2)
        toolbox.add(journal_tab, text="Journal")

        experts_tab = ttk.Frame(toolbox)
        self.experts_log = LogPanel(experts_tab)
        self.experts_log.pack(fill="both", expand=True, padx=2, pady=2)
        toolbox.add(experts_tab, text="Experts")

    def _build_status_bar(self) -> None:
        bar = ttk.Frame(self.root, padding=(8, 6))
        bar.pack(fill="x")
        bar.configure(style="TFrame")
        self.lbl_conn   = LabeledValue(bar, "Connection", "DISCONNECTED",
                                       value_color=MT5_SELL)
        self.lbl_mode   = LabeledValue(bar, "Mode", "LIVE")
        self.lbl_login  = LabeledValue(bar, "Account", "—")
        self.lbl_server = LabeledValue(bar, "Server", "—")
        self.lbl_bal    = LabeledValue(bar, "Balance", "—")
        self.lbl_eq     = LabeledValue(bar, "Equity",  "—")
        self.lbl_margin = LabeledValue(bar, "Margin",  "—")
        self.lbl_free   = LabeledValue(bar, "Free margin", "—")
        self.lbl_level  = LabeledValue(bar, "Margin lvl", "—")
        self.lbl_pnl    = LabeledValue(bar, "Daily P/L", "0.00")
        self.lbl_apnl   = LabeledValue(bar, "Open P/L", "0.00")
        self.lbl_state  = LabeledValue(bar, "Auto", "OFF",
                                       value_color=MT5_MUTED)
        widgets = [self.lbl_conn, self.lbl_mode, self.lbl_login, self.lbl_server,
                   self.lbl_bal, self.lbl_eq, self.lbl_margin, self.lbl_free,
                   self.lbl_level, self.lbl_pnl, self.lbl_apnl, self.lbl_state]
        for i, w in enumerate(widgets):
            w.grid(row=0, column=i, padx=8, sticky="w")
        ttk.Separator(self.root, orient="horizontal").pack(fill="x")

    def _build_toolbar(self) -> None:
        bar = ttk.Frame(self.root, padding=(8, 4))
        bar.pack(fill="x")
        ttk.Button(bar, text="▶  Start Auto", style="Accent.TButton",
                   command=self._start_auto).pack(side="left", padx=2)
        ttk.Button(bar, text="■  Stop",
                   command=lambda: self.sm.set_auto(False)).pack(side="left", padx=2)
        ttk.Button(bar, text="✓  Pre-Start Checks",
                   command=self._open_health_dialog).pack(side="left", padx=2)
        ttk.Button(bar, text="✖  Kill Switch", style="Danger.TButton",
                   command=self._kill).pack(side="left", padx=2)
        ttk.Separator(bar, orient="vertical").pack(side="left", fill="y", padx=8)
        ttk.Checkbutton(bar, text="Paper trading",
                        variable=self._paper_var,
                        command=self._toggle_paper).pack(side="left", padx=4)
        ttk.Separator(bar, orient="vertical").pack(side="left", fill="y", padx=8)
        ttk.Button(bar, text="⤓  Export Daily Report",
                   command=self._export_report).pack(side="left", padx=2)
        ttk.Button(bar, text="⚙  Config…",
                   command=self._open_config_editor).pack(side="left", padx=2)
        self.lbl_clock = ttk.Label(bar, text="--:--:-- UTC",
                                   foreground=MT5_MUTED,
                                   font=("Consolas", 10, "bold"))
        self.lbl_clock.pack(side="right", padx=4)
        ttk.Separator(self.root, orient="horizontal").pack(fill="x")

    # ===================================================== handlers
    def _on_pick_symbol(self, _evt=None) -> None:
        sel = self.market.tree.selection()
        if sel:
            self._selected_symbol = self.market.tree.item(sel[0])["values"][0]

    def _toggle_paper(self) -> None:
        self.sm.set_paper_mode(self._paper_var.get())

    def _kill(self) -> None:
        if messagebox.askyesno("Kill switch",
                               "Close ALL bot positions and disable auto-trade?"):
            self.sm.kill_switch()

    def _export_report(self) -> None:
        try:
            paths = self.audit.export_daily_report(factory=self.factory)
        except Exception as exc:
            messagebox.showerror("Export failed", str(exc))
            return
        messagebox.showinfo("Daily report exported",
                            f"CSV:\n{paths['csv']}\n\nJSON:\n{paths['json']}")

    def _open_config_editor(self) -> None:
        ConfigEditor(self.root, self.sm)

    def _start_auto(self) -> None:
        try:
            checks, status = run_checks(self.sm.client)
        except Exception as exc:
            messagebox.showerror("Health checks failed", str(exc))
            return
        if status == "OK":
            self.sm.set_auto(True)
            return
        HealthChecksDialog(self.root, checks, status,
                           on_confirm=lambda: self.sm.set_auto(True))

    def _open_health_dialog(self) -> None:
        try:
            checks, status = run_checks(self.sm.client)
        except Exception as exc:
            messagebox.showerror("Health checks failed", str(exc))
            return
        HealthChecksDialog(self.root, checks, status,
                           on_confirm=lambda: self.sm.set_auto(True))

    # ===================================================== polling
    def _poll(self) -> None:
        try:
            snap = self.sm.get_snapshot()
            self._render_status(snap)
            self._render_signals(snap)
            self._render_market_watch()
            self._render_chart()
            self._render_positions_history()
            self.today_card.update(snap.today)
            self.active_trades.set_rows(snap.active_trades)
            if self.factory is not None:
                try:
                    self.worst.set_rows(self.factory.worst_n(10))
                except Exception:
                    pass
        except Exception as exc:
            try:
                self.experts_log.set_lines([f"UI error: {exc!r}"])
            except Exception:
                pass
        self.root.after(config.UI_REFRESH_INTERVAL_MS, self._poll)

    # ----- status bar
    def _render_status(self, s) -> None:
        self.lbl_conn.set("CONNECTED" if s.connected else "DISCONNECTED",
                          color=MT5_BUY if s.connected else MT5_SELL)
        self.lbl_mode.set("PAPER" if s.paper else "LIVE",
                          color=MT5_ACCENT if s.paper else MT5_FG)
        self.lbl_bal.set(f"{s.balance:,.2f}")
        self.lbl_eq.set(f"{s.equity:,.2f}")
        self.lbl_pnl.set(f"{s.daily_pnl:+,.2f}",
                         color=MT5_BUY if s.daily_pnl >= 0 else MT5_SELL)
        self.lbl_apnl.set(f"{s.active_pnl:+,.2f}",
                          color=MT5_BUY if s.active_pnl >= 0 else MT5_SELL)
        self.lbl_state.set("ON" if s.running else "OFF",
                           color=MT5_BUY if s.running else MT5_MUTED)
        info = self._account_info()
        if info is not None:
            self.lbl_login.set(str(getattr(info, "login", "—")))
            self.lbl_server.set(str(getattr(info, "server", "—")))
            self.lbl_margin.set(f"{getattr(info, 'margin', 0.0):,.2f}")
            self.lbl_free.set(f"{getattr(info, 'margin_free', 0.0):,.2f}")
            lvl = float(getattr(info, "margin_level", 0.0))
            self.lbl_level.set(f"{lvl:,.2f} %" if lvl else "—")
        self.lbl_clock.configure(
            text=datetime.now(timezone.utc).strftime("%H:%M:%S UTC"))

    # ----- signals + experts
    def _render_signals(self, s) -> None:
        self.chart_consensus.set_data(s.consensus_history)
        self.chart_ml.set_data(s.ml_history)
        self.chart_strategies.set_data(s.strategy_history)
        self.top.set_rows(s.top_strategies)
        self.log.set_lines(s.log)
        # Experts log shows ranked strategies as MT5 EAs would
        expert_lines = [
            f"{sid:<40}  win={wr*100:5.1f}%  trades={n}"
            for sid, wr, n in s.top_strategies[:12]
        ]
        if expert_lines:
            self.experts_log.set_lines(
                ["# Top strategies (ensemble experts)", *expert_lines])

    # ----- market watch
    def _render_market_watch(self) -> None:
        rows = []
        for sym in self._watch_symbols:
            try:
                info = self.sm.client.symbol_info(sym)
                tick = self.sm.client.tick(sym)
            except Exception:
                info, tick = None, None
            if tick is None:
                continue
            point = float(getattr(info, "point", 0.00001)) or 0.00001
            spread_pips = (tick.ask - tick.bid) / (point * 10.0)
            rows.append((sym, float(tick.bid), float(tick.ask),
                         float(spread_pips), int(tick.time)))
        self.market.set_rows(rows)

    # ----- chart
    def _render_chart(self) -> None:
        sym = self._selected_symbol
        tf = self._tf_var.get()
        try:
            candles = self.sm.feed.rates(sym, tf, 120)
            tick = self.sm.client.tick(sym)
        except Exception:
            candles, tick = None, None
        bid = float(tick.bid) if tick else None
        ask = float(tick.ask) if tick else None
        self.candles.set_data(sym, tf, candles, bid=bid, ask=ask)

    # ----- positions + history
    def _render_positions_history(self) -> None:
        positions = self._collect_positions()
        self.positions.set_rows(positions)
        self.history.set_rows(self._collect_history())

    # ----- helpers
    def _account_info(self):
        try:
            return self.sm.client.account_info()
        except Exception:
            return None

    def _collect_positions(self):
        try:
            if getattr(self.sm.executor, "is_paper", False):
                raw = self.sm.executor.positions_get()
            else:
                raw = self.sm.client.positions_get()
        except Exception:
            raw = ()
        out = []
        for p in raw or ():
            out.append({
                "ticket": getattr(p, "ticket", 0),
                "time": int(getattr(p, "time", 0)),
                "symbol": getattr(p, "symbol", ""),
                "type": int(getattr(p, "type", 0)),
                "volume": float(getattr(p, "volume", 0.0)),
                "price_open": float(getattr(p, "price_open", 0.0)),
                "price_current": float(getattr(p, "price_current",
                                               getattr(p, "price_open", 0.0))),
                "sl": float(getattr(p, "sl", 0.0) or 0.0),
                "tp": float(getattr(p, "tp", 0.0) or 0.0),
                "swap": float(getattr(p, "swap", 0.0) or 0.0),
                "profit": float(getattr(p, "profit", 0.0) or 0.0),
            })
        return out

    def _collect_history(self):
        midnight = datetime.combine(datetime.now(timezone.utc).date(),
                                    dtime(0, 0), tzinfo=timezone.utc)
        try:
            deals = self.sm.client.history_deals_get(midnight,
                                                    datetime.now(timezone.utc))
        except Exception:
            deals = ()
        out = []
        for d in deals or ():
            entry = int(getattr(d, "entry", 0))
            # Only count exits (DEAL_ENTRY_OUT == 1) to avoid duplicates.
            if entry not in (1, 2):
                continue
            out.append({
                "time": int(getattr(d, "time", 0)),
                "ticket": getattr(d, "ticket", 0),
                "symbol": getattr(d, "symbol", ""),
                "type": int(getattr(d, "type", 0)),
                "volume": float(getattr(d, "volume", 0.0)),
                "price": float(getattr(d, "price", 0.0)),
                "commission": float(getattr(d, "commission", 0.0) or 0.0),
                "swap": float(getattr(d, "swap", 0.0) or 0.0),
                "profit": float(getattr(d, "profit", 0.0) or 0.0),
            })
        out.sort(key=lambda x: x["time"], reverse=True)
        return out[:200]

    def run(self) -> None:
        self.root.mainloop()


# ====================================================================== editor
class ConfigEditor(tk.Toplevel):
    """Modal config editor — applies whitelisted fields via state machine."""

    def __init__(self, master, sm: StateMachine) -> None:
        super().__init__(master)
        self.title("Live config")
        self.sm = sm
        self.geometry("440x600")
        self.configure(bg=MT5_BG)
        self._vars: dict[str, tk.StringVar] = {}

        frm = ttk.Frame(self, padding=10)
        frm.pack(fill="both", expand=True)
        ttk.Label(frm,
                  text="Edit parameters and Apply. Changes persist to "
                       "config_overrides.json.",
                  wraplength=400, foreground=MT5_MUTED
                  ).pack(anchor="w", pady=(0, 8))

        grid = ttk.Frame(frm)
        grid.pack(fill="both", expand=True)
        current = config.snapshot()
        for row, (k, _t) in enumerate(config.EDITABLE_FIELDS.items()):
            ttk.Label(grid, text=k).grid(row=row, column=0, sticky="w",
                                         padx=4, pady=2)
            var = tk.StringVar(value=str(current.get(k, "")))
            self._vars[k] = var
            ttk.Entry(grid, textvariable=var, width=22).grid(
                row=row, column=1, sticky="ew", padx=4, pady=2)
        grid.columnconfigure(1, weight=1)

        btns = ttk.Frame(frm)
        btns.pack(fill="x", pady=8)
        ttk.Button(btns, text="Apply", style="Accent.TButton",
                   command=self._apply).pack(side="left", padx=4)
        ttk.Button(btns, text="Apply + Restart",
                   command=self._apply_and_restart).pack(side="left", padx=4)
        ttk.Button(btns, text="Close",
                   command=self.destroy).pack(side="right", padx=4)

    def _collect(self) -> dict:
        return {k: v.get() for k, v in self._vars.items()}

    def _apply(self) -> None:
        applied = self.sm.apply_config(self._collect())
        messagebox.showinfo("Config", f"Applied {len(applied)} fields.")

    def _apply_and_restart(self) -> None:
        applied = self.sm.apply_config(self._collect())
        self.sm.restart()
        messagebox.showinfo(
            "Config",
            f"Applied {len(applied)} fields and restarted state machine.")
        self.destroy()
