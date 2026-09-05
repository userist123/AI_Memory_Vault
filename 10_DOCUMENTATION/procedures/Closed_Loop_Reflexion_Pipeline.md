---
id: "proc-reflexion-0001"
type: procedure
lifecycle: ACTIVE
category: memory-lifecycle
tags: [reflexion, self-refine, closed-loop, error-resolution, lesson-capture]
created: 2026-08-24T23:20:00Z
updated: 2026-08-24T23:20:00Z
provenance:
  source_type: official
  source_ref: "reflexion-pipeline-spec"
confidence: very_high
verification: verified
relations:
  - "00_GOVERNANCE/protocols/Memory_Protocol.md"
  - "01_ARCHITECTURE/memory/Lessons/"
---

# 🔄 Procedură Canonică: Pipeline de Reflecție Închisă (Closed-Loop Reflexion)

Această procedură definește ciclul automat de reflecție închisă (**Generate → Critique → Revise → Consolidate**) pentru conversia erorilor runtime în lecții și proceduri reutilizabile.

---

## 🔁 1. Ciclul de Reflecție în 4 Etape

```mermaid
graph TD
    Error[A. Eroare Runtime / Eșec Execution] --> Capture[B. Captură Episodică Error Note 01_ARCHITECTURE/memory/Errors/]
    Capture --> Reflexion[C. FormalReflexion & SelfRefine Critique]
    Reflexion --> Lesson[D. Generare Lesson Note 01_ARCHITECTURE/memory/Lessons/]
    Lesson --> Consolidate[E. Consolidare în Procedură Canonică 10_DOCUMENTATION/procedures/]
```

### Etapa A: Captura Erorii
- Când un task întâmpină un eșec sau o aserțiune picată, se creează o notă în `01_ARCHITECTURE/memory/Errors/` cu `type: error`, `lifecycle: REVIEW`, log-ul complet nealterat și atribuirea cauzei rădăcină.

### Etapa B: Critica SelfRefine (`cognitive_core/reflection.py`)
- `CriticAgent` și `VerifierAgent` analizează traiectul eșecului prin masca de reguli `SelfRefine`:
  1. Identificarea cauzei rădăcină (*Root Cause Analysis*).
  2. Verificarea dacă problema este specifică proiectului sau o lecție generală reutilizabilă.

### Etapa C: Extragerea Lecției (`01_ARCHITECTURE/memory/Lessons/`)
- Se generează o notă de tip `lesson` care conține:
  - Contextul problemei;
  - Soluția verificată prin test;
  - Regula de prevenție pe viitor.

### Etapa D: Consolidarea în Procedură (`10_DOCUMENTATION/procedures/`)
- Când două sau mai multe lecții descriu un pattern similar, `Consolidator` le sintetizează automat într-o procedură canonică reutilizabilă în `10_DOCUMENTATION/procedures/`.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[12 Projects and Procedures Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
