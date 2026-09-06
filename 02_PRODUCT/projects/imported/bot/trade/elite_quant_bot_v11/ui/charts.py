"""Tkinter Canvas charts — MT5-style candlesticks + rolling line/W-L bars.

No matplotlib dependency, fully thread-safe (UI thread only).
"""
from __future__ import annotations

import tkinter as tk
from tkinter import ttk
from datetime import datetime, timezone
from typing import Dict, List, Sequence

# -------------------------------------------------------- palette (MT5 dark)
CHART_BG    = "#131722"
CHART_GRID  = "#1f2233"
CHART_AXIS  = "#6a6f7c"
CHART_TEXT  = "#cfd2dc"
BULL        = "#26a69a"
BEAR        = "#ef5350"


class LineChart(ttk.Frame):
    """Single-series rolling line chart with axis baseline."""

    def __init__(self, master, title: str = "", height: int = 140,
                 width: int = 480, y_min: float = -1.0, y_max: float = 1.0,
                 baseline: float = 0.0, fg: str = "#4ade80", **kw) -> None:
        super().__init__(master, **kw)
        self.title = title
        self.y_min = y_min
        self.y_max = y_max
        self.baseline = baseline
        self.fg = fg
        ttk.Label(self, text=title, foreground="#9a9a9a",
                  font=("Segoe UI", 8, "bold")).pack(anchor="w")
        self.canvas = tk.Canvas(self, height=height, width=width,
                                bg=CHART_BG, highlightthickness=0)
        self.canvas.pack(fill="both", expand=True)

    def set_data(self, values: Sequence[float]) -> None:
        self.canvas.delete("all")
        w = int(self.canvas.winfo_width()) or int(self.canvas["width"])
        h = int(self.canvas.winfo_height()) or int(self.canvas["height"])
        if w < 10 or h < 10 or not values:
            return
        lo = min(min(values), self.y_min)
        hi = max(max(values), self.y_max)
        if hi == lo:
            hi = lo + 1.0
        bx = h - (self.baseline - lo) / (hi - lo) * h
        self.canvas.create_line(0, bx, w, bx, fill=CHART_GRID, dash=(2, 2))
        n = len(values)
        if n == 1:
            return
        dx = w / (n - 1)
        pts: List[float] = []
        for i, v in enumerate(values):
            x = i * dx
            y = h - (v - lo) / (hi - lo) * h
            pts.extend([x, y])
        self.canvas.create_line(*pts, fill=self.fg, width=2, smooth=True)
        self.canvas.create_text(w - 4, 8, anchor="ne",
                                text=f"{values[-1]:+.3f}",
                                fill=self.fg, font=("Consolas", 9))


class MultiSeriesBars(ttk.Frame):
    """Per-strategy recent W/L bars — green=win, red=loss."""

    def __init__(self, master, height: int = 160, width: int = 480, **kw):
        super().__init__(master, **kw)
        ttk.Label(self, text="Per-strategy recent W/L (top 5)",
                  foreground="#9a9a9a", font=("Segoe UI", 8, "bold")
                  ).pack(anchor="w")
        self.canvas = tk.Canvas(self, height=height, width=width,
                                bg=CHART_BG, highlightthickness=0)
        self.canvas.pack(fill="both", expand=True)

    def set_data(self, series: Dict[str, List[int]]) -> None:
        self.canvas.delete("all")
        w = int(self.canvas.winfo_width()) or int(self.canvas["width"])
        h = int(self.canvas.winfo_height()) or int(self.canvas["height"])
        if not series or w < 10 or h < 10:
            return
        n_series = len(series)
        row_h = h / n_series
        for row, (sid, vals) in enumerate(series.items()):
            y0 = row * row_h
            self.canvas.create_text(4, y0 + row_h / 2, anchor="w",
                                    text=sid[:28], fill=CHART_TEXT,
                                    font=("Consolas", 8))
            if not vals:
                continue
            bar_area_x = 200
            bar_w = max(2, (w - bar_area_x - 8) / max(len(vals), 1))
            for i, v in enumerate(vals):
                x = bar_area_x + i * bar_w
                color = BULL if v == 1 else BEAR
                self.canvas.create_rectangle(
                    x, y0 + 4, x + bar_w - 1, y0 + row_h - 4,
                    fill=color, outline="")
            wins = sum(vals)
            wr = wins / len(vals)
            self.canvas.create_text(w - 4, y0 + row_h / 2, anchor="e",
                                    text=f"{wr*100:.0f}%  ({wins}/{len(vals)})",
                                    fill=CHART_TEXT, font=("Consolas", 8))


class CandlestickChart(ttk.Frame):
    """MT5-style candlestick chart drawn directly on a Tk Canvas."""

    def __init__(self, master, height: int = 320, width: int = 720, **kw):
        super().__init__(master, **kw)
        header = ttk.Frame(self)
        header.pack(fill="x")
        self.title_var = tk.StringVar(value="—  ·  —")
        ttk.Label(header, textvariable=self.title_var, foreground=CHART_TEXT,
                  font=("Segoe UI", 10, "bold")).pack(side="left", padx=4)
        self.info_var = tk.StringVar(value="")
        ttk.Label(header, textvariable=self.info_var,
                  foreground="#9a9a9a", font=("Consolas", 9)
                  ).pack(side="right", padx=4)
        self.canvas = tk.Canvas(self, height=height, width=width,
                                bg=CHART_BG, highlightthickness=0)
        self.canvas.pack(fill="both", expand=True)

    def set_data(self, symbol: str, timeframe: str, candles,
                 bid: float | None = None, ask: float | None = None) -> None:
        """`candles` is a sequence of dict-like rows with keys
        time/open/high/low/close (matches MT5 rates numpy structured array)."""
        self.canvas.delete("all")
        self.title_var.set(f"{symbol}  ·  {timeframe}")
        w = int(self.canvas.winfo_width()) or int(self.canvas["width"])
        h = int(self.canvas.winfo_height()) or int(self.canvas["height"])
        if w < 40 or h < 40 or candles is None or len(candles) == 0:
            self.canvas.create_text(w / 2, h / 2,
                                    text="No price data",
                                    fill=CHART_AXIS,
                                    font=("Segoe UI", 10))
            return
        pad_l, pad_r, pad_t, pad_b = 6, 60, 8, 18
        plot_w = max(10, w - pad_l - pad_r)
        plot_h = max(10, h - pad_t - pad_b)

        highs = [float(c["high"]) for c in candles]
        lows = [float(c["low"]) for c in candles]
        hi = max(highs)
        lo = min(lows)
        if hi == lo:
            hi = lo + max(abs(lo) * 1e-4, 1e-5)
        rng = hi - lo
        hi += rng * 0.05
        lo -= rng * 0.05
        rng = hi - lo

        n = len(candles)
        cw = plot_w / n
        body_w = max(1.0, cw * 0.7)

        # grid + price axis
        for i in range(5):
            gy = pad_t + plot_h * i / 4
            self.canvas.create_line(pad_l, gy, pad_l + plot_w, gy,
                                    fill=CHART_GRID)
            price = hi - rng * i / 4
            self.canvas.create_text(pad_l + plot_w + 4, gy, anchor="w",
                                    text=f"{price:.5f}", fill=CHART_AXIS,
                                    font=("Consolas", 8))

        def y(p: float) -> float:
            return pad_t + (hi - p) / rng * plot_h

        for i, c in enumerate(candles):
            o, hgh, low, cl = (float(c["open"]), float(c["high"]),
                                float(c["low"]), float(c["close"]))
            x_center = pad_l + cw * (i + 0.5)
            color = BULL if cl >= o else BEAR
            # wick
            self.canvas.create_line(x_center, y(hgh), x_center, y(low),
                                    fill=color)
            # body
            top = y(max(o, cl))
            bot = y(min(o, cl))
            if bot - top < 1:
                bot = top + 1
            self.canvas.create_rectangle(
                x_center - body_w / 2, top,
                x_center + body_w / 2, bot,
                fill=color, outline=color)

        # latest bid/ask line
        if bid is not None:
            yb = y(bid)
            self.canvas.create_line(pad_l, yb, pad_l + plot_w, yb,
                                    fill=BULL, dash=(2, 2))
            self.canvas.create_rectangle(
                pad_l + plot_w, yb - 8, pad_l + plot_w + 56, yb + 8,
                fill=BULL, outline=BULL)
            self.canvas.create_text(pad_l + plot_w + 28, yb,
                                    text=f"{bid:.5f}", fill="#ffffff",
                                    font=("Consolas", 8, "bold"))
        # time axis (first/last)
        try:
            t0 = datetime.fromtimestamp(int(candles[0]["time"]),
                                        tz=timezone.utc).strftime("%m-%d %H:%M")
            t1 = datetime.fromtimestamp(int(candles[-1]["time"]),
                                        tz=timezone.utc).strftime("%m-%d %H:%M")
            self.canvas.create_text(pad_l + 4, h - 8, anchor="w",
                                    text=t0, fill=CHART_AXIS,
                                    font=("Consolas", 8))
            self.canvas.create_text(pad_l + plot_w - 4, h - 8, anchor="e",
                                    text=t1, fill=CHART_AXIS,
                                    font=("Consolas", 8))
        except Exception:
            pass

        last_close = float(candles[-1]["close"])
        spread = (ask - bid) if (bid is not None and ask is not None) else None
        info = f"O {float(candles[-1]['open']):.5f}  H {max(highs):.5f}  " \
               f"L {min(lows):.5f}  C {last_close:.5f}"
        if spread is not None:
            info += f"   ·   Spread {spread:.5f}"
        self.info_var.set(info)
