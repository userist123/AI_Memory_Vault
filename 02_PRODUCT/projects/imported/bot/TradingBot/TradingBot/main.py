#!/usr/bin/env python3
"""
Trading Bot v2.0 — AI-Powered Trading System
Entry point.
"""
import sys
import os
import logging
from pathlib import Path

# Ensure project root is in path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from core.config import APP_NAME, APP_VERSION, LOG_DIR

# ── Logging setup ────────────────────────────────────────────
log_file = LOG_DIR / "tradingbot.log"
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(name)-25s %(levelname)-8s %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S",
    handlers=[
        logging.StreamHandler(sys.stdout),
        logging.FileHandler(str(log_file), encoding="utf-8"),
    ],
)
log = logging.getLogger("tradingbot")


def main():
    log.info("=" * 60)
    log.info(f"  {APP_NAME} v{APP_VERSION}")
    log.info("=" * 60)

    from PyQt6.QtWidgets import QApplication
    from ui.main_window import MainWindow

    app = QApplication(sys.argv)
    app.setApplicationName(APP_NAME)
    app.setApplicationVersion(APP_VERSION)
    app.setOrganizationName("TradingBot")

    window = MainWindow()
    window.show()

    log.info("UI lansat cu succes")
    sys.exit(app.exec())


if __name__ == "__main__":
    main()
