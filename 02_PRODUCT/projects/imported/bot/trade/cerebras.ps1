if (-not $env:CEREBRAS_API_KEY) { throw "Set CEREBRAS_API_KEY before running this script." }
python agent.py
# sau direct cu un task:
python agent.py "Fa un trading bot care calculeaza RSI si SMA"