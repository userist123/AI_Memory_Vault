---
id: d2878277-9e14-5dd8-97bb-699c9c6f052f
type: knowledge
lifecycle: REVIEW
category: architecture/distributed_systems
tags:
- ddia
- kleppmann
- storage-engines
- lsm-tree
- b-tree
- transactions
- wal
- replication
- consensus
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: "06_INBOX/RAW_IMPORTS/BOOKS/Martin-Kleppmann-Designing-Data-Intensive-Applications.pdf"
confidence: high
verification: unverified
relations:
- relation: references
  target: 00_CORE/System_Architecture.md
- relation: references
  target: 99_SYSTEM/Memory_V6_Architecture.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Production_ML_Systems_and_Continual_Learning.md
---

# Designing Data-Intensive Applications: Distributed Storage & Reliability Foundations

**Author**: Martin Kleppmann (2017)  
**Synthesis Role**: Core Data & Storage Engineering Invariants for AI Memory Systems  

---

## 1. The Three Core Pillars of Data Systems

1. **Reliability**: Continuing to work correctly (performing the desired function at the desired level of performance) even in the face of adversity (hardware or software faults, and human error). Faults cannot be completely prevented; systems must be designed for fault tolerance and recovery.
2. **Scalability**: The system's ability to cope with increased load. Must be stated with specific load parameters (e.g. read/write ratio, cache hit rates, concurrent transactions) and percentiles ($p95$, $p99$) rather than mean latency.
3. **Maintainability**: Operability (easy operational maintenance), Simplicity (managing complexity), and Evolvability (making change easy via schema and interface abstraction).

---

## 2. Storage Engine Architectures: LSM-Trees vs. B-Trees

| Dimension | Log-Structured Merge-Trees (LSM-Trees) | B-Trees (Page-Oriented) |
|---|---|---|
| **Write Pattern** | Sequential append to Write-Ahead Log (WAL) and memory MemTable; asynchronous compaction into SSTables. | In-place random updates to fixed-size disk blocks (usually 4KB-8KB). |
| **Write Amplification** | Lower initial write amplification; excellent write throughput. | Higher write amplification due to full-page rewrites and WAL. |
| **Read Pattern** | Range scans fast; point lookups may require checking multiple SSTables (mitigated by Bloom filters). | Point lookups deterministic ($O(\log N)$); fast single-record retrieval. |
| **Crash Recovery** | Replays sequential WAL into MemTable. | Relies on Write-Ahead Log (WAL) to restore page integrity after torn writes. |

### Vault Application
`AI_Memory_Vault_CODEX_READY` implements SQLite WAL mode (`PRAGMA journal_mode=WAL`), decoupling concurrent readers from a single sequential writer, preventing database locks and torn transactions.

---

## 3. Transactions, Isolation Levels & Atomicity

- **Transactions** group operations into a single logical execution unit:
  - **Atomicity**: Prevents partial failure; if any step aborts, all modifications are cleanly rolled back.
  - **Consistency**: Invariant preservation (application-defined).
  - **Isolation**: Concurrently executing transactions cannot step on each other's toes.
  - **Durability**: Committed data will not be lost.
- **Isolation Levels**:
  - *Read Committed*: Prevents dirty reads and dirty writes.
  - *Snapshot Isolation (MVCC)*: Each transaction sees a consistent snapshot of the database at a specific timestamp; prevents read skew.
  - *Serializable*: Strict total order; prevents phantom reads and write skew. Implemented via two-phase locking (2PL), serial execution, or serializable snapshot isolation (SSI).

---

## 4. Distributed Systems: Fallacies & Consensus

1. **The Network is Unreliable**: Packet delay, duplication, reordering, and silent drops are normal operating conditions. Timeouts are required, but picking timeout thresholds involves a fundamental trade-off between failure detection latency and false positives.
2. **Clock Skew & Ordering**: Physical system clocks drift. Ordering distributed events using wall-clock time leads to silent data loss (Last-Write-Wins hazard). Logical clocks (Lamport timestamps, Vector clocks) provide causal ordering.
3. **Consistency vs. Consensus**:
   - *Consistency guarantees* (e.g., linearizability) describe the order and freshness of single-object read/write operations.
   - *Consensus* (e.g., Paxos, Raft) solves the problem of multiple nodes agreeing on a sequence of decisions (e.g., total order broadcast, leader election).
