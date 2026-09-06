---
id: "b492a8e1-5c02-4b21-9e12-c7f8a31e9202"
type: procedure
lifecycle: REVIEW
category: trading
tags: [procedure, deployment, quant_bot, mt5, python, xauusd]
created: 2026-08-25
updated: 2026-08-25
provenance:
  source_type: execution
  source_ref: "xau_kinetic/main.py"
confidence: high
verification: verified
relations:
  - target_id: "ae0206df-b0cb-4810-b2a2-cea462600561"
    type: implements
    target: "[[Elite_Quant_Bot]]"
---

# Procedure: Deployment and Operation of XAU_Kinetic Quant Bot

## Purpose
Standard operating procedure for deploying, configuring, verifying, and monitoring the **XAU_Kinetic** quantitative trading engine in standalone test or live MetaTrader 5 production environments.

## Scope
Applies to local and server environments running Python 3.12+ with MetaTrader 5 terminal connectivity on Windows.

## Preconditions
1. Python 3.12+ installed.
2. Dependencies installed: `pydantic>=2.0.0`, `pandas>=2.0.0`, `numpy>=1.24.0`.
3. MetaTrader 5 terminal installed, updated, and logged into broker account (for live trading mode).

## Dependencies
- Package dependencies: `xau_kinetic/requirements.txt`
- Configuration file: `xau_kinetic/config.json`

## Actions

### Step 1: Install Dependencies
```bash
pip install -r xau_kinetic/requirements.txt
```

### Step 2: Configure System
Edit `xau_kinetic/config.json` to define MT5 credentials, risk limits, and strategy parameters:
```json
{
  "symbol": "XAUUSD",
  "timeframe": "M15",
  "risk": {
    "max_daily_drawdown_pct": 3.0,
    "max_symbol_exposure_lots": 2.0,
    "max_risk_per_trade_pct": 1.0
  }
}
```

### Step 3: Run Verification Test Suite
```bash
python -m unittest discover -s xau_kinetic/tests
```

### Step 4: Execute Standalone Dry Run (Mock Mode)
```bash
python -m xau_kinetic.main --mock --once
```

### Step 5: Execute Historical Backtest Simulation
```bash
python -m xau_kinetic.main --mock --backtest
```

### Step 6: Verify SHA-256 Audit Log Integrity
```bash
python -m xau_kinetic.tools.verify_audit_log --db xau_kinetic_audit.db
```

### Step 7: Build C# .NET 8 WPF Desktop Control Center
```bash
dotnet build XAU_Kinetic.Desktop/XAU_Kinetic.Desktop.csproj
```

### Step 8: Launch C# .NET 8 WPF Enterprise Desktop Application
```bash
dotnet run --project XAU_Kinetic.Desktop/XAU_Kinetic.Desktop.csproj
```

## Expected Results
- All unit tests in `xau_kinetic/tests` pass with zero failures.
- `StrategyRunner` connects to broker (or mock), fetches closed rate data, evaluates signals, applies risk checks, logs `ORDER_PROPOSED` and `ORDER_EXECUTED` into `xau_kinetic_audit.db` with SHA-256 hash chaining.

## Failure Handling & Circuit Breaker Triggers
- If daily drawdown reaches 3.0% or free margin drops below $500, `RiskManager` trips the circuit breaker and vetoes all new orders.
- To reset circuit breaker manually, inspect logs and restart application after daily equity reset.

## Rollback Procedure
If unexpected behavior occurs:
1. Issue `Ctrl+C` or `SIGTERM` signal to stop `StrategyRunner` loop cleanly.
2. Verify open positions via MetaTrader 5 UI or run close command.
3. Inspect `xau_kinetic_audit.db` integrity using `SQLitePersistence.verify_chain_integrity()`.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Elite_Quant_Bot]]
- [[XAU_Kinetic_Clean_Architecture]]
- [[12 Projects and Procedures Map]]
- [[Knowledge Graph Home]]
