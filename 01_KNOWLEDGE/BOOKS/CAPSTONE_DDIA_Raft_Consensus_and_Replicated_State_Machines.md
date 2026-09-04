---
id: 3348c150-6437-40f7-8107-868b7228376b
type: knowledge
lifecycle: REVIEW
category: distributed_systems/raft_consensus_replication
tags:
- ddia
- kleppmann
- raft-consensus
- state-machine-replication
- leader-election
- split-brain
- capstone
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Martin-Kleppmann-DDIA-Ch8-9
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/ADVANCED_DDIA_Replication_Consensus_Streaming.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/GRAPH/07 Knowledge Domains Map.md
---

# DDIA Capstone: Consens Distribuit Raft, Alegerea Liderului & Replicarea Ma?inilor de Stare

**Surs?**: Martin Kleppmann, *Designing Data-Intensive Applications* (Capitolele 8-9) + Ongaro & Ousterhout (Raft Paper)  
**Domeniu**: Sisteme Distribuite, Consens Tolerant la C?deri & Replicare Replicated State Machine (RSM)

---

## 1. Problema Consensului ?i Teorema FLP
?n sistemele distribuite asincrone cu e?ecuri prin crash, consensul deterministic este imposibil conform teoremei FLP (Fischer, Lynch, Paterson 1985). Raft dep??e?te aceast? barier? utiliz?nd sincronie par?ial? (timeout-uri randomizate: $T_{\text{election}} \in [150, 300]\,\text{ms}$), garant?nd siguran?a (*safety*) necondi?ionat ?i viabilitatea (*liveness*) sub sincronie temporal? rezonabil?.

## 2. Invariantele Fundamentale Raft
1. **Election Safety**: Cel mult un lider poate fi ales ?ntr-un mandat (*term*) dat $t$.
2. **Leader Append-Only**: Un lider nu suprascrie ?i nu taie niciodat? propriile intr?ri de log; doar adaug? intr?ri noi.
3. **Log Matching Property**: Dac? dou? jurnale con?in o intrare cu acela?i index ?i mandat, ele sunt identice pe toate intr?rile p?n? la acel index.
4. **Leader Completeness**: Dac? o intrare de log este confirmat? (*committed*) ?ntr-un mandat $t$, acea intrare va fi prezent? ?n jurnalele tuturor liderilor mandata?i ulterior cu $t' > t$.
5. **State Machine Safety**: Dac? un server a aplicat o intrare de log la un index ma?inii sale de stare, niciun alt server nu va aplica vreodat? o intrare diferit? la acel index.

## 3. Replicarea Log-urilor ?i Quorum
Fiecare tranzi?ie de stare este emis? de nodul Client c?tre Lider. Liderul scrie intrarea ?n log-ul local ?i trimite `AppendEntriesRPC` c?tre to?i Urm?ritorii (*Followers*). O intrare este considerat? `committed` imediat ce a fost replicat? pe o majoritate strict? de noduri:
$$W + R > N \quad \text{unde } W = \lfloor N/2 \rfloor + 1$$
Odat? confirmat?, comanda este executat? pe State Machine local? ?i rezultatul este ?ntors clientului. ?n cazul c?derii liderului, noul lider for?eaz? urm?torii s? ??i suprascrie log-urile neconfirmate pentru a coincide perfect cu ale sale.

## 4. Leg?turi Canonice & Graf de Cuno?tin?e
- [[DDIA_Distributed_Storage_Reliability]]
- [[ADVANCED_DDIA_Replication_Consensus_Streaming]]
- [[EXPERT_DDIA_LSM_Trees_SSTables_and_Compaction]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
- [[07 Knowledge Domains Map]]
