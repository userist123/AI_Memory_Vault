"""
ZEUS Trading System - Fereastra principala
"""

from PyQt6.QtWidgets import (
    QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QSplitter, QStatusBar, QToolBar, QLabel,
    QLineEdit, QComboBox, QPushButton, QFrame,
    QMessageBox, QMenuBar, QMenu, QApplication,
    QTabWidget, QTextEdit
)
from PyQt6.QtCore import Qt, QThread, pyqtSignal, QTimer
from PyQt6.QtGui import QAction, QIcon, QFont

from ui.chart_widget import ChartWidget
from ui.ai_panel import AIPanel
from data.fetcher import DataFetcher
from ai.advisor import ZeusAIAdvisor
import pandas as pd


STYLESHEET = """
QMainWindow, QWidget {
    background-color: #0d1117;
    color: #c9d1d9;
    font-family: "Segoe UI", "Inter", sans-serif;
    font-size: 13px;
}
QToolBar {
    background: #161b22;
    border-bottom: 1px solid #30363d;
    padding: 4px 8px;
    spacing: 6px;
}
QLineEdit {
    background: #0d1117;
    border: 1px solid #30363d;
    border-radius: 6px;
    padding: 6px 10px;
    color: #c9d1d9;
    font-size: 13px;
}
QLineEdit:focus {
    border-color: #58a6ff;
}
QComboBox {
    background: #161b22;
    border: 1px solid #30363d;
    border-radius: 6px;
    padding: 5px 10px;
    color: #c9d1d9;
    min-width: 80px;
}
QComboBox::drop-down {
    border: none;
}
QComboBox QAbstractItemView {
    background: #161b22;
    border: 1px solid #30363d;
    color: #c9d1d9;
    selection-background-color: #1f6feb;
}
QPushButton {
    background: #21262d;
    border: 1px solid #30363d;
    border-radius: 6px;
    padding: 7px 16px;
    color: #c9d1d9;
    font-weight: 600;
}
QPushButton:hover {
    background: #30363d;
    border-color: #8b949e;
}
QPushButton:pressed {
    background: #161b22;
}
QPushButton#analyzeBtn {
    background: #1f6feb;
    border-color: #388bfd;
    color: white;
    font-size: 13px;
    padding: 7px 22px;
}
QPushButton#analyzeBtn:hover {
    background: #388bfd;
}
QPushButton#analyzeBtn:disabled {
    background: #21262d;
    color: #8b949e;
}
QStatusBar {
    background: #161b22;
    color: #8b949e;
    border-top: 1px solid #30363d;
    font-size: 11px;
}
QSplitter::handle {
    background: #30363d;
    width: 2px;
}
QTabWidget::pane {
    border: 1px solid #30363d;
    background: #0d1117;
}
QTabBar::tab {
    background: #161b22;
    color: #8b949e;
    padding: 6px 16px;
    border: 1px solid transparent;
    border-bottom: none;
    border-radius: 4px 4px 0 0;
    margin-right: 2px;
}
QTabBar::tab:selected {
    background: #0d1117;
    color: #c9d1d9;
    border-color: #30363d;
}
QMenuBar {
    background: #161b22;
    color: #c9d1d9;
    border-bottom: 1px solid #30363d;
    padding: 2px;
}
QMenuBar::item:selected {
    background: #1f6feb;
    border-radius: 4px;
}
QMenu {
    background: #161b22;
    border: 1px solid #30363d;
    border-radius: 6px;
    color: #c9d1d9;
}
QMenu::item:selected {
    background: #1f6feb;
}
QScrollBar:vertical {
    background: #0d1117;
    width: 8px;
    border-radius: 4px;
}
QScrollBar::handle:vertical {
    background: #30363d;
    border-radius: 4px;
    min-height: 20px;
}
QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical { height: 0; }
"""


class AnalysisWorker(QThread):
    """Thread background pentru analiza AI (nu blocheaza UI)"""
    finished = pyqtSignal(object)  # AdviceReport
    error = pyqtSignal(str)

    def __init__(self, symbol: str, interval: str):
        super().__init__()
        self.symbol = symbol
        self.interval = interval
        self.fetcher = DataFetcher()
        self.advisor = ZeusAIAdvisor()

    def run(self):
        try:
            df = self.fetcher.fetch(self.symbol, self.interval)
            report = self.advisor.analyze(df, self.symbol, self.interval)
            self.finished.emit((df, report))
        except Exception as e:
            self.error.emit(str(e))


class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("⚡ ZEUS Trading System — AI Advisor")
        self.setMinimumSize(1280, 800)
        self.resize(1600, 950)

        self.fetcher = DataFetcher()
        self.advisor = ZeusAIAdvisor()
        self.worker = None
        self.current_df = None

        self.setStyleSheet(STYLESHEET)
        self._build_menu()
        self._build_toolbar()
        self._build_central()
        self._build_statusbar()

        # Auto-timer (optional: refresh la 5 minute)
        self.refresh_timer = QTimer()
        self.refresh_timer.timeout.connect(self._auto_refresh)

        # Demo la pornire
        QTimer.singleShot(500, self._load_demo)

    def _build_menu(self):
        mb = self.menuBar()
        mb.setStyleSheet("QMenuBar { font-size: 13px; }")

        # Fisier
        file_menu = mb.addMenu("Fisier")
        action_exit = QAction("Iesire (Ctrl+Q)", self)
        action_exit.setShortcut("Ctrl+Q")
        action_exit.triggered.connect(self.close)
        file_menu.addAction(action_exit)

        # Piete
        markets_menu = mb.addMenu("Piete")
        for label, symbol in [
            ("BTC/USD (Bitcoin)", "BTC"),
            ("ETH/USD (Ethereum)", "ETH"),
            ("EUR/USD (Forex)", "EURUSD"),
            ("GBP/USD (Forex)", "GBPUSD"),
            ("XAU/USD (Gold)", "XAUUSD"),
            ("S&P 500", "SP500"),
            ("NASDAQ", "NASDAQ"),
            ("Apple (AAPL)", "AAPL"),
            ("Tesla (TSLA)", "TSLA"),
            ("NVIDIA (NVDA)", "NVDA"),
        ]:
            action = QAction(label, self)
            action.setData(symbol)
            action.triggered.connect(self._load_preset)
            markets_menu.addAction(action)

        # Help
        help_menu = mb.addMenu("Ajutor")
        action_about = QAction("Despre ZEUS", self)
        action_about.triggered.connect(self._show_about)
        help_menu.addAction(action_about)

    def _load_preset(self):
        action = self.sender()
        if action:
            symbol = action.data()
            self.symbol_input.setText(symbol)
            self._run_analysis()

    def _build_toolbar(self):
        tb = QToolBar("Main Toolbar")
        tb.setMovable(False)
        tb.setFloatable(False)
        self.addToolBar(tb)

        # Symbol input
        sym_label = QLabel("  Simbol: ")
        sym_label.setStyleSheet("color: #8b949e; font-size: 12px;")
        tb.addWidget(sym_label)

        self.symbol_input = QLineEdit()
        self.symbol_input.setPlaceholderText("ex: BTC, EURUSD, AAPL, TSLA...")
        self.symbol_input.setFixedWidth(200)
        self.symbol_input.setText("BTC")
        self.symbol_input.returnPressed.connect(self._run_analysis)
        tb.addWidget(self.symbol_input)

        # Timeframe
        tf_label = QLabel("  Timeframe: ")
        tf_label.setStyleSheet("color: #8b949e; font-size: 12px;")
        tb.addWidget(tf_label)

        self.tf_combo = QComboBox()
        self.tf_combo.addItems(["1m", "5m", "15m", "30m", "1h", "1D", "1W", "1M"])
        self.tf_combo.setCurrentText("1D")
        tb.addWidget(self.tf_combo)

        tb.addSeparator()

        # Buton analiza
        self.analyze_btn = QPushButton("⚡ ANALIZEAZA")
        self.analyze_btn.setObjectName("analyzeBtn")
        self.analyze_btn.clicked.connect(self._run_analysis)
        tb.addWidget(self.analyze_btn)

        tb.addSeparator()

        # Status live
        self.live_label = QLabel("  ○ Offline")
        self.live_label.setStyleSheet("color: #8b949e; font-size: 12px;")
        tb.addWidget(self.live_label)

    def _build_central(self):
        central = QWidget()
        self.setCentralWidget(central)
        layout = QHBoxLayout(central)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)

        splitter = QSplitter(Qt.Orientation.Horizontal)

        # Stanga: Chart
        self.chart = ChartWidget()
        splitter.addWidget(self.chart)

        # Dreapta: AI Panel
        self.ai_panel = AIPanel()
        self.ai_panel.setMaximumWidth(380)
        splitter.addWidget(self.ai_panel)

        splitter.setSizes([1200, 380])
        layout.addWidget(splitter)

    def _build_statusbar(self):
        self.status = QStatusBar()
        self.setStatusBar(self.status)
        self.status.showMessage("ZEUS Trading System gata. Introdu un simbol si apasa ANALIZEAZA.")

    def _load_demo(self):
        """Incarca date demo la pornire"""
        try:
            df = self.fetcher.generate_demo_data("BTC-DEMO", 300)
            self.current_df = df
            self.chart.load_data(df, "BTC-USD (DEMO)")
            report = self.advisor.analyze(df, "BTC-DEMO", "1D")
            self.ai_panel.update_report(report)
            self.status.showMessage("Date demo incarcate. Introdu un simbol real pentru analiza live.")
        except Exception as e:
            self.status.showMessage(f"Eroare demo: {e}")

    def _run_analysis(self):
        symbol = self.symbol_input.text().strip()
        if not symbol:
            self.status.showMessage("Introdu un simbol valid.")
            return

        timeframe = self.tf_combo.currentText()

        if self.worker and self.worker.isRunning():
            self.worker.terminate()
            self.worker.wait()

        self.analyze_btn.setEnabled(False)
        self.analyze_btn.setText("Se analizeaza...")
        self.status.showMessage(f"Se descarca date pentru {symbol.upper()} [{timeframe}]...")
        self.live_label.setText("  ● Se incarca...")
        self.live_label.setStyleSheet("color: #ff9800; font-size: 12px;")

        self.worker = AnalysisWorker(symbol, timeframe)
        self.worker.finished.connect(self._on_analysis_done)
        self.worker.error.connect(self._on_analysis_error)
        self.worker.start()

    def _on_analysis_done(self, result):
        df, report = result
        self.current_df = df

        self.chart.load_data(df, f"{self.symbol_input.text().upper()} [{self.tf_combo.currentText()}]")
        self.ai_panel.update_report(report)

        self.analyze_btn.setEnabled(True)
        self.analyze_btn.setText("⚡ ANALIZEAZA")

        direction = report.signal.direction
        emoji = "🟢" if direction == "BUY" else "🔴" if direction == "SELL" else "🟡"
        self.status.showMessage(
            f"{emoji} {self.symbol_input.text().upper()} | {direction} | "
            f"Confidenta: {report.signal.confidence:.0f}% | Scor AI: {report.score:.0f}/100 | "
            f"{len(df)} candle-uri | Entry: {report.signal.entry:.6g} | SL: {report.signal.stop_loss:.6g}"
        )
        self.live_label.setText(f"  ● Live  {self.symbol_input.text().upper()}")
        self.live_label.setStyleSheet("color: #26a69a; font-size: 12px;")

    def _on_analysis_error(self, error_msg):
        self.analyze_btn.setEnabled(True)
        self.analyze_btn.setText("⚡ ANALIZEAZA")
        self.live_label.setText("  ○ Eroare")
        self.live_label.setStyleSheet("color: #ef5350; font-size: 12px;")
        self.status.showMessage(f"Eroare: {error_msg}")
        QMessageBox.warning(self, "Eroare date", 
            f"Nu s-au putut descarca date:\n{error_msg}\n\n"
            "Verifica simbolul sau conexiunea la internet.")

    def _auto_refresh(self):
        symbol = self.symbol_input.text().strip()
        if symbol:
            self._run_analysis()

    def _show_about(self):
        QMessageBox.about(self, "Despre ZEUS", 
            "<h2>⚡ ZEUS Trading System v1.0</h2>"
            "<p>Sistem profesional de trading cu AI Advisor integrat.</p>"
            "<ul>"
            "<li>Analiza tehnica completa (20+ indicatori)</li>"
            "<li>Semnale BUY/SELL/HOLD cu confidenta</li>"
            "<li>Entry, Stop Loss si 3 Take Profit automate</li>"
            "<li>Pattern-uri candlestick automate</li>"
            "<li>Support & Resistance calculat automat</li>"
            "<li>Date live: Crypto, Forex, Actiuni, Indici</li>"
            "</ul>"
            "<p>Construit cu Python, PyQt6, yfinance, ta-lib, pyqtgraph.</p>"
            "<p><b>DISCLAIMER:</b> Aceasta aplicatie este doar pentru educatie. "
            "Nu reprezinta sfaturi financiare. Tranzactioneaza pe propria raspundere.</p>"
        )
