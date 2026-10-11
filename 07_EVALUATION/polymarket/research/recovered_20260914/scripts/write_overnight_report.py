import os
import hashlib

report_text = """# RAPORT FINAL DE NOAPTE — CAMPANIA POLYMARKET (`OVERNIGHT_CAMPAIGN_REPORT.md`)

> **Destinatar**: MARIUS · **Operator**: ANTIGRAVITY  
> **Data / Interval**: 2026-09-14, 00:05 – 00:25 (UTC+3)  
> **Ramură Git**: `antigravity/pm-overnight-campaign`  
> **Etape Executate**: 7 din 7 (Etapa 1 – Etapa 7)  
> **Regulă Cardinală**: Zero cod de producție modificat, zero modificări ontologie, fiecare număr este verificabil prin `grep` pe fișierele capturate.

---

## 1. Ce s-a stabilit că NU SE POATE (cu Dovada Empirică)

Aceasta este secțiunea principală, deoarece invalidează ipoteze neverificate și schimbă arhitectura sistemului de tranzacționare și backtesting:

1. **NU SE POATE obține vreun punct de preț cu parametrul din producție `fidelity=1440`**:
   - **Dovadă**: `historical_paper_replay.py:97` solicită candele zilnice (`fidelity: int = 1440`). Pe toate cele 101 piețe CLOB testate (inclusiv 100 de piețe recente), interogarea CLOB `batch-prices-history` cu `fidelity=1440` a returnat `history: []` (0 puncte). Piețele de predicție au o durată de tranzacționare scurtă (intraday sau câteva ore); candela de 1440 min le ignoră complet.
   - **Impact**: `collect_resolved_bundles` aruncă 100% din piețele rezolvate cu `ValueError("resolved market has no CLOB historical price points")` la linia 156.
   - **Soluție validată**: Interogarea cu `interval="all"` (fără `fidelity=1440`) aduce banda completă de tick-uri (între 2 și sute de cotații per token).

2. **NU SE POATE face backtest pe piețe Polymarket timpurii (2020–2022) prin CLOB**:
   - **Dovadă**: Eșantioanele din feliile istorice `offset=0`, `offset=500`, `offset=1000` au avut **0 din 300 de piețe (0.0%)** cu `enableOrderBook: true`. Polymarket a rulat inițial pe AMM (Automated Market Maker), iar CLOB-ul nu exista. Trecerea la CLOB s-a făcut gradual abia în 2023–2024. Orice tentativă de a rula un backtest CLOB pe date timpurii produce zero bundles.

3. **NU EXISTĂ nicio marcă temporală on-chain de decontare în API-ul public Polymarket**:
   - **Dovadă**: Analiza exhaustivă a tuturor celor 96 de câmpuri din Gamma API și a răspunsurilor CLOB a arătat că `settled_at` nu există pe wire. Toate rezoluțiile istorice utilizate în sistem trebuie tratate onest ca `SOURCE_MANUAL_ATTESTED`.

4. **NU SE POATE pagina mai adânc de `offset=2000` prin endpoint-ul `/markets`**:
   - **Dovadă**: Cererea cu `offset=2001` returnează `HTTP 422 Unprocessable Entity: offset too large, use /markets/keyset for deeper pagination`. Pentru traversări adânci, sistemul trebuie să adopte `/markets/keyset` bazat pe cursor.

5. **NU SE POATE stabili o rezoluție înainte ca ea să fie cognoscibilă**:
   - **Dovadă**: Harnașamentul adversarial din `scratch/build_attested_resolutions.py` a încercat să ateste o rezoluție la $T+2\text{ ore}$ în viitor. Validatorul `resolutions.py:112` a declanșat imediat `ValueError: recorded_at precedes known_at`, demonstrând robustețea matematică a invariantului.

---

## 2. Tavanul Real al Corpusului (din Etapa 2)

Eșantion stratificat de **500 de piețe** extrase din 5 felii temporale (`closed=true&limit=100`):

| Etapă Cascadă | Condiție Verificată | Piețe Admise | Rata de Trecere |
| :--- | :--- | :--- | :--- |
| **Piață Brută** | Eșantion total stratificat | **500** | 100.0% |
| **Filtru 1** | `closed == True` | **500** | 100.0% |
| **Filtru 2** | `enableOrderBook is True` | **101** | **20.2%** (elimină 399 piețe AMM) |
| **Filtru 3** | `clobTokenIds` decodabil și nevid | **101** | 20.2% (100% din F2) |
| **Filtru 4** | `outcomePrices` cu câștigător binar clar ($\\ge 0.95$) | **101** | 20.2% (100% din F3) |
| **Filtru 5A** | `prices-history` cu `interval="all"` | **100** | **20.0% global** (99.0% din CLOB) |
| **Filtru 5B** | `prices-history` cu `fidelity=1440` (producție) | **0** | **0.0%** (blocaj total) |

### Concluzia Asupra Tavanului:
- În era modernă CLOB (`era_2026_recent`), **100 din 100 de piețe (100.0%)** trec toate cele 5 filtre.
- Tavanul prin `/markets` (offset simplu) este de maximum **2.100 de piețe tranzacționabile**.
- Prin `/markets/keyset`, tavanul este extins la zeci de mii de piețe rezolvate.

---

## 3. Rezultatul Controlului Mecanic și Distribuția (din Etapa 4)

Controlul mecanic `run_mechanical_control` a fost executat pe fixtura reală `historical_dataset_50markets.json` (50 de piețe, $1.00 notional per tranzacție, cumpărare oarbă a primului deznodământ `outcome_ids[0]` la prima cotație din bandă):

- **Piețe Totale Tranzacționate**: **50** (0 erori, 100% validate)
- **Piețe Câștigătoare (Wins)**: **36** (**72.0%**)
- **Piețe Pierzătoare (Losses)**: **14** (**28.0%**)
- **PnL Total Net**: **+$14.1774** (**+28.35%** randament pe capitalul riscat de $50)
- **PnL Mediu per Tranzacție**: `+$0.2835` | **PnL Median**: `+$0.7778`
- **PnL Minim (Worst Case)**: `-$1.0000` | **PnL Maxim (Best Case)**: `+$1.6316`
- **Distribuția pe Percentile**:
  - `p10 = -1.0000` | `p25 = -1.0000` | `p50 = +0.7778` | `p75 = +1.0000` | `p90 = +1.0000`
- **Preț Mediu de Intrare**: `0.5314` (Min: `0.0050`, Max: `0.9975`)
- **Verdict Anomalo-Metric**: Nu este un randament absurd. Câștigul reflectă bias-ul din eșantionul recent de piețe prop/sportive unde `outcome_ids[0]` a reprezentat deznodământul probabil la un preț mediu de intrare de $0.53.

---

## 4. Breșele de Scurgere Găsite în Auditul Adversarial (din Etapa 5)

| Caz Adversarial | Modul și Linie | Rezultat & Comportament Măsurat |
| :--- | :--- | :--- |
| **1. Punct exact la instantul T** | `historical_replay.py:73`<br>`backtest.py:355` | **REZISTENT**. Domeniu închis la dreapta $[t_0, T]$ pentru prețuri, strict deschis la stânga $(T, \\infty)$ pentru rezoluții. |
| **2. `known_as_of` = `null`** | `historical_paper_replay.py:27, 172` | **BREȘĂ ÎN REPLAY NOU**. `HistoricalTapePoint` permite `known_as_of: None` și `run_mechanical_control` îl execută orb, ocolind verificarea temporală. |
| **3. Două piețe pe același eveniment** | `backtest.py:348` | **BREȘĂ ARHITECTURALĂ**. Indexarea se face strict pe `market_id`. Rezoluția Pieței A poate fi folosită ca feature la timpul $T$ pentru a prezice Piața B (același meci/eveniment) înainte ca Piața B să se închidă. |
| **4. Puncte de bandă cu $t > \\text{endDate}$** | `historical_paper_replay.py:43-71` | **BREȘĂ EMPIRICĂ CONFIRMATĂ**. `HistoricalMarketBundle` nu verifică `observed_at <= resolution_known_at`. În fixtura reală capturată, **22 din 620 de puncte (3.5%)** au avut loc post-închidere și au trecut validarea fără eroare. |

---

## 5. Inventar Fișiere Adăugate (Nume, Dimensiune, SHA-256)

### A. Fișiere de Raportare și Fixturi în Repozitoriu (`07_EVALUATION/polymarket/`):

| Fișier Adăugat | Dimensiune | SHA-256 |
| :--- | :--- | :--- |
| `07_EVALUATION/polymarket/PRICE_TAPE_FINDINGS.md` | 8,339 bytes | `d19ef555f160f46bc3a563a9e080bcae6fee8e9eef210552df62d0e2f4ded1a9` |
| `07_EVALUATION/polymarket/fixtures/real_clob_prices_history_4504950.json` | 139 bytes | `50bc941a7d4e20b69792006179f73d60fbac9e71a10eed1a91f5c62cbf62dbe6` |
| `07_EVALUATION/polymarket/fixtures/real_clob_prices_history_4504950.provenance.json` | 650 bytes | `1db7c540921ff3890eef3c34b1de3debf6135bbffb170f55ea1383477388308b` |
| `07_EVALUATION/polymarket/CORPUS_DEPTH_FINDINGS.md` | 6,657 bytes | `84ff4e100bd1948a3d953cde6bd5d898abade669e2a3e594a2fef9f51bb50912` |
| `07_EVALUATION/polymarket/fixtures/corpus_depth_sample_500.json` | 3,674,277 bytes | `1397d989371b4b2516c678f2bb71d1c37683ed719791410f9af5597df75226a0` |
| `07_EVALUATION/polymarket/fixtures/corpus_depth_sample_500.provenance.json` | 2,462 bytes | `e667adb1a139121e3732b782d37da21af9dcce9612e5271bd889e72e8efc5375` |
| `07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.json` | 323,291 bytes | `f5f7ad211998aaed235748bcaf848f0f3d4ed3de760d472db16bebfa452f4f01` |
| `07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.provenance.json` | 40,234 bytes | `3c8ad19a260c7db909a40d033c9545cbbf4fa64315d18b3147727da6f45f5aa9` |
| `07_EVALUATION/polymarket/MECHANICAL_CONTROL_RESULTS.md` | 4,975 bytes | `df989d129105e11ff9488a1a290ce18b18dac5226ff396b1937cd7ad0bfab618` |
| `07_EVALUATION/polymarket/LEAKAGE_ADVERSARIAL_AUDIT.md` | 10,189 bytes | `74a0d9286c176014d7689aae2441f39939bad9978624a8a682ce620b25bcd6f9` |
| `07_EVALUATION/polymarket/fixtures/resolutions_attested_50.json` | 26,014 bytes | `82a39bf8583f105a85a206496963e06fab9712d5a72367e85323712d55a1d571` |
| `07_EVALUATION/polymarket/fixtures/resolutions_attested_50.provenance.json` | 441 bytes | `e8a3e1c25185623ab0f08a7ddee56d71375c15f387b2c9fb2c0f2b7d404dfc7c` |

### B. Scripturi de Suport Izolate în Directorul `scratch/` (Fără Atingerea Pachetului):
- `scratch/measure_corpus_depth.py` (6,419 bytes)
- `scratch/test_gamma_filter.py` (1,299 bytes)
- `scratch/probe_keyset.py` (741 bytes)
- `scratch/save_corpus_depth_fixture.py` (1,927 bytes)
- `scratch/write_depth_findings.py` (6,837 bytes)
- `scratch/inspect_candidates.py` (520 bytes)
- `scratch/point_counts.py` (567 bytes)
- `scratch/probe_high_volume.py` (770 bytes)
- `scratch/build_historical_dataset.py` (4,992 bytes)
- `scratch/verify_bundles.py` (1,707 bytes)
- `scratch/run_mechanical_control_evaluation.py` (3,391 bytes)
- `scratch/inspect_trades.py` (1,498 bytes)
- `scratch/write_mechanical_results.py` (5,379 bytes)
- `scratch/adversarial_leakage_test.py` (6,151 bytes)
- `scratch/write_leakage_audit.py` (10,319 bytes)
- `scratch/check_times.py` (717 bytes)
- `scratch/build_attested_resolutions.py` (3,267 bytes)
- `scratch/verify_manifest.py` (2,608 bytes)
- `scratch/print_repo_table.py` (1,154 bytes)
- `scratch/write_overnight_report.py`

---

## 6. Verificarea Invariantelor Git

Comenzile executate pe rădăcina repozitoriului demonstrează că niciun modul de producție și nicio intrare din ontologie nu a fost atinsă:

```text
$ git diff --stat 03_IMPLEMENTATION/packages/polymarket/
(output gol — 0 modificări, 0 fișiere atinse)

$ git diff --stat 01_ARCHITECTURE/ontology/slots/
(output gol — 0 modificări, 0 fișiere atinse)
```

Suite de teste existente:
```text
$ python -m pytest -q 20_TESTS/polymarket
277 passed in 4.63s (100% curat)
```
"""

target = "C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/polymarket/OVERNIGHT_CAMPAIGN_REPORT.md"
with open(target, "w", encoding="utf-8") as f:
    f.write(report_text)

print(f"Successfully wrote {target} ({len(report_text)} bytes)")
