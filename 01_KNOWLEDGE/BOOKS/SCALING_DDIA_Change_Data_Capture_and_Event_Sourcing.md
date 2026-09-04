---
id: 9344f91b-bf48-572f-8ab7-d010feb4cb47
type: knowledge
lifecycle: REVIEW
category: architecture/change_data_capture_event_sourcing
tags:
- ddia
- kleppmann
- cdc
- event-sourcing
- debezium
- wal
- outbox-pattern
- event-log
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Martin-Kleppmann-DDIA-Ch11
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/PRODUCTION_DDIA_Exactly_Once_Semantics_and_Idempotency.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/HARDENING_DDIA_Byzantine_Faults_and_Clock_Drift.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# DDIA Scaling: Change Data Capture (CDC) și Event Sourcing

**Sursă**: Martin Kleppmann, *Designing Data-Intensive Applications* (Capitolul 11: Procesare de Flux)
**Domeniu**: Integrare de Sisteme prin Flux de Evenimente, Jurnal Imutabil, Derivare de Stare

---

## 1. Change Data Capture (CDC)

### 1.1 Principiul Fundamental

CDC transformă fiecare mutație din baza de date într-un **eveniment** pe un flux ordonat:

```
INSERT INTO orders (id, amount) VALUES (42, 99.50)
                    ↓ CDC
{op: "c", table: "orders", after: {id: 42, amount: 99.50}, ts: 1693...}
```

Sursa de adevăr rămâne baza de date; fluxul CDC este o **proiecție derivată**.

### 1.2 Mecanisme de Capturare

| Mecanism | Sursă | Latență | Impact Performanță |
| :--- | :--- | :--- | :--- |
| **Log-based** (WAL parsing) | Write-Ahead Log | ms | Zero (citește pasiv jurnalul) |
| **Trigger-based** | Triggere SQL | ms | Moderat (trigger per operație) |
| **Polling** | Coloană `updated_at` | secunde | Scăzut dar pierde DELETE-uri |
| **Dual-write** | Cod aplicație | ms | Riscant (fără atomicitate) |

### 1.3 Debezium — Arhitectura Standard

```
┌──────────┐     WAL      ┌──────────┐    Kafka    ┌──────────────┐
│ PostgreSQL│ ──────────→ │ Debezium  │ ─────────→ │ Consumatori  │
│  (source) │  pg_logical │ Connector │   Topic    │ (search, cache│
└──────────┘              └──────────┘             │  analytics)  │
                                                    └──────────────┘
```

**Proprietăți critice**:
- **Ordonare totală** pe partiție (per cheie primară)
- **At-least-once** delivery (consumatorul trebuie să fie idempotent)
- **Snapshot inițial** + stream continuu (pentru populare retroactivă)

---

## 2. Event Sourcing

### 2.1 Diferența față de CDC

| Aspect | CDC | Event Sourcing |
| :--- | :--- | :--- |
| **Sursa de adevăr** | Starea curentă (tabel) | Jurnalul de evenimente |
| **Derivare** | Evenimentele derivă din stare | Starea derivă din evenimente |
| **Granularitate** | Operații CRUD | Evenimente de domeniu (business) |
| **Reversibilitate** | Nu (starea e suprascrisă) | Da (replay din orice punct) |

### 2.2 Anatomia unui Eveniment

```python
@dataclass
class DomainEvent:
    event_id: str          # UUID unic
    aggregate_id: str      # Entitatea afectată
    event_type: str        # "OrderPlaced", "PaymentReceived"
    payload: dict          # Datele evenimentului
    timestamp: datetime    # Când s-a întâmplat
    version: int           # Versiunea agregatului
    metadata: dict         # Corelație, cauzalitate
```

### 2.3 Reconstrucția Stării (Fold/Reduce)

```
state₀ = {}
state₁ = apply(state₀, OrderPlaced{amount: 100})     → {status: "placed", total: 100}
state₂ = apply(state₁, PaymentReceived{amount: 100})  → {status: "paid",   total: 100}
state₃ = apply(state₂, ItemShipped{tracking: "XY"})   → {status: "shipped", total: 100}
```

**Starea curentă** = `fold(apply, initial_state, all_events)`

### 2.4 Snapshots pentru Performanță

Reconstrucția din milioane de evenimente e lentă. Soluția: **snapshots periodice**:
```
Eveniment 1..10000 → Snapshot S₁ (salvat)
Eveniment 10001..10050 → stare = apply(S₁, events[10001:10050])
```

---

## 3. CQRS (Command Query Responsibility Segregation)

### 3.1 Separarea Scriere/Citire

```
                  ┌─ Write Model (Event Store) ── comenzi, validare
Aplicație ────────┤
                  └─ Read Model (Materialized Views) ── interogări, rapoarte
```

- **Write side**: Validează comenzi, emite evenimente, stochează în event log
- **Read side**: Consumă evenimente, construiește proiecții optimizate per query

### 3.2 Avantaje

- **Scalare independentă**: Read replicas separate de write master
- **Modele optimizate**: Fiecare proiecție este optimizată pentru pattern-ul de citire
- **Audit trail natural**: Event log-ul este audit trail complet

### 3.3 Complexitate Adăugată

- **Consistență eventuală**: Read model poate fi cu câteva ms/secunde în urmă
- **Idempotență obligatorie**: Proiecțiile trebuie să suporte replay fără efecte secundare
- **Schema evolution**: Evenimentele vechi trebuie desearializate cu schema nouă

---

## 4. Log Compaction și Retenție

### 4.1 Strategii Kafka

| Strategie | `cleanup.policy` | Comportament |
| :--- | :--- | :--- |
| **Delete** | `delete` | Ștergere după TTL (ex: 7 zile) |
| **Compact** | `compact` | Reține doar ultimul mesaj per cheie |
| **Compact + Delete** | `compact,delete` | Compactare + TTL minim |

### 4.2 Log Compaction ca Snapshot

Cu `compact`: topic-ul converge la un **key-value store** distribuit:
- Fiecare cheie → ultima valoare
- Tombstone (`null` payload) → ștergere logică
- Consumator nou primește **starea completă** la subscribe

---

## 5. Aplicabilitate în Memory Vault

- **Audit Log ca Event Store**: `audit_log.jsonl` este deja un log append-only cu hash chain
- **CDC pe SQLite WAL**: `SQLiteStorageEngine` cu WAL mode poate fi monitorizat similar Debezium
- **Reconstrucție de stare**: Din audit log se poate reconstitui starea oricărei note la orice moment
- **CQRS natural**: Write = `MemoryController.create/update`, Read = `MemoryController.search`

---

## Referințe Obsidian

- [[PRODUCTION_DDIA_Exactly_Once_Semantics_and_Idempotency]]
- [[HARDENING_DDIA_Byzantine_Faults_and_Clock_Drift]]
- [[CAPSTONE_DDIA_Raft_Consensus_and_Replicated_State_Machines]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
