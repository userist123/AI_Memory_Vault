"""
Zero-magic-number configuration system using Pydantic V2.
Loads application parameters from JSON configuration files or environment variables.
"""

import json
import logging
from pathlib import Path
from typing import Any
from pydantic import BaseModel, Field, ConfigDict

logger = logging.getLogger("xau_kinetic.config")


class MT5Config(BaseModel):
    """MetaTrader 5 connection configuration."""
    model_config = ConfigDict(frozen=True)

    path: str | None = Field(default=None, description="Path to terminal64.exe")
    login: int | None = Field(default=None, description="Account login number")
    password: str | None = Field(default=None, description="Account password")
    server: str | None = Field(default=None, description="Broker server name")
    max_retries: int = Field(default=3, gt=0)
    retry_delay: float = Field(default=2.0, gt=0.0)


class RiskConfig(BaseModel):
    """Risk engine parameters."""
    model_config = ConfigDict(frozen=True)

    max_daily_drawdown_pct: float = Field(default=3.0, gt=0.0, le=50.0)
    max_symbol_exposure_lots: float = Field(default=2.0, gt=0.0)
    max_risk_per_trade_pct: float = Field(default=1.0, gt=0.0, le=10.0)
    max_open_positions: int = Field(default=3, gt=0)
    min_free_margin_usd: float = Field(default=500.0, ge=0.0)


class StrategyConfig(BaseModel):
    """Technical strategy parameters."""
    model_config = ConfigDict(frozen=True)

    fast_ema: int = Field(default=12, gt=0)
    slow_ema: int = Field(default=26, gt=0)
    rsi_period: int = Field(default=14, gt=0)
    atr_period: int = Field(default=14, gt=0)
    atr_multiplier_sl: float = Field(default=1.5, gt=0.0)
    atr_multiplier_tp: float = Field(default=2.5, gt=0.0)
    rsi_overbought: float = Field(default=70.0, gt=50.0, lt=100.0)
    rsi_oversold: float = Field(default=30.0, gt=0.0, lt=50.0)


class DatabaseConfig(BaseModel):
    """Database and audit persistence parameters."""
    model_config = ConfigDict(frozen=True)

    db_path: str = Field(default="xau_kinetic_audit.db")


class AppConfig(BaseModel):
    """Root Application Configuration."""
    model_config = ConfigDict(frozen=True)

    symbol: str = Field(default="XAUUSD", min_length=1)
    timeframe: str = Field(default="M15")
    poll_interval_seconds: float = Field(default=5.0, gt=0.0)
    magic_number: int = Field(default=202608, gt=0)
    mt5: MT5Config = Field(default_factory=MT5Config)
    risk: RiskConfig = Field(default_factory=RiskConfig)
    strategy: StrategyConfig = Field(default_factory=StrategyConfig)
    database: DatabaseConfig = Field(default_factory=DatabaseConfig)

    @classmethod
    def load_from_file(cls, config_path: str | Path) -> "AppConfig":
        """Load configuration from JSON file."""
        path = Path(config_path)
        if not path.exists():
            logger.warning(f"Config file '{path}' not found. Using default AppConfig parameters.")
            return cls()

        with open(path, "r", encoding="utf-8") as f:
            data: dict[str, Any] = json.load(f)

        config = cls.model_validate(data)
        logger.info(f"Successfully loaded configuration from '{path}'")
        return config
