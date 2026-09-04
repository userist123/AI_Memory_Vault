---
id: 04df889a-ff12-55b0-8e73-2ebbec9508e8
type: knowledge
lifecycle: REVIEW
category: architecture/serializability_and_ssi
tags:
- ddia
- kleppmann
- transactions
- serializability
- two-phase-locking
- ssi
- write-skew
- concurrency-control
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Martin-Kleppmann-DDIA-Ch7
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/DDIA_Distributed_Storage_Reliability.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/ADVANCED_DDIA_Replication_Consensus_Streaming.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/SPECIALIZED_DDIA_Batch_Joins_and_Unbundled_Databases.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# DDIA M?iestrie: Serializabilitate, Two-Phase Locking (2PL) & Serializable Snapshot Isolation (SSI)

**Surs?**: Martin Kleppmann, *Designing Data-Intensive Applications* (Capitolul 7: Tranzac?ii)  
**Domeniu**: Controlul Concuren?ei, Niveluri de Izolare & Tranzac?ii Atomice

---

## 1. Ierarhia Nivelurilor de Izolare ?i Anomaliile Concurente

?n sistemele de baze de date, tranzac?iile concurente pot genera anomalii specifice atunci c?nd nivelul de izolare este relaxat pentru a cre?te debitul (*throughput*):

| Nivel de Izolare | Dirty Reads | Non-Repeatable Read | Lost Update | Write Skew | Phantom Read |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Read Uncommitted** | Posibil | Posibil | Posibil | Posibil | Posibil |
| **Read Committed** | Prevenit | Posibil | Posibil | Posibil | Posibil |
| **Snapshot Isolation (MVCC)** | Prevenit | Prevenit | Prevenit (la nivel de r?nd) | Posibil | Prevenit (pt. citiri) |
| **Serializable** | Prevenit | Prevenit | Prevenit | Prevenit | Prevenit |

### Anomalia "Write Skew" (Devia?ia de Scriere)
Spre deosebire de o simpl? pierdere de actualizare (*Lost Update* ? unde dou? tranzac?ii concurente rescriu acela?i r?nd), **Write Skew** apare atunci c?nd:
1. Dou? tranzac?ii concurente execut? o interogare de tip premis? (ex: "Num?rul de medici de gard? activi este $\ge 2$").
2. Bazat pe rezultat, ambele tranzac?ii decid independent o ac?iune valid? la momentul citirii.
3. Fiecare tranzac?ie modific? un r?nd **diferit** (ex: Dr. Alice ??i ia liber modific?nd `shift_alice`, iar Dr. Bob modific? `shift_bob`).
4. Ambele tranzac?ii finalizeaz? cu `COMMIT`. Niciun r?nd nu a fost rescris concomitent, dar invariantul global ("cel pu?in un medic r?m?ne de gard?") a fost violat.

---

## 2. Abord?ri Tradi?ionale ale Serializabilit??ii

### A. Execu?ie Strict Secven?ial? (Serial Execution)
- Toate tranzac?iile se execut? pe un singur fir determinist (ex: Redis, VoltDB).
- **Avantaj**: F?r? blocaje (*locks*), f?r? riscuri de deadlock, simplitate maxim?.
- **Limitare**: Tranzac?iile trebuie s? fie foarte scurte ?i ?ntregul set de date active trebuie s? ?ncap? ?n memorie (RAM).

### B. Two-Phase Locking (2PL - Blocare ?n Dou? Faze)
Mecanismul pesimist predominant timp de decenii ?n SQL clasic:
- **Faza de cre?tere (*Growing Phase*)**: Tranzac?ia achizi?ioneaz? lac?te (Shared/S-locks pentru citire, Exclusive/X-locks pentru scriere), dar nu poate elibera niciunul.
- **Faza de sc?dere (*Shrinking Phase*)**: Odat? eliberat primul lac?t, tranzac?ia nu mai poate ob?ine altele.
- **Prevenirea Fantomelor (*Phantom Reads*)**:
  - *Predicate Locks*: Blocarea unei condi?ii logice arbitrare (ex: `WHERE room_id = 123 AND start_time < '2026-09-04 14:00'`). Costisitor la scar? mare.
  - *Index-Range Locks (Next-Key Locks)*: Se blocheaz? un interval concret de chei din indexul B-Tree, aproxim?nd predicatul logic printr-un lac?t eficient pe index.

---

## 3. Serializable Snapshot Isolation (SSI)

SSI (Michael Cahill, 2008; integrat ?n PostgreSQL ?i FoundationDB) ofer? garan?ii complete de serializabilitate **f?r? a bloca cititorii sau scriitorii**:

### Mecanismul de Detec?ie Optimist? a Dependen?elor rw (*Anti-Dependencies*)
1. **Citiri ne-blocante pe Snapshot Consistent**: Citirile citesc instantanee MVCC f?r? blocaj.
2. **Urm?rirea st?rilor ne-confirmate (*si-read locks*)**: C?nd tranzac?ia $T_1$ cite?te o cheie conform versiunii $v$, motorul re?ine un indicator c? $T_1$ a depins de starea acelei chei.
3. **Detec?ia ciclurilor ?n graful de serializare**: Dac? tranzac?ia concurent? $T_2$ scrie pe aceea?i cheie (cre?nd dependen?a $T_1 \xrightarrow{rw} T_2$), iar $T_1$ scrie pe o cheie citit? de $T_2$ ($T_2 \xrightarrow{rw} T_1$), se formeaz? un ciclu de conflicte ne-serializabile.
4. **Avortare selectiv?**: Motorul avorteaz? tranzac?ia la apelul de `COMMIT`, for??nd re?ncercarea aplica?iei (*optimistic abort*).

---

## 4. Leg?tura cu Arhitectura Memory Vault

- **SQLite WAL & Atomic Scopes**: Vault-ul folose?te `PRAGMA busy_timeout=5000` ?i `BEGIN IMMEDIATE` pentru a serializa scrierile concurente ?i a asigura Snapshot Isolation ne-blocant pentru cititori.
- **Invariantele `I-001..I-012`**: ?n opera?iunile de promovare a st?rii sau de atestare a provenien?ei, premisa de verificare este evaluat? atomic pentru a elimina anomaliile de tip Write Skew.

