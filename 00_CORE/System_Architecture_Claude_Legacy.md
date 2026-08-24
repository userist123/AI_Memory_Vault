---
id: "8769e1a9-045f-4153-8c18-04b938a407bc"
type: core
lifecycle: ARCHIVED
category: imported-legacy
tags: [legacy-import, core]
created: 2026-08-17T20:24:39Z
updated: 2026-08-17T20:24:39Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/claude_original/00_CORE__System_Architecture.md"
confidence: medium
verification: inferred
enriched_by: ai
---

# System Architecture — Structura Vault-ului

## Principiu
Structură PARA-adaptată, gândită ca bază de memorie pentru AI (nu doar note-taking). Fiecare folder are un rol clar în pipeline-ul: **Capture → Clasificare → Cunoaștere structurată → Retrieval**.

## Harta Folderelor

| Folder | Rol | Analog PARA |
|---|---|---|
| `00_CORE/` | Context de ancoră — Identity, Rules, Goals, Architecture. Se citește la fiecare sesiune | — (meta) |
| `01_KNOWLEDGE/` | Fapte stabile, non-time-sensitive: stack tehnic, concepte, referințe | Resources (stabile) |
| `02_PROJECTS/` | Proiecte active cu deadline/obiectiv concret | Projects |
| `03_PROCEDURES/` | Cum se face un lucru, repetabil, pas-cu-pas | Areas (procese) |
| `04_MEMORY/` | Memorie clasificată: Decisions, Experiences, Errors, Lessons, Preferences | — (specific AI-memory) |
| `05_RESOURCES/` | Linkuri, tool-uri, materiale externe | Resources |
| `06_INBOX/` | Capture nesortat — punct de intrare pentru orice notă nouă | Inbox |
| `90_TEMPLATES/` | Template-uri Templater pentru fiecare tip de notă | — (meta) |
| `99_SYSTEM/` | Documentație despre vault: RAG, Knowledge Graph, taxonomie | — (meta) |

## De ce `04_MEMORY` e separat de `01_KNOWLEDGE`
`01_KNOWLEDGE` = adevăruri stabile ("Python e limbajul X"). `04_MEMORY` = experiență acumulată în timp, cu context temporal și decizional ("am decis X pe Y, pentru că Z; a mers/n-a mers"). AI-ul are nevoie de ambele, dar nu trebuie amestecate — knowledge nu expiră, memory da.

## Convenție Frontmatter (obligatorie pe orice notă)
```yaml
---
type: core | knowledge | project | procedure | memory | resource | inbox
category: <subcategorie liberă>
tags: [tag1, tag2]
created: YYYY-MM-DD
updated: YYYY-MM-DD
status: active | archived | draft
priority: critical | high | medium | low   # opțional
related: ["[[Nota1]]", "[[Nota2]]"]         # opțional, pt Knowledge Graph
---
```

## Convenție Naming
- Fișiere: `PascalCase_Cu_Underscore.md` (fără spații, fără diacritice în nume de fișier)
- Foldere numerotate (`00_`, `01_`...) pentru ordine vizuală în file explorer
- Note index (`_Index.md`) în foldere cu multe fișiere, cu query Dataview

## Linking & Knowledge Graph
- Orice notă nouă → minim 1 link către `00_CORE` sau către o notă existentă relevantă (evită noduri izolate în graph)
- Folosește `[[wikilink]]`, nu linkuri markdown standard, pentru ca Graph View și backlinks să funcționeze
- Tag-urile sunt taxonomie orizontală (subiect), folderele sunt taxonomie verticală (tip) — nu duplica una prin cealaltă

## Dependențe Plugin Obsidian
- **Templater** — generare note din `90_TEMPLATES/`
- **Dataview** — indexuri dinamice (`_Index.md` în Projects, Memory)
- Recomandat pentru RAG: **Smart Connections** sau export periodic pentru embedding extern (detalii în `99_SYSTEM/RAG_KnowledgeGraph_Architecture.md`)

---
*Secțiune următoare: `01_KNOWLEDGE/`*

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
