---
id: 981d059a-246b-5aed-b241-9a97f87cb1d8
type: knowledge
lifecycle: REVIEW
category: architecture/distributed_systems
tags:
- ddia
- kleppmann
- replication
- quorums
- consensus
- raft
- two-phase-commit
- change-data-capture
- stream-processing
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: "06_INBOX/RAW_IMPORTS/BOOKS/Martin-Kleppmann-DDIA-Part2-3"
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/DDIA_Distributed_Storage_Reliability.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/System_Architecture.md
---

# DDIA Avansat: Replicare, Consens Distribuit & Procesare de Fluxuri

**Sursă**: Martin Kleppmann, *Designing Data-Intensive Applications* (Capitolele 5–9 și 11)  
**Domeniu**: Arhitectură Distribuită, Consistență & Fiabilitate

---

## 1. Replicare Fără Lider & Quorum-uri Dynamo (Ch 5)

Într-un sistem cu replicare fără lider (leaderless, model Dynamo):
- $n$: Numărul total de replici.
- $w$: Numărul de replici care trebuie să confirme o scriere pentru a fi considerată de succes.
- $r$: Numărul de replici care trebuie interogate pentru o citire.

### Condiția Strictă de Suprapunere a Quorum-ului
$$w + r > n$$

Dacă $w + r > n$, cel puțin una dintre cele $r$ replici citite conține cea mai recentă scriere confirmată.
- **Sloppy Quorum & Hinted Handoff**: În timpul unei partiții de rețea, scrierile sunt acceptate temporar pe noduri din afara setului principal de $n$ noduri și transferate înapoi odată ce partiția se vindecă.
- **Read Repair & Anti-Entropy**: Repararea citirii sincronizează versiunile vechi în mod oportunist, în timp ce procesele anti-entropy de fundal (folosind Merkle Trees) detectează discrepanțele fără a citi tot fluxul de date.

### Anomalii de Lag de Replicare
1. **Citire după scriere (Read-After-Write Consistency)**: Utilizatorul trebuie să vadă întotdeauna modificările pe care le-a trimis el însuși. Se obține citind datele utilizatorului exclusiv de pe lider sau de pe o replică cu timestamp verificat.
2. **Citiri monotone (Monotonic Reads)**: Un utilizator nu trebuie să observe timpul mergând înapoi (citind o replică actualizată, apoi una cu lag). Se rezolvă prin direcționarea cererilor aceluiași utilizator către aceeași replică (hash pe `user_id`).
3. **Citiri cu prefix consistent (Consistent Prefix Reads)**: Relațiile cauzale dintre scrieri trebuie păstrate (dacă $A$ cauzează $B$, orice observator trebuie să vadă $A$ înainte de $B$).

---

## 2. Consens Distribuit & Transmisie cu Ordine Totală (Ch 8–9)

Consensul reprezintă acordul formal între mai multe noduri asupra unei valori sau a unei secvențe de decizii:
- **Echivalența Consensului**: Obținerea liniarizabilității (Linearizability), a alegerii liderului cu leasing fără split-brain, a numerelor de secvență unice monotone și a tranzacțiilor distribuite este computațional echivalentă cu rezolvarea problemei consensului.
- **Algoritmi Canonici de Consens**: Paxos, Raft, Zab (ZooKeeper).
- **Mecanismele Raft**:
  - *Termeni logici (Epochs)*: Monoton crescători; previn comenzile liderilor detronați (Zombie/Split-brain).
  - *Heartbeat & Leader Election*: Dacă un Follower nu primește heartbeat într-un interval randomizat (ex: 150–300ms), inițiază un nou mandat.
  - *Log Matching Property*: Dacă două jurnale conțin o înregistrare cu același index și termen, jurnalele sunt identice până la acel punct.

### Tranzacții în Două Faze (Two-Phase Commit — 2PC)
- **Faza 1 (Prepare)**: Coordonatorul trimite un mesaj `prepare` participanților; aceștia blochează resursele, verifică invarianții și răspund cu `VOTE_COMMIT` sau `VOTE_ABORT`.
- **Faza 2 (Commit/Abort)**: Dacă toți votează favorabil, coordonatorul scrie decizia în propriul WAL și trimite `GLOBAL_COMMIT`. Dacă măcar unul votează împotrivă, se trimite `GLOBAL_ABORT`.
- *Punct slab critic*: Dacă coordonatorul pică după faza 1, participanții rămân blocați în stare de așteptare (*in-doubt*).

---

## 3. Captarea Modificărilor (CDC) & Dualitatea Flux-Tabelă (Ch 11)

- **Change Data Capture (CDC)**: Extragerea modificărilor direct din logul tranzacțional al bazei de date (ex: Debezium, SQLite hook-uri de actualizare) și publicarea lor într-un broker bazat pe log (ex: Kafka).
- **Dualitatea Flux-Tabelă (Stream-Table Duality)**:
  $$\text{Tabelă} = \text{Stare la momentul } t$$
  $$\text{Flux} = \text{Istoricul complet al mutațiilor (Changelog)}$$
  Reconstrucția tabelei se face prin cumularea (replay) fluxului de la origini sau de la cel mai recent checkpoint compactat (*Log Compaction*).

---

## 4. Playbook Operațional: Ce fac când primesc o sarcină de replicare/consens?

1. **Pentru stocare locală/hibridă**: Nu folosesc tranzacții 2PC între servicii independente din cauza riscului de blocare (*in-doubt*); folosesc tiparul **Transactional Outbox** + CDC cu garantare de livrare *at-least-once* și consumatori idempotenți.
2. **Pentru stocare SQLite locală**: Activez întotdeauna `PRAGMA busy_timeout=5000;` pentru a gestiona concurența tranzacțiilor și `BEGIN IMMEDIATE` pentru a preveni blocajele `SQLITE_BUSY` la escaladarea de la citire la scriere.
3. **Pentru partajare multi-agent**: Orice stare critică de decizie a consiliului se memorează cu număr de epocă/secvență monoton crescător pentru a preveni split-brain-ul când doi agenți acționează concurent.
