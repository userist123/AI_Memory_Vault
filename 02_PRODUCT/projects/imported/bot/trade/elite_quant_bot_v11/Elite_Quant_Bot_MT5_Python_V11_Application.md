---
title: Elite Quant Bot MT5 Python V11 Application
type: application
status: active
category: product
---

# Elite Quant Bot — MT5 + Python

Prop-firm compliant MT5 trading bot. Python 3.12, Tkinter UI, 300+ strategy
instances generated via factory, weighted ensemble voting, and an online
logistic-regression "intuition" model trained on the bot's own closed trades.

## Requirements
- Windows
- MetaTrader 5 terminal installed AND logged in to your broker account
- Python 3.12

## Install
```
pip install -r requirements.txt
```

## Run
```
python main.py
```

The Tkinter dashboard exposes:
- Connection / balance / equity / daily PnL
- Active position (ticket + live PnL)
- Top-10 strategy instances by score
- Ensemble consensus and ML probability
- Event log
- START / STOP AUTO and KILL SWITCH buttons

## Configuration
Edit `config.py` — symbol, risk %, session hours, breakers, ML thresholds.

## Notes (MT5 API correctness)
- Filling mode is auto-detected from `symbol_info().filling_mode` BITMASK.
- BUY uses `tick.ask`, SELL uses `tick.bid`.
- SL/TP are ATR-based and rounded to `info.digits`.
- Lot size = `risk% * balance` converted via `trade_tick_size` /
  `trade_tick_value`, snapped to `volume_step`.
- PnL is tracked by `position_id` + `DEAL_ENTRY_OUT`.
- Circuit breakers: daily -3%, 3 consecutive losses -> 30 min real cooldown,
  max 5 bot orders/day (counted by magic + DEAL_ENTRY_IN).
- All UI mutations from worker threads go through `root.after(0, ...)`.
