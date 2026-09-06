"""Reusable Tkinter widgets — MT5-style look."""
from __future__ import annotations

import csv
import glob
import os
import tkinter as tk
from tkinter import ttk
from datetime import datetime, timezone
from typing import Callable, List, Optional

# ------------------------------------------------------------------ palette
MT5_BG       = "#1e1e1e"
MT5_PANEL    = "#252526"
MT5_HEADER   = "#2d2d30"
MT5_BORDER   = "#3f3f46"
MT5_FG       = "#dcdcdc"
MT5_MUTED    = "#9a9a9a"
MT5_BUY      = "#2ecc71"
MT5_SELL     = "#e74c3c"
MT5_ACCENT   = "#3794ff"
MT5_GRID     = "#2a2a2a"
MT5_WARN     = "#f1c40f"


class LabeledValue(ttk.Frame):
    """Compact label/value pair used in the MT5-style status bar."""

    def __init__(self, master, label: str, value: str = "—",
                 value_color: str = MT5_FG, **kw):
        super().__init__(master, **kw)
        ttk.Label(self, text=label.upper(), foreground=MT5_MUTED,
                  font=("Segoe UI", 8)).pack(anchor="w")
        self.var = tk.StringVar(value=value)
        self._color = value_color
        self.lbl = ttk.Label(self, textvariable=self.var,
                             foreground=value_color,
                             font=("Segoe UI", 11, "bold"))
        self.lbl.pack(anchor="w")

    def set(self, value: str, color: Optional[str] = None) -> None:
        self.var.set(value)
        if color is not None and color != self._color:
            self._color = color
            self.lbl.configure(foreground=color)


class LogPanel(ttk.Frame):
    """MT5 Journal-style scrolling text log."""

    def __init__(self, master, **kw):
        super().__init__(master, **kw)
        self.text = tk.Text(self, height=12, width=70,
                            bg="#101010", fg="#cfcfcf",
                            insertbackground="#cfcfcf",
                            font=("Consolas", 9), bd=0,
                            highlightthickness=0)
        sb = ttk.Scrollbar(self, orient="vertical", command=self.text.yview)
        self.text.configure(yscrollcommand=sb.set, state="disabled")
        self.text.pack(side="left", fill="both", expand=True)
        sb.pack(side="right", fill="y")

    def set_lines(self, lines):
        self.text.configure(state="normal")
        self.text.delete("1.0", "end")
        self.text.insert("end", "\n".join(lines))
        self.text.configure(state="disabled")
        self.text.see("end")


class TopStrategiesTable(ttk.Frame):
    def __init__(self, master, **kw):
        super().__init__(master, **kw)
        cols = ("id", "win_rate", "trades")
        self.tree = ttk.Treeview(self, columns=cols, show="headings", height=10)
        for c, t, w, a in (
            ("id", "Strategy", 320, "w"),
            ("win_rate", "Win %", 80, "center"),
            ("trades", "Trades", 80, "center"),
        ):
            self.tree.heading(c, text=t)
            self.tree.column(c, width=w, anchor=a)
        self.tree.pack(fill="both", expand=True)

    def set_rows(self, rows):
        for r in self.tree.get_children():
            self.tree.delete(r)
        for sid, wr, n in rows:
            self.tree.insert("", "end", values=(sid, f"{wr*100:.1f}", n))


class TodayCard(ttk.LabelFrame):
    """Compact 'Today's Performance' panel."""

    def __init__(self, master, **kw):
        super().__init__(master, text="TODAY'S PERFORMANCE", **kw)
        grid = ttk.Frame(self)
        grid.pack(fill="x", padx=4, pady=4)
        self.lbl_trades = LabeledValue(grid, "Trades", "0")
        self.lbl_wl     = LabeledValue(grid, "W / L", "0 / 0")
        self.lbl_wr     = LabeledValue(grid, "Win rate", "0.0%")
        self.lbl_pnl    = LabeledValue(grid, "PnL", "0.00")
        self.lbl_r      = LabeledValue(grid, "Avg R", "0.00")
        self.lbl_dd     = LabeledValue(grid, "Max DD", "0.00")
        for i, w in enumerate((self.lbl_trades, self.lbl_wl, self.lbl_wr,
                                self.lbl_pnl, self.lbl_r, self.lbl_dd)):
            w.grid(row=0, column=i, padx=6, sticky="w")

    def update(self, summary: dict) -> None:
        if not summary:
            return
        n = int(summary.get("trades", 0))
        w = int(summary.get("wins", 0))
        l = int(summary.get("losses", 0))
        wr = float(summary.get("win_rate", 0.0)) * 100.0
        pnl = float(summary.get("pnl", 0.0))
        avg_r = float(summary.get("avg_r", 0.0))
        dd = float(summary.get("max_dd", 0.0))
        self.lbl_trades.set(str(n))
        self.lbl_wl.set(f"{w} / {l}")
        self.lbl_wr.set(f"{wr:.1f}%",
                        color=MT5_BUY if wr >= 50 else MT5_SELL if wr < 30 else MT5_FG)
        self.lbl_pnl.set(f"{pnl:+.2f}",
                         color=MT5_BUY if pnl >= 0 else MT5_SELL)
        self.lbl_r.set(f"{avg_r:+.2f}R",
                       color=MT5_BUY if avg_r >= 0 else MT5_SELL)
        self.lbl_dd.set(f"{dd:.2f}",
                        color=MT5_SELL if dd > 0 else MT5_MUTED)


class ActiveTradesTable(ttk.Frame):
    """Open positions enriched with TP ladder progress."""

    def __init__(self, master, **kw):
        super().__init__(master, **kw)
        cols = ("ticket", "strategy", "side", "entry", "sl",
                "ladder", "vol")
        self.tree = ttk.Treeview(self, columns=cols, show="headings", height=8)
        widths = [80, 280, 60, 90, 90, 320, 100]
        for c, t, w in zip(cols, [
            "Ticket", "Strategy", "Side", "Entry", "S/L",
            "TP Ladder (✓ hit)", "Vol (rem/init)"], widths):
            self.tree.heading(c, text=t)
            self.tree.column(c, width=w,
                             anchor="e" if c in ("entry", "sl") else "w")
        self.tree.tag_configure("buy", foreground=MT5_BUY)
        self.tree.tag_configure("sell", foreground=MT5_SELL)
        self.tree.pack(fill="both", expand=True)

    def set_rows(self, rows):
        for r in self.tree.get_children():
            self.tree.delete(r)
        for t in rows:
            ladder = t.get("ladder", [])
            hits = t.get("hits", [])
            parts = []
            for i, p in enumerate(ladder):
                mark = "✓" if i < len(hits) and hits[i] else "·"
                parts.append(f"TP{i+1}{mark}{p:.5f}")
            ladder_str = "  ".join(parts) or "—"
            tag = "buy" if t["side"] == "BUY" else "sell"
            self.tree.insert("", "end", values=(
                t["ticket"], t.get("strategy_id", "")[:36],
                t["side"], f"{t['entry']:.5f}", f"{t['sl']:.5f}",
                ladder_str,
                f"{t['remaining_volume']:.2f}/{t['initial_volume']:.2f}",
            ), tags=(tag,))


class HealthChecksDialog(tk.Toplevel):
    """Modal pre-start checks dialog. Calls on_confirm() if user proceeds."""

    def __init__(self, master, checks: list, status: str,
                 on_confirm: Callable[[], None]) -> None:
        super().__init__(master)
        self.title("Pre-Start Health Checks")
        self.geometry("640x520")
        self.configure(bg=MT5_BG)
        frm = ttk.Frame(self, padding=8)
        frm.pack(fill="both", expand=True)
        ttk.Label(frm, text="Pre-Start Health Checks",
                  font=("Segoe UI", 12, "bold")).pack(anchor="w")
        status_color = {"OK": MT5_BUY, "WARN_REQUIRES_CONFIRM": MT5_WARN,
                        "HALT": MT5_SELL}.get(status, MT5_MUTED)
        ttk.Label(frm, text=f"Overall: {status}",
                  foreground=status_color,
                  font=("Segoe UI", 10, "bold")).pack(anchor="w", pady=(0, 8))

        cols = ("cat", "name", "status", "msg")
        tree = ttk.Treeview(frm, columns=cols, show="headings", height=14)
        for c, t, w, a in (
            ("cat", "Category", 100, "w"),
            ("name", "Check", 160, "w"),
            ("status", "Status", 70, "center"),
            ("msg", "Detail", 290, "w"),
        ):
            tree.heading(c, text=t)
            tree.column(c, width=w, anchor=a)
        tree.tag_configure("PASS", foreground=MT5_BUY)
        tree.tag_configure("WARN", foreground=MT5_WARN)
        tree.tag_configure("FAIL", foreground=MT5_SELL)
        tree.pack(fill="both", expand=True)
        for c in checks:
            tree.insert("", "end",
                        values=(c.category, c.name, c.status, c.message),
                        tags=(c.status,))

        btns = ttk.Frame(frm)
        btns.pack(fill="x", pady=8)
        ttk.Button(btns, text="Cancel",
                   command=self.destroy).pack(side="right", padx=4)
        if status != "HALT":
            text = ("Confirm & Start"
                    if status == "WARN_REQUIRES_CONFIRM"
                    else "Start Auto-Trading")
            style = "Accent.TButton"

            def _proceed():
                on_confirm()
                self.destroy()
            ttk.Button(btns, text=text, style=style,
                       command=_proceed).pack(side="right", padx=4)
        else:
            ttk.Label(btns,
                      text="Critical check FAILED — fix and re-open.",
                      foreground=MT5_SELL).pack(side="left", padx=4)


class DailyReportsPanel(ttk.Frame):
    """Dropdown of daily CSV reports with preview table."""

    def __init__(self, master, reports_dir: str, **kw):
        super().__init__(master, **kw)
        self.reports_dir = reports_dir
        top = ttk.Frame(self)
        top.pack(fill="x", padx=2, pady=2)
        ttk.Label(top, text="Daily report:",
                  foreground=MT5_MUTED).pack(side="left")
        self.var = tk.StringVar()
        self.combo = ttk.Combobox(top, textvariable=self.var,
                                  state="readonly", width=40)
        self.combo.pack(side="left", padx=4)
        ttk.Button(top, text="Refresh",
                   command=self.refresh).pack(side="left", padx=4)
        ttk.Button(top, text="Load",
                   command=self._load).pack(side="left", padx=4)
        cols = ("strategy_id", "trades", "wr", "pnl", "avg_r",
                "spread", "slip_in", "slip_out", "slip_tot")
        self.tree = ttk.Treeview(self, columns=cols, show="headings",
                                 height=12)
        headers = ["Strategy", "Trades", "Win %", "PnL", "Avg R",
                   "Spread (pips)", "Slip In", "Slip Out", "Slip Total"]
        widths = [300, 60, 60, 80, 60, 85, 65, 65, 75]
        for c, t, w in zip(cols, headers, widths):
            self.tree.heading(c, text=t)
            self.tree.column(c, width=w,
                             anchor="e" if c != "strategy_id" else "w")
        self.tree.pack(fill="both", expand=True)

        # Footer with portfolio-wide averages
        self.footer = ttk.Label(self, text="", foreground=MT5_MUTED,
                                font=("Consolas", 9))
        self.footer.pack(fill="x", padx=4, pady=(2, 2))
        self.refresh()

    def refresh(self) -> None:
        files = sorted(glob.glob(os.path.join(self.reports_dir,
                                              "daily_report_*.csv")),
                       reverse=True)
        names = [os.path.basename(f) for f in files]
        self.combo["values"] = names
        if names and not self.var.get():
            self.var.set(names[0])
            self._load()

    def _load(self) -> None:
        name = self.var.get()
        if not name:
            return
        path = os.path.join(self.reports_dir, name)
        for r in self.tree.get_children():
            self.tree.delete(r)
        n_trades = 0
        tot_pnl = 0.0
        spread_acc = 0.0
        slip_acc = 0.0
        weight = 0
        try:
            with open(path, encoding="utf-8") as f:
                for row in csv.DictReader(f):
                    tr = int(float(row.get("trades_today", 0) or 0))
                    pnl = float(row.get("gross_pnl", 0) or 0)
                    sp = float(row.get("avg_spread_pips", 0) or 0)
                    si = float(row.get("avg_slippage_entry_pips", 0) or 0)
                    so = float(row.get("avg_slippage_exit_pips", 0) or 0)
                    st = float(row.get("avg_slippage_pips", 0) or 0)
                    n_trades += tr
                    tot_pnl += pnl
                    spread_acc += sp * tr
                    slip_acc += st * tr
                    weight += tr
                    self.tree.insert("", "end", values=(
                        row.get("strategy_id", ""),
                        tr,
                        f"{float(row.get('win_rate_today', 0) or 0)*100:.1f}",
                        f"{pnl:+.2f}",
                        row.get("avg_R_today", ""),
                        f"{sp:.2f}", f"{si:.2f}",
                        f"{so:.2f}", f"{st:.2f}",
                    ))
        except OSError:
            return
        avg_sp = (spread_acc / weight) if weight else 0.0
        avg_sl = (slip_acc / weight) if weight else 0.0
        self.footer.configure(
            text=(f"Totals — trades: {n_trades}   PnL: {tot_pnl:+.2f}   "
                  f"avg spread: {avg_sp:.2f} pips   "
                  f"avg slippage: {avg_sl:.2f} pips"))


# ============================================================== MT5 widgets
class MarketWatch(ttk.Frame):
    def __init__(self, master, on_select=None, **kw):
        super().__init__(master, **kw)
        ttk.Label(self, text="Market Watch", foreground=MT5_MUTED,
                  font=("Segoe UI", 9, "bold")).pack(anchor="w", padx=4, pady=2)
        cols = ("symbol", "bid", "ask", "spread", "time")
        self.tree = ttk.Treeview(self, columns=cols, show="headings", height=14)
        for c, t, w, a in (
            ("symbol", "Symbol", 90, "w"),
            ("bid", "Bid", 70, "e"),
            ("ask", "Ask", 70, "e"),
            ("spread", "Spread", 55, "center"),
            ("time", "Time", 70, "center"),
        ):
            self.tree.heading(c, text=t)
            self.tree.column(c, width=w, anchor=a)
        self.tree.tag_configure("up", foreground=MT5_BUY)
        self.tree.tag_configure("down", foreground=MT5_SELL)
        self.tree.pack(fill="both", expand=True, padx=2)
        if on_select:
            self.tree.bind("<<TreeviewSelect>>", on_select)
        self._last_bids: dict = {}

    def set_rows(self, rows):
        sel = self.tree.selection()
        sel_sym = self.tree.item(sel[0])["values"][0] if sel else None
        for r in self.tree.get_children():
            self.tree.delete(r)
        for sym, bid, ask, spread, ts in rows:
            prev = self._last_bids.get(sym)
            tag = "up" if prev is not None and bid > prev else (
                "down" if prev is not None and bid < prev else "")
            self._last_bids[sym] = bid
            time_str = datetime.fromtimestamp(ts, tz=timezone.utc).strftime("%H:%M:%S") \
                if ts else "--:--:--"
            iid = self.tree.insert(
                "", "end",
                values=(sym, f"{bid:.5f}", f"{ask:.5f}",
                        f"{spread:.1f}", time_str),
                tags=(tag,) if tag else ())
            if sym == sel_sym:
                self.tree.selection_set(iid)


class PositionsTable(ttk.Frame):
    def __init__(self, master, **kw):
        super().__init__(master, **kw)
        cols = ("ticket", "time", "symbol", "type", "volume",
                "price", "sl", "tp", "current", "swap", "profit")
        self.tree = ttk.Treeview(self, columns=cols, show="headings", height=8)
        widths = [80, 130, 80, 60, 70, 80, 80, 80, 80, 60, 80]
        for c, t, w in zip(cols, [
            "Ticket", "Time", "Symbol", "Type", "Volume",
            "Price", "S/L", "T/P", "Price", "Swap", "Profit"], widths):
            self.tree.heading(c, text=t)
            self.tree.column(c, width=w,
                             anchor="e" if c not in ("time", "symbol", "type") else "w")
        self.tree.tag_configure("buy", foreground=MT5_BUY)
        self.tree.tag_configure("sell", foreground=MT5_SELL)
        self.tree.tag_configure("profit_pos", foreground=MT5_BUY)
        self.tree.tag_configure("profit_neg", foreground=MT5_SELL)
        self.tree.pack(fill="both", expand=True)

    def set_rows(self, positions):
        for r in self.tree.get_children():
            self.tree.delete(r)
        for p in positions:
            side = "buy" if p["type"] == 0 else "sell"
            tag = "profit_pos" if p["profit"] >= 0 else "profit_neg"
            self.tree.insert("", "end", values=(
                p["ticket"],
                datetime.fromtimestamp(p["time"], tz=timezone.utc)
                    .strftime("%Y.%m.%d %H:%M:%S"),
                p["symbol"], side.upper(), f"{p['volume']:.2f}",
                f"{p['price_open']:.5f}",
                f"{p['sl']:.5f}" if p['sl'] else "—",
                f"{p['tp']:.5f}" if p['tp'] else "—",
                f"{p['price_current']:.5f}",
                f"{p['swap']:+.2f}",
                f"{p['profit']:+.2f}",
            ), tags=(tag,))


class HistoryTable(ttk.Frame):
    def __init__(self, master, **kw):
        super().__init__(master, **kw)
        cols = ("time", "ticket", "symbol", "type", "volume",
                "price", "commission", "swap", "profit")
        self.tree = ttk.Treeview(self, columns=cols, show="headings", height=8)
        widths = [140, 90, 80, 60, 70, 90, 80, 60, 90]
        for c, t, w in zip(cols, [
            "Time", "Deal", "Symbol", "Type", "Volume", "Price",
            "Commission", "Swap", "Profit"], widths):
            self.tree.heading(c, text=t)
            self.tree.column(c, width=w,
                             anchor="e" if c not in ("time", "symbol", "type") else "w")
        self.tree.tag_configure("profit_pos", foreground=MT5_BUY)
        self.tree.tag_configure("profit_neg", foreground=MT5_SELL)
        self.tree.pack(fill="both", expand=True)

    def set_rows(self, deals):
        for r in self.tree.get_children():
            self.tree.delete(r)
        for d in deals:
            total = d["profit"] + d["commission"] + d["swap"]
            tag = "profit_pos" if total >= 0 else "profit_neg"
            side = "buy" if d["type"] == 0 else "sell"
            self.tree.insert("", "end", values=(
                datetime.fromtimestamp(d["time"], tz=timezone.utc)
                    .strftime("%Y.%m.%d %H:%M:%S"),
                d["ticket"], d["symbol"], side.upper(),
                f"{d['volume']:.2f}", f"{d['price']:.5f}",
                f"{d['commission']:+.2f}", f"{d['swap']:+.2f}",
                f"{total:+.2f}",
            ), tags=(tag,))


def apply_mt5_style(root) -> None:
    style = ttk.Style(root)
    try:
        style.theme_use("clam")
    except tk.TclError:
        pass
    root.configure(bg=MT5_BG)
    style.configure(".", background=MT5_BG, foreground=MT5_FG,
                    fieldbackground=MT5_PANEL, bordercolor=MT5_BORDER,
                    lightcolor=MT5_BORDER, darkcolor=MT5_BORDER,
                    troughcolor=MT5_PANEL)
    style.configure("TFrame", background=MT5_BG)
    style.configure("TLabel", background=MT5_BG, foreground=MT5_FG)
    style.configure("TLabelframe", background=MT5_BG, foreground=MT5_MUTED,
                    bordercolor=MT5_BORDER)
    style.configure("TLabelframe.Label", background=MT5_BG,
                    foreground=MT5_MUTED, font=("Segoe UI", 9, "bold"))
    style.configure("TButton", background=MT5_HEADER, foreground=MT5_FG,
                    bordercolor=MT5_BORDER, focusthickness=0, padding=6)
    style.map("TButton",
              background=[("active", MT5_ACCENT), ("pressed", MT5_ACCENT)],
              foreground=[("active", "#ffffff")])
    style.configure("Accent.TButton", background=MT5_ACCENT, foreground="#ffffff")
    style.configure("Danger.TButton", background=MT5_SELL, foreground="#ffffff")
    style.configure("TCheckbutton", background=MT5_BG, foreground=MT5_FG)
    style.configure("TNotebook", background=MT5_BG, bordercolor=MT5_BORDER)
    style.configure("TNotebook.Tab", background=MT5_HEADER,
                    foreground=MT5_MUTED, padding=(12, 6),
                    bordercolor=MT5_BORDER)
    style.map("TNotebook.Tab",
              background=[("selected", MT5_PANEL)],
              foreground=[("selected", MT5_FG)])
    style.configure("Treeview", background=MT5_PANEL, fieldbackground=MT5_PANEL,
                    foreground=MT5_FG, bordercolor=MT5_BORDER,
                    rowheight=22, font=("Consolas", 9))
    style.configure("Treeview.Heading", background=MT5_HEADER,
                    foreground=MT5_MUTED, bordercolor=MT5_BORDER,
                    font=("Segoe UI", 9, "bold"))
    style.map("Treeview", background=[("selected", MT5_ACCENT)],
              foreground=[("selected", "#ffffff")])
    style.configure("TEntry", fieldbackground=MT5_PANEL, foreground=MT5_FG,
                    bordercolor=MT5_BORDER)
    style.configure("TSeparator", background=MT5_BORDER)
    style.configure("TCombobox", fieldbackground=MT5_PANEL, foreground=MT5_FG)
