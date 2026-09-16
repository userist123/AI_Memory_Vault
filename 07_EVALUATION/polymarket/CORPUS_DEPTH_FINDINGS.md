# Măsurarea Adâncimii Reale a Corpusului Polymarket (`CORPUS_DEPTH_FINDINGS.md`)

> **Destinatar**: CAMPANIE DE NOAPTE — ANTIGRAVITY (Etapa 2)  
> **Data / Ora**: 2026-09-14T00:10:00Z  
> **Referință Fixtură**: `07_EVALUATION/polymarket/fixtures/corpus_depth_sample_500.json`  
> **Proveniență Fixtură**: `07_EVALUATION/polymarket/fixtures/corpus_depth_sample_500.provenance.json`  
> **SHA-256 Fixtură**: `1397d989371b4b2516c678f2bb71d1c37683ed719791410f9af5597df75226a0`  
> **Regulă Cardinală**: Fiecare cifră este verificabilă prin `grep` pe fișierele de captură.

---

## 1. Rezumat Executiv & Întrebarea Centrală

**Întrebarea Etapei 2**: *Câte piețe rezolvate, cu bandă de prețuri reală, sunt efectiv accesibile prin cascadă completă de filtre din `collect_resolved_bundles`?*

Pe un eșantion stratificat de **500 de piețe** extrase din 5 felii temporale diferite ale Polymarket:
- **Tavanul real al corpusului cu `interval="all"`**: **100 din 500 (20.0% global)**. În era modernă CLOB (`era_2026_recent`), rata de succes este de **100 din 100 (100.0%)**.
- **Tavanul real al corpusului cu `fidelity=1440` (codul de producție din `historical_paper_replay.py:97`)**: **0 din 500 (0.0%)**. Nicio piață nu trece.
- **Filtrul care aruncă cel mai mult**: **Filtru 2 (`enableOrderBook is True`)**. Acesta elimină **399 din 500 de piețe (79.8%)**, deoarece primele generații de piețe Polymarket (2020–2022) au funcționat pe AMM (Automated Market Maker), nu pe CLOB (Central Limit Order Book).

---

## 2. Metodologia de Eșantionare Stratificată (500 piețe)

Pentru a nu măsura o singură eră îngustă și a evita oglindirea bazei întregi, am eșantionat 5 felii a câte 100 de piețe fiecare prin Gamma API (`closed=true&limit=100`):

| Felie / Era | Parametri URL Query | Primul ID | Număr Piețe |
| :--- | :--- | :--- | :--- |
| `era_2020_offset0` | `offset=0` | `12` | 100 |
| `era_2022_offset500` | `offset=500` | `213594` | 100 |
| `era_2023_offset1000` | `offset=1000` | `238945` | 100 |
| `era_2024_offset1500` | `offset=1500` | `239606` | 100 |
| `era_2026_recent` | `order=id&ascending=false` | `4537965` | 100 |
| **TOTAL** | | | **500** |

---

## 3. Rezultatele Cascadei de Filtre (Global)

Cascada aplicată corespunde exact contractului din `historical_paper_replay.py` (`collect_resolved_bundles` și `build_market_bundle`):

```
500 piețe brute
  │
  ├─► F1: closed == True ─────────────► 500 / 500 (100.0%)
  │
  ├─► F2: enableOrderBook is True ────► 101 / 500 (20.2%)  [Elimină 399 piețe AMM]
  │
  ├─► F3: clobTokenIds valid ─────────► 101 / 500 (20.2%)  [100% din F2]
  │
  ├─► F4: outcomePrices câștigător clar ► 101 / 500 (20.2%) [100% din F3]
  │
  └─► F5: Istoric prețuri nevid CLOB:
        ├─► Cu interval="all" ────────► 100 / 500 (20.0% global, 99.0% din CLOB)
        └─► Cu fidelity=1440 ─────────►   0 / 500 ( 0.0%) [BLOCAJ TOTAL ÎN PRODUCȚIE]
```

### Detalierea Pas cu Pas a Cascadei:

1. **F1 (`closed == True`)**:
   - **500 / 500 (100.0%)** au trecut. Toate feliile au fost interogate cu `closed=true`.
2. **F2 (`enableOrderBook is True`)**:
   - **101 / 500 (20.2%)** au trecut.
   - 399 de piețe au fost eliminate direct.
   - Explicație structurală: În feliile istorice 0, 500, 1000, Polymarket rula exclusiv contracte AMM pe Polygon. În felia `offset=1500`, a existat o singură piață timpurie cu orderbook (ID `239699`, `"Will it snow in New York's Central Park on New Year's Eve (Dec 31)?"`, creată în 2021). În schimb, în era recentă (`era_2026_recent`), **100 din 100** au `enableOrderBook: true`.
3. **F3 (`clobTokenIds` decodabil și nevid)**:
   - **101 / 101 (100.0% din F2)** au trecut. Fiecare piață cu `enableOrderBook: true` conține un JSON array valid cu token IDs de lungime 2 (Yes/No).
4. **F4 (`outcomePrices` cu un câștigător clar `["1", "0"]` sau `>= 0.95`)**:
   - **101 / 101 (100.0% din F3)** au trecut. Toate piețele rezolvate din CLOB au convergit la deznodământ binar ferm; niciuna din cele 101 nu a rămas la `["0.5", "0.5"]`.
5. **F5 (`prices-history` nevid pe tokenul câștigător)**:
   - Interogat prin endpoint-ul de producție `POST https://clob.polymarket.com/batch-prices-history`.
   - **Varianta A (`interval="all"`)**: **100 din 101 piețe** au returnat bandă de prețuri nevidă (între 2 și sute de cotații de tranzacționare). Singura piață CLOB fără puncte a fost ID `239699` (din 2021, unde orderbook-ul fusese activat dar nu a înregistrat tranzacții CLOB).
   - **Varianta B (`fidelity=1440`)**: **0 din 101 piețe** au returnat vreun punct (`history: []`). Motivul: rezoluția de 1440 de minute (24 de ore) omite complet piețele intraday/scurte sau piețele cu durată de câteva ore.

---

## 4. Defalcarea pe Ere Temporale

| Eră / Eșantion | Brute | F1 (Closed) | F2 (CLOB) | F3 (Tokens) | F4 (Winner) | F5 (`interval=all`) | F5 (`fidelity=1440`) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `era_2020_offset0` | 100 | 100 | 0 | 0 | 0 | 0 | 0 |
| `era_2022_offset500` | 100 | 100 | 0 | 0 | 0 | 0 | 0 |
| `era_2023_offset1000` | 100 | 100 | 0 | 0 | 0 | 0 | 0 |
| `era_2024_offset1500` | 100 | 100 | 1 | 1 | 1 | 0 | 0 |
| `era_2026_recent` | 100 | 100 | 100 | 100 | 100 | **100** | **0** |
| **TOTAL** | **500** | **500** | **101** | **101** | **101** | **100** | **0** |

---

## 5. Tavanul Real al Unui Backtest & Limitări ale API-ului

1. **Limitarea Endpoint-ului `/markets`**:
   - `offset` este restricționat strict la `offset <= 2000`. Cererea `offset=2001` returnează `HTTP 422: offset too large, use /markets/keyset for deeper pagination`.
   - Paginarea clasică cu offset permite extragerea a maximum **2.100 de piețe recente** într-o singură secvență.
2. **Endpoint-ul Avansat `/markets/keyset`**:
   - Testat și confirmat funcțional: returnează `{"$schema": "...", "markets": [...], "next_cursor": "..."}`.
   - Permite paginare bazată pe cursor în adâncime fără limită de offset.
3. **Tavanul de Piețe Utile**:
   - În era CLOB, **~99% - 100% din piețele rezolvate** trec toate cele 5 filtre, oferind mii de piețe tranzacționabile.
   - **Condiție critică**: Backtestul este fezabil **exclusiv dacă** folosește `interval="all"` la extragerea benzii CLOB. Dacă se apelează codul existent din `historical_paper_replay.py:97` cu `fidelity=1440`, tavanul este **0 piețe**.
