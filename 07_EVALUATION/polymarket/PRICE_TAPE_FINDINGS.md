# RAPORT EMPIRIC: BANDA DE PREȚURI ȘI CELE DOUĂ CÂMPURI (PRICE_TAPE_FINDINGS.md)

> **Document de analiză empirică și investigație arhitecturală (Etapa 1)**  
> **Destinatar**: Owner  
> **Data**: 2026-09-14  
> **Ramură**: `antigravity/pm-overnight-campaign`  
> **Capturi de referință**:  
> - `07_EVALUATION/polymarket/fixtures/real_clob_prices_history_4504950.json`  
> - `07_EVALUATION/polymarket/fixtures/real_clob_prices_history_4504950.provenance.json`  
> - Eșantionul de 100 de piețe din `real_gamma_markets_resolved.json`  

---

## 1. Forma Reală a Răspunsului: Cheie cu Cheie

Răspunsul brut de la `https://clob.polymarket.com/prices-history?market=65804651831039537125654373265121851049085423295502084947322820720829800576360&interval=all` are următoarea structură:

```json
{
  "history": [
    {
      "t": 1789245614,
      "p": 0.9955
    },
    {
      "t": 1789246217,
      "p": 0.996
    }
  ]
}
```

### Detalii Cheie cu Cheie:
1. **Nivelul Superior**: Conține o singură cheie: `"history"`, a cărei valoare este o listă JSON (`list`) de obiecte dicționar. Nu există alte chei (fără `status`, fără `cursor`, fără `market_id`).
2. **Elementele din `history`**: Fiecare element este un dicționar cu exact două chei:
   - `"t"`: întreg (`int`), reprezentând timestamp-ul UNIX în **SECUNDE** (ex: `1789245614`).
   - `"p"`: număr în virgulă mobilă (`float`), reprezentând prețul observat (ex: `0.9955`).
3. **Unitatea lui `t`**:
   - `t = 1789245614` convertit ca secunde: `datetime.fromtimestamp(1789245614, tz=timezone.utc)` $ightarrow$ **`2026-09-12T20:40:14Z`**.
   - Aceasta se plasează exact în intervalul de viață al pieței `4504950`: deschisă la `acceptingOrdersTimestamp = 2026-09-12T20:19:23Z` și închisă la `closedTime = 2026-09-12 20:54:55+00`.
   - **Nu sunt milisecunde**: Dacă `1789245614` ar fi fost interpretat ca milisecunde, ar fi reprezentat `1970-01-21` (deplasare de 56 de ani).

---

## 2. Verificarea Presupunerilor din `historical_paper_replay.py`

Am confruntat codul din `historical_paper_replay.py` cu datele reale din rețea:

| Presupunere din Cod | Linie Cod | Presupunere în Cod | Realitate pe Fir (Wire) | Verdict |
|---|:---:|---|---|:---:|
| `payload.get("history")` | **L101-103** | Răspunsul este un dict cu lista `history[]` | Răspunsul este exact `{"history": [...]}` | **CONFIRMAT** |
| `raw["t"]` și `raw["p"]` | **L149-150** | Obiectele din listă au cheile `"t"` și `"p"` | Fiecare punct are exact cheile `"t"` și `"p"` | **CONFIRMAT** |
| Unitatea lui `t` | **L151** | `datetime.fromtimestamp(float(raw["t"]))` | `t` este în secunde UNIX | **CONFIRMAT** |
| Parametrul `fidelity=1440` | **L97** | Valoarea implicită `fidelity: int = 1440` | `fidelity=1440` cere bare zilnice (24h). Pentru piețe intraday (<24h), **întoarce `history: []` (0 puncte)**! | **INFIRMAT / DEFECT CRITIC** |
| POST `batch-prices-history` | **L110-114** | Așteaptă dict `{"history": {token_id: [...]}}` | Endpoint-ul acceptă `{"markets": [...], "interval": "all"}` și întoarce exact `{"history": {...}}` | **CONFIRMAT** |

### Defectul Major Descoperit la `fidelity=1440` (L97, L155):
- În `fetch_price_history` (L97) este hardcodat `fidelity: int = 1440` (1440 minute = 1 zi).
- Pentru piețe intraday (de ex. evenimente sportive sau piețe crypto orare care durează sub 24 de ore), CLOB nu poate construi o lumânare zilnică completă și returnează `history: []`.
- La linia 155-156, `build_market_bundle` conține:
  ```python
  if not bundle.price_history:
      raise ValueError("resolved market has no CLOB historical price points")
  ```
- **Consecință empirică**: Cu `fidelity=1440`, **0 din 100 de piețe rezolvate recente trec**! Cu `interval="all"` (sau granularitate de minute), **100 din 100 trec**.

---

## 3. Răspunsul la Întrebarea din Secțiunea 2: Proveniența Benzii de Prețuri

### Întrebarea:
> *Poartă răspunsul de istoric vreo marcă temporală în afară de `t`? Ce ar trebui să conțină `acquired_at` și `known_as_of`?*

### Răspunsul:
**NU.** Răspunsul CLOB conține strict lista de puncte `{"t": ..., "p": ...}`. Nu există nicio marcă temporală care să indice când a fost publicată observația, când a fost stocată pe server sau când a fost preluată.

### Analiza Celor Trei Variante de Modelare:

1. **Varianta 1: `observed_at` copiat în amândouă (`acquired_at = observed_at`, `known_as_of = observed_at`)**:
   - *Argument*: Pe o bursă electronică cu registru public centralizat (CLOB), momentul tranzacționării este simultan momentul în care prețul devine public pentru toți participanții.
   - *Ce se strică*: Șterge distincția epistemologică dintre momentul evenimentului și momentul achiziției datelor în depozit. Totuși, din punct de vedere al apărării temporale a backtest-ului, dacă decizia se ia la momentul $T$, un preț cu `known_as_of = observed_at < T` este strict consumabil înainte de $T$, păstrând logica de cauzalitate a pieței.
2. **Varianta 2: Momentul interogării (`datetime.now()`), marcat explicit ca atestare**:
   - *Argument*: Reflectă adevărul strict despre procesul de colectare al cercetătorului.
   - *Ce se strică*: **Distruge orice backtest.** Funcția `score_decisions()` și filtrele de scurgere temporală din `historical_paper_replay.py` refuză să execute decizii dacă punctele de preț au `known_as_of` ulterior deciziei. Dacă toate prețurile din 2026 poartă `known_as_of = 2026-09-14T00:06:00Z` (ora rulării scriptului), un agent plasat istoric la `2026-09-12` nu va avea voie să vadă niciun preț!
3. **Varianta 3: `None`, cu obligația ca apelantul să furnizeze atestarea**:
   - *Argument*: Onestitate matematică maximă — refuzul de a fabrica date.
   - *Ce se strică*: Sparge clasele frozen dataclass `HistoricalTapePoint` și `MarketSnapshot` unde `acquired_at` și `known_as_of` sunt declarate ca `str` non-nullable. În plus, `MarketSnapshot.validate()` și `_parse_datetime` aruncă imediat excepție la întâlnirea lui `None`.

> **Concluzie recomandată pentru decizia de dimineață**: Pentru observații de preț de piață lichidă, `known_as_of = observed_at` (Varianta 1) este singura variantă operațională care menține cauzalitatea pieței într-un backtest fără a respinge întregul corpus.

---

## 4. Trecerea Prin Filtre: Câte Piețe Rezolvate Au Bandă de Prețuri?

Am testat toate cele 100 de piețe rezolvate din captura `real_gamma_markets_resolved.json` împotriva endpoint-ului `batch-prices-history`:

1. **Test cu `fidelity=1440` (așa cum cere codul actual la L97)**:
   - **0 / 100 piețe** au puncte de preț.
   - Cauza: piețele din eșantion sunt intraday (durată < 24h); agregarea la 1440m nu produce nicio lumânare.
2. **Test cu `interval="all"` (fără agregare zilnică forțată)**:
   - **100 / 100 piețe** au istoric de prețuri valid și nevid!
   - Fiecare piață din eșantion are între 2 și 4+ puncte de preț reale înregistrate în timpul derulării ei.

---

## 5. Impactul Filtrului `enableOrderBook is True`

În `collect_resolved_bundles` din `historical_paper_replay.py`, există filtrul:
```python
if payload.get("enableOrderBook") is not True:
    continue
```
Am măsurat incidența acestui câmp pe ambele eșantioane capturate:
- În `real_gamma_markets_resolved.json`: **100 din 100 (100%)** au `enableOrderBook: true`.
- În `real_gamma_markets_page0.json` (active): **100 din 100 (100%)** au `enableOrderBook: true`.

> **Constatare**: Spre deosebire de ipoteza inițială că `enableOrderBook` ar elimina majoritatea piețelor, pe piețele moderne din 2026 (CLOB), `enableOrderBook` este activat pe **100% din piețe**. Nu acest filtru aruncă datele, ci parametrul `fidelity=1440` din cererea de istoric de prețuri.

---

## 6. Verdictul Porții către Etapa 2

- `history[]` **există** pe firul CLOB.
- Obiectele poartă cheile `"t"` și `"p"`.
- `t` este în **secunde UNIX** și se plasează exact în intervalul evenimentului.
- Cu parametrul corect (`interval="all"`), **100% din piețele rezolvate au bandă de prețuri utilizabilă**.

**POARTA CĂTRE ETAPA 2 ESTE DESCHISĂ.**
