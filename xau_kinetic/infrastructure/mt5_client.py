"""
MetaTrader 5 infrastructure client wrapper implementing IBrokerClient interface.
Handles terminal connection retry logic, rate fetching, order execution,
and translates MT5 retcodes / mt5.last_error() into domain exceptions.
"""

import logging
import time
from datetime import datetime, timezone
from typing import Any
import pandas as pd

from xau_kinetic.application.interfaces import IBrokerClient
from xau_kinetic.domain.models import (
    TickData,
    Position,
    AccountInfo,
    OrderResult,
    TimeFrame,
    SignalType,
)

logger = logging.getLogger("xau_kinetic.mt5_client")

# Optional import for MetaTrader5 (available on Windows with MT5 installed)
try:
    import MetaTrader5 as mt5  # type: ignore
    HAS_MT5 = True
except ImportError:
    mt5 = None
    HAS_MT5 = False


class MT5ConnectionError(Exception):
    """Raised when MT5 connection or initialization fails."""
    pass


class MT5OrderError(Exception):
    """Raised when an order submission fails with non-zero retcode."""
    def __init__(self, retcode: int, message: str) -> None:
        self.retcode = retcode
        super().__init__(f"MT5 Order Failed [RetCode: {retcode}]: {message}")


# Timeframe mapping dictionary
TIMEFRAME_MAP: dict[TimeFrame, Any] = {}
if HAS_MT5 and mt5 is not None:
    TIMEFRAME_MAP = {
        TimeFrame.M1: mt5.TIMEFRAME_M1,
        TimeFrame.M5: mt5.TIMEFRAME_M5,
        TimeFrame.M15: mt5.TIMEFRAME_M15,
        TimeFrame.M30: mt5.TIMEFRAME_M30,
        TimeFrame.H1: mt5.TIMEFRAME_H1,
        TimeFrame.H4: mt5.TIMEFRAME_H4,
        TimeFrame.D1: mt5.TIMEFRAME_D1,
    }


class MT5Client(IBrokerClient):
    """Secure MetaTrader 5 Broker Client Wrapper with retry mechanics and exception translation."""

    def __init__(
        self,
        path: str | None = None,
        login: int | None = None,
        password: str | None = None,
        server: str | None = None,
        max_retries: int = 3,
        retry_delay: float = 2.0,
        mock_mode: bool = False,
    ) -> None:
        self.path = path
        self.login = login
        self.password = password
        self.server = server
        self.max_retries = max_retries
        self.retry_delay = retry_delay
        self.mock_mode = mock_mode or not HAS_MT5
        self._connected = False

    def initialize(self) -> bool:
        """Initialize connection with retry loop."""
        if self.mock_mode:
            logger.warning("MT5Client running in MOCK mode (MT5 binary not active or requested mock).")
            self._connected = True
            return True

        for attempt in range(1, self.max_retries + 1):
            logger.info(f"Connecting to MetaTrader 5 (Attempt {attempt}/{self.max_retries})...")
            init_kwargs: dict[str, Any] = {}
            if self.path:
                init_kwargs["path"] = self.path
            if self.login:
                init_kwargs["login"] = self.login
            if self.password:
                init_kwargs["password"] = self.password
            if self.server:
                init_kwargs["server"] = self.server

            if mt5.initialize(**init_kwargs):
                self._connected = True
                terminal_info = mt5.terminal_info()
                logger.info(f"Successfully connected to MT5 Terminal: {terminal_info._asdict() if terminal_info else 'OK'}")
                return True

            err_code, err_msg = mt5.last_error()
            logger.warning(f"MT5 Init attempt {attempt} failed: [{err_code}] {err_msg}")
            if attempt < self.max_retries:
                time.sleep(self.retry_delay)

        err_code, err_msg = mt5.last_error()
        raise MT5ConnectionError(f"Failed to connect to MT5 after {self.max_retries} attempts: [{err_code}] {err_msg}")

    def get_ticks(self, symbol: str, count: int = 100) -> list[TickData]:
        """Fetch latest ticks for symbol."""
        if self.mock_mode:
            now = datetime.now(timezone.utc)
            return [
                TickData(
                    symbol=symbol,
                    bid=2650.50,
                    ask=2650.80,
                    last=2650.65,
                    volume=10.0,
                    timestamp=now,
                )
            ]

        ticks = mt5.copy_ticks_from(symbol, datetime.now(timezone.utc), count, mt5.COPY_TICKS_ALL)
        if ticks is None or len(ticks) == 0:
            err_code, err_msg = mt5.last_error()
            logger.error(f"Failed to copy ticks for {symbol}: [{err_code}] {err_msg}")
            return []

        result: list[TickData] = []
        for t in ticks:
            # MT5 timestamp to timezone-aware UTC datetime
            ts = datetime.fromtimestamp(t["time"], tz=timezone.utc)
            result.append(
                TickData(
                    symbol=symbol,
                    bid=float(t["bid"]),
                    ask=float(t["ask"]),
                    last=float(t["last"]),
                    volume=float(t["volume"]),
                    timestamp=ts,
                )
            )
        return result

    def get_rates(self, symbol: str, timeframe: TimeFrame, count: int = 500) -> pd.DataFrame:
        """Fetch rates as pandas DataFrame."""
        if self.mock_mode:
            # Return synthetic DataFrame for testing
            now = datetime.now(timezone.utc)
            dates = pd.date_range(end=now, periods=count, freq="15min", tz=timezone.utc)
            df = pd.DataFrame(
                {
                    "time": dates,
                    "open": [2650.0 + i * 0.1 for i in range(count)],
                    "high": [2652.0 + i * 0.1 for i in range(count)],
                    "low": [2648.0 + i * 0.1 for i in range(count)],
                    "close": [2651.0 + i * 0.1 for i in range(count)],
                    "tick_volume": [100 + i for i in range(count)],
                    "spread": [30 for _ in range(count)],
                    "real_volume": [0 for _ in range(count)],
                }
            )
            return df

        mt5_tf = TIMEFRAME_MAP.get(timeframe, mt5.TIMEFRAME_M15)
        rates = mt5.copy_rates_from_pos(symbol, mt5_tf, 0, count)
        if rates is None or len(rates) == 0:
            err_code, err_msg = mt5.last_error()
            logger.error(f"Failed to fetch rates for {symbol}: [{err_code}] {err_msg}")
            return pd.DataFrame()

        df = pd.DataFrame(rates)
        df["time"] = pd.to_datetime(df["time"], unit="s", utc=True)
        return df

    def send_order(self, order_dict: dict[str, Any]) -> OrderResult:
        """Submit trade order to MT5 with retcode verification."""
        if self.mock_mode:
            logger.info(f"Mock Order executed: {order_dict}")
            return OrderResult(
                success=True,
                retcode=10009,  # TRADE_RETCODE_DONE
                deal=12345678,
                order=87654321,
                volume=order_dict.get("volume", 0.1),
                price=order_dict.get("price", 2650.50),
                bid=2650.50,
                ask=2650.80,
                comment="Mock fill successful",
                request_id=1,
            )

        symbol = order_dict["symbol"]
        action = order_dict.get("action", "TRADE")
        order_type_str = order_dict["type"]
        volume = float(order_dict["volume"])
        price = float(order_dict["price"])
        sl = float(order_dict.get("sl", 0.0))
        tp = float(order_dict.get("tp", 0.0))
        magic = int(order_dict.get("magic", 202608))
        comment = str(order_dict.get("comment", ""))

        if action == "TRADE":
            trade_type = mt5.ORDER_TYPE_BUY if order_type_str == SignalType.BUY.value else mt5.ORDER_TYPE_SELL
            request = {
                "action": mt5.TRADE_ACTION_DEAL,
                "symbol": symbol,
                "volume": volume,
                "type": trade_type,
                "price": price,
                "sl": sl,
                "tp": tp,
                "magic": magic,
                "comment": comment,
                "type_time": mt5.ORDER_TIME_GTC,
                "type_filling": mt5.ORDER_FILLING_IOC,
            }
        elif action == "CLOSE":
            position_ticket = int(order_dict["position"])
            trade_type = mt5.ORDER_TYPE_SELL if order_type_str == SignalType.SELL.value else mt5.ORDER_TYPE_BUY
            request = {
                "action": mt5.TRADE_ACTION_DEAL,
                "symbol": symbol,
                "volume": volume,
                "type": trade_type,
                "position": position_ticket,
                "price": price,
                "magic": magic,
                "comment": comment,
                "type_time": mt5.ORDER_TIME_GTC,
                "type_filling": mt5.ORDER_FILLING_IOC,
            }
        else:
            raise ValueError(f"Unsupported order action: {action}")

        result = mt5.order_send(request)
        if result is None:
            err_code, err_msg = mt5.last_error()
            raise MT5OrderError(err_code, f"order_send returned None: {err_msg}")

        # retcode 10009 = TRADE_RETCODE_DONE, 10008 = TRADE_RETCODE_PLACED
        success = result.retcode in (mt5.TRADE_RETCODE_DONE, mt5.TRADE_RETCODE_PLACED)
        if not success:
            err_msg = f"Retcode {result.retcode}: {result.comment}"
            logger.error(f"MT5 Order Execution Failed: {err_msg}")

        return OrderResult(
            success=success,
            retcode=result.retcode,
            deal=result.deal,
            order=result.order,
            volume=result.volume,
            price=result.price,
            bid=result.bid,
            ask=result.ask,
            comment=result.comment,
            request_id=result.request_id,
            error_message="" if success else result.comment,
        )

    def get_account_info(self) -> AccountInfo:
        """Fetch account snapshot."""
        if self.mock_mode:
            return AccountInfo(
                login=1234567,
                trade_mode=0,
                leverage=100,
                balance=10000.0,
                equity=10000.0,
                margin=0.0,
                free_margin=10000.0,
                profit=0.0,
                currency="USD",
            )

        acc = mt5.account_info()
        if acc is None:
            err_code, err_msg = mt5.last_error()
            raise MT5ConnectionError(f"Failed to fetch account info: [{err_code}] {err_msg}")

        return AccountInfo(
            login=acc.login,
            trade_mode=acc.trade_mode,
            leverage=acc.leverage,
            balance=float(acc.balance),
            equity=float(acc.equity),
            margin=float(acc.margin),
            free_margin=float(acc.margin_free),
            profit=float(acc.profit),
            currency=acc.currency,
        )

    def get_positions(self, symbol: str | None = None) -> list[Position]:
        """Fetch open positions."""
        if self.mock_mode:
            return []

        if symbol:
            raw_positions = mt5.positions_get(symbol=symbol)
        else:
            raw_positions = mt5.positions_get()

        if raw_positions is None:
            return []

        positions: list[Position] = []
        for p in raw_positions:
            pos_type = SignalType.BUY if p.type == mt5.ORDER_TYPE_BUY else SignalType.SELL
            positions.append(
                Position(
                    ticket=int(p.ticket),
                    symbol=p.symbol,
                    type=pos_type,
                    volume=float(p.volume),
                    open_price=float(p.price_open),
                    sl=float(p.sl),
                    tp=float(p.tp),
                    profit=float(p.profit),
                    timestamp=datetime.fromtimestamp(p.time, tz=timezone.utc),
                )
            )
        return positions

    def shutdown(self) -> None:
        """Close MT5 connection."""
        if not self.mock_mode and HAS_MT5 and self._connected:
            mt5.shutdown()
            self._connected = False
            logger.info("MT5 connection shutdown complete.")
