# Audit Inițial — Faza 9: Matricea de Trasabilitate a Problemelor & Evaluarea Ipotezelor

**Document Version**: 1.0.0  
**Phase**: Phase 9 — Problem Matrix Traceability & Hypothesis Validation Engine  
**Branch**: `research/book-to-memory-phase9-hypothesis`  
**Parent HEAD**: `b0cf20727` (`feat(book-to-memory): implement Phase 8 Corpus Catalog & Reversible Consolidation`)  
**Audit Timestamp**: 2026-10-04T13:08:00+03:00  

---

## 1. Evaluarea Stării la Intrarea în Faza 9

La finalul Fazei 8, infrastructura de ontologie, ciclu de viață, registre de conflicte, testare pe sarcini, ablație pereche, regăsire delimitată, pipeline complet și catalogul monografiilor canonice sunt 100% validate (211 teste PASS):
- `book_to_memory_schema.py`
- `book_to_memory_lifecycle.py`
- `book_to_memory_conflict.py`
- `book_to_memory_usage_test.py`
- `book_to_memory_ablation.py`
- `book_to_memory_retrieval.py`
- `book_to_memory_pipeline.py`
- `book_to_memory_catalog.py`

Cu toate acestea, `00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md` Secțiunea 14 și `08_RESEARCH/BOOK_TO_MEMORY/RESEARCH-TRACK-CONTRACT.md` impun o cerință epistemică majoră:
> „Niciun fapt biologic sau mecanism cognitiv nu este implementat în producție doar pentru că este descris într-o carte.
> Orice ipoteză derivată din literatură urmează lanțul:
> `BIOLOGICAL FACT -> ENGINEERING HYPOTHESIS -> EXPERIMENT -> VAULT MECHANISM`”

---

## 2. Problema Adresată în Faza 9

1. **Lipsa unui motor de trasabilitate tipizată**:
   - `BOOK_TO_HYPOTHESIS_MAPPING.md` conține ipoteze în format YAML text, dar nu există un modul Python executabil care să valideze schemele acestor ipoteze, să le lege de cele 16 probleme din `PROBLEM_MATRIX.md` și să asigure tranziția stărilor de evidență:
     `SOURCE_PENDING -> HYPOTHESIS_READY -> EXPERIMENT_READY -> EVIDENCE_PENDING -> EVIDENCE_AVAILABLE -> DECISION_PENDING -> CLOSED_CHANGE_VALIDATED / CLOSED_NO_CHANGE`.
2. **Prevenirea salturilor neautorizate (Anti-Bypass Guard)**:
   - Fără un validator formal, există riscul ca o ipoteză să sară direct de la `HYPOTHESIS_READY` la mecanism de producție fără date experimentale înghețate și fără decizie formală.
3. **Regulile Anti-Gaming**:
   - `RESEARCH-TRACK-CONTRACT.md` impune ca nicio ipoteză să nu fie declarată „câștigătoare” exclusiv pe medii agregate sau ignorând rulările eșuate.

---

## 3. Obiectivele Fazei 9

1. **Construirea Modulului `BookToMemoryHypothesisRegistry`**:
   - Fișier: `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_hypothesis.py`.
   - Înregistrarea tipizată a ipotezelor inginerești cu verificarea sursei, locației exacte (capitol/pagină), principiului, predicției falsificabile, controlului, metricii și pragurilor de succes/eșec.
   - Verificarea mașinii de stări a lanțului de evidențe (`TrackState`).
   - Înregistrarea deciziilor experimentale (`DecisionRecord`) cu interzicerea omiterii eșecurilor.
2. **Sincronizarea cu `PROBLEM_MATRIX.md`**:
   - Maparea celor 16 probleme canonice și calculul indicelui de rezoluție a dovezilor.
3. **Conectarea cu `BookToMemoryCatalog`**:
   - Asigurarea că fiecare ipoteză este legată de o monografie validată din catalog.
4. **Validare & Suită de Teste**:
   - `20_TESTS/test_book_to_memory_hypothesis.py` acoperind toate regulile de guvernanță, anti-gaming și tranziții interzise.
   - Rapoarte de arhitectură, matrice și finalizare în `08_RESEARCH/BOOK_TO_MEMORY/`.
