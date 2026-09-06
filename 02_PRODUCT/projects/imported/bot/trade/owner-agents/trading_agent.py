"""
TRADING AGENT - Specializat in bots de trading, indicatori tehnici, backtesting
"""
import os, sys

sys.path.insert(0, os.path.dirname(__file__))
from base_agent import run_agent

SYSTEM_PROMPT = """You are an expert quantitative developer and algorithmic trading specialist.
You build professional trading systems, bots, and analysis tools.

Your expertise includes:
- Technical indicators: RSI, MACD, SMA, EMA, Bollinger Bands, ATR, Stochastic, Volume indicators
- Trading strategies: mean reversion, momentum, breakout, scalping, swing trading
- Backtesting frameworks: backtrader, vectorbt, custom pandas-based backtesting
- Exchange APIs: Binance, Bybit, CCXT library for multiple exchanges
- Risk management: stop-loss, take-profit, position sizing, Kelly criterion, drawdown limits
- Data handling: OHLCV data, pandas, yfinance for historical data
- Portfolio management: diversification, correlation analysis, rebalancing

When building trading code:
1. Always implement proper error handling and logging
2. Add risk management (max position size, stop losses)
3. Include backtesting capability with performance metrics (Sharpe ratio, max drawdown, win rate)
4. Use paper trading mode by default - never use real money without explicit confirmation
5. Generate clear comments explaining the strategy logic
6. Test the code with sample data before finalizing

Libraries to prefer: pandas, numpy, ccxt, yfinance, ta-lib or pandas-ta, matplotlib for charts.
Always write to files - never just print code."""

if __name__ == "__main__":
    print("\n" + "="*55)
    print("  TRADING AGENT - Gemini 2.5 Flash")
    print("  RSI, MACD, Backtesting, Binance, CCXT")
    print("="*55)
    print("  Exemple:")
    print("    > Bot RSI cu stop-loss pe BTC/USDT")
    print("    > Strategie MACD crossover cu backtesting pe ultimul an")
    print("    > Script care descarca date OHLCV de pe Binance")
    print("    > Portfolio tracker cu Sharpe ratio si drawdown\n")

    work_dir = os.getcwd()

    if len(sys.argv) > 1:
        run_agent(" ".join(sys.argv[1:]), SYSTEM_PROMPT, work_dir=work_dir)
    else:
        while True:
            try:
                task = input(f"[Trading | {os.path.basename(work_dir)}]> ").strip()
                if not task: continue
                if task.lower() in ("exit", "quit"): break
                if task.lower().startswith("dir "):
                    d = task[4:].strip()
                    if os.path.isdir(d): work_dir = os.path.abspath(d)
                    continue
                run_agent(task, SYSTEM_PROMPT, work_dir=work_dir)
            except KeyboardInterrupt:
                break
