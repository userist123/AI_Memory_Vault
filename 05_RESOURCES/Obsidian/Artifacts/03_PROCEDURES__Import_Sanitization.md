---
type: procedure
category: vault-maintenance
tags: [procedure, import, sanitization, memory]
created: 2026-08-09
updated: 2026-08-09
status: active
priority: high
---

# Procedură: Sanitizare Import Memorie Externă

## Scop
Curățare conversații exportate din ChatGPT/Gemini/alte instanțe Claude înainte de a intra în `04_MEMORY/`.

## Pași

1. **Export brut** → salvat temporar în `06_INBOX/_raw_imports/` (nu direct în Memory)
2. **Filtrare zgomot**
   - [ ] Elimină conversații redundante (același subiect, fără informație nouă)
   - [ ] Elimină informații expirate (versiuni vechi, decizii suprascrise)
   - [ ] Elimină small talk fără valoare de context
3. **Clasificare pe categorie** (vezi `04_MEMORY/README.md`)
   - Knowledge → dacă e fapt stabil, mută în `01_KNOWLEDGE/`
   - Decision → `04_MEMORY/Decisions/`
   - Experience → `04_MEMORY/Experiences/`
   - Error → `04_MEMORY/Errors/`
   - Lesson → `04_MEMORY/Lessons/`
   - Preference → `04_MEMORY/Preferences/`
4. **Verificare securitate** — obligatoriu înainte de commit final
   - [ ] Zero date operaționale/clasificate (context militar)
   - [ ] Zero date personale ale terților
5. **Frontmatter + linking** — aplică template-ul corespunzător din `90_TEMPLATES/`, adaugă minim 1 link relevant
6. **Ștergere raw import** din `06_INBOX/_raw_imports/` după procesare completă

## Anti-pattern
❌ Import direct, în masă, fără trecere prin pașii 2-4 → duce la vault poluat, retrieval degradat pentru AI.
