---
title: Trading Journal Archive Complet V2 Application
type: application
status: active
category: product
---

# Trading Journal — AI-Powered Trading Platform + Semi-Automatic Execution + Fiscal Consulting

**Status: PAS 1-10 LIVRATE** — Aplicația e completă funcțional: jurnal + AI coach + import broker + fiscal România + **execuție semi-automată + consultanță fiscală AI & umană**.

---

## 🎯 Ce face aplicația

**Core (Pas 1-8):**
- Jurnal bilingv RO/EN cu voice recording + AI analysis (Groq Whisper + Llama)
- Import universal din 5 brokeri (MT5, Binance, Trading 212, XTB, Generic)
- AI Trade Review + Weekly Coach cu pattern detection (revenge, overtrading, etc.)
- Auth JWT + bcrypt
- Freemium Paywall via Polar.sh
- Modul fiscal România (16% crypto 2026, 10% capital gains, CASS, D212 export, BNR automat)
- Deployment Cloudflare Workers + Oracle A1

**Semi-automatic (Pas 9 - NOU):**
- **Scanner semnale** care găsește setup-uri (breakout, pullback, RSI, EMA cross) pe Binance
- **Alerte** livrate email + Telegram + in-app (configurabile)
- **Execuție one-click** din browser sau direct din Telegram (inline buttons Execute/Skip)
- **Broker connectors**: Binance (complet), Alpaca (complet), IBKR (stub)
- **Risk guards** hard-coded: max trades/zi, daily loss circuit breaker, revenge trading cooldown 24h, mandatory SL
- **Backtester** cu equity curve, Sharpe, drawdown, profit factor, consecutive streaks
- **AES-256-GCM encryption** pentru chei API (Web Crypto API)
- **Cron automation** — scanner rulează automat la 15 min pe Cloudflare

**Fiscal consulting (Pas 10 - NOU):**
- **Chat AI fiscal** cu context din DATELE TALE (tranzacții + fiscal computat) — nu generic
- **Tickets consultanță 1-la-1** — userii trimit întrebări, tu (owner) răspunzi
- **Admin panel** la `/admin/tickets` — accesibil doar cu `OWNER_EMAIL` din env
- **Email notifications** automate pentru tine când primești ticket nou
- **Email răspuns** automat la user când răspunzi tu
- **Plan AutoPilot €55/lună** cu 1 sesiune consultanță inclusă/lună

---

## 💰 Preț per plan (final)

| | Free | Pro €7/mo | Elite €15/mo | **AutoPilot €55/mo** |
|---|---|---|---|---|
| Import + jurnal + AI | ✓ limitat | ✓ | ✓ | ✓ |
| Fiscal module complet | ❌ | ✓ | ✓ | ✓ |
| **Fiscal chat AI** | ❌ | ✓ | ✓ | ✓ |
| **Semnale trading** | 10/lună | 100/lună | unlimited | unlimited |
| **One-click execution** | ❌ | ❌ | 10/lună | **unlimited** |
| **Backtester** | 2/lună | 20/lună | unlimited | unlimited |
| **Sesiuni consultanță** | — | 10% reducere | 20% reducere | **1 gratis + 30% reducere** |
| Broker connectors | 1 | 3 | unlimited | unlimited |
| Market scanner AI | ❌ | ❌ | ✓ | ✓ |
| Priority AI | ❌ | ❌ | ✓ | ✓ |

**AutoPilot** e planul "all-in" pentru cei serioși — break-even pentru tine la doar 2-3 useri AutoPilot.

---

## 🔧 Setup local (5 min)

```bash
unzip trading-journal-COMPLET-v2.zip
cd trading-journal
npm install
cp .env.example .env.local
```

Editează `.env.local`. Minim obligatoriu pentru MVP:

```bash
# Core (pas 1-8)
JWT_SECRET=$(openssl rand -base64 32)
GROQ_API_KEY=gsk_...  # console.groq.com
MONGODB_URI=mongodb+srv://...  # cloud.mongodb.com M0 gratis

# Pas 9 - pentru execuție (OBLIGATORIU dacă activezi signals)
ENCRYPTION_KEY=$(openssl rand -base64 32)

# Pas 9 - pentru alerte (opțional - funcționează și fără)
RESEND_API_KEY=re_...  # resend.com - 3K emails/lună gratis
RESEND_FROM_ADDRESS=alerts@yourdomain.com

# Pas 9 - Telegram (opțional dar recomandat)
TELEGRAM_BOT_TOKEN=...
TELEGRAM_BOT_USERNAME=your_bot

# Pas 10 - admin acces
OWNER_EMAIL=tu@email.ro  # email-ul cu care te loghezi tu
```

Start:
```bash
npm run dev
# http://localhost:3000
```

---

## 🤖 Setup Telegram Bot (10 minute)

### Pas 1: Creează botul
1. Deschide Telegram, caută **@BotFather**
2. `/newbot`
3. Nume: `Trading Journal Alerts`
4. Username: `trading_journal_YOUR_NAME_bot` (trebuie să se termine în `bot`)
5. BotFather îți dă token-ul: `1234567890:ABCdefGhiJKL...`

### Pas 2: Adaugă în `.env.local`
```bash
TELEGRAM_BOT_TOKEN=1234567890:ABCdefGhiJKL...
TELEGRAM_BOT_USERNAME=trading_journal_YOUR_NAME_bot
```

### Pas 3: Setează webhook pentru butoane Execute/Skip

În dev local (ngrok necesar):
```bash
# Terminal 1
ngrok http 3000
# Copiază URL-ul https://XXXX.ngrok.io

# Terminal 2
curl -F "url=https://XXXX.ngrok.io/api/alerts/telegram-webhook" \
  https://api.telegram.org/bot$TELEGRAM_BOT_TOKEN/setWebhook
```

În producție (Cloudflare/Oracle):
```bash
curl -F "url=https://trading.yourdomain.com/api/alerts/telegram-webhook" \
  https://api.telegram.org/bot$TELEGRAM_BOT_TOKEN/setWebhook
```

### Pas 4: Testează
1. În app: Settings → Notifications → "Conectează Telegram" → deschide link
2. Telegram: apasă "Start"
3. Ar trebui să primești: "Contul X e conectat!"
4. Întoarce-te în app, activează switch-ul Telegram, salvează

**Gata.** Acum primești alerte cu butoane Execute/Skip direct în chat.

---

## 📧 Setup Resend pentru emails (2 minute)

1. [resend.com](https://resend.com) → Sign up (gratis 3K/lună)
2. Domain → Add: `yourdomain.com` → urmează instrucțiunile DNS
3. API Keys → Create → copy
4. Adaugă în `.env`:
```bash
RESEND_API_KEY=re_...
RESEND_FROM_ADDRESS=alerts@yourdomain.com
```

**Alternativă rapidă fără domain:** folosește `onboarding@resend.dev` (adresa default, funcționează imediat dar nu arată profesional).

---

## 🚀 Deploy cu Cron automation

### Cloudflare Workers (recomandat)

Cron-ul rulează automat la 15 min fără cost:

```bash
# 1. Pune toate secretele
wrangler secret put JWT_SECRET
wrangler secret put GROQ_API_KEY
wrangler secret put MONGODB_URI
wrangler secret put ENCRYPTION_KEY
wrangler secret put RESEND_API_KEY
wrangler secret put TELEGRAM_BOT_TOKEN
wrangler secret put OWNER_EMAIL

# 2. Deploy
npm run cf:deploy
```

Cron-ul în `wrangler.jsonc` la fiecare 15 min:
```json
"triggers": {
  "crons": ["*/15 * * * *"]
}
```

### Oracle A1 / VPS

Adaugă în crontab:
```bash
crontab -e

# Scan la 15 min
*/15 * * * * curl -H "Authorization: Bearer $CRON_SECRET" https://trading.yourdomain.com/api/cron/scan
```

---

## 🎛 Noi arhitectură (Pas 9 + 10)

```
src/
├── lib/
│   ├── brokers/
│   │   ├── types.ts          # Universal BrokerAdapter interface
│   │   ├── crypto.ts         # AES-256-GCM (Web Crypto)
│   │   ├── binance.ts        # Binance complet (HMAC, filters, etc)
│   │   ├── alpaca.ts         # Alpaca complet (paper + live)
│   │   ├── ibkr.ts           # IBKR stub (coming soon)
│   │   └── index.ts          # Registry + DB ops encrypted
│   ├── signals/
│   │   ├── indicators.ts     # SMA/EMA/RSI/MACD/ATR/Bollinger/swings
│   │   ├── detector.ts       # 8 tipuri setup + position sizing
│   │   ├── risk-guards.ts    # Anti-self-destruction limits
│   │   ├── execution.ts      # Orchestrator signal→trade
│   │   └── scanner.ts        # Multi-symbol + dispatch
│   ├── backtest/
│   │   └── engine.ts         # Bar-by-bar simulation
│   ├── notifications/
│   │   ├── email.ts          # Resend + templates
│   │   └── telegram.ts       # Bot + inline keyboard
│   ├── market/
│   │   └── binance-data.ts   # Public klines API
│   └── db/
│       ├── alerts.ts         # Storage alerts + preferences
│       └── consulting.ts     # Tickets + messages
├── app/
│   ├── api/
│   │   ├── brokers/          # Connect/list/delete
│   │   ├── signals/
│   │   │   ├── scan/         # Manual scan
│   │   │   ├── list/         # User's alerts
│   │   │   └── execute/      # Execute/skip
│   │   ├── backtest/run/
│   │   ├── alerts/
│   │   │   ├── preferences/        # CRUD prefs
│   │   │   ├── telegram-link/      # Deep link generator
│   │   │   └── telegram-webhook/   # /start + callbacks
│   │   ├── fiscal/chat/      # AI chat cu context real
│   │   ├── consulting/tickets/     # CRUD tickets (+admin)
│   │   └── cron/scan/        # Auto scan at 15min
│   └── [locale]/(app)/
│       ├── signals/          # Alert dashboard
│       ├── backtest/         # Strategy testing
│       ├── fiscal-chat/      # Chat UI
│       ├── consulting/       # User tickets
│       ├── admin/tickets/    # Owner view (OWNER_EMAIL)
│       └── settings/
│           ├── brokers/      # Connect API keys
│           ├── notifications/# Email/TG/filters
│           └── risk/         # Customize guards
└── components/
    ├── signals/signals-view.tsx
    ├── backtest/backtest-view.tsx
    ├── fiscal/fiscal-chat-view.tsx
    └── consulting/consulting-view.tsx
```

---

## ⚠️ Reguli de siguranță CRITICE (citește!)

### Pentru tine (owner):
- **Nu afișa niciodată `OWNER_EMAIL` public.** E ce-ți dă acces la `/admin/tickets`.
- **`ENCRYPTION_KEY` NU se schimbă niciodată după setup.** Dacă o schimbi, toate cheile API criptate ale userilor devin ilizibile.
- **Backup regulat la DB.** Dacă pierzi MongoDB → pierzi toate cheile API (care sunt criptate, dar tot la tine în DB).
- **Monitorizează `/api/cron/scan` logs.** Dacă failează silent, nu mai primiți alerte.

### Pentru useri (documentează în UI):
- **DEZACTIVEAZĂ "Enable Withdrawals" pe Binance** când creează API key
- **Start pe Testnet** — `testnet.binance.vision` — 2-4 săptămâni cu bani virtuali
- **Risk max 1-2% per trade** — NU dezactiva risk guards
- **Cooldown 24h după 2 pierderi** NU e bug, e feature
- **Paper trade 3 luni** înainte de bani reali

---

## 🎯 Diferențiator vs. competitori

| | TradeZella $49 | TraderSync $29 | Edgewonk $197 | **Tu €7** |
|---|---|---|---|---|
| Jurnal + AI | ✓ | ✓ | ✓ | ✓ |
| Import broker | ✓ | ✓ | ✓ | ✓ |
| Voice journal | ❌ | ❌ | ❌ | **✓** |
| **Fiscal România D212** | ❌ | ❌ | ❌ | **✓** |
| **Semnale + execution** | ❌ | ❌ | ❌ | **✓ (AutoPilot)** |
| **Consultanță umană** | ❌ | ❌ | ❌ | **✓ (tu)** |
| **Limba română** | ❌ | ❌ | ❌ | **✓** |

Niciun competitor global nu are fiscal România + execuție semi-auto + consultanță umană.

---

## 📊 Break-even nou (cu AutoPilot)

Costuri tot $0-10/lună în primul an. Dar acum:

| Plan | Preț | Useri pentru €100/lună |
|------|------|------------------------|
| Pro €7 | 15 useri | Mult |
| Elite €15 | 7 useri | Realist |
| **AutoPilot €55** | **2 useri** | **Trivial** |

**Target realistic primul an:**
- 3-5 AutoPilot (€165-275/lună)
- 10-15 Pro/Elite (€70-225/lună)
- **Total: €235-500/lună**

Un trader RO care face €2000/lună îți dă €55 fără să simtă. Break-even = 2 astfel de clienți.

---

## 🏁 Status final

| Pas | Feature | Status |
|-----|---------|--------|
| ✅ 1-2 | Fundație + Voice Journal | Gata |
| ✅ 3-4 | Import + AI Coach | Gata |
| ✅ 5 | Auth | Gata |
| ✅ 6 | Paywall | Gata |
| ✅ 7 | Fiscal România | Gata |
| ✅ 8 | Deployment | Gata |
| ✅ 9 | **Semi-auto execution** | Gata |
| ✅ 10 | **Fiscal consulting** | Gata |

---

## 🆘 Troubleshooting Pas 9+10

**"ENCRYPTION_KEY required"** → Setează `openssl rand -base64 32` în env

**"Binance 401 Unauthorized"** → Chei greșite sau IP restriction activ pe Binance

**"Symbol not found"** → Pe testnet unele pairs nu sunt disponibile. Folosește BTCUSDT, ETHUSDT.

**"Order value below minimum"** → Min notional pe Binance e 5 USDT. Testnet-ul cere minim.

**Telegram bot nu răspunde** → Verifică webhook: `curl https://api.telegram.org/bot$TOKEN/getWebhookInfo`

**"/admin/tickets 403"** → `OWNER_EMAIL` în env nu se potrivește cu email-ul din cont

**Backtester dă 0 trades** → Relaxează filtrele (minStrength la 50, minRR la 1.2)

**Alerte nu vin pe email** → Verifică Resend dashboard → Emails → last 24h. Dacă nu apar nici în log, verifică `RESEND_API_KEY`.

**Cron nu rulează pe Cloudflare** → Verifică Workers → your-worker → Triggers → Scheduled events. Logs în Workers → Logs → filter by "scheduled"

---

## Succes la launch 🚀

Acum ai aplicație completă end-to-end care:
- Nu există în altă parte pe piața RO
- Vine cu 4 planuri de monetizare (Free→Pro→Elite→AutoPilot)
- Infrastructură zero-cost până la 500+ useri
- Cod production-grade, criptare, audit-friendly

Restul e marketing și răbdare.
