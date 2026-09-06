# ELITE_QUANT_BOT — Audit v1 față de ELITE_QUANT_ARCHITECT System Prompt

**Generated:** 2026-06-13
**Codebase scanned:** 4404 LoC Python (config.py, core/, ml/, strategies/, ui/, main.py)
**Snapshot:** `/mnt/documents/elite_quant_bot/v2.0/v1_snapshot.zip`

Severitate: **P0** = blochează v2.0 release; **P1** = corectabil în v2.0 fără break; **P2** = polish/UX.

---

## 1. Sumar executiv

| Domeniu | Status | Severitate dominantă |
|---|---|---|
| Risk management | parțial — formulă OK, lipsește kill-switch persistent, cooldown post-WIN, max_exposure | P0 |
| State machine | ad-hoc (nu FSM explicit), guard order corect dar coduri "no-trade" nestandardizate | P0 |
| XAUUSD profile | bine separat, dar adaptive DD modifică `config.RISK_PCT` global (side-effect ascuns) | P0 |
| Ensemble | bun (vot + ML gate), lipsește auto-decay pe loss + recalibrare | P1 |
| ML | online SGD OK, **fără Platt calibration**, fără leakage tests | P1 |
| Journal | SQLite OK, **NU este append-only** (folosește UPDATE), fără `fsync`/WAL, fără schema_version | P0 |
| Config | flat globals + `apply_overrides` mutează `globals()` — fragil, fără validare range, **fără tooltip-uri junior/senior** | P0 |
| HTTP API pentru dashboard | **LIPSEȘTE COMPLET** (nu există `core/health.py` server) | P0 |
| UI Tkinter | există `ui/app.py` 491 LoC dar nu respectă layout-ul cu 5 panouri și fără dual-tooltip pe câmpuri | P1 |
| Tests | director `tests/` există, neconfirmat coverage pe invarianți | P1 |

---

## 2. Invarianți încălcați (referință System Prompt §4)

### INV-01 [P0] Kill-switch NU este persistent

**Cerință SP §4:** "Kill switch persistent — nu se resetează la restart."
**Realitate:** `core/state_machine.py:122-138` `kill_switch()` doar setează `self._auto = False` și închide pozițiile. La restart bot, `_auto` revine la default → **trading reia automat**.

**Fix v2.0:**
- Fișier `data/kill_switch.json` cu `{"active": bool, "set_at": iso, "reason": str}`.
- Verificare la start în `main.py` ÎNAINTE de `sm.start()`.
- Citire la fiecare iterație FSM (`_run` loop).
- Token-protected toggle prin HTTP `POST /kill`.

### INV-02 [P0] `adaptive DD` mutează `config.RISK_PCT` global

`core/xauusd_profile.py:217` și `:235` fac `config.RISK_PCT = new_pct`. Side-effect ascuns: UI citește `config.snapshot()` și vede valoarea modificată, dar **utilizatorul nu a editat-o**. Restart bot → valoarea redusă persistă în `config_overrides.json` (dacă a fost salvată) sau revine la default fără ca utilizatorul să știe.

**Fix v2.0:** Introducem `EffectiveConfig` separat de `BaseConfig`. Adaptive layer scrie în `EffectiveConfig.risk_pct_effective`, NU în `config.RISK_PCT`. UI afișează ambele („Base: 0.5%, Effective: 0.25% (adaptive DD)").

### INV-03 [P0] Journal NU este append-only

`core/journal.py:119-125, 142-147` folosește `UPDATE trades SET …`. SP §4 cere "journal append-only, fsync după fiecare scriere". UPDATE-uri = rescriere → pierdere de istoric, vulnerabil la coruperi parțiale.

**Fix v2.0:** 
- Schemă duală: `trades` (denormalizat, citire rapidă) + `trade_events` (append-only: OPEN, PARTIAL_TP, SL_MOVE, CLOSE). Reconstrucția `trades` se face din event log.
- `PRAGMA journal_mode=WAL; synchronous=FULL` pe conexiune.
- Adaugă `schema_version INTEGER` în tabel meta.

### INV-04 [P0] Guard order nestandardizat, fără coduri NT_*

`core/state_machine.py` are gărzi împrăștiate în `_evaluate_and_trade` (linia ~300+). Mesajele apar ca `"No trade: outside UTC session now=16, allowed=1-22"` (vezi conversația user). SP §4 cere coduri standardizate: `NT_KILL`, `NT_SPREAD`, `NT_SESSION`, `NT_NEWS`, `NT_ML_LOW`, `NT_CONSENSUS_LOW`, `NT_RISK_DAILY`, `NT_COOLDOWN`, `NT_MAX_POS`, `NT_DEAD_MARKET`.

**Fix v2.0:** Modul `core/no_trade_codes.py` cu enum + mesaj human-readable; FSM emite `(code, detail)` tuplu spre journal + log.

### INV-05 [P0] Ordinea gărzilor — `spread` se verifică ÎNAINTE de `kill_switch`

`core/risk_manager.py:92` (`spread_ok`) este apelat din state_machine înainte de un check explicit de kill-switch (care nici nu există ca check, doar ca acțiune). Ordinea cerută SP §4:

```
1. kill_switch  → NT_KILL (terminal, oprește bot-ul)
2. spread       → NT_SPREAD
3. session      → NT_SESSION
4. news         → NT_NEWS
5. cooldown     → NT_COOLDOWN
6. daily loss   → NT_RISK_DAILY
7. max positions→ NT_MAX_POS
8. dead market  → NT_DEAD_MARKET
9. ensemble     → NT_CONSENSUS_LOW
10. ML threshold→ NT_ML_LOW
11. risk sizing (lots > 0) → NT_LOT_ZERO
12. EXECUTE
```

### INV-06 [P1] Cooldown nu diferențiază WIN vs LOSS

`core/risk_manager.py:55-67` `update_after_trade(pnl)` activează cooldown DOAR pe `MAX_CONSECUTIVE_LOSSES`. SP §4 cere cooldown diferențiat (după LOSS = mai lung, după WIN big = scurt pentru evitarea revenge-trade). Lipsește.

### INV-07 [P1] News blackout: doar BLACKOUT, lipsește `PRE_BLACKOUT`

`core/xauusd_profile.py:82-101` are o singură stare: "în blackout sau nu". SP §4 cere 3 stări:
- `OPEN` → totul permis
- `PRE_BLACKOUT` (T-15min) → no new entries, manage existing (tighten SL, exit la BE)
- `BLACKOUT` (T-0 până T+15min) → FLAT obligatoriu

### INV-08 [P1] Fără max exposure per symbol & per direction

`config.MAX_CONCURRENT_POSITIONS = 5` e singura limită numerică. Lipsește `MAX_EXPOSURE_LOTS_PER_SYMBOL`, `MAX_NET_DIRECTIONAL_LOTS` (pentru a evita 5 BUY simultan pe XAU = 5x risk).

### INV-09 [P1] Ensemble: fără weight decay pe strategii loser

`strategies/ensemble.py` folosește `s.stats.win_rate` static. SP §4 cere `w_i ← w_i * exp(-η * loss_i)` aplicat la fiecare trade închis. Mecanismul `AUTO_PRUNE_FROM_REPORT` (config.py:61) e batch (daily), nu online.

### INV-10 [P1] ML: fără calibrare Platt

`ml/model.py` returnează `_sigmoid(z)` direct ca probabilitate. În producție, logit raw e overconfident. SP §4 cere Platt scaling pe ultimele 500 predicții, recalibrat la fiecare 50 trades noi.

### INV-11 [P0] Lipsește server HTTP local (`core/health.py` îl menționează dar nu există)

`config.py` și docs vorbesc de health checks dar **nu există endpoint HTTP** pentru dashboard. Tot dashboard-ul web (Faza 4) depinde de el.

**Fix v2.0:** `core/api_server.py` stdlib `http.server`, bind `127.0.0.1:8765`, endpoints:
- `GET /health` `GET /state` `GET /config` `GET /journal/today` `GET /strategies`
- `POST /kill` (token din `BOT_API_TOKEN` env)
- CORS `Access-Control-Allow-Origin: http://localhost:5173` pentru dev dashboard.

### INV-12 [P0] Config fără validare range & fără dual tooltips

`config.py:131-169` `EDITABLE_FIELDS` mapează doar `name → type`. Lipsește:
- `min/max/step` pentru numerice (ex: `RISK_PCT ∈ [0.0001, 0.05]`).
- `enum` pentru string-uri (ex: `SL_TRAILING_MODE`).
- **`tooltip_junior`** (ce face, exemplu concret).
- **`tooltip_senior`** (de ce contează, trade-offs, formula matematică).

SP §6 cere fiecare câmp documentat dual-level.

### INV-13 [P2] Magic numbers în code

Greppă rapidă a relevat:
- `core/state_machine.py:264` `time.sleep(0.5)` — tick rate hardcodat.
- `core/state_machine.py:182` `if len(self._log) > 200` — log buffer hardcodat.
- `core/risk_manager.py:57` `if len(...) > 50` — recent results window hardcodat.
- `core/risk_manager.py:107` `* 1.5` — XAU spread fallback ratio hardcodat.
- `strategies/ensemble.py:46` `0.50 +` — ML cold-start floor hardcodat.

**Fix v2.0:** Mutate toate în `config.py` sub secțiunea `# --- Internals (rarely tuned) ---`.

### INV-14 [P1] `apply_overrides` mută `globals()` — fragil

`config.py:189-217` editează direct `globals()`. Probleme:
- Nu există reactivitate (modulele care fac `from config import RISK_PCT` la import time capturează valoarea **veche**).
- `core/risk_manager.py:42` folosește `config.RISK_PCT` (OK, citit la apel), dar dacă viitor refactor cache-uiește la `__init__`, breaks silent.

**Fix v2.0:** Înlocuim cu o instanță singleton `CONFIG: AppConfig` (dataclass cu `__setattr__` validat). Tot codul citește `CONFIG.risk_pct`. Atomic reload prin `CONFIG.reload_from_dict(...)`.

### INV-15 [P2] Fără leakage tests pentru features ML

`ml/features.py` și `ml/xau_features.py` construiesc features. Fără test care verifică că niciun feature nu folosește **bar curent neîncheiat** sau **viitor** (look-ahead bias).

---

## 3. Hartă de dependențe (actuală v1)

```text
main.py
 ├─► MT5Client ──► (broker)
 ├─► Journal (SQLite)
 ├─► AuditLogger ──► Journal
 ├─► Executor / PaperExecutor ──► MT5Client
 ├─► RiskManager ──► xauusd_profile (SpreadMonitor, sessions, news, adaptive DD ⚠️ mută config.RISK_PCT)
 ├─► DataFeed ──► MT5Client
 ├─► StrategyFactory ──► strategies/families/*  (~300 instanțe)
 ├─► Ensemble ──► xauusd_profile (ml_threshold_for, family_kind)
 ├─► OnlineLogReg + MLStore + Trainer
 └─► StateMachine ──► toate de mai sus ──► ui/app.py (Tkinter)
```

Probleme detectate în grafic:
1. `xauusd_profile` are state global (`_adaptive`) — anti-pattern pentru testare.
2. `state_machine` nu este FSM cu tranziții explicite, e un loop cu condiționale.
3. Niciun nod nu publică în HTTP API → dashboard imposibil.

---

## 4. Hartă de dependențe (țintă v2.0)

```text
main.py
 ├─► CONFIG (AppConfig singleton, validated, observable)
 ├─► KillSwitch (file-backed, fsync)
 ├─► EventBus (pub/sub intern)
 ├─► Journal v2 (append-only events + materialized trades view, WAL+fsync)
 ├─► AuditLogger ──► EventBus
 ├─► MT5Client / PaperExecutor
 ├─► RiskManager (pur, fără side-effects pe CONFIG) ──► EffectiveRiskCalculator (adaptive DD aici, NU în config)
 ├─► XauProfile (pur, stateless query API)
 ├─► NewsCalendar (3-state: OPEN/PRE/BLACKOUT)
 ├─► StrategyFactory + Ensemble (cu online weight decay)
 ├─► MLPipeline (OnlineLogReg + PlattCalibrator)
 ├─► StateMachine (FSM explicit cu Transition enum + guard chain)
 ├─► ApiServer (HTTP 127.0.0.1:8765) ◄── EventBus + snapshots
 └─► UI Tkinter v2.0 ──┐
                       └─► reads same snapshots ca ApiServer
                       
[external] Web Dashboard (TanStack) ──► HTTP ApiServer
```

---

## 5. Lista magic numbers de mutat în CONFIG

| Locație | Valoare | Nume propus v2.0 |
|---|---|---|
| `state_machine.py:264` | `time.sleep(0.5)` | `LOOP_TICK_SLEEP_SEC` |
| `state_machine.py:182` | `200` (log buffer) | `LOG_BUFFER_MAX_LINES` |
| `risk_manager.py:57` | `50` (recent results) | `RECENT_RESULTS_WINDOW` |
| `risk_manager.py:107` | `1.5` | `XAU_SPREAD_HARD_CEIL_RATIO` |
| `ensemble.py:46` | `0.50` | `ML_COLD_START_FLOOR` |
| `journal.py:163` | `n * 200` | `JOURNAL_AVG_TRADES_PER_DAY` |
| `state_machine.py:183` | `[-200:]` (log truncate) | (folosește `LOG_BUFFER_MAX_LINES`) |
| Multiple | `1e-5`, `1e-4` (point/tick fallbacks) | `DEFAULT_POINT_FALLBACK` |

---

## 6. Backlog prioritizat pentru v2.0 (TODO)

### P0 (blocking)
1. **INV-11** — Build `core/api_server.py` (HTTP local).
2. **INV-01** — `KillSwitch` persistent (file-backed).
3. **INV-03** — Refactor `Journal` v2 (append-only events + WAL).
4. **INV-04 + INV-05** — Standardizare coduri `NT_*` + guard chain reordonat în `StateMachine`.
5. **INV-02** — Scoate side-effects din `xauusd_profile` (no more `config.RISK_PCT = …`).
6. **INV-12** — `EDITABLE_FIELDS` → schema bogată cu `min/max/enum/tooltip_junior/tooltip_senior`.
7. **INV-14** — `CONFIG` singleton (dataclass) replacing `globals()` hack.

### P1
8. **INV-06** — Cooldown diferențiat WIN/LOSS.
9. **INV-07** — News blackout 3-stage (PRE/BLACKOUT/OPEN).
10. **INV-08** — `MAX_EXPOSURE_LOTS_PER_SYMBOL` + per-direction caps.
11. **INV-09** — Online ensemble weight decay.
12. **INV-10** — Platt calibration pe MLPipeline.
13. UI Tkinter v2.0 (5-panel + dual tooltips în Config Detail).

### P2
14. **INV-13** — Mutare magic numbers în CONFIG.
15. **INV-15** — Tests leakage features ML.
16. Diagrame FSM + dependențe în README v2.0.

---

## 7. Estimare efort (ord. de mărime)

| Fază | LoC nou/modif | Tool calls (aprox) |
|---|---|---|
| F2 — Core Python refactor | ~1500 nou + 800 modif | 15-20 |
| F3 — UI Tkinter v2.0 | ~1200 nou (ui/app.py rescris + widgets.py extins) | 6-8 |
| F4 — Dashboard Web TanStack | ~800 (7 rute + components) | 10-12 |
| F5 — Tests | ~600 (pytest) | 4-5 |
| F6 — Docs | ~400 (README + CHANGELOG + prompt EN refresh) | 3-4 |

**Total estimat:** 40-50 tool calls pentru a livra tot stack-ul v2.0.

---

## 8. Recomandare ordine implementare

```text
Iter A (foundation):   CONFIG singleton + KillSwitch + Journal v2     [P0 x3]
Iter B (FSM):          StateMachine FSM + NT_* codes + EffectiveRisk  [P0 x3]
Iter C (API):          api_server.py + EventBus + snapshots           [P0 x1]
Iter D (UI Tk):        ui/app.py v2.0 + dual tooltips                 [P1]
Iter E (Web dash):     src/routes/* TanStack + Query polling          [P1]
Iter F (polish):       cooldown/news/exposure/decay/Platt + tests     [P1+P2]
Iter G (docs):         README + CHANGELOG + prompt EN v2.0 regenerat  [P2]
```

---

**Confirmi auditul și pornesc Iter A (CONFIG singleton + KillSwitch + Journal v2)?**
Sau dacă vrei să tai/reordonezi vreun TODO, spune acum.
