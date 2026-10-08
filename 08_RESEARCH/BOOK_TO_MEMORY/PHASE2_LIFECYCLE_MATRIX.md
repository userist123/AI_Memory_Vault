# Matricea Formală de Ciclu de Viață și Actori — Faza 2

**Repository:** `userist123/AI_Memory_Vault_CODEX_READY`  
**Document:** `PHASE2_LIFECYCLE_MATRIX.md`  
**Conformitate:** `00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md`, `03_IMPLEMENTATION/packages/lifecycle/policy.py`

---

## 1. Matricea Formală de Tranziție a Notelor Book-to-Memory

| De la (From) | Către (To) | Permis? | Actor Autorizat | Cerințe și Porți Obligatorii |
|---|---|---|---|---|
| **RAW** | **UNVERIFIED** | **DA** | Ingestor autorizat | `GATE-01` (Schema), `GATE-02` (Provenance), `GATE-03` (Security) |
| **RAW** | **VERIFIED** | **NU** | - | *Interzis:* Trebuie să treacă prin starea intermediară `UNVERIFIED` |
| **RAW** | **ACTIVE** | **NU** | - | *Imposibil:* Ocolire interzisă prin construcție arhitecturală |
| **UNVERIFIED** | **VERIFIED** | **DA** | Verifier autorizat / Evaluator | `GATE-04` (Corroborare >= 1), `GATE-05` (Usage Test score >= 8/10, Accuracy >= 1) |
| **UNVERIFIED** | **ACTIVE** | **NU** | - | *Imposibil:* Trecerea directă fără starea `VERIFIED` este respinsă |
| **VERIFIED** | **ACTIVE** | **DA** | `Principal.HUMAN` / Owner | `GATE-06` (Fără conflict HIGH deschis), `GATE-07` (Ablation Test OK: Delta >= 0), `GATE-08` (Aprobare explicită, netransferabilă a Owner-ului) |
| **ACTIVE** | **VERIFIED** | **NU / Controlat** | Guvernanță | Retragere controlată în caz de revizuire metodologică |
| **ACTIVE** | **UNVERIFIED** | **DA** | Sistem / Guvernanță | Demotare automată la apariția unui conflict HIGH, invalidarea sursei, regresie la test sau invalidare de evidență |
| **Orice stare** | **REJECTED** | **DA** | Guvernanță autorizată | Respingere definitivă (conținut invalidat, eșec repetat la testul de utilizare < 5/10, violare de integritate) |

---

## 2. Matricea de Privilegii a Actorilor (Actor Matrix)

| Operațiune / Poartă | `Principal.AI_AGENT` | `Principal.HUMAN` (Owner) | `Principal.ADMIN` |
|---|---|---|---|
| **Propunere notă (`propose`)** | DA (doar RAW, REVIEW, UNVERIFIED) | DA | DA |
| **Atestare verificare (`attest`)** | **STRICT INTERZIS** | DA (cu motiv & referință evidență) | DA |
| **Promovare (`promote`)** | **STRICT INTERZIS** | DA | DA |
| **Setare `verification="verified"`** | **STRICT INTERZIS** | DA | DA |
| **Setare `lifecycle="ACTIVE"`** | **STRICT INTERZIS** | DA | DA |
| **Acordare Owner Approval** | **STRICT INTERZIS** | DA (verificabil prin token/actor) | DA |
| **Ocolire Conflict Gate** | **STRICT INTERZIS** | **STRICT INTERZIS** (Politica 02 interzice) | **STRICT INTERZIS** |
| **Ocolire Usage Gate (< 8/10)** | **STRICT INTERZIS** | **STRICT INTERZIS** | **STRICT INTERZIS** |
| **Ocolire Ablation Gate** | **STRICT INTERZIS** | **STRICT INTERZIS** | **STRICT INTERZIS** |
| **Modificare Politică de Securitate** | **STRICT INTERZIS** | DA (prin proces de guvernanță) | DA |

---

## 3. Descrierea Porților de Calitate (GATE-01 .. GATE-08)

1. **`GATE-01: Schema Completeness`**  
   - Nota respectă schema JSON Draft-7 pentru tipul său specific (unul dintre cele 11 tipuri Book-to-Memory) și schema de frontmatter canonic.
2. **`GATE-02: Provenance Integrity`**  
   - Conține `source_title`, `chapter`, `page_range` concrete.  
   - Fără mențiuni vagi/lazy (`source unknown`, `probably`, `around chapter`, etc.).  
   - Calculele, formulele, constantele și cerințele critice au specificat `exact_page`.  
   - Experiența proprie este marcată distinct cu `experienta proprie` și context.
3. **`GATE-03: Security & Untrusted Input Isolation`**  
   - Textul cărții este exclusiv în planul de date pasiv.  
   - Nu conține chei de execuție sau directive privilegiate (`tool_call`, `shell_command`, `exec`, `override_lifecycle`, `grant_permission`, etc.).
4. **`GATE-04: Corroboration`**  
   - Cel puțin 1 sursă independentă sau coroborare documentată (în domenii critice precum securitate, medical sau legal, este obligatorie o a doua sursă).
5. **`GATE-05: Usage Test (Test de Utilizare)`**  
   - Agentul rezolvă un task realist folosind doar nota, fără acces la carte.  
   - Scor minim: `>= 8/10`, cu minimum `1` la Corectitudine.
6. **`GATE-06: Conflict Resolution`**  
   - Verifică registrul de conflicte. Niciun conflict deschis (`status == "open"`) de severitate `high` sau `critical` nu poate afecta nota.
7. **`GATE-07: Ablation Evaluation (Evaluare cu/fără Notă)`**  
   - Se măsoară diferența de performanță: `Delta = (S_cu - S_fara) / S_fara >= 0`.  
   - Nicio notă nu este considerată utilă fără demonstrație empirică reproductibilă.
8. **`GATE-08: Explicit Owner Approval`**  
   - Aprobare personală, verificabilă, asociată cu actorul `HUMAN`/`ADMIN`.  
   - Token criptografic/atestare atașată, imposibil de fabricat de un agent AI sau dintr-un text de carte.
