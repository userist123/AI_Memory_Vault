---
id: 4f82e6aa-3157-535a-9cf4-4d759b4d999a
type: knowledge
lifecycle: REVIEW
category: architecture/storage_engines_lsm
tags:
- ddia
- kleppmann
- lsm-tree
- sstable
- bloom-filter
- compaction
- storage-engine
- rocksdb
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Martin-Kleppmann-DDIA-Ch3
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/DDIA_Distributed_Storage_Reliability.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/MASTERY_DDIA_Serializability_SSI_and_Locking.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# DDIA Expert: Motoare de Stocare Structurate pe Jurnal, LSM-Trees & Compactare SSTable

**Surs?**: Martin Kleppmann, *Designing Data-Intensive Applications* (Capitolul 3: Stocare ?i Reg?sire)  
**Domeniu**: Arhitectura Motoarelor de Baze de Date, Optimizare I/O & Structuri de Date Imutabile

---

## 1. Dihotomia Fundamental?: B-Trees vs LSM-Trees

?n proiectarea motoarelor de persisten?? exist? dou? filosofii majore:

| Proprietate | B-Trees (PostgreSQL, SQLite, InnoDB) | LSM-Trees (LevelDB, RocksDB, Cassandra) |
| :--- | :--- | :--- |
| **Model de Scriere** | *Update-in-place* (modific? pagini pe disc de 4KB-8KB) | *Append-only* secven?ial (scrie ?n MemTable ?i fi?iere SSTable) |
| **Amplificare de Scriere (*Write Amplification*)** | Mare (o mic? modificare rescrie ?ntreaga pagin? + WAL) | Medie spre Mic? (scrieri secven?iale mari) |
| **Performan?? Scriere** | Limitat? de I/O aleatoriu | Maxim? (aproape de l??imea de band? fizic? a discului) |
| **Performan?? Citire** | Deterministic? (c?utare $\mathcal{O}(\log N)$ ?ntr-un singur arbore) | Variabil? (necesit? verificarea mai multor niveluri SSTable) |
| **Fragmentare Spa?iu** | Goluri ?n pagini dup? ?tergeri | Spa?iul este recuperat prin *Compaction* |

---

## 2. Arhitectura Intern? LSM-Tree (Log-Structured Merge-Tree)

Un motor LSM complet func?ioneaz? prin coordonarea a trei componente fundamentale:

```text
[ Cerere Scriere (k, v) ]
          |
          +---> 1. Write-Ahead Log (WAL pe disc - recuperare la crash)
          |
          +---> 2. MemTable (Arbore Red-Black / SkipList ?n RAM)
                     | (C?nd atinge pragul ex: 64MB)
                     v
                3. Flush Imutabil pe Disc -> [ SSTable Nivel 0 ]
```

### A. SSTable (Sorted String Table)
- **Format Imutabil**: Fi?ierele scrise pe disc nu sunt niciodat? modificate la fa?a locului.
- **Chei Sortate**: Cheile din fi?ier sunt sortate strict cresc?tor, permi??nd c?ut?ri binare rapide pe blocuri ?i parcurgeri liniare de tip merge-sort.
- **Index Spars ?n Memorie**: ?n loc s? indexeze fiecare cheie, motorul ?ine ?n RAM doar un index spars (ex: o cheie la fiecare 4KB de date comprimate).

### B. Filtre Bloom (Bloom Filters)
Pentru chei care nu exist? ?n baza de date, citirea ar necesita scanarea tuturor fi?ierelor SSTable de pe disc.
- **Solu?ie**: Un filtru Bloom probabilistic stocat ?n RAM per SSTable.
- **Garan?ie matematic?**:
  - Dac? filtrul spune *Nu*, cheia garantat **NU** exist? ?n acel fi?ier (se evit? un I/O scump).
  - Dac? filtrul spune *Da*, cheia **posibil** exist? (rat? controlabil? de fals pozitiv $p \approx 1\%$ cu 10 bi?i per element).

---

## 3. Strategii de Compactare (Compaction)

Deoarece datele sunt imutabile, suprascrierile ?i ?tergerile (marcate prin *Tombstone*) creeaz? duplicate pe disc. Procesul de compactare combin? fi?ierele ?i cur??? istoricul vechi:

### A. Size-Tiered Compaction
- Fi?ierele de dimensiuni similare sunt compactate ?mpreun? c?nd se acumuleaz? un num?r fix de fi?iere (ex: 4 fi?iere mici devin un fi?ier mediu).
- **Avantaj**: Simplu, bun pentru sarcini mari de scriere.
- **Dezavantaj**: Cerin?e mari de spa?iu liber temporar (p?n? la $50\%$ din disc).

### B. Leveled Compaction (RocksDB standard)
- Nivelurile sunt organizate ierarhic ($L_0, L_1, L_2, \dots$).
- Fiecare nivel are o capacitate maxim? fix? (ex: $L_1 = 10\text{MB}, L_2 = 100\text{MB}, L_3 = 1\text{GB}$).
- La nivelurile $L_1+$, intervalele de chei ale fi?ierelor **nu se suprapun**.
- O c?utare cite?te cel mult un singur fi?ier per nivel, reduc?nd drastic amplificarea de citire (*Read Amplification*).

