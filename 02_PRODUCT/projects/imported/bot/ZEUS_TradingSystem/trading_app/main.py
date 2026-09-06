#!/usr/bin/env python3
"""
ZEUS TRADING SYSTEM - AI-Powered Trading Advisor
"""

import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from ui.main_window import MainWindow
from PyQt6.QtWidgets import QApplication

def main():
    app = QApplication(sys.argv)
    app.setApplicationName("ZEUS Trading System")
    app.setApplicationVersion("1.0.0")
    app.setOrganizationName("Zeus Trading")
    # AA_UseHighDpiPixmaps removed in PyQt6 — High DPI e activat implicit

    window = MainWindow()
    window.show()

    sys.exit(app.exec())

if __name__ == "__main__":
    main()
