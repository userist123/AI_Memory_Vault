"""
Main Application Entry Point for XAU_Kinetic Quantitative Trading Engine.
Configures Dependency Injection, configuration loading, logging, and initiates strategy runner execution.
"""

import argparse
import logging
import signal
import sys
from pathlib import Path
from typing import Any

from xau_kinetic.config import AppConfig
from xau_kinetic.application.strategy_runner import StrategyRunner
from xau_kinetic.domain.models import TimeFrame
from xau_kinetic.infrastructure.mt5_client import MT5Client
from xau_kinetic.infrastructure.persistence import SQLitePersistence
from xau_kinetic.risk.risk_manager import RiskManager
from xau_kinetic.strategies.xau_kinetic_v2 import XAUKineticV2Strategy

# Configure structured logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    handlers=[logging.StreamHandler(sys.stdout)],
)

logger = logging.getLogger("xau_kinetic.main")


def parse_args() -> argparse.Namespace:
    """Parse command line arguments."""
    parser = argparse.ArgumentParser(description="XAU_Kinetic Quantitative Trading Bot Engine")
    parser.add_argument("--config", type=str, default="xau_kinetic/config.json", help="Path to config.json file")
    parser.add_argument("--symbol", type=str, default=None, help="Override trading asset symbol")
    parser.add_argument("--timeframe", type=str, default=None, help="Override timeframe (M1, M5, M15, M30, H1, H4, D1)")
    parser.add_argument("--mock", action="store_true", help="Run with synthetic mock broker data")
    parser.add_argument("--once", action="store_true", help="Execute single cycle and exit")
    parser.add_argument("--backtest", action="store_true", help="Run historical simulation backtest")
    parser.add_argument("--db-path", type=str, default=None, help="Override SQLite DB path")
    parser.add_argument("--poll-interval", type=float, default=None, help="Override poll interval in seconds")
    return parser.parse_args()


def main() -> None:
    """Application entry point with Dependency Injection setup."""
    args = parse_args()
    logger.info("Initializing XAU_Kinetic Quantitative Trading Engine (v2.0)...")

    # Load AppConfig
    config_file = Path(args.config)
    config = AppConfig.load_from_file(config_file)

    # CLI Overrides
    symbol = args.symbol or config.symbol
    timeframe_str = args.timeframe or config.timeframe
    db_path = args.db_path or config.database.db_path
    poll_interval = args.poll_interval or config.poll_interval_seconds

    # Parse TimeFrame
    try:
        tf_enum = TimeFrame(timeframe_str.upper())
    except ValueError:
        logger.error(f"Invalid timeframe: {timeframe_str}. Supported: {[t.value for t in TimeFrame]}")
        sys.exit(1)

    # 1. Dependency Injection - Infrastructure Layer
    logger.info(f"Setting up BrokerClient (Mock Mode: {args.mock})...")
    broker = MT5Client(
        path=config.mt5.path,
        login=config.mt5.login,
        password=config.mt5.password,
        server=config.mt5.server,
        max_retries=config.mt5.max_retries,
        retry_delay=config.mt5.retry_delay,
        mock_mode=args.mock,
    )

    db_file = Path(db_path)
    logger.info(f"Setting up SQLite Persistence at {db_file.resolve()}...")
    persistence = SQLitePersistence(db_path=db_file)

    # 2. Dependency Injection - Risk Layer
    logger.info("Setting up Risk Engine & Circuit Breaker...")
    risk_manager = RiskManager(
        max_daily_drawdown_pct=config.risk.max_daily_drawdown_pct,
        max_symbol_exposure_lots=config.risk.max_symbol_exposure_lots,
        max_risk_per_trade_pct=config.risk.max_risk_per_trade_pct,
        max_open_positions=config.risk.max_open_positions,
        min_free_margin_usd=config.risk.min_free_margin_usd,
    )

    # 3. Dependency Injection - Strategy Layer
    logger.info(f"Setting up Strategy for {symbol}...")
    strategy = XAUKineticV2Strategy(
        symbol=symbol,
        fast_ema=config.strategy.fast_ema,
        slow_ema=config.strategy.slow_ema,
        rsi_period=config.strategy.rsi_period,
        atr_period=config.strategy.atr_period,
        atr_multiplier_sl=config.strategy.atr_multiplier_sl,
        atr_multiplier_tp=config.strategy.atr_multiplier_tp,
        rsi_overbought=config.strategy.rsi_overbought,
        rsi_oversold=config.strategy.rsi_oversold,
    )

    # If --backtest requested, run Backtester
    if args.backtest:
        from xau_kinetic.backtest.backtester import Backtester
        logger.info("Starting Historical Backtest Simulation...")
        if not broker.initialize():
            logger.error("Broker failed to initialize for backtest.")
            sys.exit(1)

        rates_df = broker.get_rates(symbol, tf_enum, count=500)
        broker.shutdown()

        backtester = Backtester(strategy=strategy, risk_manager=risk_manager)
        result = backtester.run(rates_df, min_bars=50)

        print("\n=== XAU_Kinetic Backtest Performance Report ===")
        print(f"Symbol:               {result.symbol}")
        print(f"Initial Balance:      ${result.initial_balance:,.2f}")
        print(f"Final Balance:        ${result.final_balance:,.2f}")
        print(f"Total Net Profit:     ${result.total_net_profit:,.2f}")
        print(f"Total Trades:         {result.total_trades}")
        print(f"Winning Trades:       {result.winning_trades}")
        print(f"Losing Trades:        {result.losing_trades}")
        print(f"Win Rate %:           {result.win_rate_pct:.2f}%")
        print(f"Profit Factor:        {result.profit_factor:.2f}")
        print(f"Max Drawdown %:       {result.max_drawdown_pct:.2f}%")
        print(f"Sharpe Ratio:         {result.sharpe_ratio:.2f}")
        print("=================================================\n")
        sys.exit(0)

    # 4. Dependency Injection - Application Layer (StrategyRunner Orchestrator)
    runner = StrategyRunner(
        symbol=symbol,
        timeframe=tf_enum,
        broker=broker,
        strategy=strategy,
        risk_manager=risk_manager,
        persistence=persistence,
        magic_number=config.magic_number,
    )

    # Signal handler for graceful shutdown
    def handle_shutdown(sig: int, frame: Any) -> None:
        logger.info(f"Received signal {sig}. Initiating graceful shutdown...")
        runner.stop_loop()
        sys.exit(0)

    signal.signal(signal.SIGINT, handle_shutdown)
    signal.signal(signal.SIGTERM, handle_shutdown)

    # 5. Execution
    if args.once:
        logger.info("Executing single test iteration...")
        if broker.initialize():
            result = runner.run_once()
            logger.info(f"Single iteration completed. Order result: {result}")
            broker.shutdown()
        else:
            logger.error("Failed to initialize broker for single iteration.")
    else:
        logger.info("Starting continuous execution loop. Press Ctrl+C to stop.")
        runner.start_loop(poll_interval_seconds=poll_interval)


if __name__ == "__main__":
    main()
