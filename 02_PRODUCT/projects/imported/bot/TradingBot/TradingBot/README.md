# Trading Bot v2.0

Sistem complet de trading cu AI Advisor, executie ordine pe broker real, portofoliu live si management de risc.

## Instalare

```bash
# 1. Instaleaza dependentele
pip install -r requirements.txt

# 2. Porneste aplicatia
python main.py
```

**Windows:** Dublu-click pe `INSTALL.bat` apoi `START_BOT.bat`

## Ce contine

### Motor AI (25+ indicatori)
- **Trend:** EMA 9/20/50/100/200, SMA 50/200, Golden/Death Cross, ADX (+DI/-DI)
- **Momentum:** RSI(14), MACD + histograma, Stochastic K/D, Williams %R, MFI
- **Volatilitate:** Bollinger Bands (width, %B), ATR, Keltner Channel
- **Volum:** OBV trend, Volume Ratio, MFI
- **Cloud:** Ichimoku (Conversion, Base, Span A/B)
- **Pattern-uri:** Doji, Hammer, Shooting Star, Engulfing, Three Soldiers/Crows, Morning/Evening Star
- **Regim piata:** Trending, Ranging, Squeeze (pre-breakout), Transitie

### Broker Integration (via ccxt)
- **Crypto:** Binance, Binance Futures, Kraken, Coinbase, Bybit, KuCoin, OKX
- **Actiuni US:** Alpaca
- **Altele:** Interactive Brokers, XTB (extensibil)
- Paper trading (testnet) sau LIVE
- Executie ordine: Market, Limit, Stop Loss
- Portofoliu live: balante, pozitii, ordine, istoric

### Securitate
- Credentiale API criptate local cu AES-256 (Fernet)
- Parola master pentru decriptare — nu se stocheaza nicaieri
- Fisierul `~/.tradingbot/credentials.enc` contine doar date criptate

### Interfata
- Grafic candlestick interactiv (pyqtgraph)
- EMAs, Bollinger Bands, S/R levels pe chart
- RSI, MACD, Volume sub-charts
- AI Panel cu semnal, scor, indicatori, warnings
- Portfolio panel cu balante, pozitii, ordine
- Strategy log
- Auto-refresh configurabil (30s / 1min / 5min)
- Piete rapide din meniu

## Arhitectura

```
TradingBot/
├── main.py              # Entry point
├── core/
│   ├── config.py        # Configurare, constante, symbol maps
│   └── security.py      # Criptare/decriptare credentiale
├── ai/
│   └── advisor.py       # Motor AI cu 25+ indicatori
├── broker/
│   └── interface.py     # Interfata unificata broker (ccxt)
├── data/
│   └── fetcher.py       # Date live via yfinance
├── strategies/
│   └── engine.py        # Motor strategii automate
├── ui/
│   ├── main_window.py   # Fereastra principala
│   ├── chart_widget.py  # Grafic candlestick
│   ├── ai_panel.py      # Panou AI advisor
│   ├── broker_dialog.py # Dialog autentificare broker
│   └── portfolio_panel.py # Portofoliu si ordine
├── requirements.txt
├── INSTALL.bat
├── START_BOT.bat
└── start_bot.sh
```

## Flux de lucru

1. **Porneste** aplicatia (`python main.py`)
2. **Introdu** un simbol (BTC, AAPL, EURUSD, GOLD, etc.) si apasa ANALIZEAZA
3. **Citeste** analiza AI: semnal, scor, indicatori, warnings
4. **Conecteaza broker** din meniu (Broker > Conecteaza) cu API Key
5. **Executa trade** din butonul din AI Panel (cu confirmare)
6. **Monitorizeaza** portofoliul din tab-ul Portofoliu & Ordine

## Simboluri suportate

- **Crypto:** BTC, ETH, SOL, XRP, ADA, DOGE, AVAX, LINK, DOT, MATIC, UNI, etc.
- **Forex:** EURUSD, GBPUSD, USDJPY, USDCHF, AUDUSD, EURRON, USDRON
- **Commodities:** GOLD, SILVER, OIL, BRENT, NATGAS, COPPER
- **Indici:** SP500, NASDAQ, DOW, VIX, DAX, FTSE, NIKKEI, RUSSELL
- **Actiuni:** AAPL, MSFT, NVDA, TSLA, META, AMZN, GOOGL + orice ticker valid

## Disclaimer

Acest software este pentru uz personal si educational. NU constituie sfat financiar.
Tranzactioneaza pe propria raspundere. Foloseste paper trading (testnet) inainte de bani reali.
