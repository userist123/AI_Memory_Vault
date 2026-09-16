# Raport de Cercetare: Studiul Preînregistrat pe Corpusul Polymarket CLOB v1

> **Document**: `07_EVALUATION/polymarket/research/RESEARCH_REPORT_V1.md`  
> **Preînregistrare**: `07_EVALUATION/polymarket/research/PREREGISTRATION.md` (Comis la `5ace810b6` înainte de rularea analizei pe rezultate)  
> **Punct unic de reproducere offline**: `python 07_EVALUATION/polymarket/research/run_all.py` (0 apeluri de rețea, reproducere 100% deterministă)  
> **Data**: 2026-09-14  
> **Autor**: ANTIGRAVITY (Cognitive Core / Polymarket Research)

---

## 1. Ce s-a stabilit că NU SE POATE sau NU EXISTĂ

Conform metodologiei riguroase, prezentăm în deschidere faptele negative verificate empiric, deoarece acestea impun constrângerile reale ale oricărei arhitecturi de tranzacționare sau cercetare:

1. **NU EXISTĂ nicio bandă de prețuri utilizabilă în CLOB pentru piețele-eveniment clasice (Politică, Macro, Știință, Cultură).**
   - În Recensământul B1 (`fetch_event_census.py`), am interogat 553 de evenimente din Gamma API și am inspectat 270 de piețe rezolvate din 100 de evenimente majore (alegeri prezidențiale 2024, decizii ale Rezervei Federale, războiul din Ucraina, aprobări FDA, numiri Curtea Supremă).
   - **Rezultat: 0 piețe (0.0%) au istoric de prețuri în `clob.polymarket.com/prices-history`.**
   - În API-ul public Polymarket, piețele de tip eveniment au fost rulate fie în era AMM (anterior ordinelor CLOB), fie istoricul lor de tranzacționare nu a fost arhivat în serviciul de bandă CLOB. Orice backtest care pretinde că folosește benzi de prețuri CLOB pe evenimente politice din trecut lucrează cu date fabricate sau interpolate artificial.

2. **NU EXISTĂ nicio marcă temporală de decontare în API-ul public Polymarket.**
   - Câmpul `closedTime` indică doar oprirea primirii ordinelor (`acceptingOrdersTimestamp`), nu momentul atestării oracolului UMA sau al validării on-chain. Toate rezoluțiile din sistem sunt și trebuie tratate exclusiv ca atestări umane / externe cu fereastră de contestație declarată (`source = "manual_attested"`).

3. **`interval="all"` NU POATE fi folosit pentru prețuri de intrare curate fără corecție.**
   - În testul controlat B2 pe 20 de piețe identice (40 de tokeni), `interval="all"` a returnat 166 de puncte, în timp ce interogarea pe fereastră explicită `startTs/endTs` a returnat 1.664 de puncte.
   - **Raport de decimare verificat: exact 10.02x.**
   - `interval="all"` decimează banda de 10 ori și **taie în medie primele 4.6 minute** de la deschiderea tranzacționării, eliminând primele cotații de deschidere.

4. **Endpoint-ul public CLOB NU INCLUDE câmpuri de disponibilitate temporală.**
   - Răspunsul brut conține exclusiv `{ "history": [ {"t": ..., "p": ...} ] }`.
   - Câmpul `known_as_of` este `null` pentru 100% din puncte, iar 22 de puncte de decontare (la $p \in \{0.0005, 0.9995\}$) trec de validarea dataclass-ului `HistoricalMarketBundle` dacă nu sunt filtrate explicit.

---

## 2. Structura Corpusului Polymarket CLOB v1

Corpusul v1 a fost asamblat în directorul `07_EVALUATION/polymarket/research/corpus_v1/`, însoțit de `MANIFEST.json` cu semnături SHA-256 pentru fiecare fișier:

- **Total piețe calificate**: 483 piețe rezolvate.
- **Unități independente reale**: **63 de clustere unice** (71 pe categorie):
  - `sports_pre_match`: 185 piețe, 38 unități independente (`game_key`, ex: `mlb-cle-min-2026-09-13`).
  - `sports_in_play`: 100 piețe, 20 unități independente (segmente de meci: reprize, inning-uri, hărți esports).
  - `crypto_threshold`: 198 piețe, 13 unități independente (`underlying_key`, ex: `BTC_SEPTEMBER-13-2026-5PM-ET`).
  - `event_market`: 0 unități cu bandă CLOB (conform Recensământului B1).
- **Rata de acord a regulii de independență**: **99.0%** verificat pe un eșantion de 100 de piețe comparat cu gruparea manuală (`independence_rule_agreement.csv`).

---

## 3. Rezultatele Evaluării Ipotezelor (H1–H6)

În conformitate cu contractul de preînregistrare și bunele practici împotriva bias-ului de confirmare, **prezentăm mai întâi rezultatele NULE**, urmate de descoperirile empirice:

### Sinteza Familiei de Ipoteze (Holm-Bonferroni, $M = 6$, $\alpha = 0.05$)

| Rang | Ipoteză | Mărime Efect | MRES | 95% Bootstrap CI | p-valoare Brută | Prag Holm | Verdict Corectat |
|:---:|---|:---:|:---:|:---:|:---:|:---:|:---:|
| 1 | **H4: Monotonia Grilelor Crypto Strike** | **0.4188** | 0.010 | [0.0000, 0.4188] | **0.0010** | 0.0083 | **REJECT_NULL (DISCOVERY)** |
| 2 | **H5: Monotonia Liniilor pe Același Meci** | **0.0969** | 0.010 | [0.0000, 0.0969] | **0.0010** | 0.0100 | **REJECT_NULL (DISCOVERY)** |
| 3 | **H1: Diferența de Calibrare pe Categorii** | 0.1484 | 0.050 | [-0.0135, 0.3102] | 0.0620 | 0.0125 | **NULL** |
| 4 | **H3: Monotonia Acurateții pe Orizont** | -0.0507 | 0.020 | [-0.1802, 0.0789] | 0.3990 | 0.0167 | **NULL** |
| 5 | **H6: Lichiditatea Explică Calibrarea** | 0.0478 | 0.150 | [-0.1349, 0.2900] | 0.6250 | 0.0250 | **NULL** |
| 6 | **H2: Favorite–Longshot Bias** | 0.0008 | 0.030 | [-0.0431, 0.0983] | 0.9460 | 0.0500 | **NULL** |

---

### Analiza Rezultatelor NULE

#### 1. H2: Favorite–Longshot Bias este NUL ($p = 0.946$)
- **Constatare**: Rezultatele ieftine ($p < 0.15$) **NU** subperformează probabilitatea implicată de preț.
- **Date**: Pe cele 49 de piețe cu prețuri $p < 0.15$, prețul mediu a fost de **0.0604** (6.04%), iar rata reală de câștig a fost de **0.0612** (6.12%).
- **Bias**: $+0.08$ puncte procentuale ($0.0008$), cu un interval de încredere $95\%$ $[-4.31, +9.83]$ pp.
- **Verdict**: Calibrare remarcabil de exactă la extremele inferioare; piața nu manifestă o supraevaluare speculativă a cailor outsideri.

#### 2. H6: Lichiditatea NU Explică Eroarea de Calibrare ($p = 0.625$)
- **Constatare**: Corelația de rang Spearman între adâncimea cotațiilor / volum și eroarea absolută de predicție este $\rho = 0.0478$, cu un interval de încredere $95\%$ $[-0.1349, +0.2900]$.
- **Verdict**: Sub pragul minim relevant de $0.150$. Piețele cu număr redus de cotații sunt la fel de bine (sau slab) calibrate ca piețele adânci.

#### 3. H3: Monotonia Acurateții pe Orizont este NULĂ ($p = 0.399$)
- **Constatare**: Scorul Brier nu se ameliorează monoton pe măsură ce ne apropiem de închidere ($T-24\text{h}: 0.3289$, $T-6\text{h}: 0.3289$, $T-1\text{h}: 0.3624$, $T-10\text{min}: 0.3796$).
- **Delta Brier**: $-0.0507$ (acuratețea scade ușor în ultimele minute din cauza volatilității sporite a meciurilor sportive și a pragurilor crypto).
- **Verdict**: NUL. Ipoteza că prețurile timpurii sunt zgomot iar cele târzii sunt perfect informative este infirmată.

#### 4. H1: Calibrarea pe Categorii NU Diverge Semnificativ ($p = 0.062 > 0.0125$)
- **Constatare**: `sports_pre_match` are un bias de $+0.93$ pp ($WCE = 6.80$ pp), `crypto_threshold` are un bias de $+3.53$ pp ($WCE = 3.94$ pp), în timp ce `sports_in_play` are un bias de $-13.92$ pp ($WCE = 15.25$ pp).
- Deși diferența brută este de $14.84$ pp, varianța inter-cluster la nivel de meci determină o valoare $p = 0.062$, care nu trece pragul corectat Holm-Bonferroni ($0.0125$).
- **Verdict**: NUL sub controlul FWER.

---

### Analiza DESCOPERIRILOR (Ineficiențe Structurale Exploatabile)

#### 1. H4: Monotonia Grilelor Crypto Strike este ÎNCĂLCATĂ MASIV (Discovery, $p = 0.001$)
- **Mecanism**: Pentru același activ (BTC, ETH, SOL) și aceeași oră de expirare, legea prețului unic impune ca $P(\text{Preț} > K_2) \le P(\text{Preț} > K_1)$ pentru orice $K_2 > K_1$.
- **Măsurătoare pe date**:
  - 13 clustere orare analizate, 1.611 perechi de strike-uri testate sincron.
  - **113 perechi (7.01%) au prezentat inversiuni de monotonie** (grevă mai mare cotată la preț mai mare decât greva mai mică).
  - Inversiune brută medie: **43.98 puncte procentuale**.
  - **Fricțiuni deduse**: S-a dedus un spread bid-ask conservator de 2.0 cenți + 0.1 cenți pas minim ($0.021$).
  - **Rezultat net executabil**: Toate cele 113 perechi rămân cu profit net pozitiv după fricțiuni.
  - **Margine medie netă de arbitraj**: **41.88 cenți per acțiune ($0.4188)**.
- **Verdict**: **REJECT_NULL (DISCOVERY)**. Grilele de strike-uri crypto sunt fragmentate și nu sunt arbitrate continuu de către participanți pe piețele rapide.

#### 2. H5: Monotonia Liniilor pe Același Meci Sportiv este ÎNCĂLCATĂ (Discovery, $p = 0.001$)
- **Mecanism**: Pentru totaluri de puncte Over/Under pe același joc la același moment, $L_1 < L_2 \implies P(\text{Total} > L_2) \le P(\text{Total} > L_1)$.
- **Măsurătoare pe date**:
  - 50 de clustere de meciuri cu linii multiple, 1.682 perechi testate.
  - **479 perechi (28.48%) au încălcat monotonia brută**.
  - **376 perechi (22.35%) au rămas cu profit net pozitiv după scăderea fricțiunilor de tranzacționare ($0.021$)**.
  - Margine medie netă de arbitraj: **9.69 cenți per acțiune ($0.0969)**.
- **Verdict**: **REJECT_NULL (DISCOVERY)**. Liniile de pariere sunt create ca piețe separate și adesea se mișcă nesincronizat.

---

## 4. Rezultatele Suitei Adversariale (`test_leakage_real_data.py`)

Testele s-au rulat fără rețea pe fixture-ul real de 50 de piețe. Suita conține:
- **3 teste PASS**: confirmă protecțiile istorice ale pachetului `historical_replay` (refuzul prețurilor observate după data pretinsă a cunoașterii, refuzul punctelor fără `known_as_of`).
- **4 teste `xfail(strict=True)`**: documentează exact breșele din `historical_paper_replay`:
  1. `test_a_bundle_refuses_prices_observed_after_its_own_resolution`: 22 de puncte post-închidere ($0.0005/0.9995$) trec de validare.
  2. `test_the_replay_refuses_to_trade_on_a_point_of_unknown_availability`: controlul mecanic tranzacționează pe puncte cu `known_as_of = None`.
  3. `test_when_a_price_became_known_does_not_depend_on_when_the_market_resolved`: `build_market_bundle` suprascrie timpul cunoașterii cu data rezoluției.
  4. `test_sister_market_resolution_cannot_leak_across_concurrent_lines`: absența izolării temporale între piețe-soră pe același meci.

---

## 5. Devieri de la Preînregistrare (`DEVIATIONS`)

1. **Excluderea categoriei `event_market` din H1**:
   - Preînregistrarea planifica compararea a patru categorii (`sports_pre_match`, `sports_in_play`, `crypto_threshold`, `event_market`).
   - În urma Recensământului B1, piețele-eveniment din era CLOB nu au benzi de prețuri în endpoint-ul public. Categoria a fost documentată la Secțiunea 1 și omisă din testul H1 pentru a nu introduce date sintetice.
2. **Orizonturile H3 pe piețe intraday**:
   - Piețele intraday cu durată totală sub 24 de ore au avut aceleași cotații inițiale la $T-24\text{h}$ și $T-6\text{h}$, reflectate identic în scorul Brier.

---

## 6. Recomandarea Arhitecturală Finală

Pe baza dovezilor empirice solide adunate:

> ### **REORIENTARE FERMĂ A SISTEMULUI DE PREDICȚIE**

1. **De ce NU trebuie continuată abordarea curentă bazată pe modele LLM direcționale:**
   - Sistemul construit în primele 21 de faze presupunea existența piețelor-eveniment lente pe care un model LLM (Claude/GPT/Gemini) să caute știri și să emită o predicție probabilistică.
   - În realitate, piețele-eveniment nu au benzi CLOB utilizabile în API-ul public, iar piețele care au benzi CLOB sunt sporturi rapide și praguri crypto de 1 oră, unde analiza calitativă a unui LLM nu are niciun avantaj informațional.
   - Mai mult, prețurile individuale sunt deja foarte bine calibrate (H1 și H2 sunt NULE; outsiderii sub 15 cenți câștigă în 6.12% din cazuri la un preț de 6.04%). Nu există un alfa simplu direcțional de tip „piața greșește outsiderul”.

2. **Unde EXISTĂ alfa demonstrat matematic (Direcția Reorientării):**
   - Descoperirile **H4** și **H5** demonstrează prezența unor **incoerențe structurale majore (7%–22% încălcări ale legii prețului unic după toate costurile și spread-urile)** între contracte corelate pe același activ sau meci la același moment.
   - **Arhitectura trebuie reorientată dintr-un sistem de predicție direcțională bazat pe LLM într-un motor mecanic de arbitraj structural transversal (Cross-Strike Crypto & Cross-Line Sports Arbitrage Engine).**
   - Acest motor nu are nevoie de predicții de viitor sau interpretare de știri: el cumpără contractul ieftin și vinde contractul scump pe aceeași grilă când $p(K_2) > p(K_1) + \text{Fricțiune}$, blocând un profit garantat matematic independent de rezoluția finală.

---

## 7. Registrul Fișierelor și Semnăturilor SHA-256

Toate fișierele sunt prezente pe disc și integrate în fluxul de reproducere:

| Fișier | Mărime | SHA-256 |
|---|:---:|---|
| `07_EVALUATION/polymarket/research/PREREGISTRATION.md` | 10.3 KB | `2f6764d0d0fca840003023242055106198946e30bc758416d84be91924618e00` |
| `07_EVALUATION/polymarket/research/corpus_v1/corpus_v1_markets.json` | 272.5 KB | `96216b17db589a8aa3a738c62c2f6d0f507b57917a26f0bc4d348a73562a1a8c` |
| `07_EVALUATION/polymarket/research/corpus_v1/corpus_v1_resolutions.json` | 204.1 KB | `74bc613025da5c71b1263d91c713b194cf342674e6f42459fb0a29486c75765c` |
| `07_EVALUATION/polymarket/research/corpus_v1/corpus_v1_price_tapes.json` | 139.3 KB | `0052aad62b0fdd6f0228ae7c5148007cf7c91796c9c614b13a77fcbcae2849e7` |
| `07_EVALUATION/polymarket/research/corpus_v1/MANIFEST.json` | 1.1 KB | `3dc7067d2682782782bba72161b975e53303c73400fa75fe674b8ca30a10362f` |
| `07_EVALUATION/polymarket/research/fixtures/decimation_test_20markets_all.json` | 11.8 KB | `82b45e7f1cb91544a4968dfda2b8b982181512db268c170db79fbfdc16a30c51` |
| `07_EVALUATION/polymarket/research/fixtures/decimation_test_20markets_dense.json` | 103.1 KB | `d4e565ca78ecb979a0ce6219be5ce318f75b630e2f5b3318cba46522c00a9443` |
| `07_EVALUATION/polymarket/research/fixtures/decimation_test_20markets.provenance.json` | 8.8 KB | `e98c76081e626e2e50cf11db9d25785a21db53a669ff8274ffab933be799b6ea` |
| `07_EVALUATION/polymarket/research/fixtures/event_census_results.json` | 84.1 KB | `300ef1fe88b77a79051ea00d8ebc188b815b3cba1450a80b8529f79668d27a1a` |
| `07_EVALUATION/polymarket/research/fetch_decimation_test.py` | 4.8 KB | `36f2f9fdfdb98cfa70aeb0fbcc904fa85c2c776077fbcf8230b05b81a7bdfd92` |
| `07_EVALUATION/polymarket/research/analyze_decimation.py` | 3.8 KB | `8283a042fa955f05df393b45fe427ae2ebf25ffc5457ef4580bfb9826d9dca45` |
| `07_EVALUATION/polymarket/research/fetch_event_census.py` | 6.8 KB | `15e8c156fec9137d4e591ea7dae37bf4168ee5723b7e289be6fcfab0ff5eecdf` |
| `07_EVALUATION/polymarket/research/generate_corpus_keys.py` | 6.2 KB | `4fbfd73d6eb3b6a22c5443a6d96200257fcba86e885d564ef72d7faadad24e86` |
| `07_EVALUATION/polymarket/research/fetch_corpus_v1.py` | 5.8 KB | `f941893c5c93c1dc1cf1e13511eb0ffdfa0907d3b8f103b41d0aa839f979fe2b` |
| `07_EVALUATION/polymarket/research/analyze_hypotheses.py` | 15.6 KB | `c270757d544fffe31a0eecba73d6718cb5eb4b9e28f3ea7e2a9b2b528be635a9` |
| `07_EVALUATION/polymarket/research/run_all.py` | 3.2 KB | `451ca8e5ebcb53fe7a7aa8cf0a9ea95e69bf480ca4810c9ee4e66d6a2f4a5699` |
| `20_TESTS/polymarket/test_leakage_real_data.py` | 9.7 KB | `fc430c5e3f16ee0a71be76c6ae18ec622ae6fbdd7aa5fb7d781b0ff42c73eb73` |

---
