"""
Trading Bot — Portfolio & Orders Panel
Shows balances, positions, open orders, trade history.
"""
from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QLabel,
                              QTableWidget, QTableWidgetItem, QHeaderView,
                              QPushButton, QTabWidget, QFrame)
from PyQt6.QtCore import Qt, pyqtSignal


TABLE_STYLE = """
    QTableWidget {
        background: #0a0e17; color: #c9d1d9; border: 1px solid #1e293b;
        border-radius: 6px; font-size: 11px; gridline-color: #1e293b;
    }
    QHeaderView::section {
        background: #111827; color: #94a3b8; font-weight: bold;
        border: 1px solid #1e293b; padding: 4px; font-size: 10px;
    }
    QTableWidget::item { padding: 4px; }
    QTableWidget::item:selected { background: #1e3a5f; }
"""


class PortfolioPanel(QWidget):
    cancel_order_requested = pyqtSignal(str, str)  # order_id, symbol

    def __init__(self, parent=None):
        super().__init__(parent)
        self._setup_ui()

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(8, 8, 8, 8)
        layout.setSpacing(6)

        # Balance summary
        self.balance_frame = QFrame()
        self.balance_frame.setStyleSheet("""
            QFrame { background: #0f172a; border: 1px solid #1e3a5f; border-radius: 8px; padding: 12px; }
        """)
        bf_layout = QHBoxLayout(self.balance_frame)

        self.total_lbl = QLabel("Balanta totala: --")
        self.total_lbl.setStyleSheet("color: #38bdf8; font-size: 16px; font-weight: bold;")
        bf_layout.addWidget(self.total_lbl)

        self.pnl_lbl = QLabel("P&L: --")
        self.pnl_lbl.setStyleSheet("color: #94a3b8; font-size: 13px;")
        bf_layout.addWidget(self.pnl_lbl)
        bf_layout.addStretch()

        self.refresh_btn = QPushButton("Actualizeaza")
        self.refresh_btn.setStyleSheet("""
            QPushButton { background: #1e3a5f; color: #38bdf8; border-radius: 6px;
                          padding: 6px 16px; font-size: 11px; border: 1px solid #2563eb; }
            QPushButton:hover { background: #2563eb; color: white; }
        """)
        bf_layout.addWidget(self.refresh_btn)
        layout.addWidget(self.balance_frame)

        # Tabs
        tabs = QTabWidget()
        tabs.setStyleSheet("""
            QTabWidget::pane { border: 1px solid #1e293b; background: #0a0e17; }
            QTabBar::tab { background: #111827; color: #94a3b8; padding: 6px 16px;
                           border: 1px solid transparent; border-bottom: none; border-radius: 4px 4px 0 0; }
            QTabBar::tab:selected { background: #0a0e17; color: #e2e8f0; border-color: #1e293b; }
        """)

        # Balances tab
        self.bal_table = QTableWidget(0, 4)
        self.bal_table.setHorizontalHeaderLabels(["Moneda", "Disponibil", "In ordine", "Total"])
        self.bal_table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        self.bal_table.setStyleSheet(TABLE_STYLE)
        self.bal_table.verticalHeader().setVisible(False)
        tabs.addTab(self.bal_table, "Balante")

        # Positions tab
        self.pos_table = QTableWidget(0, 7)
        self.pos_table.setHorizontalHeaderLabels(["Simbol", "Directie", "Size", "Entry", "Pret Curent", "P&L", "Leverage"])
        self.pos_table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        self.pos_table.setStyleSheet(TABLE_STYLE)
        self.pos_table.verticalHeader().setVisible(False)
        tabs.addTab(self.pos_table, "Pozitii Deschise")

        # Open orders tab
        self.orders_table = QTableWidget(0, 7)
        self.orders_table.setHorizontalHeaderLabels(["ID", "Simbol", "Tip", "Directie", "Cantitate", "Pret", "Status"])
        self.orders_table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        self.orders_table.setStyleSheet(TABLE_STYLE)
        self.orders_table.verticalHeader().setVisible(False)
        tabs.addTab(self.orders_table, "Ordine Active")

        # History tab
        self.history_table = QTableWidget(0, 7)
        self.history_table.setHorizontalHeaderLabels(["Data", "Simbol", "Directie", "Cantitate", "Pret", "Cost", "Fee"])
        self.history_table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        self.history_table.setStyleSheet(TABLE_STYLE)
        self.history_table.verticalHeader().setVisible(False)
        tabs.addTab(self.history_table, "Istoric Tranzactii")

        layout.addWidget(tabs)

    def update_balances(self, balances: list):
        self.bal_table.setRowCount(len(balances))
        for i, b in enumerate(balances):
            self.bal_table.setItem(i, 0, QTableWidgetItem(b.currency))
            self.bal_table.setItem(i, 1, QTableWidgetItem(f"{b.free:.8g}"))
            self.bal_table.setItem(i, 2, QTableWidgetItem(f"{b.used:.8g}"))
            self.bal_table.setItem(i, 3, QTableWidgetItem(f"{b.total:.8g}"))

    def update_total_balance(self, usd_value: float):
        self.total_lbl.setText(f"Balanta: ${usd_value:,.2f}")

    def update_positions(self, positions: list):
        self.pos_table.setRowCount(len(positions))
        for i, p in enumerate(positions):
            self.pos_table.setItem(i, 0, QTableWidgetItem(p.symbol))
            self.pos_table.setItem(i, 1, QTableWidgetItem(p.side.upper()))
            self.pos_table.setItem(i, 2, QTableWidgetItem(f"{p.size:.6g}"))
            self.pos_table.setItem(i, 3, QTableWidgetItem(f"{p.entry_price:.6g}"))
            self.pos_table.setItem(i, 4, QTableWidgetItem(f"{p.current_price:.6g}"))

            pnl_item = QTableWidgetItem(f"{p.unrealized_pnl:+.2f}")
            pnl_item.setForeground(Qt.GlobalColor.green if p.unrealized_pnl >= 0 else Qt.GlobalColor.red)
            self.pos_table.setItem(i, 5, pnl_item)
            self.pos_table.setItem(i, 6, QTableWidgetItem(f"{p.leverage:.0f}x"))

    def update_open_orders(self, orders: list):
        self.orders_table.setRowCount(len(orders))
        for i, o in enumerate(orders):
            self.orders_table.setItem(i, 0, QTableWidgetItem(o.id[:12]))
            self.orders_table.setItem(i, 1, QTableWidgetItem(o.symbol))
            self.orders_table.setItem(i, 2, QTableWidgetItem(o.type))
            side_item = QTableWidgetItem(o.side.upper())
            side_item.setForeground(Qt.GlobalColor.green if o.side == "buy" else Qt.GlobalColor.red)
            self.orders_table.setItem(i, 3, side_item)
            self.orders_table.setItem(i, 4, QTableWidgetItem(f"{o.amount:.6g}"))
            self.orders_table.setItem(i, 5, QTableWidgetItem(f"{o.price:.6g}"))
            self.orders_table.setItem(i, 6, QTableWidgetItem(o.status))

    def update_history(self, orders: list):
        self.history_table.setRowCount(len(orders))
        for i, o in enumerate(orders):
            self.history_table.setItem(i, 0, QTableWidgetItem(o.timestamp[:16]))
            self.history_table.setItem(i, 1, QTableWidgetItem(o.symbol))
            side_item = QTableWidgetItem(o.side.upper())
            side_item.setForeground(Qt.GlobalColor.green if o.side == "buy" else Qt.GlobalColor.red)
            self.history_table.setItem(i, 2, side_item)
            self.history_table.setItem(i, 3, QTableWidgetItem(f"{o.amount:.6g}"))
            self.history_table.setItem(i, 4, QTableWidgetItem(f"{o.price:.6g}"))
            self.history_table.setItem(i, 5, QTableWidgetItem(f"{o.cost:.2f}"))
            self.history_table.setItem(i, 6, QTableWidgetItem(f"{o.fee:.4f}"))
