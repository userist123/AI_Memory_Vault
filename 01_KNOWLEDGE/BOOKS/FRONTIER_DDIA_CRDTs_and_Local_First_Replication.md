---
id: 026573e2-76c6-540b-90e2-9bb025d72600
type: knowledge
lifecycle: REVIEW
category: architecture/crdts_local_first_replication
tags:
- ddia
- kleppmann
- crdt
- local-first
- pn-counter
- lww-element-set
- state-based-crdt
- distributed-systems
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Martin-Kleppmann-DDIA-Ch5-Ch12
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/SCALING_DDIA_Change_Data_Capture_and_Event_Sourcing.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/CAPSTONE_DDIA_Raft_Consensus_and_Replicated_State_Machines.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# DDIA Frontier: Tipuri de Date Replicate Fără Conflicte (CRDT) și Software Local-First

**Sursă**: Martin Kleppmann, *Designing Data-Intensive Applications* (Capitolele 5 și 12) & cercetările autorului despre *Local-First Software* (Ink & Switch / Automerge)  
**Domeniu**: Replicare fără Coordonare Centrală, Convergență Matematică, Sincronizare Multi-Master

---

## 1. Paradigma Local-First și Limitările Consensului Centralizat

Sistemele distribuite tradiționale bazate pe consens (Raft, Paxos, 2PC) depind de un nod coordonator sau de un cvorum activ. În scenarii edge, mobile sau peer-to-peer (offline-first), coordonarea sinkronă devine un punct unic de blocaj.

### 1.1 Cele 7 Ideale ale Software-ului Local-First
1. **Fără latență la scriere**: Datele sunt scrise instantaneu pe discul local.
2. **Multi-dispozitiv nativ**: Sincronizare automată la reconectare.
3. **Funcționare offline completă**: Nicio degradare funcțională fără rețea.
4. **Colaborare în timp real**: Editare concurentă fără blocaje globale (*locks*).
5. **Durabilitate pe termen lung**: Datele trăiesc în fișiere locale, independente de cloud.
6. **Securitate și confidențialitate**: Criptare end-to-end pe transport.
7. **Controlul utilizatorului asupra datelor**: Proprietate directă a stocării.

---

## 2. Fundamente Teoretice ale CRDT (Shapiro et al., 2011)

Un CRDT (Conflict-free Replicated Data Type) este o structură de date distribuită garantată matematic să conveargă la aceeași stare pe toate replicile odată ce toate mutațiile au fost livrate (chiar și out-of-order), fără negociere activă.

```
       Replica A: Stare A ───(mutație locală)───→ A' ───┐
                                                         ├──→ Merge(A', B') = Stare Unică Convergentă
       Replica B: Stare B ───(mutație locală)───→ B' ───┘
```

### 2.1 Structura Algebrică: Semilattice Join (CvRDT)
Un State-based CRDT (CvRDT) operează peste o mulțime $S$ echipată cu o ordine parțială $\le$ și o operație de unire (*join* / cel mai mic majorant comun) $\sqcup$:
- **Comutativitate**: $x \sqcup y = y \sqcup x$
- **Asociativitate**: $(x \sqcup y) \sqcup z = x \sqcup (y \sqcup z)$
- **Idempotență**: $x \sqcup x = x$
- **Monotonie**: $x \le x \sqcup y$

Datorită acestor 4 proprietăți, ordinea de recepționare a stărilor de la alte noduri este irelevantă; aplicarea multiplă sau reordonată garantează convergența deterministă (*Strong Eventual Consistency*).

---

## 3. Tipuri Canonice de CRDT

### 3.1 PN-Counter (Positive-Negative Counter)
Permite incrementări și decrementări concurente:
- Stare per nod $i$: Vector $P[i]$ (creșteri) și Vector $N[i]$ (scăderi).
- **Valoare scalară**: $V = \sum P[i] - \sum N[i]$.
- **Merge**: $P_{\text{merged}}[i] = \max(P_A[i], P_B[i])$, $N_{\text{merged}}[i] = \max(N_A[i], N_B[i])$.

### 3.2 LWW-Element-Set (Last-Write-Wins Element Set)
Set distribuit cu adăugare și ștergere bazat pe timestampuri Lamport:
- **Add-Set**: $\{(e, t_{\text{add}})\}$, **Remove-Set**: $\{(e, t_{\text{rem}})\}$.
- Un element $e \in S$ dacă există în Add-Set și $(e \notin \text{Remove-Set} \lor t_{\text{add}} > t_{\text{rem}})$.

### 3.3 RGA / Automerge / Yjs (Text CRDTs)
Pentru editare colaborativă de text, caracterele sunt reprezentate ca noduri într-un arbore cu ID-uri unice imutabile (e.g. `(nod_id, counter)`). Ștergerile utilizează *tombstones* pentru a menține referințele de plasare stabilite concurent.

---

## 4. Comparativ Arhitectural: Consens vs CRDT

| Metrică / Proprietate | Consens Clasic (Raft / Paxos) | CRDT (Local-First) |
| :--- | :--- | :--- |
| **Disponibilitate la Scriere** | $N/2 + 1$ noduri necesare | 100% (chiar și izolat offline) |
| **Latență de Scriere** | 1-2 RTT de rețea | 0 ms (memorie/disc local) |
| **Complexitate Conflict** | Prevenit prin serializare strictă | Rezolvat matematic la merge |
| **Overhead Metadata** | Mic (index de log) | Moderat (vector clocks, tombstones) |
| **Garbage Collection** | Trunchiere log simplă | Necesară compactare tombstones |

---

## 5. Aplicabilitate în AI Memory Vault

- **Sincronizare Multi-Dispozitiv fără Cloud Coordonat**: Notele Markdown și audit log-ul pot converge prin CRDT între laptopul utilizatorului și servere edge.
- **Editare Simultană Multi-Agent**: Sub-agenții pot colabora pe același fișier de plan sau artefact fără blocaje de scriere la nivel de fișier OS.
- **Jurnal Tamper-Evident Imutabil**: Structurile CRDT monotone se potrivesc natural cu hash-chain-ul `audit_log.jsonl`.

---

## Referințe Obsidian

- [[SCALING_DDIA_Change_Data_Capture_and_Event_Sourcing]]
- [[CAPSTONE_DDIA_Raft_Consensus_and_Replicated_State_Machines]]
- [[HARDENING_DDIA_Byzantine_Faults_and_Clock_Drift]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
