---
title: Zeus Trading System AI Advisor Application
type: application
status: active
category: product
---

# ⚡ ZEUS TRADING SYSTEM — AI Advisor

Aplicatie profesionala de trading cu motor AI integrat, rulabila 100% local pe PC.

---

## 🚀 PORNIRE RAPIDA (Windows)

### Pasul 1 — Instalare (o singura data)
Dublu-click pe `INSTALL.bat`

### Pasul 2 — Pornire aplicatie
Dublu-click pe `START_ZEUS.bat`

---

## 🐧 Linux / Mac

```bash
chmod +x start_zeus.sh
./start_zeus.sh
```

Sau manual:
```bash
pip install -r requirements.txt
python3 main.py
```

---

## 📋 CERINTE SISTEM

- **Python 3.10+** (descarca de la python.org — bifeaza "Add to PATH")
- **Windows 10/11** sau Linux / macOS
- **Internet** (pentru date live) — sau functioneaza offline cu date demo
- **RAM**: minim 512MB liberi
- **Spatiu**: ~200MB (inclusiv dependinte)

---

## 🎯 FUNCTIONALITATI

### Grafic interactiv
- Candlestick live cu zoom/pan
- EMA 20, 50, 200 (toggle)
- Bollinger Bands (toggle)
- Volum (toggle)
- RSI panel (14)
- MACD panel cu histograma

### AI Advisor (20+ indicatori)
- **Semnal**: BUY / SELL / HOLD cu nivel de confidenta
- **Entry price** calculat automat
- **Stop Loss** inteligent bazat pe ATR + Suport/Rezistenta
- **3 Take Profit** (TP1, TP2, TP3)
- **Risk:Reward ratio** calculat automat
- **Scor AI** 0-100 bazat pe 5 categorii:
  - Trend (EMA cross, ADX)
  - Momentum (RSI, MACD, Stochastic)
  - Volatilitate (Bollinger Bands, ATR)
  - Volum (confirmare)
  - Pattern-uri candlestick

### Pattern-uri candlestick detectate automat
- Doji, Hammer, Shooting Star
- Bullish/Bearish Engulfing
- Three White Soldiers / Three Black Crows

### Support & Resistance
- Calculat automat din fractali locali
- 3 nivele de rezistenta + 3 nivele de suport

### Piete suportate
| Categorie | Exemple |
|-----------|---------|
| Crypto | BTC, ETH, BNB, SOL, XRP, ADA, DOGE |
| Forex | EURUSD, GBPUSD, USDJPY, XAUUSD (Gold) |
| Actiuni US | AAPL, TSLA, NVDA, MSFT, GOOGL, META |
| Indici | SP500, NASDAQ, DOW, VIX |

### Timeframe-uri
1m, 5m, 15m, 30m, 1h, 1D, 1W, 1M

---

## ⌨️ SHORTCUTS
| Actiune | Shortcut |
|---------|----------|
| Iesire | Ctrl+Q |
| Analizeaza (din input) | Enter |

---

## ⚠️ DISCLAIMER

Aceasta aplicatie este creata **exclusiv in scop educational**.
Nu reprezinta consiliere financiara.
Tranzactioneaza **intotdeauna** pe propria raspundere.
Piata poate evolua diferit de predictii.

---

## 🛠️ STRUCTURA PROIECT

```
trading_app/
├── main.py              # Entry point
├── requirements.txt     # Dependinte Python
├── INSTALL.bat          # Installer Windows
├── START_ZEUS.bat       # Launcher Windows
├── start_zeus.sh        # Launcher Linux/Mac
├── ai/
│   └── advisor.py       # Motor AI (20+ indicatori)
├── data/
│   └── fetcher.py       # Descarca date (yfinance)
└── ui/
    ├── main_window.py   # Fereastra principala
    ├── chart_widget.py  # Grafic candlestick
    └── ai_panel.py      # Panou AI sfaturi
```
