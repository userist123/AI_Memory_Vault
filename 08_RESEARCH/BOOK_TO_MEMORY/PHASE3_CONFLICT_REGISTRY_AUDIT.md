# Auditul Suportului Existent pentru Conflicte — Faza 3

**Repository:** `userist123/AI_Memory_Vault_CODEX_READY`  
**Branch:** `research/book-to-memory-phase3-conflicts`  
**Data:** 2026-10-04  
**Obiectiv:** Identificarea tuturor mecanismelor existente de reprezentare, detecție și rezolvare a conflictelor în vederea unificării într-un Conflict Registry formal conform `POLICY-LEARNING-QUALITY-02`.

---

## 1. Componente Existente Identificate în Vault

### 1.1 Câmpul Canonic `conflicts_with`
- **Locație:** `03_IMPLEMENTATION/packages/lifecycle/validation/schema.py:51`
- **Definiție:** `"conflicts_with": {"type": "string", "format": "uuid"}`
- **Comportament:** Permite unei note să indice UUID-ul unei alte note cu care se află în conflict direct.
- **Limitare identificată:** Permite doar o referință unilaterală de tip 1-la-1 (UUID singular); nu stochează natura divergenței, evidența comparativă, severitatea sau starea rezoluției.

### 1.2 `ConflictDetector` (Detecție Heuristică / Consolidare Nocturnă)
- **Locație:** `03_IMPLEMENTATION/packages/security/conflict_detector.py`
- **Comportament:**
  - Analiză lexicală bazată pe coeficient Jaccard de suprapunere tokeni și detecție de polaritate/negație (`_NEGATION_TOKENS`).
  - Strict consultativ (*advisory-only*); nu blochează și nu rezolvă automat conflictele.
- **Relație cu Book-to-Memory:** Poate fi utilizat pentru semnalarea automată a potențialelor conflicte între note noi și note existente, dar nu constituie un registru de adevăr.

### 1.3 `Confidence_Model.md` & Reguli Epistemice
- **Locație:** `00_GOVERNANCE/protocols/Confidence_Model.md`
- **Reguli:** Conflictele nerezolvate coboară nivelul de încredere al afirmației (`confidence`). O afirmație aflată în conflict activ nu poate beneficia de `confidence: very_high` sau `verification: verified` fără coroborare și arbitraj metodologic.

### 1.4 Porți de Ciclu de Viață (`GATE-06 Conflict`)
- **Locație:** `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_lifecycle.py`
- **Regulă:** `GATE-06 Conflict` evaluează registrul de conflicte asociat notei. Orice conflict deschis (`status == "open"`) cu severitate `HIGH` sau `CRITICAL` blochează tranziția notei către starea `ACTIVE`.
- **Demotare:** La apariția unui conflict nou de severitate `HIGH` raportat pe o notă `ACTIVE`, funcția `demote_active_note()` retrogradează starea în `UNVERIFIED` și atașează referința conflictului în `demotion_history`.

---

## 2. Puncte Vulnerabile și Locuri unde Conflictele puteau fi Ignorate (Pre-Faza 3)

1. **Lipsa Identității Deterministe:** Nu exista o schemă formală pentru generarea deterministă a ID-urilor `CONFLICT-<domain>-<slug>`.
2. **Lipsa Înregistrării Duale:** Fără un registru dedicat, exista riscul ca un agent să modifice Claim A pentru a se conforma cu Claim B (sau invers), ștergând divergența științifică.
3. **Închiderea Arbitrară de către Agenți:** Fără control strict al actorului pe tranziția de stare a conflictului, un agent AI ar fi putut seta `"status": "resolved"` pentru a debloca promovarea în `ACTIVE`.
4. **Lipsa Taxonomiei de Rezoluție:** Rezoluția nu poate fi binară („A are dreptate / B greșește”). Este esențial să existe suport pentru rezoluții de tip *context-dependent*, *ambele valide*, *evidență insuficientă*, *sursă caducă* etc.

---

## 3. Planul de Implementare pentru Faza 3

Pentru a închide aceste lacune, Faza 3 implementează:
- Modulul dedicat: `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_conflict.py`
- Identificator determinist stabil: `CONFLICT-<domain>-<slug>` calculat din domeniul și cheile canonice ale afirmațiilor (invariant la ordinea A/B și spații albe).
- Registru imutabil de conflicte: Conservă intacte Claim A, Sursa A, Evidența A, Claim B, Sursa B, Evidența B.
- Porți de rezoluție controlate exclusiv de `Principal.HUMAN` / `Principal.ADMIN`.
