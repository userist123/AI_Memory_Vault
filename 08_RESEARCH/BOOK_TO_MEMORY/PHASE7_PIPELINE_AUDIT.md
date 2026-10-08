# Audit Inițial — Faza 7: Book-to-Memory End-to-End Pipeline & Pilot

**Document Version**: 1.0.0  
**Phase**: Phase 7 — End-to-End Pipeline & Book Pilot Ingestion  
**Branch**: `research/book-to-memory-phase7-pipeline`  
**Parent HEAD**: `7bfc5ff61` (`feat(book-to-memory): implement Phase 6 Retrieval and Working Memory Validation`)  
**Audit Timestamp**: 2026-10-04T03:35:00+03:00  

---

## 1. Context și Evaluarea Componentelor Moștenite (Fazele 1–6)

La intrarea în Faza 7, toate subsistemele fundamentale cerute de `00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md` sunt implementate, izolate și acoperite cu teste unitare și adversariale la nivel de producție:

1. **Faza 1 — Ontologie & Scheme Atomice** (`book_to_memory_schema.py`):
   - Cele 11 tipuri atomice: `book_map`, `concept`, `procedure`, `rule`, `pattern`, `pitfall`, `metric`, `example`, `problem`, `conflict`, `repro_test`.
   - Separare epistemică strictă (`FACT`, `INTERPRETATION`, `HYPOTHESIS`, `EXPERIMENT`, `ENGINEERING_MECHANISM`).
   - Verificarea provenienței (`source_title`, `chapter`, `page_range`, `exact_page` pentru formule/metrice) și blocarea directivelor executabile untrusted.
2. **Faza 2 — Porțile de Ciclu de Viață** (`book_to_memory_lifecycle.py`):
   - Porțile canonice `GATE-01..GATE-08`.
   - Tranzițiile `RAW -> UNVERIFIED -> VERIFIED -> ACTIVE` cu token HMAC semnat de Owner pentru `ACTIVE`.
   - Demotare reversibilă automată în caz de regres.
3. **Faza 3 — Registrul de Conflicte** (`book_to_memory_conflict.py`):
   - `ConflictRegistry`, identitate canonică `CONFLICT-<domain>-<slug>`.
   - Păstrarea pozițiilor duale (Claim A și Claim B) fără ștergere tacită.
   - Blocarea stării `ACTIVE` dacă există un conflict deschis `HIGH`/`CRITICAL`.
4. **Faza 4 — Testul de Utilizare Bazat pe Sarcini** (`book_to_memory_usage_test.py`):
   - `TaskSpecification` (structură imutabilă anti-memorare).
   - `TaskBasedValidator` cu grila canonică în 5 dimensiuni (prag minim 8/10).
5. **Faza 5 — Ablația Pereche With-Note vs Without-Note** (`book_to_memory_ablation.py`):
   - `AblationExperimentRunner` cu condițiile `WITH_NOTE` și `WITHOUT_NOTE`.
   - Formula canonică $\text{Delta} = (S_{\text{cu}} - S_{\text{fără}}) / S_{\text{fără}}$, cerință $\text{Delta} \ge 0$.
6. **Faza 6 — Validarea de Retrieval & Working Memory** (`book_to_memory_retrieval.py`):
   - Principiul „QUERY MUST MATTER”, filtrare negativă, limite de candidați.
   - Admiterea în `WorkingMemory` pe baza scorului de atenție și respectarea plafoanelor de capacitate și tokeni (`hard_token_cap`).
   - Încapsularea în delimitatori pasivi `<!-- BEGIN UNTRUSTED INERT MEMORY CONTEXT -->`.

### Teste Moștenite Validate:
- **Total Book-to-Memory (F1–F6)**: **181 / 181 teste PASS**
- **Securitate & Invariante Core**: **23 / 23 teste PASS**
- **Total Suite Active**: **204 / 204 teste PASS**

---

## 2. Inventarul Cărților Noi din `06_INBOX/Carti/Altele`

În directorul `06_INBOX/Carti/Altele` au fost adăugate lucrări de referință în domeniul ingineriei software, sistemelor distribuite, AI și științelor cognitive:
1. `Accelerate: The Science of Lean Software and DevOps` (Nicole Forsgren, Jez Humble, Gene Kim);
2. `Designing Data-Intensive Applications` (Martin Kleppmann);
3. `Thinking, Fast and Slow` (Daniel Kahneman);
4. `Deep Learning` (Ian Goodfellow, Yoshua Bengio, Aaron Courville);
5. `Designing Machine Learning Systems` (Chip Huyen);
6. `Reinforcement Learning: An Introduction` (Richard S. Sutton, Andrew G. Barto);
7. `A Philosophy of Software Design` (John Ousterhout);
8. `Making Software: What Really Works` (Andy Oram, Greg Wilson);
9. Lucrări & rapoarte DORA / RAG architecture (2018–2026).

---

## 3. Obiectivele Fazei 7

1. **Unificarea Pipeline-ului (`book_to_memory_pipeline.py`)**:
   - Construirea orchestratorului `BookToMemoryPipeline` care execută secvențial etapele definite de `POLICY-LEARNING-QUALITY-02.md`:
     `PROBLEMA -> CARTE/HARTA -> EXTRACTIE ATOMICA -> SCHEME GATE -> LIFECYCLE GATE -> CONFLICT CHECK -> USAGE TEST -> ABLATION TEST -> RETRIEVAL PACK`.
2. **Execuția unui Pilot Reversibil**:
   - Generarea Hărții de Carte (`book_map`) pentru pilotul `Accelerate` (Nicole Forsgren et al.) și `Thinking, Fast and Slow` (Daniel Kahneman).
   - Extragerea de concepte atomice (ex: `metric: Four Key Metrics / Lead Time for Changes`, `concept: System 1 and System 2 Cognitive Modes`).
   - Trecerea completă a notelor prin validare până la starea `VERIFIED`.
   - **Garantarea că nicio notă nu trece în `ACTIVE` fără token HMAC explicit semnat de Owner**.
3. **Suită de Teste End-to-End**:
   - `20_TESTS/test_book_to_memory_pipeline.py` acoperind toate ramurile și scenariile de eșec.
