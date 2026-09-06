---
type: index
category: lessons
tags:
- memory
- lessons
created: 2026-08-09
updated: 2026-08-09
status: active
priority: high
id: 62b32d02-4bed-44ff-b94d-97741fbcec87
document_kind: index
document_status: active
provenance_status: incomplete
relations: []
index_scope: vault-navigation
title: Architectural Lessons Registry
---

# Lessons

Pattern-uri generalizabile extrase din Errors/corecții. Acestea sunt regulile pe care AI-ul trebuie să le recitească la începutul unei sesiuni pe un proiect relevant (vezi `00_CORE/Rules.md`, secțiunea Self-Improvement Loop).

```dataview
TABLE created
FROM "04_MEMORY/Lessons"
WHERE file.name != "README"
SORT created DESC
```

---


## Index Canonic Note de Lecții
- [[Modularize_Prompts_For_Token_Limited_Models]] — Tehnici de sparsificare context pentru modele LLM
- [[Define_MultiEntry_Requirements_Before_Backtest]] — Cerințe de execuție multi-entry înainte de testare
- [[Trust_Boundary_Hardening_Requires_Attest_Not_Overlay]] — Întărirea barierelor de încredere prin atestare
- [[Windows_Security_Audit_Log_And_Registry_Forensics_Correlation]] — Corelare Event Log și Registry în analize criminalistice
- [[WPF_Splash_Screen_And_EventLogReader_DFIR]] — Decuplarea UI WPF de analiza evenimentelor sistem

## 🔗 Legături de Memorie & Graf Obsidian
- [[Memory - Lessons Map]]
- [[08 Memory Subsystems Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
