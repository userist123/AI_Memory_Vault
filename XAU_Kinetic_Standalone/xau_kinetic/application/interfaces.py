"""
Abstract Base Classes defining application contracts (BrokerClient, Strategy, RiskManager, Persistence).
Preserves Clean Architecture isolation between domain, application, and infrastructure.
"""

from abc import ABC, abstractmethod
from typing import Any
import pandas as pd

from xau_kinetic.domain.models import (
    TickData,
    SignalObject,
    Position,
    AccountInfo,
    OrderResult,
    AuditEvent,
    TimeFrame,
)


class IBrokerClient(ABC):
    """Abstract interface for external broker interaction (MetaTrader 5, etc.)."""

    @abstractmethod
    def initialize(self) -> bool:
        """Initialize connection to broker terminal with retry logic."""
        pass

    @abstractmethod
    def get_ticks(self, symbol: str, count: int = 100) -> list[TickData]:
        """Fetch latest ticks for symbol."""
        pass

    @abstractmethod
    def get_rates(self, symbol: str, timeframe: TimeFrame, count: int = 500) -> pd.DataFrame:
        """Fetch historical candle rates as a pandas DataFrame."""
        pass

    @abstractmethod
    def send_order(self, order_dict: dict[str, Any]) -> OrderResult:
        """Submit trade order to broker."""
        pass

    @abstractmethod
    def get_account_info(self) -> AccountInfo:
        """Fetch snapshot of broker account state."""
        pass

    @abstractmethod
    def get_positions(self, symbol: str | None = None) -> list[Position]:
        """Fetch active positions."""
        pass

    @abstractmethod
    def shutdown(self) -> None:
        """Close broker connection safely."""
        pass


class IStrategy(ABC):
    """
    Abstract interface for trading strategies.
    MUST be pure functional: accepts market data, returns decision SignalObject.
    Prohibited from invoking network, disk, or broker calls directly.
    """

    @property
    @abstractmethod
    def name(self) -> str:
        """Strategy identifier name."""
        pass

    @abstractmethod
    def generate_signal(self, market_data: pd.DataFrame) -> SignalObject:
        """
        Generate trading decision signal from historical market data.
        Data must consist of CLOSED candles (anti-look-ahead invariant).
        """
        pass


class IRiskManager(ABC):
    """
    Abstract interface for Risk Engine & Circuit Breaker.
    Evaluates proposed SignalObject against account risk parameters and open positions.
    """

    @abstractmethod
    def evaluate_signal(
        self,
        signal: SignalObject,
        account: AccountInfo,
        positions: list[Position],
    ) -> tuple[bool, SignalObject]:
        """
        Evaluate and optionally adjust signal (e.g. scaled volume).
        Returns (approved: bool, adjusted_signal: SignalObject).
        """
        pass

    @abstractmethod
    def is_circuit_broken(self, account: AccountInfo) -> bool:
        """Check if daily drawdown or emergency stop conditions are active."""
        pass


class IPersistence(ABC):
    """Abstract interface for database persistence and audit log chained hashing."""

    @abstractmethod
    def save_ticks(self, ticks: list[TickData]) -> None:
        """Persist tick data."""
        pass

    @abstractmethod
    def log_audit_event(self, event_type: str, payload: dict[str, Any]) -> AuditEvent:
        """Record audit event into SHA-256 chained hash ledger."""
        pass

    @abstractmethod
    def get_last_audit_hash(self) -> str:
        """Return the SHA-256 hash of the most recent audit entry."""
        pass
