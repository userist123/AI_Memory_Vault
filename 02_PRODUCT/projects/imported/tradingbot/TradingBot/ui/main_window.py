"""
Trading Bot — Main Window
Central hub: chart, AI advisor, broker connection, portfolio, strategy engine.
"""
from PyQt6.QtWidgets import (
    QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QSplitter, QStatusBar, QToolBar, QLabel,
    QLineEdit, QComboBox, QPushButton, QFrame,
    QMessageBox, QMenuBar, QMenu, QApplication,
    QTabWidget, QDockWidget, QDialog, QFormLayout,
    QDoubleSpinBox, QSpinBox, QCheckBox, QTextEdit
)
from PyQt6.QtCore import Qt, QThread, pyqtSignal, QTimer
from PyQt6.QtGui import QAction, QFont

from ui.chart_widget import ChartWidget
from ui.ai_panel import AIPanel
from ui.portfolio_panel import PortfolioPanel
from ui.broker_dialog import BrokerAuthDialog
from ui.ghid_panel import GhidPracticPanel
from data.fetcher import DataFetcher
from ai.advisor import AIAdvisor
from broker.interface import BrokerInterface
from strategies.engine import StrategyEngine, StrategyConfig
from core.config import AppConfig, APP_NAME, APP_VERSION
import logging

log = logging.getLogger("tradingbot.main")

STYLESHEET = """
QMainWindow, QWidget {
    background-color: #0a0e17;
    color: #c9d1d9;
    font-family: "Segoe UI", "Inter", sans-serif;
    font-size: 13px;
}
QToolBar {
    background: #0f172a;
    border-bottom: 1px solid #1e293b;
    padding: 4px 8px;
    spacing: 6px;
}
QLineEdit {
    background: #111827;
    border: 1px solid #1e293b;
    border-radius: 6px;
    padding: 6px 10px;
    color: #e2e8f0;
    font-size: 13px;
}
QLineEdit:focus { border-color: #38bdf8; }
QComboBox {
    background: #111827;
    border: 1px solid #1e293b;
    border-radius: 6px;
    padding: 5px 10px;
    color: #e2e8f0;
    min-width: 80px;
}
QComboBox::drop-down { border: none; }
QComboBox QAbstractItemView {
    background: #111827;
    border: 1px solid #1e293b;
    color: #e2e8f0;
    selection-background-color: #1e40af;
}
QPushButton {
    background: #1e293b;
    border: 1px solid #334155;
    border-radius: 6px;
    padding: 7px 16px;
    color: #e2e8f0;
    font-weight: 600;
}
QPushButton:hover { background: #334155; }
QPushButton:pressed { background: #0f172a; }
QPushButton#analyzeBtn {
    background: #1e40af;
    border-color: #2563eb;
    color: white;
    font-size: 13px;
    padding: 7px 22px;
}
QPushButton#analyzeBtn:hover { background: #2563eb; }
QPushButton#analyzeBtn:disabled { background: #1e293b; color: #64748b; }
QPushButton#connectBtn {
    background: #065f46;
    border-color: #059669;
    color: white;
}
QPushButton#connectBtn:hover { background: #059669; }
QStatusBar {
    background: #0f172a;
    color: #64748b;
    border-top: 1px solid #1e293b;
    font-size: 11px;
}
QSplitter::handle { background: #1e293b; width: 2px; }
QTabWidget::pane { border: 1px solid #1e293b; background: #0a0e17; }
QTabBar::tab {
    background: #0f172a;
    color: #64748b;
    padding: 8px 20px;
    border: 1px solid transparent;
    border-bottom: none;
    border-radius: 4px 4px 0 0;
    margin-right: 2px;
    font-weight: 600;
}
QTabBar::tab:selected {
    background: #0a0e17;
    color: #38bdf8;
    border-color: #1e293b;
}
QMenuBar {
    background: #0f172a;
    color: #c9d1d9;
    border-bottom: 1px solid #1e293b;
    padding: 2px;
}
QMenuBar::item:selected { background: #1e40af; border-radius: 4px; }
QMenu {
    background: #0f172a;
    border: 1px solid #1e293b;
    border-radius: 6px;
    color: #c9d1d9;
}
QMenu::item:selected { background: #1e40af; }
QScrollBar:vertical { background: #0a0e17; width: 8px; border-radius: 4px; }
QScrollBar::handle:vertical { background: #334155; border-radius: 4px; min-height: 20px; }
QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical { height: 0; }
QDockWidget {
    titlebar-close-icon: none;
    color: #94a3b8;
    font-weight: bold;
}
QDockWidget::title {
    background: #0f172a;
    border: 1px solid #1e293b;
    padding: 6px;
}
"""


class AnalysisWorker(QThread):
    finished = pyqtSignal(object)
    error = pyqtSignal(str)

    def __init__(self, symbol, interval, portfolio_value=10000, max_risk=2.0):
        super().__init__()
        self.symbol = symbol
        self.interval = interval
        self.portfolio_value = portfolio_value
        self.max_risk = max_risk
        self.fetcher = DataFetcher()
        self.advisor = AIAdvisor()

    def run(self):
        try:
            df = self.fetcher.fetch(self.symbol, self.interval)
            report = self.advisor.analyze(
                df, self.symbol, self.interval,
                self.portfolio_value, self.max_risk
            )
            self.finished.emit((df, report))
        except Exception as e:
            self.error.emit(str(e))


class BrokerWorker(QThread):
    """Background thread for broker operations."""
    result = pyqtSignal(str, object)  # operation_name, data
    error = pyqtSignal(str, str)

    def __init__(self, broker: BrokerInterface, operation: str, **kwargs):
        super().__init__()
        self.broker = broker
        self.operation = operation
        self.kwargs = kwargs

    def run(self):
        try:
            if self.operation == "balance":
                data = self.broker.get_balance()
                self.result.emit("balance", data)
            elif self.operation == "total_usd":
                data = self.broker.get_total_balance_usd()
                self.result.emit("total_usd", data)
            elif self.operation == "positions":
                data = self.broker.get_positions()
                self.result.emit("positions", data)
            elif self.operation == "open_orders":
                data = self.broker.get_open_orders(self.kwargs.get("symbol"))
                self.result.emit("open_orders", data)
            elif self.operation == "history":
                data = self.broker.get_order_history(self.kwargs.get("symbol"), limit=50)
                self.result.emit("history", data)
            elif self.operation == "place_order":
                data = self.broker.place_order(**self.kwargs)
                self.result.emit("place_order", data)
            elif self.operation == "refresh_all":
                bal = self.broker.get_balance()
                self.result.emit("balance", bal)
                total = self.broker.get_total_balance_usd()
                self.result.emit("total_usd", total)
                pos = self.broker.get_positions()
                self.result.emit("positions", pos)
                orders = self.broker.get_open_orders()
                self.result.emit("open_orders", orders)
                hist = self.broker.get_order_history(limit=50)
                self.result.emit("history", hist)
        except Exception as e:
            self.error.emit(self.operation, str(e))


class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle(f"{APP_NAME} v{APP_VERSION}")
        self.setMinimumSize(1280, 800)
        self.resize(1700, 1000)

        self.config = AppConfig.load()
        self.fetcher = DataFetcher()
        self.advisor = AIAdvisor()
        self.broker = BrokerInterface()
        self.strategy = StrategyEngine()
        self.worker = None
        self.broker_worker = None
        self.current_df = None
        self.current_report = None

        self.setStyleSheet(STYLESHEET)
        self._build_menu()
        self._build_toolbar()
        self._build_central()
        self._build_statusbar()

        # Auto-refresh timer
        self.refresh_timer = QTimer()
        self.refresh_timer.timeout.connect(self._auto_refresh)

    # ── Menu ──────────────────────────────────────────────────────

    def _build_menu(self):
        mb = self.menuBar()

        # Fisier
        file_menu = mb.addMenu("Fisier")
        act_settings = QAction("Setari Strategie...", self)
        act_settings.triggered.connect(self._show_strategy_settings)
        file_menu.addAction(act_settings)
        file_menu.addSeparator()
        act_exit = QAction("Iesire (Ctrl+Q)", self)
        act_exit.setShortcut("Ctrl+Q")
        act_exit.triggered.connect(self.close)
        file_menu.addAction(act_exit)

        # Broker
        broker_menu = mb.addMenu("Broker")
        act_connect = QAction("Conecteaza Broker...", self)
        act_connect.triggered.connect(self._show_broker_dialog)
        broker_menu.addAction(act_connect)
        act_disconnect = QAction("Deconecteaza", self)
        act_disconnect.triggered.connect(self._disconnect_broker)
        broker_menu.addAction(act_disconnect)
        broker_menu.addSeparator()
        act_refresh_port = QAction("Actualizeaza Portofoliu", self)
        act_refresh_port.triggered.connect(self._refresh_portfolio)
        broker_menu.addAction(act_refresh_port)

        # Piete
        markets_menu = mb.addMenu("Piete Rapide")
        presets = [
            ("BTC/USD", "BTC"), ("ETH/USD", "ETH"), ("SOL/USD", "SOL"),
            ("---", ""), ("EUR/USD", "EURUSD"), ("GBP/USD", "GBPUSD"),
            ("Gold", "GOLD"), ("Oil WTI", "OIL"),
            ("---", ""), ("S&P 500", "SP500"), ("NASDAQ", "NASDAQ"),
            ("---", ""), ("AAPL", "AAPL"), ("TSLA", "TSLA"),
            ("NVDA", "NVDA"), ("MSFT", "MSFT"), ("META", "META"),
            ("AMZN", "AMZN"), ("GOOGL", "GOOGL"),
        ]
        for label, symbol in presets:
            if label == "---":
                markets_menu.addSeparator()
                continue
            a = QAction(label, self)
            a.setData(symbol)
            a.triggered.connect(self._load_preset)
            markets_menu.addAction(a)

        # Auto-refresh
        refresh_menu = mb.addMenu("Auto-Refresh")
        for seconds, label in [(0, "Oprit"), (30, "30s"), (60, "1 min"), (300, "5 min")]:
            a = QAction(label, self)
            a.setData(seconds)
            a.triggered.connect(self._set_auto_refresh)
            refresh_menu.addAction(a)

        # Help
        help_menu = mb.addMenu("Ajutor")
        act_about = QAction("Despre Trading Bot", self)
        act_about.triggered.connect(self._show_about)
        help_menu.addAction(act_about)

    def _load_preset(self):
        action = self.sender()
        if action:
            self.symbol_input.setText(action.data())
            self._run_analysis()

    def _set_auto_refresh(self):
        action = self.sender()
        seconds = action.data()
        if seconds > 0:
            self.refresh_timer.start(seconds * 1000)
            self.status.showMessage(f"Auto-refresh: la fiecare {seconds}s")
        else:
            self.refresh_timer.stop()
            self.status.showMessage("Auto-refresh oprit")

    # ── Toolbar ───────────────────────────────────────────────────

    def _build_toolbar(self):
        tb = QToolBar("Main")
        tb.setMovable(False)
        tb.setFloatable(False)
        self.addToolBar(tb)

        sym_label = QLabel("  Simbol: ")
        sym_label.setStyleSheet("color: #64748b; font-size: 12px;")
        tb.addWidget(sym_label)

        self.symbol_input = QLineEdit()
        self.symbol_input.setPlaceholderText("BTC, AAPL, EURUSD, GOLD...")
        self.symbol_input.setFixedWidth(200)
        self.symbol_input.setText(self.config.default_symbol.split("-")[0])
        self.symbol_input.returnPressed.connect(self._run_analysis)
        tb.addWidget(self.symbol_input)

        tf_label = QLabel("  TF: ")
        tf_label.setStyleSheet("color: #64748b; font-size: 12px;")
        tb.addWidget(tf_label)

        self.tf_combo = QComboBox()
        self.tf_combo.addItems(["1m", "5m", "15m", "30m", "1h", "4h", "1D", "1W", "1M"])
        self.tf_combo.setCurrentText(self.config.default_timeframe)
        tb.addWidget(self.tf_combo)

        tb.addSeparator()

        self.analyze_btn = QPushButton("ANALIZEAZA")
        self.analyze_btn.setObjectName("analyzeBtn")
        self.analyze_btn.clicked.connect(self._run_analysis)
        tb.addWidget(self.analyze_btn)

        tb.addSeparator()

        self.connect_btn = QPushButton("Conecteaza Broker")
        self.connect_btn.setObjectName("connectBtn")
        self.connect_btn.clicked.connect(self._show_broker_dialog)
        tb.addWidget(self.connect_btn)

        tb.addSeparator()

        self.broker_label = QLabel("  Deconectat")
        self.broker_label.setStyleSheet("color: #64748b; font-size: 12px;")
        tb.addWidget(self.broker_label)

        tb.addSeparator()

        self.live_label = QLabel("  ○ Offline")
        self.live_label.setStyleSheet("color: #64748b; font-size: 12px;")
        tb.addWidget(self.live_label)

    # ── Central ───────────────────────────────────────────────────

    def _build_central(self):
        central = QWidget()
        self.setCentralWidget(central)
        layout = QVBoxLayout(central)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)

        # Main tabs
        self.main_tabs = QTabWidget()

        # Tab 1: Chart + AI
        chart_tab = QWidget()
        chart_layout = QHBoxLayout(chart_tab)
        chart_layout.setContentsMargins(0, 0, 0, 0)
        chart_layout.setSpacing(0)

        splitter = QSplitter(Qt.Orientation.Horizontal)
        self.chart = ChartWidget()
        splitter.addWidget(self.chart)

        self.ai_panel = AIPanel()
        self.ai_panel.setMaximumWidth(380)
        self.ai_panel.trade_requested.connect(self._on_trade_requested)
        splitter.addWidget(self.ai_panel)
        splitter.setSizes([1300, 380])
        chart_layout.addWidget(splitter)
        self.main_tabs.addTab(chart_tab, "Analiza & Grafic")

        # Tab 2: Portfolio
        self.portfolio = PortfolioPanel()
        self.portfolio.refresh_btn.clicked.connect(self._refresh_portfolio)
        self.main_tabs.addTab(self.portfolio, "Portofoliu & Ordine")

        # Tab 3: Strategy Log
        strategy_tab = QWidget()
        strat_layout = QVBoxLayout(strategy_tab)
        strat_layout.setContentsMargins(12, 12, 12, 12)

        strat_header = QLabel("JURNAL STRATEGIE & DECIZII AI")
        strat_header.setStyleSheet("color: #38bdf8; font-size: 14px; font-weight: bold; letter-spacing: 2px;")
        strat_layout.addWidget(strat_header)

        self.strategy_log = QTextEdit()
        self.strategy_log.setReadOnly(True)
        self.strategy_log.setStyleSheet("""
            QTextEdit { background: #0a0e17; color: #c9d1d9; border: 1px solid #1e293b;
                        border-radius: 6px; font-family: "Consolas", monospace; font-size: 11px; padding: 8px; }
        """)
        strat_layout.addWidget(self.strategy_log)
        self.main_tabs.addTab(strategy_tab, "Jurnal Strategie")

        # Tab 4: Ghid Practic
        self.ghid_panel = GhidPracticPanel(self.config.watchlist)
        self.main_tabs.addTab(self.ghid_panel, "Ghid Practic")

        layout.addWidget(self.main_tabs)

    def _build_statusbar(self):
        self.status = QStatusBar()
        self.setStatusBar(self.status)
        self.status.showMessage(f"{APP_NAME} v{APP_VERSION} — Introdu un simbol si apasa ANALIZEAZA")

    # ── Analysis ──────────────────────────────────────────────────

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

        portfolio_val = 10000
        if self.broker.connected:
            portfolio_val = max(self.broker.get_total_balance_usd(), 10000)

        self.worker = AnalysisWorker(symbol, timeframe, portfolio_val, self.config.max_risk_per_trade_pct)
        self.worker.finished.connect(self._on_analysis_done)
        self.worker.error.connect(self._on_analysis_error)
        self.worker.start()

    def _on_analysis_done(self, result):
        df, report = result
        self.current_df = df
        self.current_report = report

        self.chart.load_data(
            df, f"{self.symbol_input.text().upper()} [{self.tf_combo.currentText()}]",
            supports=report.support_levels,
            resistances=report.resistance_levels,
        )
        self.ai_panel.update_report(report)

        self.analyze_btn.setEnabled(True)
        self.analyze_btn.setText("ANALIZEAZA")

        sig = report.signal
        emoji = "BUY" if sig.direction == "BUY" else "SELL" if sig.direction == "SELL" else "HOLD"
        self.status.showMessage(
            f"{emoji} {self.symbol_input.text().upper()} | {sig.direction} ({sig.strength}) | "
            f"Conf: {sig.confidence:.0f}% | Scor: {report.score:.0f} | "
            f"Entry: {sig.entry:.6g} | SL: {sig.stop_loss:.6g} | R:R 1:{sig.risk_reward} | "
            f"{report.market_regime}"
        )
        self.live_label.setText(f"  ● Live  {self.symbol_input.text().upper()}")
        self.live_label.setStyleSheet("color: #00c853; font-size: 12px;")

        # Strategy evaluation
        if self.strategy.strategies:
            decision = self.strategy.evaluate(report)
            self._log_strategy(
                f"[{report.symbol}] Decizie: {decision['action']} — {decision['reason']}"
            )

    def _on_analysis_error(self, error_msg):
        self.analyze_btn.setEnabled(True)
        self.analyze_btn.setText("ANALIZEAZA")
        self.live_label.setText("  ○ Eroare")
        self.live_label.setStyleSheet("color: #ff1744; font-size: 12px;")
        self.status.showMessage(f"Eroare: {error_msg}")
        QMessageBox.warning(self, "Eroare",
            f"Nu s-au putut descarca date:\n{error_msg}\n\nVerifica simbolul sau conexiunea.")

    def _auto_refresh(self):
        symbol = self.symbol_input.text().strip()
        if symbol:
            self._run_analysis()

    # ── Broker ────────────────────────────────────────────────────

    def _show_broker_dialog(self):
        dialog = BrokerAuthDialog(self)
        dialog.connected.connect(self._connect_broker)
        dialog.exec()

    def _connect_broker(self, broker_name, api_key, api_secret, passphrase, sandbox):
        self.status.showMessage(f"Se conecteaza la {broker_name}...")
        success = self.broker.connect(broker_name, api_key, api_secret, passphrase, sandbox)

        if success:
            mode = "PAPER" if sandbox else "LIVE"
            self.broker_label.setText(f"  {broker_name.upper()} ({mode})")
            self.broker_label.setStyleSheet("color: #00c853; font-size: 12px; font-weight: bold;")
            self.connect_btn.setText("Reconecteaza")
            self.status.showMessage(f"Conectat la {broker_name} ({mode})")
            self._refresh_portfolio()
        else:
            self.broker_label.setText("  Eroare conectare")
            self.broker_label.setStyleSheet("color: #ff1744; font-size: 12px;")
            self.status.showMessage("Eroare la conectarea brokerului")
            QMessageBox.warning(self, "Eroare Broker",
                "Nu s-a putut conecta la broker.\n"
                "Verifica API Key, Secret si conexiunea la internet.\n"
                "Asigura-te ca ai instalat ccxt: pip install ccxt")

    def _disconnect_broker(self):
        self.broker.disconnect()
        self.broker_label.setText("  Deconectat")
        self.broker_label.setStyleSheet("color: #64748b; font-size: 12px;")
        self.connect_btn.setText("Conecteaza Broker")
        self.status.showMessage("Deconectat de la broker")

    def _refresh_portfolio(self):
        if not self.broker.connected:
            self.status.showMessage("Conecteaza-te la un broker intai")
            return

        self.status.showMessage("Se actualizeaza portofoliul...")
        self.broker_worker = BrokerWorker(self.broker, "refresh_all")
        self.broker_worker.result.connect(self._on_broker_result)
        self.broker_worker.error.connect(self._on_broker_error)
        self.broker_worker.start()

    def _on_broker_result(self, operation, data):
        if operation == "balance":
            self.portfolio.update_balances(data)
        elif operation == "total_usd":
            self.portfolio.update_total_balance(data)
        elif operation == "positions":
            self.portfolio.update_positions(data)
        elif operation == "open_orders":
            self.portfolio.update_open_orders(data)
        elif operation == "history":
            self.portfolio.update_history(data)
        elif operation == "place_order":
            if data:
                self.status.showMessage(f"Ordin executat: {data.id} {data.side} {data.amount} {data.symbol}")
                self._log_strategy(f"ORDIN EXECUTAT: {data.side.upper()} {data.amount} {data.symbol} @ {data.price}")
                self._refresh_portfolio()
            else:
                self.status.showMessage("Eroare la plasarea ordinului")

    def _on_broker_error(self, operation, error_msg):
        self.status.showMessage(f"Eroare broker ({operation}): {error_msg}")

    # ── Trade execution ───────────────────────────────────────────

    def _on_trade_requested(self, symbol, side, size_pct, price, stop_loss):
        if not self.broker.connected:
            QMessageBox.warning(self, "Broker neconectat",
                "Conecteaza-te la un broker din meniul Broker > Conecteaza Broker")
            return

        # Confirm dialog
        reply = QMessageBox.question(
            self, "Confirmare Trade",
            f"Vrei sa plasezi un ordin?\n\n"
            f"Simbol: {symbol}\n"
            f"Directie: {side}\n"
            f"Pozitie: {size_pct:.1f}% din portofoliu\n"
            f"Pret estimat: {price:.6g}\n"
            f"Stop Loss: {stop_loss:.6g}\n\n"
            f"Broker: {self.broker.broker_name.upper()}\n"
            f"CONFIRMI?",
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No
        )

        if reply != QMessageBox.StandardButton.Yes:
            return

        # Resolve symbol for broker
        resolved = self.fetcher.resolve_symbol(symbol)
        # Convert to broker format (e.g., BTC-USD -> BTC/USDT for Binance)
        broker_symbol = resolved.replace("-", "/")
        if "=X" in broker_symbol or "=F" in broker_symbol:
            broker_symbol = broker_symbol.replace("=X", "").replace("=F", "")

        # Calculate amount (simplified — in real usage, needs proper sizing)
        total_bal = self.broker.get_total_balance_usd()
        trade_value = total_bal * (size_pct / 100)
        amount = trade_value / price if price > 0 else 0

        if amount <= 0:
            self.status.showMessage("Cantitate calculata: 0. Verifica balanta.")
            return

        self.status.showMessage(f"Se plaseaza ordin: {side} {amount:.6g} {broker_symbol}...")
        self.broker_worker = BrokerWorker(
            self.broker, "place_order",
            symbol=broker_symbol, side=side.lower(),
            order_type="market", amount=amount
        )
        self.broker_worker.result.connect(self._on_broker_result)
        self.broker_worker.error.connect(self._on_broker_error)
        self.broker_worker.start()

    # ── Strategy Settings ─────────────────────────────────────────

    def _show_strategy_settings(self):
        dialog = QDialog(self)
        dialog.setWindowTitle("Setari Strategie")
        dialog.setFixedSize(450, 500)
        dialog.setStyleSheet("""
            QDialog { background: #0a0e17; color: #c9d1d9; }
            QLabel { color: #94a3b8; }
            QGroupBox { border: 1px solid #1e3a5f; border-radius: 8px; margin-top: 10px; padding: 14px; padding-top: 22px; }
            QGroupBox::title { color: #38bdf8; font-weight: bold; }
            QDoubleSpinBox, QSpinBox { background: #111827; border: 1px solid #1e293b; border-radius: 4px; padding: 4px; color: #e2e8f0; }
            QCheckBox { color: #94a3b8; }
            QPushButton { background: #1e40af; color: white; border-radius: 6px; padding: 8px 20px; font-weight: bold; }
            QPushButton:hover { background: #2563eb; }
        """)

        layout = QVBoxLayout(dialog)
        form = QFormLayout()

        risk_spin = QDoubleSpinBox()
        risk_spin.setRange(0.1, 10.0)
        risk_spin.setValue(self.config.max_risk_per_trade_pct)
        risk_spin.setSuffix("%")
        form.addRow("Max risc per trade:", risk_spin)

        pos_spin = QSpinBox()
        pos_spin.setRange(1, 20)
        pos_spin.setValue(self.config.max_open_positions)
        form.addRow("Max pozitii deschise:", pos_spin)

        refresh_spin = QSpinBox()
        refresh_spin.setRange(10, 600)
        refresh_spin.setValue(self.config.auto_refresh_seconds)
        refresh_spin.setSuffix("s")
        form.addRow("Auto-refresh interval:", refresh_spin)

        score_spin = QDoubleSpinBox()
        score_spin.setRange(50, 95)
        score_spin.setValue(70)
        form.addRow("Scor minim pentru trade:", score_spin)

        conf_spin = QDoubleSpinBox()
        conf_spin.setRange(30, 95)
        conf_spin.setValue(60)
        conf_spin.setSuffix("%")
        form.addRow("Confidenta minima:", conf_spin)

        avoid_ob = QCheckBox("Evita BUY cand RSI > 75")
        avoid_ob.setChecked(True)
        form.addRow("", avoid_ob)

        avoid_os = QCheckBox("Evita SELL cand RSI < 25")
        avoid_os.setChecked(True)
        form.addRow("", avoid_os)

        layout.addLayout(form)

        save_btn = QPushButton("Salveaza")
        def _save():
            self.config.max_risk_per_trade_pct = risk_spin.value()
            self.config.max_open_positions = pos_spin.value()
            self.config.auto_refresh_seconds = refresh_spin.value()
            self.config.save()

            # Update strategy
            cfg = StrategyConfig(
                name="Principal",
                enabled=True,
                min_score=score_spin.value(),
                min_confidence=conf_spin.value(),
                max_risk_pct=risk_spin.value(),
                max_positions=pos_spin.value(),
                avoid_overbought=avoid_ob.isChecked(),
                avoid_oversold_shorts=avoid_os.isChecked(),
            )
            self.strategy.strategies = [cfg]
            self._log_strategy(f"Strategie actualizata: risc={risk_spin.value()}%, scor_min={score_spin.value()}")
            dialog.accept()

        save_btn.clicked.connect(_save)
        layout.addWidget(save_btn)

    # ── Strategy Log ──────────────────────────────────────────────

    def _log_strategy(self, msg: str):
        from datetime import datetime
        ts = datetime.now().strftime("%H:%M:%S")
        self.strategy_log.append(f"[{ts}] {msg}")

    # ── About ─────────────────────────────────────────────────────

    def _show_about(self):
        QMessageBox.about(self, f"Despre {APP_NAME}",
            f"<h2>{APP_NAME} v{APP_VERSION}</h2>"
            "<p>Sistem complet de trading cu AI Advisor si executie automata.</p>"
            "<ul>"
            "<li>25+ indicatori tehnici (RSI, MACD, Bollinger, ADX, Ichimoku, MFI, OBV...)</li>"
            "<li>Pattern detection (Engulfing, Hammer, Doji, Morning/Evening Star...)</li>"
            "<li>Semnale BUY/SELL/HOLD cu confidenta si position sizing</li>"
            "<li>Conectare broker reala (Binance, Kraken, Coinbase, Alpaca, etc.)</li>"
            "<li>Executie ordine cu confirmare</li>"
            "<li>Portofoliu live, ordine active, istoric tranzactii</li>"
            "<li>Credentiale criptate local cu AES-256</li>"
            "<li>Market regime detection (Trending/Ranging/Squeeze)</li>"
            "<li>Risk management si position sizing automat</li>"
            "</ul>"
            "<p><b>DISCLAIMER:</b> Acest software este pentru uz personal. "
            "Nu constituie sfat financiar. Tranzactioneaza pe propria raspundere.</p>"
        )

    def closeEvent(self, event):
        self.config.default_symbol = self.symbol_input.text().strip() or "BTC"
        self.config.default_timeframe = self.tf_combo.currentText()
        self.config.save()
        if self.broker.connected:
            self.broker.disconnect()
        event.accept()
