"""
Trading Bot — AI Analysis Panel
"""
from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QLabel,
                              QTextEdit, QFrame, QScrollArea, QProgressBar,
                              QGridLayout, QPushButton)
from PyQt6.QtCore import Qt, pyqtSignal
from ai.advisor import AdviceReport


class MetricCard(QFrame):
    def __init__(self, title, value, color="#c9d1d9", parent=None):
        super().__init__(parent)
        self.setStyleSheet(f"""
            QFrame {{ background: #111827; border: 1px solid #1e293b; border-radius: 6px; }}
        """)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(8, 5, 8, 5)
        layout.setSpacing(1)
        t = QLabel(title)
        t.setStyleSheet("color: #64748b; font-size: 9px; border: none;")
        v = QLabel(str(value))
        v.setStyleSheet(f"color: {color}; font-size: 12px; font-weight: bold; border: none;")
        v.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(t)
        layout.addWidget(v)


class AIPanel(QWidget):
    trade_requested = pyqtSignal(str, str, float, float, float)  # symbol, side, amount, price, sl

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setMinimumWidth(320)
        self._report = None
        self._setup_ui()

    def _setup_ui(self):
        main = QVBoxLayout(self)
        main.setContentsMargins(8, 8, 8, 8)
        main.setSpacing(6)

        header = QLabel("TRADING BOT — AI ADVISOR")
        header.setAlignment(Qt.AlignmentFlag.AlignCenter)
        header.setStyleSheet("""
            font-size: 14px; font-weight: bold; color: #38bdf8;
            padding: 8px; background: #0f172a; border-radius: 6px;
            border: 1px solid #1e3a5f; letter-spacing: 2px;
        """)
        main.addWidget(header)

        self.placeholder = QLabel("\n\nSelecteaza un simbol\nsi apasa ANALIZEAZA\n\n")
        self.placeholder.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.placeholder.setStyleSheet("color: #64748b; font-size: 13px;")
        main.addWidget(self.placeholder)

        self.content = QWidget()
        self.content.setVisible(False)
        cl = QVBoxLayout(self.content)
        cl.setContentsMargins(0, 0, 0, 0)
        cl.setSpacing(6)

        self.signal_container = QVBoxLayout()
        cl.addLayout(self.signal_container)
        self.score_container = QVBoxLayout()
        cl.addLayout(self.score_container)
        self.info_grid = QGridLayout()
        self.info_grid.setSpacing(3)
        cl.addLayout(self.info_grid)

        # Trade button
        self.trade_btn = QPushButton("EXECUTA TRADE")
        self.trade_btn.setStyleSheet("""
            QPushButton { background: #1e40af; color: white; border-radius: 6px;
                          padding: 10px; font-weight: bold; font-size: 13px; border: 1px solid #2563eb; }
            QPushButton:hover { background: #2563eb; }
            QPushButton:disabled { background: #1e293b; color: #64748b; }
        """)
        self.trade_btn.setEnabled(False)
        self.trade_btn.clicked.connect(self._on_trade_click)
        cl.addWidget(self.trade_btn)

        lbl = QLabel("INDICATORI"); lbl.setStyleSheet("color: #64748b; font-size: 9px; font-weight: bold; letter-spacing: 1px;")
        cl.addWidget(lbl)
        self.ind_grid = QGridLayout(); self.ind_grid.setSpacing(3)
        cl.addLayout(self.ind_grid)

        lbl2 = QLabel("SUPORT & REZISTENTA"); lbl2.setStyleSheet("color: #64748b; font-size: 9px; font-weight: bold; letter-spacing: 1px;")
        cl.addWidget(lbl2)
        self.sr_grid = QGridLayout(); self.sr_grid.setSpacing(3)
        cl.addLayout(self.sr_grid)

        self.warnings_text = QTextEdit()
        self.warnings_text.setReadOnly(True)
        self.warnings_text.setMaximumHeight(70)
        self.warnings_text.setStyleSheet("""
            QTextEdit { background: #1a0f00; color: #fb923c; border: 1px solid #92400e;
                        border-radius: 6px; font-size: 10px; padding: 4px; }
        """)
        self.warnings_text.setVisible(False)
        cl.addWidget(self.warnings_text)

        lbl3 = QLabel("ANALIZA COMPLETA"); lbl3.setStyleSheet("color: #64748b; font-size: 9px; font-weight: bold; letter-spacing: 1px;")
        cl.addWidget(lbl3)
        self.summary_text = QTextEdit()
        self.summary_text.setReadOnly(True)
        self.summary_text.setStyleSheet("""
            QTextEdit { background: #0a0e17; color: #c9d1d9; border: 1px solid #1e293b;
                        border-radius: 6px; font-size: 10px; font-family: "Consolas", monospace; padding: 6px; }
        """)
        cl.addWidget(self.summary_text)
        cl.addStretch()

        scroll = QScrollArea()
        scroll.setWidget(self.content)
        scroll.setWidgetResizable(True)
        scroll.setStyleSheet("QScrollArea { border: none; background: transparent; }")
        main.addWidget(scroll)

    def _clear(self, layout):
        while layout.count():
            item = layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()

    def update_report(self, report: AdviceReport):
        self._report = report
        self.placeholder.setVisible(False)
        self.content.setVisible(True)

        self._clear(self.signal_container)
        sig = report.signal
        colors = {"BUY": "#00c853", "SELL": "#ff1744", "HOLD": "#ff9800"}
        bg = colors.get(sig.direction, "#666")
        badge = QLabel(f" {sig.direction} — {sig.strength} ")
        badge.setAlignment(Qt.AlignmentFlag.AlignCenter)
        badge.setStyleSheet(f"""
            background: {bg}; color: white; font-size: 18px; font-weight: bold;
            padding: 10px 20px; border-radius: 8px; letter-spacing: 2px;
        """)
        self.signal_container.addWidget(badge)
        conf = QLabel(f"Confidenta: {sig.confidence:.0f}%  |  Pozitie: {sig.position_size_pct:.1f}%")
        conf.setAlignment(Qt.AlignmentFlag.AlignCenter)
        conf.setStyleSheet("color: #c9d1d9; font-size: 11px;")
        self.signal_container.addWidget(conf)

        # Score
        self._clear(self.score_container)
        score_lbl = QLabel(f"SCOR AI: {report.score:.0f} / 100  |  {report.market_regime}")
        score_lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
        score_lbl.setStyleSheet("color: #c9d1d9; font-size: 12px; font-weight: bold;")
        bar = QProgressBar()
        bar.setValue(int(report.score))
        bar.setTextVisible(False)
        bar.setFixedHeight(12)
        sc = "#00c853" if report.score >= 65 else "#ff1744" if report.score <= 35 else "#ff9800"
        bar.setStyleSheet(f"""
            QProgressBar {{ background: #111827; border-radius: 6px; }}
            QProgressBar::chunk {{ background: {sc}; border-radius: 6px; }}
        """)
        self.score_container.addWidget(score_lbl)
        self.score_container.addWidget(bar)

        # Info grid
        self._clear(self.info_grid)
        infos = [
            ("Pret", f"{report.price:.6g}", colors.get(sig.direction, "#ccc")),
            ("Trend", report.trend[:15], "#c9d1d9"),
            ("Entry", f"{sig.entry:.6g}", "#00c853"),
            ("Stop Loss", f"{sig.stop_loss:.6g}", "#ff1744"),
            ("TP1", f"{sig.take_profit_1:.6g}", "#00c853"),
            ("TP2", f"{sig.take_profit_2:.6g}", "#00c853"),
            ("TP3", f"{sig.take_profit_3:.6g}", "#00c853"),
            ("R:R", f"1:{sig.risk_reward}", "#fbbf24"),
        ]
        for i, (k, v, c) in enumerate(infos):
            self.info_grid.addWidget(MetricCard(k, v, c), i // 2, i % 2)

        # Trade button
        self.trade_btn.setEnabled(sig.direction in ("BUY", "SELL"))
        btn_color = "#15803d" if sig.direction == "BUY" else "#b91c1c" if sig.direction == "SELL" else "#1e293b"
        self.trade_btn.setStyleSheet(f"""
            QPushButton {{ background: {btn_color}; color: white; border-radius: 6px;
                          padding: 10px; font-weight: bold; font-size: 13px; border: none; }}
            QPushButton:hover {{ opacity: 0.9; }}
            QPushButton:disabled {{ background: #1e293b; color: #64748b; }}
        """)
        self.trade_btn.setText(f"EXECUTA {sig.direction}" if sig.direction != "HOLD" else "HOLD — NU TRANZACTIONA")

        # Indicators
        self._clear(self.ind_grid)
        ind = report.key_indicators
        rsi = ind.get("RSI", 50)
        rsi_c = "#ff1744" if rsi > 70 else "#00c853" if rsi < 30 else "#c9d1d9"
        items = [
            ("RSI(14)", f"{rsi:.1f}", rsi_c),
            ("MACD", f"{ind.get('MACD', 0):.4f}", "#c9d1d9"),
            ("ADX", f"{ind.get('ADX', 0):.1f}", "#c9d1d9"),
            ("ATR%", f"{ind.get('ATR_%', 0):.2f}%", "#c9d1d9"),
            ("Stoch K", f"{ind.get('Stoch_K', 0):.1f}", "#c9d1d9"),
            ("BB %B", f"{ind.get('BB_%B', 0):.2f}", "#c9d1d9"),
            ("MFI", f"{ind.get('MFI', 50):.1f}", "#c9d1d9"),
            ("OBV", ind.get("OBV_Trend", "N/A"), "#c9d1d9"),
        ]
        for i, (k, v, c) in enumerate(items):
            self.ind_grid.addWidget(MetricCard(k, v, c), i // 2, i % 2)

        # S/R
        self._clear(self.sr_grid)
        for i, r in enumerate(report.resistance_levels[:3]):
            self.sr_grid.addWidget(MetricCard(f"R{i+1}", f"{r:.6g}", "#ff1744"), 0, i)
        for i, s in enumerate(report.support_levels[:3]):
            self.sr_grid.addWidget(MetricCard(f"S{i+1}", f"{s:.6g}", "#00c853"), 1, i)

        # Warnings
        if report.warnings:
            self.warnings_text.setVisible(True)
            self.warnings_text.setPlainText("\n".join(report.warnings))
        else:
            self.warnings_text.setVisible(False)

        self.summary_text.setPlainText(report.summary)

    def _on_trade_click(self):
        if self._report and self._report.signal.direction in ("BUY", "SELL"):
            sig = self._report.signal
            self.trade_requested.emit(
                self._report.symbol, sig.direction,
                sig.position_size_pct, sig.entry, sig.stop_loss
            )
