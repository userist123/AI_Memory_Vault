# Auditul Adversarial al Scurgerii Temporale pe Date Reale Polymarket (`LEAKAGE_ADVERSARIAL_AUDIT.md`)

> **Destinatar**: CAMPANIE DE NOAPTE — ANTIGRAVITY (Etapa 5)  
> **Data / Ora**: 2026-09-14T00:20:00Z  
> **Set de Date Testat**: `07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.json`  
> **Fișier Proveniență**: `07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.provenance.json`  
> **Harnașament Adversarial**: `scratch/adversarial_leakage_test.py`  
> **Regulă Cardinală**: Fiecare funcție, fișier, linie și comportament este documentat exact așa cum execută codul pe disc. Nicio reparație nu este aplicată în producție.

---

## 1. Rezumat Executiv

S-a efectuat un audit de securitate adversarial asupra sistemului de replay temporal, evaluare și prevenire a scurgerilor de informație (`LeakageProof`, `HistoricalReplay`, `phase12_temporal_contract`, `resolutions.py`, `backtest.py`, `historical_paper_replay.py`).

Au fost construite și executate **4 atacuri adversariale țintite** pe datele reale din Etapa 3.

### Matricea de Rezultate a Atacurilor:

| Caz Adversarial | Modul / Funcție / Linie | Comportament Efectiv al Codului | Verdict Securitate |
| :--- | :--- | :--- | :--- |
| **Cazul 1: Punct la exact momentul T** | `historical_replay.py:73`<br>`backtest.py:297,355`<br>`phase12_temporal_contract.py:40,46` | Punctele cu `observed_at == T` sunt **admise** (interval închis la dreapta $[t_0, T]$). Rezoluțiile cu `known_at == T` sunt **respinse ferm** (interval strict deschis la stânga $(T, \infty)$). | **REZISTENT** (asimetrie intenționată, dar risc de preț post-eveniment la $T$) |
| **Cazul 2: `known_as_of` / `known_at` este `null`** | `resolutions.py:214-219`<br>`historical_paper_replay.py:26-40, 172-186` | În `resolutions.py`, este **respins cu excepție**.<br>În `historical_paper_replay.py`, este **acceptat tacit** și executat în `run_mechanical_control`. | **BREȘĂ ÎN HISTORICAL_PAPER_REPLAY** |
| **Cazul 3: Două piețe pe același eveniment** | `backtest.py:348`<br>`resolutions.py:78, 148` | Sistemul indexează exclusiv pe `market_id`. Rezoluția pieței A consumată înainte de decizia pieței B trece toate filtrele. | **BREȘĂ ARHITECTURALĂ DE COERENȚĂ DE EVENIMENT** |
| **Cazul 4: Punct în bandă cu `t > endDate`** | `historical_paper_replay.py:43-71` | `HistoricalMarketBundle` nu stochează `endDate` și nu verifică dacă `observed_at <= resolution_known_at`. **22 de puncte reale** din captură au $t > \text{closedTime}$ și trec validarea. | **BREȘĂ EMPIRICĂ CONFIRMATĂ (22 puncte reale)** |

---

## 2. Analiza Detaliată a Celor 4 Cazuri de Atac

---

### Cazul 1: Punctul din Bandă la Exact Momentul T (Regula de Graniță)

**Întrebare**: *Cine primește un punct la momentul $T$? Este intervalul semi-deschis sau închis?*

#### 1. Ce face codul efectiv:
- **În `historical_replay.py:72-74`**:
  ```python
  @staticmethod
  def _available(observed_at: str, acquired_at: str, known_as_of: str, cutoff) -> bool:
      return all(_parse_datetime(value, field_name="replay timestamp") <= cutoff for value in (observed_at, acquired_at, known_as_of))
  ```
  Folosește operatorul `<= cutoff`. Un punct de preț observat la exact `cutoff` este **admis**.
- **În `backtest.py:297` (`build_leakage_proof`)**:
  ```python
  moment = _parse_datetime(raw, field_name="known_as_of")
  if moment > cutoff:
      rejected += 1
      continue
  ```
  Un punct cu `known_as_of == cutoff` **nu este respins**; intră în `proof.latest_input_known_as_of`.
- **În `phase12_temporal_contract.py:40-47`**:
  ```python
  if observed > cutoff:
      raise ValueError("historical observation is after prediction cutoff")
  ...
  if resolution_known <= cutoff:
      raise ValueError("terminal resolution was known at or before prediction cutoff")
  ```
  Observația la `cutoff` este **permisă**, dar rezoluția la `cutoff` este **strict interzisă** (`<= cutoff` aruncă eroare).
- **În `backtest.py:355-357` (`score_decisions`)**:
  ```python
  if _parse_datetime(outcome.known_at, field_name="outcome.known_at") <= _parse_datetime(decision.as_of, field_name="as_of"):
      not_after += 1
      continue
  ```
  Rezoluția la exact `decision.as_of` este marcată ca `unscored_resolution_not_after_decision` și **refuzată la scorare**.

#### 2. Implicație de Securitate:
Există o asimetrie riguroasă: banda de observații are domeniu închis la dreapta $[t_0, T]$, iar rezoluțiile au domeniu strict deschis la stânga $(T, \infty)$.
**Riscul subtil**: Dacă un meci s-a terminat la 20:40:00Z, ultimul trade din CLOB executat la 20:40:00Z la prețul de `0.999` intră în setul de tranzacționare al modelului la `cutoff = 20:40:00Z`. Modelul poate citi prețul terminal, deși rezoluția oficială este marcată la `20:40:01Z`.

---

### Cazul 2: Rezoluție sau Punct de Bandă cu `known_as_of` = `null`

**Întrebare**: *Este respinsă sau trece ca „necunoscut deci permis"?*

#### 1. Ce face codul efectiv:
- **În `resolutions.py:214-219` (`parse_resolution`)**:
  ```python
  missing = [key for key in ("market_id", "status", "known_at", "source", "source_ref") if not payload.get(key)]
  if missing:
      raise ValueError(f"resolution is missing required field(s): {missing}")
  ```
  Rezultatul execuției: **REJECTED**. Ridică imediat `ValueError: resolution is missing required field(s): ['known_at']`.
- **În `historical_paper_replay.py:26-40` (`HistoricalTapePoint`)**:
  ```python
  acquired_at: str | None = None
  known_as_of: str | None = None
  ...
  if self.known_as_of is not None:
      _parse_iso(self.known_as_of)
  ```
  Dacă `known_as_of` este `None`, clasa **nu ridică nicio eroare**.
- **În `historical_paper_replay.py:172-186` (`run_mechanical_control`)**:
  Nu verifică niciodată `point.known_as_of` sau `point.acquired_at`! Caută doar `point.observed_at`.
  Rularea harnașamentului cu `known_as_of = None` s-a executat complet: `EXECUTED CLEANLY! PnL=+0.8182`.

#### 2. Implicație de Securitate (Breșă):
În timp ce pachetul principal (`resolutions.py`, `historical_replay.py`, `backtest.py`) respinge `known_as_of = None`, noul modul `historical_paper_replay.py` permite ca punctele de bandă să aibă `known_as_of: None` și le tranzacționează fără verificare de proveniență temporală.

---

### Cazul 3: Două Piețe pe Același Eveniment (Scurgere Transversală)

**Întrebare**: *Ce se întâmplă când două piețe corelate pe același eveniment au momente de rezoluție diferite?*

#### 1. Ce face codul efectiv:
- În `backtest.py:348-356`:
  ```python
  outcome = by_market.get(decision.market_id)
  ```
  Scorarea și verificarea de scurgere compară `decision.as_of` **exclusiv** cu rezoluția care are **același `market_id`**.
- Pachetul nu conține niciun câmp `event_id`, nicio structură de graf relațional între piețe și nicio verificare de corelație.

#### 2. Implicație de Securitate (Breșă Arhitecturală):
Dacă Evenimentul X conține:
- Piața A: „Cine câștigă meciul?” (rezolvată la `20:00:00Z`)
- Piața B: „Scor Handicap -3.5” (rezolvată la `21:00:00Z`)
O decizie pentru Piața B luată la `20:30:00Z` poate consuma rezoluția Pieței A (deoarece $20:00:00Z \le 20:30:00Z$). `LeakageProof` validează decizia, iar `score_decisions` validează rezultatul (deoarece $21:00:00Z > 20:30:00Z$).
Modelul beneficiază de un avantaj informațional masiv asupra Pieței B, iar sistemul raportează că rularea a fost „100% blind”.

---

### Cazul 4: Punct în Bandă cu `t > endDate` (Tranzacții Post-Închidere)

**Întrebare**: *Ce face codul când banda de prețuri conține cotații înregistrate după `endDate` sau `resolution_known_at`?*

#### 1. Ce face codul efectiv:
- În `historical_paper_replay.py:43-51`:
  ```python
  @dataclass(frozen=True)
  class HistoricalMarketBundle:
      market_id: str
      question: str
      outcome_ids: tuple[str, ...]
      outcomes: tuple[str, ...]
      resolution_outcome_ids: tuple[str, ...]
      resolution_known_at: str
      price_history: tuple[HistoricalTapePoint, ...]
      metadata_source_ref: str
  ```
  `HistoricalMarketBundle` **nu reține deloc `endDate`**! Câmpul este complet abandonat la deserializare.
- În `HistoricalMarketBundle.validate()` (`historical_paper_replay.py:52-71`):
  Se verifică identificatorii `point.market_id == self.market_id` și `point.outcome_id in self.outcome_ids`. **Nu există nicio verificare** de tip `point.observed_at <= self.resolution_known_at`.
  Harnașamentul adversarial cu un punct la `22:00:00Z` într-o piață închisă la `20:46:54Z` a fost **acceptat fără eroare**.

#### 2. Măsurătoare Empirică pe Datele Reale din Etapa 3:
S-a scanat fișierul capturat `07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.json` (620 puncte de preț):
- **22 de puncte reale din 620 (3.5%)** au `observed_at > resolution_known_at`!
- Exemple de piețe afectate:
  - Piața ID `4537965`: Închisă conform Gamma la `2026-09-13T20:46:54Z`. Banda CLOB conține tranzacții înregistrate la `2026-09-13T20:47:12Z` și `2026-09-13T20:48:05Z`.
- Aceste tranzacții reprezintă execuții de lichidare sau închideri de poziții post-factum. Dacă o strategie de test accesează ultimul punct din bandă, ea citește o tranzacție executată **după** ce piața a fost declarată închisă!

---

## 3. Concluzia Auditului

1. Sistemul de bază din `backtest.py` și `resolutions.py` respectă cu strictețe bariera de scurgere pentru decizii vs. rezoluții pe aceeași piață.
2. Noul strat din `historical_paper_replay.py` suferă de două vulnerabilități concrete de scurgere temporală:
   - Acceptă puncte cu `known_as_of = None`.
   - Ignoră complet `endDate` și permite existența a 22 de puncte CLOB posterioare rezoluției.
3. Conform regulamentului de campanie, **niciun cod de producție nu a fost modificat**. Defectele sunt semnalate pentru remediere în cadrul programului de zi.
