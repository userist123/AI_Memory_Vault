"""
Trading Bot — Broker Interface
Unified trading API via ccxt for crypto exchanges + custom adapters for stock brokers.
Supports: Binance, Kraken, Coinbase, Bybit, KuCoin, OKX, Alpaca, etc.
"""
import logging
from dataclasses import dataclass, field
from typing import Optional, List
from datetime import datetime
from enum import Enum

log = logging.getLogger("tradingbot.broker")


class OrderSide(Enum):
    BUY = "buy"
    SELL = "sell"


class OrderType(Enum):
    MARKET = "market"
    LIMIT = "limit"
    STOP_LOSS = "stop_loss"
    STOP_LIMIT = "stop_limit"
    TAKE_PROFIT = "take_profit"


class OrderStatus(Enum):
    OPEN = "open"
    CLOSED = "closed"
    CANCELED = "canceled"
    EXPIRED = "expired"
    REJECTED = "rejected"
    PENDING = "pending"


@dataclass
class Position:
    symbol: str
    side: str           # "long" / "short"
    size: float
    entry_price: float
    current_price: float = 0.0
    unrealized_pnl: float = 0.0
    realized_pnl: float = 0.0
    leverage: float = 1.0
    timestamp: str = ""


@dataclass
class Order:
    id: str
    symbol: str
    side: str
    type: str
    amount: float
    price: float
    status: str
    filled: float = 0.0
    remaining: float = 0.0
    cost: float = 0.0
    fee: float = 0.0
    timestamp: str = ""


@dataclass
class Balance:
    currency: str
    free: float
    used: float
    total: float


class BrokerInterface:
    """
    Unified broker interface. Uses ccxt for crypto/forex, with extension
    points for stock brokers (Alpaca, IBKR via their own APIs).
    """

    def __init__(self):
        self._exchange = None
        self._broker_name = ""
        self._connected = False
        self._sandbox = True

    @property
    def connected(self) -> bool:
        return self._connected

    @property
    def broker_name(self) -> str:
        return self._broker_name

    def connect(self, broker_name: str, api_key: str, api_secret: str,
                passphrase: str = "", sandbox: bool = True, base_url: str = "") -> bool:
        """
        Connect to a broker. Returns True on success.
        """
        try:
            import ccxt

            self._broker_name = broker_name.lower().strip()
            self._sandbox = sandbox

            # Map broker name to ccxt exchange class
            exchange_map = {
                "binance": ccxt.binance,
                "kraken": ccxt.kraken,
                "coinbasepro": ccxt.coinbase, "coinbase": ccxt.coinbase,
                "bybit": ccxt.bybit,
                "kucoin": ccxt.kucoin,
                "okx": ccxt.okx,
                "bitfinex": ccxt.bitfinex,
                "huobi": ccxt.huobi,
                "gateio": ccxt.gateio,
            }

            # Alpaca (stocks) — uses ccxt-compatible wrapper or direct API
            if self._broker_name in ("alpaca",):
                return self._connect_alpaca(api_key, api_secret, sandbox, base_url)

            exchange_cls = exchange_map.get(self._broker_name)
            if not exchange_cls:
                log.error(f"Broker necunoscut: {broker_name}")
                return False

            config = {
                "apiKey": api_key,
                "secret": api_secret,
                "enableRateLimit": True,
                "options": {"defaultType": "spot"},
            }
            if passphrase:
                config["password"] = passphrase
            if sandbox:
                config["sandbox"] = True

            self._exchange = exchange_cls(config)

            # Test connection
            self._exchange.load_markets()
            self._connected = True
            log.info(f"Conectat la {broker_name} ({'sandbox' if sandbox else 'LIVE'})")
            return True

        except ImportError:
            log.error("ccxt nu este instalat. pip install ccxt")
            return False
        except Exception as e:
            log.error(f"Eroare conectare {broker_name}: {e}")
            self._connected = False
            return False

    def _connect_alpaca(self, api_key: str, api_secret: str,
                        sandbox: bool, base_url: str) -> bool:
        """Connect to Alpaca for US stock trading."""
        try:
            import ccxt
            config = {
                "apiKey": api_key,
                "secret": api_secret,
                "enableRateLimit": True,
            }
            if sandbox:
                config["sandbox"] = True

            # Alpaca is available in ccxt as 'alpaca'
            self._exchange = ccxt.alpaca(config)
            self._exchange.load_markets()
            self._connected = True
            log.info(f"Conectat la Alpaca ({'paper' if sandbox else 'LIVE'})")
            return True
        except Exception as e:
            log.error(f"Eroare conectare Alpaca: {e}")
            return False

    def disconnect(self):
        self._exchange = None
        self._connected = False
        self._broker_name = ""
        log.info("Deconectat de la broker")

    # ── Balance ──────────────────────────────────────────────────

    def get_balance(self) -> List[Balance]:
        """Get account balances."""
        if not self._connected:
            return []
        try:
            bal = self._exchange.fetch_balance()
            result = []
            for currency, amounts in bal.get("total", {}).items():
                total = float(amounts) if amounts else 0.0
                if total > 0:
                    free = float(bal.get("free", {}).get(currency, 0) or 0)
                    used = float(bal.get("used", {}).get(currency, 0) or 0)
                    result.append(Balance(currency, free, used, total))
            return sorted(result, key=lambda b: b.total, reverse=True)
        except Exception as e:
            log.error(f"Eroare get_balance: {e}")
            return []

    def get_total_balance_usd(self) -> float:
        """Estimate total balance in USD."""
        if not self._connected:
            return 0.0
        try:
            bal = self._exchange.fetch_balance()
            total = bal.get("total", {})
            usd_value = 0.0
            for currency, amount in total.items():
                amt = float(amount) if amount else 0.0
                if amt <= 0:
                    continue
                if currency in ("USD", "USDT", "USDC", "BUSD", "DAI"):
                    usd_value += amt
                else:
                    try:
                        ticker = self._exchange.fetch_ticker(f"{currency}/USDT")
                        usd_value += amt * ticker["last"]
                    except Exception:
                        try:
                            ticker = self._exchange.fetch_ticker(f"{currency}/USD")
                            usd_value += amt * ticker["last"]
                        except Exception:
                            pass
            return usd_value
        except Exception as e:
            log.error(f"Eroare get_total_balance_usd: {e}")
            return 0.0

    # ── Orders ───────────────────────────────────────────────────

    def place_order(self, symbol: str, side: str, order_type: str,
                    amount: float, price: float = None,
                    stop_price: float = None, params: dict = None) -> Optional[Order]:
        """Place a trade order."""
        if not self._connected:
            log.error("Nu esti conectat la broker")
            return None
        try:
            extra = params or {}

            if order_type == "market":
                raw = self._exchange.create_order(symbol, "market", side, amount, params=extra)
            elif order_type == "limit":
                raw = self._exchange.create_order(symbol, "limit", side, amount, price, params=extra)
            elif order_type == "stop_loss":
                extra["stopPrice"] = stop_price or price
                raw = self._exchange.create_order(symbol, "stop", side, amount, price, params=extra)
            elif order_type == "stop_limit":
                extra["stopPrice"] = stop_price
                raw = self._exchange.create_order(symbol, "stopLimit", side, amount, price, params=extra)
            else:
                raw = self._exchange.create_order(symbol, order_type, side, amount, price, params=extra)

            order = self._parse_order(raw)
            log.info(f"Ordin plasat: {order.id} {side.upper()} {amount} {symbol} @ {price or 'market'}")
            return order

        except Exception as e:
            log.error(f"Eroare place_order: {e}")
            return None

    def cancel_order(self, order_id: str, symbol: str) -> bool:
        if not self._connected:
            return False
        try:
            self._exchange.cancel_order(order_id, symbol)
            log.info(f"Ordin anulat: {order_id}")
            return True
        except Exception as e:
            log.error(f"Eroare cancel_order: {e}")
            return False

    def get_open_orders(self, symbol: str = None) -> List[Order]:
        if not self._connected:
            return []
        try:
            raw_orders = self._exchange.fetch_open_orders(symbol)
            return [self._parse_order(o) for o in raw_orders]
        except Exception as e:
            log.error(f"Eroare get_open_orders: {e}")
            return []

    def get_order_history(self, symbol: str = None, limit: int = 50) -> List[Order]:
        if not self._connected:
            return []
        try:
            raw = self._exchange.fetch_closed_orders(symbol, limit=limit)
            return [self._parse_order(o) for o in raw]
        except Exception as e:
            log.error(f"Eroare get_order_history: {e}")
            return []

    # ── Positions ────────────────────────────────────────────────

    def get_positions(self) -> List[Position]:
        if not self._connected:
            return []
        try:
            if hasattr(self._exchange, "fetch_positions"):
                raw = self._exchange.fetch_positions()
                return [self._parse_position(p) for p in raw if float(p.get("contracts", 0) or 0) > 0]
            return []
        except Exception as e:
            log.error(f"Eroare get_positions: {e}")
            return []

    # ── Market data from broker ──────────────────────────────────

    def get_ticker(self, symbol: str) -> dict:
        if not self._connected:
            return {}
        try:
            return self._exchange.fetch_ticker(symbol)
        except Exception as e:
            log.error(f"Eroare get_ticker: {e}")
            return {}

    def get_orderbook(self, symbol: str, limit: int = 20) -> dict:
        if not self._connected:
            return {}
        try:
            return self._exchange.fetch_order_book(symbol, limit)
        except Exception as e:
            return {}

    def get_available_symbols(self) -> list:
        if not self._connected:
            return []
        try:
            return list(self._exchange.markets.keys())
        except Exception:
            return []

    # ── Helpers ───────────────────────────────────────────────────

    def _parse_order(self, raw: dict) -> Order:
        return Order(
            id=str(raw.get("id", "")),
            symbol=raw.get("symbol", ""),
            side=raw.get("side", ""),
            type=raw.get("type", ""),
            amount=float(raw.get("amount", 0) or 0),
            price=float(raw.get("price", 0) or 0),
            status=raw.get("status", ""),
            filled=float(raw.get("filled", 0) or 0),
            remaining=float(raw.get("remaining", 0) or 0),
            cost=float(raw.get("cost", 0) or 0),
            fee=float((raw.get("fee") or {}).get("cost", 0) or 0),
            timestamp=str(raw.get("datetime", "")),
        )

    def _parse_position(self, raw: dict) -> Position:
        return Position(
            symbol=raw.get("symbol", ""),
            side=raw.get("side", "long"),
            size=float(raw.get("contracts", 0) or 0),
            entry_price=float(raw.get("entryPrice", 0) or 0),
            current_price=float(raw.get("markPrice", 0) or 0),
            unrealized_pnl=float(raw.get("unrealizedPnl", 0) or 0),
            leverage=float(raw.get("leverage", 1) or 1),
            timestamp=str(raw.get("datetime", "")),
        )
