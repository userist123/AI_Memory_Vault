"""
Isolated Risk Manager and Circuit Breaker Engine.
Enforces maximum symbol exposure, daily drawdown hard stop, and equity-based position sizing.
Strategy logic has NO permission to override risk manager decisions (VETO invariant).
"""

import logging
from datetime import datetime, date, timezone
from xau_kinetic.application.interfaces import IRiskManager
from xau_kinetic.domain.models import SignalObject, SignalType, AccountInfo, Position

logger = logging.getLogger("xau_kinetic.risk_manager")


class RiskManager(IRiskManager):
    """
    Isolated Risk Engine implementing hard risk rules & circuit breaker.
    """

    def __init__(
        self,
        max_daily_drawdown_pct: float = 3.0,
        max_symbol_exposure_lots: float = 2.0,
        max_risk_per_trade_pct: float = 1.0,
        max_open_positions: int = 3,
        min_free_margin_usd: float = 500.0,
    ) -> None:
        self.max_daily_drawdown_pct = max_daily_drawdown_pct
        self.max_symbol_exposure_lots = max_symbol_exposure_lots
        self.max_risk_per_trade_pct = max_risk_per_trade_pct
        self.max_open_positions = max_open_positions
        self.min_free_margin_usd = min_free_margin_usd

        # Circuit breaker state
        self._initial_daily_equity: float | None = None
        self._last_reset_date: date | None = None
        self._circuit_triggered: bool = False
        self._circuit_reason: str = ""

    def _update_daily_equity_baseline(
        self, current_equity: float, current_time: datetime | None = None
    ) -> None:
        """Reset equity baseline at start of new day based on market or UTC timestamp."""
        now_dt = current_time or datetime.now(timezone.utc)
        current_date = now_dt.date() if isinstance(now_dt, datetime) else datetime.now(timezone.utc).date()

        if self._last_reset_date != current_date or self._initial_daily_equity is None:
            self._initial_daily_equity = current_equity
            self._last_reset_date = current_date
            self._circuit_triggered = False
            self._circuit_reason = ""
            logger.info(f"Daily Risk Baseline updated to ${current_equity:.2f} for date {current_date}")

    def is_circuit_broken(self, account: AccountInfo, current_time: datetime | None = None) -> bool:
        """Check if daily drawdown or emergency stop conditions are triggered."""
        self._update_daily_equity_baseline(account.equity, current_time=current_time)

        if self._circuit_triggered:
            return True

        if self._initial_daily_equity and self._initial_daily_equity > 0.0:
            drawdown_usd = self._initial_daily_equity - account.equity
            drawdown_pct = (drawdown_usd / self._initial_daily_equity) * 100.0

            if drawdown_pct >= self.max_daily_drawdown_pct:
                self._circuit_triggered = True
                self._circuit_reason = (
                    f"Max Daily Drawdown exceeded ({drawdown_pct:.2f}% >= {self.max_daily_drawdown_pct}%)"
                )
                logger.critical(f"CIRCUIT BREAKER TRIGGERED: {self._circuit_reason}")
                return True

        if account.free_margin < self.min_free_margin_usd:
            self._circuit_triggered = True
            self._circuit_reason = (
                f"Free margin below threshold (${account.free_margin:.2f} < ${self.min_free_margin_usd:.2f})"
            )
            logger.critical(f"CIRCUIT BREAKER TRIGGERED: {self._circuit_reason}")
            return True

        return False

    def calculate_position_size(
        self,
        signal: SignalObject,
        account: AccountInfo,
        entry_price: float,
    ) -> float:
        """
        Calculate lot size based on fixed % risk of equity and Stop Loss distance.
        Returns 0.0 if account is insolvent or SL distance is invalid.
        """
        if account.equity <= 0.0 or account.free_margin <= 0.0:
            return 0.0

        if signal.stop_loss <= 0.0 or entry_price <= 0.0:
            return 0.01

        sl_distance = abs(entry_price - signal.stop_loss)
        if sl_distance <= 0.001:
            return 0.01

        max_risk_usd = account.equity * (self.max_risk_per_trade_pct / 100.0)
        contract_size = 100.0  # XAUUSD standard lot = 100 oz
        calculated_lots = max_risk_usd / (sl_distance * contract_size)

        clamped_lots = round(max(0.01, min(calculated_lots, self.max_symbol_exposure_lots)), 2)
        return clamped_lots

    def evaluate_signal(
        self,
        signal: SignalObject,
        account: AccountInfo,
        positions: list[Position],
    ) -> tuple[bool, SignalObject]:
        """
        Evaluate proposed signal against all risk parameters.
        Returns (approved: bool, adjusted_signal: SignalObject).
        """
        if self.is_circuit_broken(account, current_time=signal.timestamp):
            logger.warning(f"Risk Veto: Circuit breaker active ({self._circuit_reason})")
            return False, signal

        if signal.signal_type == SignalType.HOLD:
            return False, signal

        if signal.signal_type == SignalType.CLOSE:
            return True, signal

        matching_positions = [p for p in positions if p.symbol == signal.symbol]
        if len(matching_positions) >= self.max_open_positions:
            logger.warning(
                f"Risk Veto: Open position limit reached ({len(matching_positions)} >= {self.max_open_positions})"
            )
            return False, signal

        current_volume = sum(p.volume for p in matching_positions)
        if current_volume >= self.max_symbol_exposure_lots:
            logger.warning(
                f"Risk Veto: Max exposure reached ({current_volume:.2f} >= {self.max_symbol_exposure_lots:.2f} lots)"
            )
            return False, signal

        entry_price = signal.target_price if signal.target_price > 0.0 else 2650.0
        calculated_volume = self.calculate_position_size(signal, account, entry_price)

        available_lots = self.max_symbol_exposure_lots - current_volume
        final_volume = round(min(calculated_volume, available_lots), 2)

        if final_volume < 0.01:
            logger.warning("Risk Veto: Calculated volume less than minimum lot size (0.01)")
            return False, signal

        adjusted_signal = SignalObject(
            symbol=signal.symbol,
            signal_type=signal.signal_type,
            confidence=signal.confidence,
            target_price=signal.target_price,
            stop_loss=signal.stop_loss,
            take_profit=signal.take_profit,
            volume=final_volume,
            metadata=signal.metadata,
            timestamp=signal.timestamp,
        )

        logger.info(
            f"Risk Approved: {signal.signal_type.value} on {signal.symbol} | Adjusted Vol: {final_volume} Lots"
        )
        return True, adjusted_signal
