"""
ZEUS Chart Widget - Grafic candlestick interactiv cu indicatori
"""

import pyqtgraph as pg
from pyqtgraph import PlotWidget, mkPen, mkBrush
from PyQt6.QtWidgets import QWidget, QVBoxLayout, QHBoxLayout, QCheckBox, QLabel
from PyQt6.QtCore import Qt, pyqtSignal
from PyQt6.QtGui import QPainter, QColor, QPen, QBrush, QFont
import numpy as np
import pandas as pd
from PyQt6.QtCore import QRectF

pg.setConfigOption("background", "#0d1117")
pg.setConfigOption("foreground", "#c9d1d9")


class CandlestickItem(pg.GraphicsObject):
    def __init__(self, data):
        super().__init__()
        self.data = data
        self.picture = None
        self.generatePicture()

    def generatePicture(self):
        self.picture = pg.QtGui.QPicture()
        p = QPainter(self.picture)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        w = 0.3
        for idx, o, h, l, c in self.data:
            color = QColor("#26a69a") if c >= o else QColor("#ef5350")
            p.setPen(QPen(color, 1))
            p.setBrush(QBrush(color))
            p.drawLine(pg.QtCore.QPointF(idx, l), pg.QtCore.QPointF(idx, h))
            body_top = max(o, c)
            body_bot = min(o, c)
            body_h = max(body_top - body_bot, 0.0001)
            p.drawRect(QRectF(idx - w, body_bot, 2 * w, body_h))
        p.end()

    def paint(self, p, *args):
        p.drawPicture(0, 0, self.picture)

    def boundingRect(self):
        return pg.QtCore.QRectF(self.picture.boundingRect())


class ChartWidget(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.df = None
        self.current_symbol = ""
        self._setup_ui()

    def _make_plot(self, height=None, label=None):
        plot = PlotWidget()
        if height:
            plot.setMaximumHeight(height)
        plot.showGrid(x=True, y=True, alpha=0.2)
        if label:
            plot.setLabel("left", label, color="#c9d1d9", size="9pt")
        return plot

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(2)

        # Toolbar
        toolbar = QHBoxLayout()
        toolbar.setContentsMargins(8, 4, 8, 4)
        self.cb_ema20  = QCheckBox("EMA20");  self.cb_ema20.setChecked(True)
        self.cb_ema50  = QCheckBox("EMA50");  self.cb_ema50.setChecked(True)
        self.cb_ema200 = QCheckBox("EMA200"); self.cb_ema200.setChecked(True)
        self.cb_bb     = QCheckBox("Bollinger"); self.cb_bb.setChecked(True)
        self.cb_volume = QCheckBox("Volum");  self.cb_volume.setChecked(True)
        for cb in [self.cb_ema20, self.cb_ema50, self.cb_ema200, self.cb_bb, self.cb_volume]:
            cb.setStyleSheet("color: #c9d1d9; font-size: 11px;")
            cb.toggled.connect(self.refresh_chart)
            toolbar.addWidget(cb)
        toolbar.addStretch()
        layout.addLayout(toolbar)

        # Charts
        self.price_plot = self._make_plot()
        self.price_plot.setMinimumHeight(350)

        self.vol_plot  = self._make_plot(height=80,  label="Vol")
        self.rsi_plot  = self._make_plot(height=100, label="RSI")
        self.macd_plot = self._make_plot(height=100, label="MACD")

        # Link X axes
        for p in [self.vol_plot, self.rsi_plot, self.macd_plot]:
            p.setXLink(self.price_plot)

        # RSI reference lines
        self.rsi_plot.addLine(y=70, pen=mkPen("#ef5350", width=1, style=Qt.PenStyle.DashLine))
        self.rsi_plot.addLine(y=30, pen=mkPen("#26a69a", width=1, style=Qt.PenStyle.DashLine))

        layout.addWidget(self.price_plot, 5)
        layout.addWidget(self.vol_plot,   1)
        layout.addWidget(self.rsi_plot,   1)
        layout.addWidget(self.macd_plot,  1)

    def load_data(self, df: pd.DataFrame, symbol: str):
        self.df = df.copy()
        self.current_symbol = symbol
        self.refresh_chart()

    def refresh_chart(self):
        if self.df is None or len(self.df) < 5:
            return

        df = self.df.copy()
        df.columns = [c.lower() for c in df.columns]
        n = len(df)
        x = np.arange(n)

        self.price_plot.clear()
        self.vol_plot.clear()
        self.rsi_plot.clear()
        self.macd_plot.clear()

        # Re-add ref lines after clear
        self.rsi_plot.addLine(y=70, pen=mkPen("#ef5350", width=1, style=Qt.PenStyle.DashLine))
        self.rsi_plot.addLine(y=30, pen=mkPen("#26a69a", width=1, style=Qt.PenStyle.DashLine))
        self.macd_plot.addLine(y=0,  pen=mkPen("#555",   width=1, style=Qt.PenStyle.DashLine))

        # Candlestick
        candle_data = list(zip(x, df["open"].values, df["high"].values,
                               df["low"].values, df["close"].values))
        self.price_plot.addItem(CandlestickItem(candle_data))

        # EMAs
        try:
            from ta.trend import EMAIndicator
            if self.cb_ema20.isChecked():
                v = EMAIndicator(close=df["close"], window=20).ema_indicator().values
                self.price_plot.plot(x, v, pen=mkPen("#ff9800", width=1.5))
            if self.cb_ema50.isChecked():
                v = EMAIndicator(close=df["close"], window=50).ema_indicator().values
                self.price_plot.plot(x, v, pen=mkPen("#2196f3", width=1.5))
            if self.cb_ema200.isChecked():
                v = EMAIndicator(close=df["close"], window=200).ema_indicator().values
                self.price_plot.plot(x, v, pen=mkPen("#9c27b0", width=1.5))
        except Exception:
            pass

        # Bollinger
        try:
            from ta.volatility import BollingerBands
            if self.cb_bb.isChecked():
                bb = BollingerBands(close=df["close"], window=20, window_dev=2)
                dash = Qt.PenStyle.DashLine
                self.price_plot.plot(x, bb.bollinger_hband().values, pen=mkPen("#607d8b", width=1, style=dash))
                self.price_plot.plot(x, bb.bollinger_lband().values, pen=mkPen("#607d8b", width=1, style=dash))
                self.price_plot.plot(x, bb.bollinger_mavg().values,  pen=mkPen("#607d8b", width=1))
        except Exception:
            pass

        # Volume
        if self.cb_volume.isChecked():
            for i in range(n):
                color = "#26a69a" if df["close"].values[i] >= df["open"].values[i] else "#ef5350"
                bar = pg.BarGraphItem(x=[x[i]], height=[df["volume"].values[i]],
                                      width=0.6, brush=QColor(color), pen=pg.mkPen(None))
                self.vol_plot.addItem(bar)

        # RSI
        try:
            from ta.momentum import RSIIndicator
            rsi = RSIIndicator(close=df["close"], window=14).rsi().values
            self.rsi_plot.plot(x, rsi, pen=mkPen("#ffeb3b", width=1.5))
            self.rsi_plot.setYRange(0, 100)
        except Exception:
            pass

        # MACD
        try:
            from ta.trend import MACD
            m = MACD(close=df["close"])
            self.macd_plot.plot(x, m.macd().values,        pen=mkPen("#2196f3", width=1.5))
            self.macd_plot.plot(x, m.macd_signal().values, pen=mkPen("#ff9800", width=1.5))
            hist = m.macd_diff().values
            for i, h in enumerate(hist):
                color = "#26a69a" if h >= 0 else "#ef5350"
                bar = pg.BarGraphItem(x=[x[i]], height=[h], width=0.6,
                                      brush=QColor(color), pen=pg.mkPen(None))
                self.macd_plot.addItem(bar)
        except Exception:
            pass

        self.price_plot.setTitle(f"  {self.current_symbol}", color="#c9d1d9", size="12pt")
        self.price_plot.autoRange()
