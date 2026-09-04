"""
Main execution loop coordinating data retrieval, strategy signal generation,
risk management validation, order submission, and SHA-256 audit logging.
"""

import logging
import time
from typing import Any
from xau_kinetic.application.interfaces import (
    IBrokerClient,
    IStrategy,
    IRiskManager,
    IPersistence,
)
from xau_kinetic.domain.models import (
    SignalType,
    TimeFrame,
    SignalObject,
    OrderResult,
)

logger = logging.getLogger("xau_kinetic.strategy_runner")


class StrategyRunner:
    """Orchestrator for quantitative trading workflow execution."""

    def __init__(
        self,
        symbol: str,
        timeframe: TimeFrame,
        broker: IBrokerClient,
        strategy: IStrategy,
        risk_manager: IRiskManager,
        persistence: IPersistence,
        magic_number: int = 202608,
    ) -> None:
        self.symbol = symbol
        self.timeframe = timeframe
        self.broker = broker
        self.strategy = strategy
        self.risk_manager = risk_manager
        self.persistence = persistence
        self.magic_number = magic_number
        self._is_running = False

    def run_once(self) -> OrderResult | None:
        """
        Executes one full iteration of the trading event loop:
        1. Fetch rates & market state from BrokerClient.
        2. Generate signal from strategy using CLOSED bars only.
        3. Evaluate signal with RiskManager (circuit breaker + sizing).
        4. Transmit approved order to BrokerClient.
        5. Record execution audit event with SHA-256 hash.
        """
        logger.debug(f"Starting execution step for {self.symbol} ({self.timeframe.value})...")

        # Step 1: Fetch account info and market rates
        account = self.broker.get_account_info()
        positions = self.broker.get_positions(self.symbol)
        rates_df = self.broker.get_rates(self.symbol, self.timeframe, count=300)

        if rates_df.empty:
            logger.warning(f"No rate data received for {self.symbol}. Skipping cycle.")
            return None

        # Step 2: Generate signal (Pure functional call)
        signal: SignalObject = self.strategy.generate_signal(rates_df)
        logger.info(f"Generated signal: {signal.signal_type.value} | Confidence: {signal.confidence:.2f}")

        # Record signal generation event
        self.persistence.log_audit_event(
            event_type="SIGNAL_GENERATED",
            payload={
                "symbol": self.symbol,
                "strategy": self.strategy.name,
                "signal_type": signal.signal_type.value,
                "confidence": signal.confidence,
                "volume": signal.volume,
                "stop_loss": signal.stop_loss,
                "take_profit": signal.take_profit,
            },
        )

        if signal.signal_type in (SignalType.HOLD, SignalType.CLOSE):
            # If CLOSE signal, handle position exit if open position exists
            if signal.signal_type == SignalType.CLOSE and positions:
                for pos in positions:
                    logger.info(f"Closing position ticket {pos.ticket} for {self.symbol}")
                    close_type = SignalType.SELL if pos.type == SignalType.BUY else SignalType.BUY
                    ticks = self.broker.get_ticks(self.symbol, count=1)
                    close_price = ticks[0].bid if close_type == SignalType.SELL else ticks[0].ask if ticks else 0.0

                    order_dict = {
                        "action": "CLOSE",
                        "symbol": self.symbol,
                        "volume": pos.volume,
                        "type": close_type.value,
                        "position": pos.ticket,
                        "price": close_price,
                        "magic": self.magic_number,
                        "comment": f"Strategy Close {self.strategy.name}",
                    }
                    # Log pre-execution intent into audit ledger before broker dispatch
                    self.persistence.log_audit_event("ORDER_PROPOSED", order_dict)
                    result = self.broker.send_order(order_dict)
                    self.persistence.log_audit_event("ORDER_EXECUTED", result.model_dump(mode="json"))
                    return result
            return None

        # Step 3: Risk Evaluation
        approved, adjusted_signal = self.risk_manager.evaluate_signal(signal, account, positions)
        if not approved:
            logger.warning(f"Signal {signal.signal_type.value} VETOED by RiskManager.")
            self.persistence.log_audit_event(
                event_type="SIGNAL_VETOED",
                payload={"symbol": self.symbol, "reason": "Risk manager veto"},
            )
            return None

        # Step 4: Formulate and transmit order
        ticks = self.broker.get_ticks(self.symbol, count=1)
        if not ticks:
            logger.error("Failed to fetch latest tick price for order formulation.")
            return None

        current_tick = ticks[0]
        execution_price = current_tick.ask if adjusted_signal.signal_type == SignalType.BUY else current_tick.bid

        order_dict: dict[str, Any] = {
            "action": "TRADE",
            "symbol": self.symbol,
            "volume": adjusted_signal.volume,
            "type": adjusted_signal.signal_type.value,
            "price": execution_price,
            "sl": adjusted_signal.stop_loss,
            "tp": adjusted_signal.take_profit,
            "magic": self.magic_number,
            "comment": f"XAU_Kinetic {self.strategy.name}",
        }

        # Log pre-execution order intent into SHA-256 audit ledger BEFORE dispatching to broker
        self.persistence.log_audit_event("ORDER_PROPOSED", order_dict)

        logger.info(f"Submitting {adjusted_signal.signal_type.value} order: Vol={adjusted_signal.volume} Price={execution_price}")
        result: OrderResult = self.broker.send_order(order_dict)

        # Step 5: Audit Persistence
        self.persistence.log_audit_event(
            event_type="ORDER_EXECUTED",
            payload=result.model_dump(mode="json"),
        )
        return result

    def start_loop(self, poll_interval_seconds: float = 5.0) -> None:
        """Run continuous execution loop until stopped."""
        self._is_running = True
        logger.info(f"Starting StrategyRunner loop for {self.symbol} with poll interval {poll_interval_seconds}s...")

        if not self.broker.initialize():
            logger.critical("Broker initialization failed. Cannot start StrategyRunner loop.")
            return

        try:
            while self._is_running:
                try:
                    self.run_once()
                except Exception as e:
                    logger.exception(f"Unhandled error during strategy cycle: {e}")
                    self.persistence.log_audit_event(
                        event_type="SYSTEM_ERROR",
                        payload={"error": str(e)},
                    )
                time.sleep(poll_interval_seconds)
        finally:
            logger.info("Stopping StrategyRunner loop...")
            self.broker.shutdown()

    def stop_loop(self) -> None:
        """Signal loop to stop gracefully."""
        self._is_running = False
