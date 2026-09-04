---
id: 00781b12-e93d-5fd6-82ed-2478dd12d8e7
type: knowledge
lifecycle: REVIEW
category: architecture/exactly_once_semantics
tags:
- ddia
- kleppmann
- exactly-once
- idempotency
- kafka-transactions
- two-phase-commit
- deduplication
- stream-processing
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Martin-Kleppmann-DDIA-Ch11-Ch12
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/EXPERT_DDIA_LSM_Trees_SSTables_and_Compaction.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/HARDENING_DDIA_Byzantine_Faults_and_Clock_Drift.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# DDIA Production: Semantici End-to-End Exactly-Once și Idempotență

**Sursă**: Martin Kleppmann, *Designing Data-Intensive Applications* (Capitolele 11-12: Procesare de Flux & Viitorul Sistemelor de Date)
**Domeniu**: Garantii de Livrare, Tranzacții în Flux, Deduplicare la Nivel de Aplicație

---

## 1. Mitul „Exactly-Once" și Realitatea Inginerească

Kleppmann avertizează că „exactly-once" nu este o proprietate a protocolului de transport, ci un **comportament end-to-end** obținut prin combinarea mai multor mecanisme:

| Nivel | Mecanism | Ce Garantează |
| :--- | :--- | :--- |
| **Transport** | At-least-once delivery + ACK | Mesajul ajunge cel puțin o dată |
| **Idempotență** | Chei de deduplicare (idempotency key) | Procesarea repetată produce același efect |
| **Tranzacție atomică** | Kafka Transactions / 2PC | Exact o dată la nivel de grup de scrieri |

### 1.1 Principiul End-to-End (Saltzer, Reed, Clark)

> *„Funcționalitatea de fiabilitate poate fi implementată complet și corect doar de capetele comunicației."*

Implicații practice:
- **Retry-ul la nivel de transport** nu elimină duplicatele vizibile aplicației
- **Broker-ul nu poate garanta idempotență** fără cooperarea consumatorului
- **Exactly-once = at-least-once delivery + idempotent processing + atomic commit**

---

## 2. Mecanisme de Idempotență

### 2.1 Chei de Deduplicare (Idempotency Keys)

```
Client → [request_id: "abc-123", amount: 100] → Server
Server: IF request_id EXISTS in dedup_table → RETURN cached_response
         ELSE → process + INSERT request_id → RETURN new_response
```

**Cerințe critice**:
- **Unicitate**: UUID v4 sau hash deterministic al payload-ului
- **TTL**: Dedup table trebuie curățată periodic (de obicei 24-72h)
- **Atomicitate**: INSERT în dedup table + operația de business trebuie într-o singură tranzacție

### 2.2 Operații Natural-Idempotente

| Operație | Idempotentă? | Exemplu |
| :--- | :--- | :--- |
| `SET balance = 500` | ✅ Da | Overwrite absolut |
| `balance += 100` | ❌ Nu | Incrementare relativă |
| `SET balance = 500 WHERE version = 3` | ✅ Da | Conditional update (CAS) |
| `INSERT ... ON CONFLICT DO NOTHING` | ✅ Da | Upsert cu cheie naturală |

---

## 3. Kafka Transactions — Atomic Multi-Partition Writes

### 3.1 Protocolul în 3 Faze

```
1. BEGIN_TXN → TransactionCoordinator alocă TxnID
2. PRODUCE mesaje pe partițiile P1, P2, P3 (cu TxnID marker)
3. COMMIT_TXN → Coordinator scrie COMMIT marker pe __transaction_state
   → Consumatorii cu isolation.level=read_committed văd mesajele
```

### 3.2 Proprietăți Garantate

- **Atomicitate**: Fie toate mesajele din tranzacție sunt vizibile, fie niciunul
- **Izolare read_committed**: Consumatorii nu văd mesaje din tranzacții neconfirmate
- **Idempotent Producer**: Broker-ul deduplică pe baza `(ProducerID, SequenceNumber)` → elimină duplicate la retry

### 3.3 Limitări

- **NU este 2PC clasic**: Nu implică sisteme externe (doar partițiile Kafka)
- **Latență adăugată**: ~5-15ms per tranzacție din cauza coordonatorului
- **NU traversează granița Kafka**: Scrierea în DB externă + Kafka necesită pattern Outbox sau Saga

---

## 4. Deduplicare la Nivel de Consumator

### 4.1 Outbox Pattern cu Change Data Capture

```
┌─────────┐    CDC     ┌─────────┐     ┌──────────┐
│  App DB  │ ────────→ │ Debezium │ ──→ │  Kafka   │
│ (orders  │           │  (CDC)   │     │  Topic   │
│ + outbox)│           └─────────┘     └──────────┘
└─────────┘
```

1. Aplicația scrie **ordinul + evenimentul outbox** într-o singură tranzacție DB
2. Debezium citește WAL-ul și publică evenimentul în Kafka
3. Consumatorul procesează + marchează offset → exactly-once semantic end-to-end

### 4.2 Log Compaction ca Deduplicare Naturală

- Kafka topic cu `cleanup.policy=compact` reține doar **ultimul mesaj per cheie**
- Consumatorul la restart va vedea starea finală, nu duplicatele intermediare
- Echivalent funcțional cu un key-value store distribuit

---

## 5. Anti-Patternuri Comune

| Anti-Pattern | Consecință | Soluție |
| :--- | :--- | :--- |
| Retry fără idempotency key | Duplicare silențioasă de plăți/comenzi | UUID per request + dedup table |
| 2PC între Kafka + DB externă | Blocare la eșecul coordinatorului | Outbox Pattern + CDC |
| `read_uncommitted` pe consumator | Citire de mesaje din tranzacții abandonate | `isolation.level=read_committed` |
| Dedup table fără TTL | Creștere nelimitată a tabelei | TTL 48-72h + cleanup periodic |

---

## 6. Aplicabilitate în AI Memory Vault

- **Audit Log Integrity**: Fiecare `audit_log.jsonl` entry are `entry_hash` ca cheie de deduplicare naturală
- **Memory Ingestion**: Operațiile de `upsert` pe note sunt idempotente prin design (overwrite pe UUID)
- **CDC pentru Observabilitate**: WAL-ul SQLite din `SQLiteStorageEngine` poate fi monitorizat similar Debezium

---

## Referințe Obsidian

- [[EXPERT_DDIA_LSM_Trees_SSTables_and_Compaction]]
- [[HARDENING_DDIA_Byzantine_Faults_and_Clock_Drift]]
- [[CAPSTONE_DDIA_Raft_Consensus_and_Replicated_State_Machines]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
