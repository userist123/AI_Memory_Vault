# Audit Inițial — Faza 8: Book-to-Memory Corpus Catalog & Reversible Consolidation

**Document Version**: 1.0.0  
**Phase**: Phase 8 — Corpus Catalog & Knowledge Consolidation  
**Branch**: `research/book-to-memory-phase8-consolidation`  
**Parent HEAD**: `41463ab18` (`feat(book-to-memory): implement Phase 7 End-to-End Pipeline & Pilot Validation`)  
**Audit Timestamp**: 2026-10-04T12:56:00+03:00  

---

## 1. Evaluarea Stării Curente la Intrarea în Faza 8

La finalul Fazei 7, întregul lanț funcțional prescris de `00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md` este implementat și acoperit prin 197 de teste unitare și de integrare PASS:
- **Faza 1 (Scheme & Ontologie)**: 11 tipuri atomice + separare epistemică strictă.
- **Faza 2 (Porți Ciclu de Viață)**: `GATE-01..GATE-08` + token HMAC obligatoriu pentru `ACTIVE`.
- **Faza 3 (Registru de Conflicte)**: Detectarea contradicțiilor deschise de severitate `HIGH`/`CRITICAL` care blochează `ACTIVE`.
- **Faza 4 (Test de Utilizare)**: Grila în 5 dimensiuni, prag minim $\ge 8/10$.
- **Faza 5 (Ablație Pereche)**: Condițiile `WITH_NOTE` vs `WITHOUT_NOTE`, verificare $\text{Delta} \ge 0$.
- **Faza 6 (Regăsire & Working Memory)**: Bounded context pack, delimitatori pasivi de memorie, excludere texte brute din cărți.
- **Faza 7 (Pipeline End-to-End & Pilot)**: Clasa orchestratoare `BookToMemoryPipeline` verificată pe *Thinking, Fast and Slow* și *Design for a Brain*.

---

## 2. Problema Identificată pentru Faza 8

Deși `BookToMemoryPipeline` poate valida orice notă individuală sau hartă de carte, în repository există o discrepanță între vechile schițe de hărți (`BOOK-*-map.md`) din `08_RESEARCH/BOOK_TO_MEMORY/` și standardul canonic `type: book_map` din `POLICY-LEARNING-QUALITY-02` Secțiunea 2:
1. Vechile fișiere `BOOK-*-map.md` sunt documente nestructurate de text liber, fără schema formală cerută de `book_to_memory_schema.py`.
2. Nu există un catalog centralizat (`BookToMemoryCatalog`) care să stocheze, interogheze și sincronizeze hărțile canonice cu notele atomice validate.
3. Nu există o metodă automatizată de calcul al acoperirii capitolelor (`chapter_coverage`) și de raportare a golurilor de cunoaștere (`coverage gaps`) conform cerințelor din `POLICY-02`.

---

## 3. Obiectivele Fazei 8

1. **Implementarea `BookToMemoryCatalog`**:
   - Modul: `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_catalog.py`.
   - Înregistrarea, validarea și gestionarea tamper-evident a hărților canonice de cărți (`type: book_map`).
   - Asocierea bidirecțională între notele atomice validate și hărțile de carte părinte (`map-<source_identity>`).
   - Calculul acoperirii per carte și identificarea capitolelor neacoperite.
2. **Materializarea Hărților Canonice pentru Corpusul din `06_INBOX/Carti`**:
   - *Thinking, Fast and Slow* (Daniel Kahneman, 2011)
   - *Design for a Brain* (W. Ross Ashby, 1952)
   - *An Introduction to Cybernetics* (W. Ross Ashby, 1956)
   - *The Soar Cognitive Architecture* (John E. Laird, 2012)
   - *Memory Systems 1994* (Daniel L. Schacter, Endel Tulving, 1994)
   - *Memory: From Mind to Molecules* (Larry R. Squire, Eric R. Kandel, 2000)
   - *The Molecular Biology of Memory Storage* (Eric R. Kandel, 2001)
3. **Garanții Invariante**:
   - Zero auto-promovare în `ACTIVE`: toate hărțile și notele catalogate rămân strict în starea `VERIFIED` (`AWAITING_OWNER_APPROVAL`).
   - Zero text brut de carte admis în indexul de regăsire activă.
   - Izolarea directivelor executabile untrusted.
4. **Validare & Raportare**:
   - Suită dedicată `20_TESTS/test_book_to_memory_catalog.py`.
   - Rapoarte de arhitectură, registru și finalizare în `08_RESEARCH/BOOK_TO_MEMORY/`.
