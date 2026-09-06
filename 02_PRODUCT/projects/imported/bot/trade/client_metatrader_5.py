# ==============================================================================
# ELITE QUANT BOT - XAUUSD EDITION (v11)
# Wrapper MetaTrader 5 (Conexiune și Date)
# ==============================================================================

import MetaTrader5 as mt5
import pandas as pd
import logging
import time

logger = logging.getLogger("XAUUSD_MT5Client")

class MT5Client:
    def __init__(self):
        self.connected = False

    def connect(self) -> bool:
        """Inițializează conexiunea cu terminalul MetaTrader 5."""
        if not mt5.initialize():
            logger.error(f"Eroare la inițializarea MT5: {mt5.last_error()}")
            return False
            
        logger.info(f"Conectat la MT5. Versiune: {mt5.version()}")
        self.connected = True
        return True

    def disconnect(self):
        """Închide conexiunea."""
        if self.connected:
            mt5.shutdown()
            logger.info("Deconectat de la MT5.")
            self.connected = False

    def check_symbol(self, symbol: str) -> bool:
        """Verifică dacă simbolul există și îl adaugă în Market Watch."""
        symbol_info = mt5.symbol_info(symbol)
        if symbol_info is None:
            logger.error(f"Simbolul {symbol} nu a fost găsit!")
            return False
            
        if not symbol_info.visible:
            logger.info(f"Simbolul {symbol} nu este vizibil. Se încearcă adăugarea...")
            if not mt5.symbol_select(symbol, True):
                logger.error(f"Nu s-a putut adăuga {symbol} în Market Watch.")
                return False
                
        return True

    def get_historical_data(self, symbol: str, timeframe: int, n_candles: int = 1000) -> pd.DataFrame:
        """
        Extrage istoricul de preț (candlesticks) pentru calculul indicatorilor.
        Ex: timeframe = mt5.TIMEFRAME_H1
        """
        rates = mt5.copy_rates_from_pos(symbol, timeframe, 0, n_candles)
        if rates is None or len(rates) == 0:
            logger.error(f"Eroare la extragerea datelor istorice pentru {symbol}.")
            return pd.DataFrame()
            
        df = pd.DataFrame(rates)
        df['time'] = pd.to_datetime(df['time'], unit='s')
        df.set_index('time', inplace=True)
        return df

    def get_live_tick(self, symbol: str) -> dict:
        """Extrage prețul curent (Bid/Ask) și spread-ul."""
        tick = mt5.symbol_info_tick(symbol)
        if tick is None:
            return None
            
        return {
            "bid": tick.bid,
            "ask": tick.ask,
            "time": tick.time,
            "spread": mt5.symbol_info(symbol).spread
        }