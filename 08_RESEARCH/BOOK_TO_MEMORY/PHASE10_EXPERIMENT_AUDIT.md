# Audit Inițial — Faza 10: Controlled Experimentation Harness & Shadow Mode Execution

**Document Version**: 1.0.0  
**Phase**: Phase 10 — Controlled Experimentation Harness & Shadow Mode Execution  
**Branch**: `research/book-to-memory-phase10-experiment-harness`  
**Parent HEAD**: `46212022e` (Phase 9 validat)  
**Status**: IN PROGRESS  
**Date**: 2026-10-04  

---

## 1. Context și Motivație

În Faza 9 s-a implementat registrul de ipoteze (`BookToMemoryHypothesisRegistry`), care gestionează stările formale ale ipotezelor conform cerinței din `POLICY-LEARNING-QUALITY-02` Secțiunea 14:
$$\text{BIOLOGICAL FACT} \longrightarrow \text{ENGINEERING HYPOTHESIS} \longrightarrow \text{CONTROLLED EXPERIMENT} \longrightarrow \text{VAULT MECHANISM}$$

Cele 5 ipoteze inițiale (`H1-BOOK-001..005`) se află în prezent în starea `HYPOTHESIS_READY`.
Pentru a progresa prin lanțul de dovezi fără a periclita stabilitatea seifului cognitiv, este necesar un **Harness de Experimentare Controlată în Shadow-Mode** (`BookToMemoryExperimentHarness`).

---

## 2. Invariante Obligatorii pentru Faza 10

Conform contractului `RESEARCH-TRACK-CONTRACT.md` (Secțiunile 88–148) și `POLICY-LEARNING-QUALITY-02`:

1. **Shadow Mode Obligatoriu (Secțiunea 130)**:
   - Orice mecanism care modifică căutarea, ranking-ul, deduplicarea sau sinteza de memorie trebuie evaluat exclusiv pe un strat de simulare/copie înghețată.
   - Nicio modificare nu se aplică direct peste indexul sau memoria de producție în timpul experimentului.
2. **Evaluare Pereche (Paired Evaluation - Regula 1)**:
   - Fiecare caz de testare este evaluat sub două brațe identice: `control_arm` (fără mecanism) vs. `variant_arm` (cu mecanism).
   - Se raportează atât diferența absolută (`absolute_delta`), cât și cea relativă (`relative_delta`).
3. **Eșantionare Minimă Robustă (Anti-Gaming Regula 2 & 6)**:
   - Numărul minim de cazuri per experiment este $\ge 5$ (`sample_count >= 5`).
   - Rulările eșuate sau degradate nu sunt ignorate sau eliminate din medii (`failure_count >= 0`).
4. **Bariera Criptografică de Atestare Umană (I-004)**:
   - `Principal.AI_AGENT` nu poate aproba promovarea în `CLOSED_CHANGE_VALIDATED`.
   - Doar `Principal.HUMAN` sau `Principal.ADMIN` are autoritatea de a semna pachetul de decizie.
5. **Zero Modificări în `main`**:
   - Toată activitatea rămâne strict izolată pe branch-ul dedicat `research/book-to-memory-phase10-experiment-harness`.

---

## 3. Planul de Implementare pentru Faza 10

1. **Modul Implementare**: `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_experiment.py`
   - Clasa `ExperimentConfig` cu validare strictă a cazurilor și parametrilor.
   - Clasa `ExperimentResult` cu metrici pereche, delte absolute/relative și calcul hash/digest.
   - Clasa `BookToMemoryExperimentHarness` orchestrând FSM-ul:
     `HYPOTHESIS_READY` $\to$ `EXPERIMENT_READY` $\to$ `EVIDENCE_PENDING` $\to$ `EVIDENCE_AVAILABLE` $\to$ `DECISION_PENDING` $\to$ `CLOSED_CHANGE_VALIDATED` / `CLOSED_NO_CHANGE`.
2. **Specificație Arhitecturală**: `08_RESEARCH/BOOK_TO_MEMORY/PHASE10_EXPERIMENT_ARCHITECTURE.md`.
3. **Suită de Teste**: `20_TESTS/test_book_to_memory_experiment.py` (acoperire completă a tranzițiilor, anti-gaming, shadow mode și porți de securitate).
4. **Raport de Finalizare**: `08_RESEARCH/BOOK_TO_MEMORY/PHASE10_COMPLETION_REPORT.md`.
