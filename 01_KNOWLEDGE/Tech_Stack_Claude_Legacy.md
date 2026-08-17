---
id: "ade25e85-6720-43d3-8c6d-33b3dc082b9e"
type: knowledge
lifecycle: ARCHIVED
category: imported-legacy
tags: [legacy-import, knowledge]
created: 2026-08-17T20:24:39Z
updated: 2026-08-17T20:24:39Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/claude_original/01_KNOWLEDGE__Tech_Stack.md"
confidence: medium
verification: inferred
enriched_by: ai
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
