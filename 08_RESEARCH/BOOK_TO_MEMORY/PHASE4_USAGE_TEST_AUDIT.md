# Audit de Intrare — Faza 4: Usage Test / Task-Based Validation

**Document Version**: 1.0.0  
**Phase**: Phase 4 — Usage Test / Task-Based Validation  
**Branch**: `research/book-to-memory-phase4-usage-test`  
**Parent HEAD**: `ab0a7c7ac` (`feat(book-to-memory): implement Phase 3 Conflict Registry and Policy-02 contradiction gates`)  
**Audit Timestamp**: 2026-10-04T00:27:00+03:00  

---

## 1. Inspectarea HEAD-ului și a Arhitecturii Moștenite

HEAD-ul curent este `ab0a7c7ac`, conținând integral implementările și testele verificate ale Fazelor 1, 2 și 3:
1. `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_schema.py` (Faza 1):
   - Prezent și verificat;
   - Definește cele 11 tipuri atomice de note, lanțul epistemic, porțile de securitate și proveniență.
2. `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_lifecycle.py` (Faza 2):
   - Prezent și verificat;
   - Implementează stările `RAW -> UNVERIFIED -> VERIFIED -> ACTIVE` (cu `REJECTED`), porțile `GATE-01..GATE-08`, token-ul HMAC semnat de Owner (`OwnerApprovalToken`) și demotarea reversibilă.
3. `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_conflict.py` (Faza 3):
   - Prezent și verificat;
   - Implementează `ConflictRegistry`, identitatea deterministă `CONFLICT-<domain>-<slug>`, prezervarea duală, taxonomia de severitate/rezoluție și blocajul stării `ACTIVE`.

### Teste Validate Moștenite
- `20_TESTS/test_book_to_memory_schema.py`: 36 / 36 PASS
- `20_TESTS/test_book_to_memory_lifecycle_gates.py`: 32 / 32 PASS
- `20_TESTS/test_book_to_memory_conflicts.py`: 33 / 33 PASS
- Teste cumulate Book-to-Memory: **101 / 101 PASS**
- Invariante de Securitate Core (`P0..P15`): **29 / 29 PASS**
- Lifecycle & Promotion Gates: **207 / 207 PASS**

---

## 2. Auditul Politicii Autoritative (`POLICY-LEARNING-QUALITY-02.md`)

Secțiunea 6 a politicii autoritative stabilește cadrul normativ obligatoriu pentru Usage Test:
1. **Definiție**: *"Agentul rezolvă un task realist folosind doar nota și dependențele declarate, fără acces la carte."*
2. **Grila de Evaluare** (5 criterii x 2 puncte = maximum 10 puncte):
   - **Corectitudine**: 0–2
   - **Completitudine**: 0–2
   - **Fără ghicit**: 0–2
   - **Fără surse externe**: 0–2
   - **Reproductibilitate**: 0–2
3. **Praguri de Trecere**:
   - Scor total: **>= 8/10**
   - Condiție obligatorie: **minimum 1 la Corectitudine** (un scor de 8 obținut cu 0 la corectitudine este invalid).
4. **Acțiuni pe Baza Rezultatului**:
   - `score >= 8`: `PASS` (nota devine eligibilă pentru starea `VERIFIED`);
   - `5 <= score <= 7`: Îmbunătățire și retestare necesară;
   - `score < 5`: Respingere (`REJECTED`) sau rescriere completă.
5. **Multi-Agent Requirement**:
   - Pentru notele importante: evaluare de către **minimum doi agenți/evaluatori diferiți**, cu rezultate separate, aceleași criterii și fără partajarea rezultatelor înaintea evaluării.

---

## 3. Auditul Componentelor Existente în Repozitoriu

### Ce există deja:
1. **Proprietăți în Schemă**:
   - `03_IMPLEMENTATION/packages/lifecycle/validation/schema.py` recunoaște `usage_test_score` și `usage_test_accuracy` ca metadate opționale de validare.
2. **Garanția din Lifecycle (GATE-05)**:
   - `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_lifecycle.py` linia 224: `GATE-05` blochează tranziția spre `VERIFIED` și `ACTIVE` dacă `usage_score < 8` sau este `None`.
3. **Ablation Benchmark General**:
   - `40_EXPERIMENTS/harnesses/memory_ablation_benchmark.py` conține `MemoryAblationExperimentRunner` pentru măsurarea performanței retrieval vs zero-retrieval la nivel de vault global.

### Ce lipsește (Gaps identificate):
1. **Engine Dedicat de Validare Task-Based (Usage Test)**:
   - Nu există un modul specific `book_to_memory_usage_test.py` care să execute și să valideze probele de task pentru note atomice.
2. **Modelul de Date Formal pentru Usage Test**:
   - Structura completă a unei încercări (`note_id`, `task_id`, `task_description`, `allowed_context`, `model/agent`, `attempt`, `answer`, `expected_criteria`, `score`, `max_score`, `evidence`, `failure_reason`, `timestamp`, `evaluator`, `source_provenance`) nu este definită ca clasă sau schemă de validare.
3. **Granița de Izolare a Sursei (Source Isolation Barrier)**:
   - Nu există un mecanism de inspecție și blocare a accesului la cartea sursă, fișiere PDF, scanări brute sau interogări externe în timpul rulării task-ului.
4. **Protecția Împotriva Manipulării Scorului și Task-ului**:
   - Lipsesc gardurile de imutabilitate care împiedică un `AI_AGENT` sau o notă cu payload de prompt injection să suprascrie criteriile de evaluare, să altereze scorul brut sau să elimine eșecurile.
5. **Multi-Agent Evaluation Harness**:
   - Nu există un validator structurat care să asigure izolarea și non-contaminarea evaluărilor realizate de doi agenți distincți.

---

## 4. Planul de Implementare pentru Faza 4

Pentru a acoperi toate cerințele din prompt și `POLICY-02`:
1. Crearea modulului:
   `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_usage_test.py`
   - Clasa `TaskSpecification` (imutabilă, definită înainte de rulare);
   - Clasa `RubricScore` (Corectitudine, Completitudine, Fara ghicit, Fara surse externe, Reproductibilitate);
   - Clasa `UsageTestResult` (rezultat brut, semnătură hash a încercării, audit trail complet);
   - Clasa `TaskBasedValidator` (izolare strictă a contextului, blocare acces la PDF/carte, verificare praguri, evaluare multi-agent fără contaminare);
2. Dezvoltarea suitei de teste `20_TESTS/test_book_to_memory_usage_test.py`:
   - Toate cele 10 scenarii negative obligatorii;
   - Toate cazurile de atac adversarial (prompt injection, încercare de modificare a evaluatorului, cerere de acces la sursă, autopromovare);
   - Testarea evaluării multi-agent;
   - Integrarea cu lifecycle (`GATE-05`) și independența față de conflicte.
3. Elaborarea rapoartelor de arhitectură, securitate și finalizare.
