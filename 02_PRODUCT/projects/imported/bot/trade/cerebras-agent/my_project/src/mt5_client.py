"""mt5_client.py
Mock implementation of an MT5 client for XAUUSD price retrieval.
In a real environment this would use the MetaTrader5 package to fetch
live prices. Here we simulate a price series using a simple random walk.
"""
import random
import time
from threading import Lock

class MT5Client:
    def __init__(self, symbol: str = "XAUUSD"):
        self.symbol = symbol
        self._price = 1900.0  # starting price
        self._lock = Lock()
        # start background price updater
        self._running = True
        # simple thread to update price every second
        import threading
        self._thread = threading.Thread(target=self._update_price_loop, daemon=True)
        self._thread.start()

    def _update_price_loop(self):
        while self._running:
            with self._lock:
                # random walk: +/- 0.5% per second
                change = random.uniform(-0.5, 0.5)
                self._price *= 1 + change / 1000.0
            time.sleep(1)

    def get_current_price(self) -> float:
        """Return the latest simulated price for the symbol."""
        with self._lock:
            return round(self._price, 2)

    def stop(self):
        self._running = False
        self._thread.join()
