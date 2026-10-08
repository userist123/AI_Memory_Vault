# Auditul Punctelor de Intrare (Entry Points Audit) — Faza 2

**Repository:** `userist123/AI_Memory_Vault_CODEX_READY`  
**Branch:** `research/book-to-memory-phase2-lifecycle`  
**Data:** 2026-10-04  
**Obiectiv:** Identificarea tuturor punctelor de creare, modificare, verificare, promovare și scriere a notelor de memorie pentru a garanta că nicio cale alternativă („side door”) nu permite ocolirea porților de calitate și securitate (`RAW -> ACTIVE`, `UNVERIFIED -> ACTIVE`, `AI_AGENT -> VERIFIED`, `AI_AGENT -> ACTIVE`).

---

## 1. Puncte de Intrare Identificate

### 1.1 `MemoryController` (API Canonic de Rulare)
- **`propose(principal, note_data)`**:
  - *Locație:* `03_IMPLEMENTATION/packages/memory/controller.py:1343`
  - *Comportament:*
    - Autorizare `Operation.PROPOSE` prin `DefaultAuthorizer`.
    - Respinge `verification == "verified"` la propunere (impune utilizarea `attest()`).
    - Validează sursa de proveniență conform rolului (`AI_AGENT` limitat la `execution`, `ai`, `inference`, `unknown`).
    - Limitează starea inițială de lifecycle conform `lifecycle_policy`: `AI_AGENT` poate propune doar în `RAW`, `CLASSIFIED`, `NORMALIZED`, `REVIEW`.
    - Respinge injectarea stărilor privilegiate (`ACTIVE`, `VERIFIED`).
    - Validează nota prin `_validate_note()` -> `schema.py` (`validate_frontmatter`).
  - *Concluzie audit:* **Închis pentru promovare directă**.

- **`review(principal, note_id, decision, comments)`**:
  - *Locație:* `03_IMPLEMENTATION/packages/memory/controller.py:1437`
  - *Comportament:*
    - Autorizare `Operation.REVIEW` permisă **doar** pentru `Principal.HUMAN` și `Principal.ADMIN`.
    - `Principal.AI_AGENT` primește `PermissionDeniedError`.
    - Tranziționează nota doar în starea `REVIEW`. Nu atinge `ACTIVE`.
  - *Concluzie audit:* **Securizat împotriva agenților**.

- **`promote(principal, note_id)`**:
  - *Locație:* `03_IMPLEMENTATION/packages/memory/controller.py:1479`
  - *Comportament:*
    - Autorizare `Operation.PROMOTE` permisă **doar** pentru `Principal.HUMAN` și `Principal.ADMIN`.
    - `Principal.AI_AGENT` este respins de `Authorizer`.
    - Permite promovarea în `ACTIVE` doar din `REVIEW` sau `VERIFIED` prin evaluare în `lifecycle_policy`.
  - *Concluzie audit:* **Securizat prin Authorizer și Lifecycle Policy**.

- **`attest(principal, note_id, verification_reason, evidence_reference, ...)`**:
  - *Locație:* `03_IMPLEMENTATION/packages/memory/controller.py:1566`
  - *Comportament:*
    - Autorizare `Operation.ATTEST` permisă **doar** pentru `Principal.HUMAN` și `Principal.ADMIN`.
    - `Principal.AI_AGENT` primește refuz imediat.
    - Cere motiv non-vid și referință de evidență validă.
  - *Concluzie audit:* **Securizat împotriva auto-atestării AI**.

- **`update(principal, note_id, updates, ...)`**:
  - *Locație:* `03_IMPLEMENTATION/packages/memory/controller.py:1508`
  - *Comportament:*
    - Câmpurile `id` și `lifecycle` sunt declarate **strict imutabile** post-creare (`Field lifecycle is immutable`).
    - Respinge `verification == "verified"` via `update()`.
    - Respinge modificarea `provenance.source_type`.
  - *Concluzie audit:* **Imutabilitatea câmpului lifecycle previne escaladarea prin update**.

---

### 1.2 `StorageEngine` (Stratul de Persistență SQLite / In-Memory)
- **`SQLiteStorageEngine.set(note_id, data)`**:
  - *Locație:* `03_IMPLEMENTATION/packages/memory/storage/sqlite_engine.py:140`
  - *Comportament:*
    - Execută `INSERT ... ON CONFLICT(id) DO UPDATE`.
    - Validare mecanică: `CHECK(lifecycle IN ('RAW', 'CLASSIFIED', 'NORMALIZED', 'REVIEW', 'VERIFIED', 'ACTIVE', 'RECONSOLIDATING', 'SUPERSEDED', 'ARCHIVED'))`.
    - *Vulnerabilitate potențială identificată dacă este accesat direct:* `StorageEngine` este un strat tehnic pur, fără conștientizare de `Principal` sau reguli de porți de calitate. Dacă un script sau apelant ocolește `MemoryController` și instanțiază direct `SQLiteStorageEngine`, poate scrie orice stare validă sintactic.
  - *Mitigare existentă & cerință Faza 2:* Accesul direct la `storage` este interzis în runtime-ul de producție; orice barieră de Book-to-Memory trebuie să verifice că notele din spațiul canonic sunt supuse validatorului `book_to_memory_lifecycle` și verificate la integritate.

---

### 1.3 `interfaces/memory_access.py` (Interfața MCP / Recall)
- **`propose(controller, title, body, note_type, ...)`**:
  - *Locație:* `03_IMPLEMENTATION/packages/interfaces/memory_access.py:153`
  - *Comportament:*
    - Forțează `lifecycle = "REVIEW"`, `verification = "unverified"`, `confidence = "low"`.
    - Apelează `controller.propose(Principal.AI_AGENT, note)`.
    - Nu permite specificarea arbitrară a ciclului de viață.
  - *Concluzie audit:* **Fail-closed conform specificațiilor**.

---

### 1.4 Scripturi de Ingestie și Promovare
- **`30_SCRIPTS/ingestion/promote_candidate_concept.py`**:
  - *Comportament:*
    - Promovează candidați din sloturi exclusiv în `01_ARCHITECTURE/knowledge/Promoted_<slug>.md`.
    - Setează forțat `"lifecycle": "REVIEW"` (marcat explicit `# STRICT: NEVER ACTIVE`).
    - Validează nota prin `schema.py`.
    - Necesită manifest de verdicte (`verdicts_file`) și manifest bibliografic pentru curriculum.
  - *Concluzie audit:* **Nu poate produce ACTIVE**.

- **`30_SCRIPTS/verification/active_note_integrity.py`**:
  - *Comportament:*
    - Monitorizează amprenta criptografică SHA-256 a fiecărei note din vault raportate ca `ACTIVE`.
    - Orice fișier Markdown creat sau modificat direct în sistemul de fișiere cu `lifecycle: ACTIVE` generează semnalul critic `ENTERED_ACTIVE` sau `drifted`, blocând testele de integritate și CI-ul până la aprobarea umană explicită via `--record`.
  - *Concluzie audit:* **Protecție criminalistică completă împotriva scrierilor directe în filesystem**.

---

## 2. Matricea de Risc a Punctelor de Intrare

| Punct de Intrare | Risc Teoretic | Mecanism de Blocare Existent | Verificare Faza 2 |
|---|---|---|---|
| `controller.propose()` | Escaladare la ACTIVE | `lifecycle_policy.evaluate()` refuză `ACTIVE` pentru `AI_AGENT` | Testat adversarial |
| `controller.update()` | Modificare stare la ACTIVE | `lifecycle` este imutabil în `update()` | Testat adversarial |
| `controller.promote()` | Apel neautorizat agent | `Authorizer` permite doar HUMAN/ADMIN | Testat adversarial |
| `controller.attest()` | Auto-verificare agent | `Authorizer` permite doar HUMAN/ADMIN | Testat adversarial |
| Direct `storage.set()` | Ocolire controller | Nu este expus agenților; apelabil doar intern | Barieră `book_to_memory_lifecycle` |
| Direct filesystem write | Creare fișier `.md` ACTIVE | Monitorizat de `active_note_integrity.py` | Detectat la audit hash |
| Scripturi de ingestie | Scriere stare greșită | Toate scriu doar `REVIEW` sau `RAW` | Verificat |

---

## 3. Concluzii și Decizii Arhitecturale pentru Faza 2

1. Nu există o cale directă („side door”) prin care un agent AI sau o funcție de asistență poate promova o notă în `ACTIVE`.
2. Pentru a asigura conformitatea totală cu `POLICY-LEARNING-QUALITY-02`, vom implementa modulul formal de porți:
   `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_lifecycle.py`
   care va centraliza validarea porților GATE-01..GATE-08, validarea token-ului de aprobare al owner-ului, și mecanismele de demotare (ACTIVE -> UNVERIFIED).
