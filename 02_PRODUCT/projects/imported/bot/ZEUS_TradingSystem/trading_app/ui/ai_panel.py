"""
ZEUS AI Panel - Panou de afisare a sfaturilor AI
"""

from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QLabel,
                              QTextEdit, QFrame, QScrollArea, QProgressBar,
                              QGridLayout, QPushButton)
from PyQt6.QtCore import Qt, QTimer
from PyQt6.QtGui import QColor, QFont, QPalette
from ai.advisor import AdviceReport


class SignalBadge(QLabel):
    """Badge colorat pentru directia semnalului"""

    COLORS = {
        "BUY": ("#26a69a", "#001a1a"),
        "SELL": ("#ef5350", "#1a0000"),
        "HOLD": ("#ff9800", "#1a0f00"),
    }

    def __init__(self, direction: str, strength: str, parent=None):
        super().__init__(parent)
        bg, fg_fallback = self.COLORS.get(direction, ("#888", "#000"))
        self.setText(f" {direction} — {strength} ")
        self.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.setStyleSheet(f"""
            QLabel {{
                background-color: {bg};
                color: #ffffff;
                font-size: 18px;
                font-weight: bold;
                padding: 10px 20px;
                border-radius: 8px;
                letter-spacing: 2px;
            }}
        """)


class ScoreGauge(QWidget):
    """Gauge vizual pentru scorul AI 0-100"""

    def __init__(self, score: float, parent=None):
        super().__init__(parent)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(2)

        lbl = QLabel(f"SCOR AI: {score:.0f} / 100")
        lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
        lbl.setStyleSheet("color: #c9d1d9; font-size: 13px; font-weight: bold;")

        bar = QProgressBar()
        bar.setValue(int(score))
        bar.setTextVisible(False)
        bar.setFixedHeight(14)

        if score >= 65: color = "#26a69a"
        elif score <= 35: color = "#ef5350"
        else: color = "#ff9800"

        bar.setStyleSheet(f"""
            QProgressBar {{ background: #1e2227; border-radius: 7px; }}
            QProgressBar::chunk {{ background: {color}; border-radius: 7px; }}
        """)

        layout.addWidget(lbl)
        layout.addWidget(bar)


class MetricCard(QFrame):
    """Card pentru un indicator tehnic"""

    def __init__(self, title: str, value: str, color: str = "#c9d1d9", parent=None):
        super().__init__(parent)
        self.setFrameShape(QFrame.Shape.StyledPanel)
        self.setStyleSheet(f"""
            QFrame {{
                background: #161b22;
                border: 1px solid #30363d;
                border-radius: 6px;
                padding: 4px;
            }}
        """)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(8, 6, 8, 6)
        layout.setSpacing(2)

        ttl = QLabel(title)
        ttl.setStyleSheet("color: #8b949e; font-size: 10px;")

        val = QLabel(value)
        val.setStyleSheet(f"color: {color}; font-size: 13px; font-weight: bold;")
        val.setAlignment(Qt.AlignmentFlag.AlignCenter)

        layout.addWidget(ttl)
        layout.addWidget(val)


class AIPanel(QWidget):
    """Panou principal AI — afiseaza raportul de analiza"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setMinimumWidth(320)
        self._setup_ui()

    def _setup_ui(self):
        main = QVBoxLayout(self)
        main.setContentsMargins(8, 8, 8, 8)
        main.setSpacing(8)

        # Header
        header = QLabel("⚡ ZEUS AI ADVISOR")
        header.setAlignment(Qt.AlignmentFlag.AlignCenter)
        header.setStyleSheet("""
            font-size: 16px;
            font-weight: bold;
            color: #58a6ff;
            padding: 8px;
            background: #161b22;
            border-radius: 6px;
            border: 1px solid #30363d;
        """)
        main.addWidget(header)

        # Placeholder initial
        self.placeholder = QLabel(
            "\n\n\n"
            "Selecteaza un simbol\n"
            "si apasa ANALIZEAZA\n"
            "pentru a primi sfaturi AI\n\n\n"
        )
        self.placeholder.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.placeholder.setStyleSheet("color: #8b949e; font-size: 13px;")
        main.addWidget(self.placeholder)

        # Container principal (ascuns initial)
        self.content = QWidget()
        self.content.setVisible(False)
        content_layout = QVBoxLayout(self.content)
        content_layout.setContentsMargins(0, 0, 0, 0)
        content_layout.setSpacing(8)

        # Signal badge placeholder
        self.signal_container = QVBoxLayout()
        content_layout.addLayout(self.signal_container)

        # Score gauge placeholder
        self.score_container = QVBoxLayout()
        content_layout.addLayout(self.score_container)

        # Info rapida: pret, trend
        self.info_grid = QGridLayout()
        self.info_grid.setSpacing(4)
        content_layout.addLayout(self.info_grid)

        # Indicatori cheie
        ind_lbl = QLabel("INDICATORI TEHNICI")
        ind_lbl.setStyleSheet("color: #8b949e; font-size: 10px; font-weight: bold; letter-spacing: 1px;")
        content_layout.addWidget(ind_lbl)

        self.indicators_grid = QGridLayout()
        self.indicators_grid.setSpacing(4)
        content_layout.addLayout(self.indicators_grid)

        # Support / Resistance
        sr_lbl = QLabel("SUPORT & REZISTENTA")
        sr_lbl.setStyleSheet("color: #8b949e; font-size: 10px; font-weight: bold; letter-spacing: 1px;")
        content_layout.addWidget(sr_lbl)

        self.sr_grid = QGridLayout()
        self.sr_grid.setSpacing(4)
        content_layout.addLayout(self.sr_grid)

        # Avertizari
        self.warnings_label = QLabel("AVERTIZARI")
        self.warnings_label.setStyleSheet("color: #8b949e; font-size: 10px; font-weight: bold; letter-spacing: 1px;")
        content_layout.addWidget(self.warnings_label)

        self.warnings_text = QTextEdit()
        self.warnings_text.setReadOnly(True)
        self.warnings_text.setMaximumHeight(80)
        self.warnings_text.setStyleSheet("""
            QTextEdit {
                background: #161b22;
                color: #f0883e;
                border: 1px solid #30363d;
                border-radius: 6px;
                font-size: 11px;
                padding: 4px;
            }
        """)
        content_layout.addWidget(self.warnings_text)

        # Rezumat complet
        summary_lbl = QLabel("ANALIZA COMPLETA")
        summary_lbl.setStyleSheet("color: #8b949e; font-size: 10px; font-weight: bold; letter-spacing: 1px;")
        content_layout.addWidget(summary_lbl)

        self.summary_text = QTextEdit()
        self.summary_text.setReadOnly(True)
        self.summary_text.setStyleSheet("""
            QTextEdit {
                background: #0d1117;
                color: #c9d1d9;
                border: 1px solid #30363d;
                border-radius: 6px;
                font-size: 11px;
                font-family: "Consolas", "Courier New", monospace;
                padding: 6px;
            }
        """)
        content_layout.addWidget(self.summary_text)

        content_layout.addStretch()

        # Scroll area
        scroll = QScrollArea()
        scroll.setWidget(self.content)
        scroll.setWidgetResizable(True)
        scroll.setStyleSheet("QScrollArea { border: none; background: transparent; }")
        main.addWidget(scroll)

    def _clear_layout(self, layout):
        while layout.count():
            item = layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()

    def update_report(self, report: AdviceReport):
        """Actualizeaza UI-ul cu noul raport AI"""
        self.placeholder.setVisible(False)
        self.content.setVisible(True)

        # Signal badge
        self._clear_layout(self.signal_container)
        badge = SignalBadge(report.signal.direction, report.signal.strength)
        self.signal_container.addWidget(badge)

        conf_lbl = QLabel(f"Confidenta: {report.signal.confidence:.0f}%")
        conf_lbl.setAlignment(Qt.AlignmentFlag.AlignCenter)
        conf_lbl.setStyleSheet("color: #c9d1d9; font-size: 12px;")
        self.signal_container.addWidget(conf_lbl)

        # Score gauge
        self._clear_layout(self.score_container)
        gauge = ScoreGauge(report.score)
        self.score_container.addWidget(gauge)

        # Info rapida
        self._clear_layout(self.info_grid)
        price_color = "#26a69a" if report.signal.direction == "BUY" else "#ef5350" if report.signal.direction == "SELL" else "#ff9800"
        infos = [
            ("Pret", f"{report.price:.6g}", price_color),
            ("Trend", report.trend[:15], "#c9d1d9"),
            ("Entry", f"{report.signal.entry:.6g}", "#26a69a"),
            ("Stop Loss", f"{report.signal.stop_loss:.6g}", "#ef5350"),
            ("TP1", f"{report.signal.take_profit_1:.6g}", "#26a69a"),
            ("TP2", f"{report.signal.take_profit_2:.6g}", "#26a69a"),
            ("TP3", f"{report.signal.take_profit_3:.6g}", "#26a69a"),
            ("R:R", f"1:{report.signal.risk_reward}", "#ffeb3b"),
        ]
        for i, (k, v, c) in enumerate(infos):
            card = MetricCard(k, v, c)
            self.info_grid.addWidget(card, i // 2, i % 2)

        # Indicatori tehnici
        self._clear_layout(self.indicators_grid)
        ind = report.key_indicators

        rsi = ind.get("RSI", 50)
        rsi_color = "#ef5350" if rsi > 70 else "#26a69a" if rsi < 30 else "#c9d1d9"

        items = [
            ("RSI(14)", f"{rsi:.1f}", rsi_color),
            ("MACD", f"{ind.get('MACD', 0):.4f}", "#c9d1d9"),
            ("ADX", f"{ind.get('ADX', 0):.1f}", "#c9d1d9"),
            ("ATR", f"{ind.get('ATR', 0):.4f}", "#c9d1d9"),
            ("Stoch K", f"{ind.get('Stoch_K', 0):.1f}", "#c9d1d9"),
            ("BB Width", f"{ind.get('BB_Width', 0):.4f}", "#c9d1d9"),
            ("EMA 20", f"{ind.get('EMA_20', 0):.4g}", "#ff9800"),
            ("EMA 200", f"{ind.get('EMA_200', 0):.4g}", "#9c27b0"),
        ]
        for i, (k, v, c) in enumerate(items):
            card = MetricCard(k, v, c)
            self.indicators_grid.addWidget(card, i // 2, i % 2)

        # S/R levels
        self._clear_layout(self.sr_grid)
        for i, r in enumerate(report.resistance_levels[:3]):
            card = MetricCard(f"Rezistenta {i+1}", f"{r:.6g}", "#ef5350")
            self.sr_grid.addWidget(card, 0, i)
        for i, s in enumerate(report.support_levels[:3]):
            card = MetricCard(f"Suport {i+1}", f"{s:.6g}", "#26a69a")
            self.sr_grid.addWidget(card, 1, i)

        # Warnings
        if report.warnings:
            self.warnings_label.setVisible(True)
            self.warnings_text.setVisible(True)
            self.warnings_text.setPlainText("\n".join(report.warnings))
        else:
            self.warnings_label.setVisible(False)
            self.warnings_text.setVisible(False)

        # Summary
        self.summary_text.setPlainText(report.summary)
