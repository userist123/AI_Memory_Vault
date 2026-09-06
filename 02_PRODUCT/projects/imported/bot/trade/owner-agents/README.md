# Owner AI Agent Team

4 agenti specializati + orchestrator, bazati pe Gemini 2.5 Flash (GRATUIT).

## Agenti disponibili

| Agent | Specializare |
|-------|-------------|
| Trading Agent | RSI, MACD, backtesting, Binance, CCXT, strategii algoritmice |
| Web Dev Agent | Next.js, React, FastAPI, REST APIs, SQLite, Tailwind |
| Infra Agent | PowerShell, Active Directory, retea, Docker, securitate |
| General Dev Agent | Python, automatizare, debugging, scripturi, CSV/Excel |

## Setup rapid

### 1. Instaleaza dependintele
```bash
pip install google-generativeai
```

### 2. API key GRATUIT
https://aistudio.google.com/app/apikey -> Create API key

### 3. Seteaza cheia (permanent)
```cmd
setx GEMINI_API_KEY "AIza..."
```
Inchide si redeschide CMD dupa setx!

### 4. Porneste
```cmd
start.bat              # cu meniu si auto-detectie agent
python orchestrator.py # direct orchestratorul
python trading_agent.py "Bot RSI pe BTC/USDT"
python webdev_agent.py  "API FastAPI cu JWT auth"
python infra_agent.py   "Export useri AD in CSV"
python general_agent.py "Script backup cu logging"
```

## Schimba directorul de lucru

In orice agent, scrie:
```
dir C:\proiecte\trading-bot
```
Agentul va crea fisierele acolo.

## Structura fisiere

```
owner-agents/
├── orchestrator.py      <- Porneste de aici
├── base_agent.py        <- Logica comuna (nu modifica)
├── trading_agent.py     <- Agent trading
├── webdev_agent.py      <- Agent web dev
├── infra_agent.py       <- Agent infra/PowerShell
├── general_agent.py     <- Agent general
├── requirements.txt
├── start.bat            <- Launcher Windows
└── README.md
```

## Sfaturi pentru taskuri bune

- **Fii specific**: "Bot RSI 14 cu stop-loss 2% si take-profit 4% pe BTC/USDT" > "fa un bot"
- **Indica directorul**: schimba cu `dir` inainte sa dai taskul
- **Iteratii**: dupa ce agentul termina, poti da follow-up: "adauga si Bollinger Bands"
- **Debug**: "citeste fisierul X si corecteaza eroarea Y"
