---
type: knowledge
category: tech-stack
tags: [knowledge, tech-stack, reference]
created: 2026-08-09
updated: 2026-08-09
status: review
related: ["[[Identity]]"]
id: "e94d5053-389d-421c-a9cb-9306e3d6d9d8"
lifecycle: REVIEW
provenance_status: incomplete
provenance:
  source_type: unknown
  source_ref: ""
  redaction: not_applicable
confidence: unknown
verification: unverified
relations: []
---

# Tech Stack — Referință

## Limbaje & Runtime
| Tehnologie | Context principal de utilizare |
|---|---|
| Python | Automation, algo trading, tooling securitate |
| PowerShell | Administrare AD, GPO automation, scripting sistem |
| JavaScript/Node.js | Tooling, integrări |
| SQL | Interogări/administrare baze de date |
| MQL5 | Expert Advisors MetaTrader5 |
| .NET (C#, WPF) | Aplicații desktop (LogAnalyzer MVP) |

## Infrastructură
- **Active Directory** — administrare, GPO
- **Docker** — containerizare tooling/servicii
- **Windows Server** — mediu principal de administrare

## Note tehnice recurente (constrângeri cunoscute)
- **MQL5:** const string globals, static vs dynamic arrays, `TimeCurrent`/`TimeToStruct`, auto-detecție filling mode broker
- **PowerShell standalone scripts:** `$PSScriptRoot` nu se rezolvă corect când scriptul rulează în afara contextului standalone de execuție → necesită fallback explicit de path resolution
- **.NET multi-project solutions:** separare Core/Infrastructure/UI → risc de tipuri concrete lipsă la compilare dacă interfețele sunt definite înainte de implementări (vezi `02_PROJECTS/LogAnalyzer_MVP.md`)

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
