# Audit Inițial de Intrare — Faza 5: With-Note vs Without-Note Ablation

**Document Version**: 1.0.0  
**Phase**: Phase 5 — With-Note vs Without-Note Ablation  
**Branch**: `research/book-to-memory-phase5-ablation`  
**Parent HEAD**: `1e81c2250` (`feat(book-to-memory): implement Phase 4 Usage Test and Policy-02 Task-Based Validation`)  
**Audit Timestamp**: 2026-10-04T00:33:30+03:00  

---

## 1. Inspectarea HEAD-ului și a Componentelor Existente (Fazele 1–4)

HEAD-ul validat al Fazei 4 este `1e81c2250`, având un working tree curat și integrând componentele:
1. `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_schema.py` (Faza 1):
   - Cele 11 tipuri atomice, separarea epistemică, verificarea provenienței și neutralitatea datelor pasive.
2. `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_lifecycle.py` (Faza 2):
   - Porțile de tranziție `GATE-01..GATE-08`, token-ul HMAC semnat de Owner (`OwnerApprovalToken`), demotarea reversibilă și verificarea `GATE-07 Ablation` (`ablation_delta >= 0`).
3. `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_conflict.py` (Faza 3):
   - `ConflictRegistry`, identitatea deterministă `CONFLICT-<domain>-<slug>`, păstrarea pozițiilor duale și blocajul stării `ACTIVE`.
4. `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_usage_test.py` (Faza 4):
   - `TaskSpecification` (structură imutabilă anti-memorare), `TaskBasedValidator`, grila canonică de notare (5 dimensiuni, max 10), bariera de izolare a sursei și audit trail semnat SHA-256.

### Teste Validate Moștenite:
- `20_TESTS/test_book_to_memory_schema.py`: 36 / 36 PASS
- `20_TESTS/test_book_to_memory_lifecycle_gates.py`: 32 / 32 PASS
- `20_TESTS/test_book_to_memory_conflicts.py`: 33 / 33 PASS
- `20_TESTS/test_book_to_memory_usage_test.py`: 34 / 34 PASS
- **Total Book-to-Memory**: **135 / 135 PASS**
- **Securitate & Invariante Core (P0..P15)**: **29 / 29 PASS**
- **Lifecycle & Candidate Gates**: **207 / 207 PASS**
- **Total Teste Active**: **371 / 371 PASS**

---

## 2. Auditul Politicii Autoritative (`POLICY-LEARNING-QUALITY-02.md`, Secțiunea 7)

Secțiunea 7 a politicii autoritative stabilește cadrul metodologic obligatoriu pentru evaluarea cu/fără notă:
1. **Condiții Experimentale**:
   - Condiția A: `WITHOUT_NOTE` (agent execută sarcina fără nota testată);
   - Condiția B: `WITH_NOTE` (agent execută sarcina cu nota pusă la dispoziție).
2. **Cerințe Minime de Control**:
   - Set fix de sarcini (`TaskSpecification`);
   - Minimum **2 modele/agenți diferiți**;
   - Minimum **3 repetări (trials) per sarcină**;
   - Aceeași grilă (cele 5 dimensiuni din Faza 4) și aceleași setări experimentale.
3. **Formula Matematică Canonică a Ablation Delta**:
   $$\text{Delta} = \frac{S_{\text{cu}} - S_{\text{fără}}}{S_{\text{fără}}}$$
   *Regulă de frontieră specificată explicit*:  
   Dacă numitorul $S_{\text{fără}} = 0$, se raportează diferența absolută:
   $$\text{Delta} = S_{\text{cu}} - S_{\text{fără}}$$
4. **Semnificația Scorului Delta**:
   - $\text{Delta} > 0$: Nota produce o îmbunătățire măsurabilă;
   - $\text{Delta} = 0$: Nicio îmbunătățire incrementală;
   - $\text{Delta} < 0$: Regres de performanță (nota introduce confuzie sau perturbare).
5. **Cerință de Promovare**:
   - `GATE-07 Ablation` din `book_to_memory_lifecycle.py` impune `ablation_delta >= 0` ca precondiție pentru starea `ACTIVE`.

---

## 3. Evaluarea Infrastructurii Existente și Identificarea Gap-urilor

### Ce există:
- Cadrul de evaluare a sarcinilor (`TaskSpecification`, `TaskBasedValidator`, `RubricDimension`) din `book_to_memory_usage_test.py`;
- Poarta `GATE-07` în `book_to_memory_lifecycle.py` care verifică valoarea `ablation_delta`;
- `40_EXPERIMENTS/harnesses/memory_ablation_benchmark.py` (benchmark la nivel de sistem de retrieval complet, nepotrivit direct pentru validarea unitară a notelor atomice din cărți).

### Ce lipsește pentru Faza 5 (Gaps):
1. **Harness Dedicat de A/B Ablation pentru Note Atomice**:
   - Nu există un modul `book_to_memory_ablation.py` care să orchestreze comparația pereche `WITH_NOTE` vs `WITHOUT_NOTE` pe aceeași notă și sarcină;
2. **Mecanism de Izolare a Condiției `WITHOUT_NOTE`**:
   - Trebuie asigurat prin proiectare că în condiția `WITHOUT_NOTE`, nicio urmă a notei (conținut, ID, titlu revelator, cache, prompt anterior) nu pătrunde în contextul agentului;
3. **Controlul Bias-ului de Ordine (Alternating / Order Randomization)**:
   - Rulările trebuie să alterneze ordinea (ex: trial 1: WITHOUT $\to$ WITH, trial 2: WITH $\to$ WITHOUT) folosind un seed determinist;
4. **Calculul și Agregarea Formală a Valorilor Delta**:
   - Calcul separat per model, per condiție, per trial și calcul global agregat cu semnătură SHA-256;
5. **Suită de Teste Adversariale și de Non-Contaminare**:
   - Verificarea că nicio instrucțiune injectată în notă nu poate forța calculul `Delta = +100` sau modifica evaluatorul.

---

## 4. Plan de Implementare Faza 5

1. Modul nou:
   `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_ablation.py`
   - Reutilizează `TaskSpecification` și `TaskBasedValidator` din Faza 4;
   - Definește `AblationCondition` (`WITH_NOTE`, `WITHOUT_NOTE`);
   - Definește `AblationTrial` și `AblationExperimentRecord`;
   - Implementează `calculate_ablation_delta(score_with, score_without)` conform formulei din `POLICY-02`;
   - Implementează `AblationExperimentRunner` cu cerința minimă de 2 modele și 3 repetări (12 trial-uri) și alternare de ordine.
2. Suită de teste nouă:
   `20_TESTS/test_book_to_memory_ablation.py`
   - Toate cele 10 scenarii de atac adversarial (Secțiunea 16);
   - Teste de izolare și non-contaminare;
   - Teste pentru calculul Delta și cazurile de frontieră ($S_{\text{fără}} = 0$);
   - Teste pentru blocajul conflictelor `HIGH`.
3. Rapoarte tehnice și de finalizare.
