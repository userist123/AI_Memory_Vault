---
id: "d4d26bc1-8073-47e1-bc21-c4800b2ffdd3"
type: project
lifecycle: ARCHIVED
category: imported-legacy
tags: [legacy-import, project]
created: 2026-08-17T20:24:39Z
updated: 2026-08-17T20:24:39Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/claude_original/02_PROJECTS__GPO_Baseline_Deployment.md"
confidence: medium
verification: inferred
enriched_by: ai
---

# GPO Baseline Deployment

## Descriere
Aplicare backup-uri GPO pe workstation-uri Windows standalone folosind `LGPO.exe` + scripturi PowerShell de automatizare.

## Status curent
🟡 Funcțional parțial — problemă cunoscută cu rezolvarea `$PSScriptRoot` când scriptul rulează în afara contextului de execuție standalone.

## Componente
- `LGPO.exe` — aplicare backup GPO la nivel local
- Scripturi PowerShell — orchestrare, validare, logging

## Problemă activă
`$PSScriptRoot` returnează gol/incorect când scriptul e invocat altfel decât execuție directă standalone (ex: dot-sourcing, ISE, alt working directory).

### Fix candidat
```powershell
$ScriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
```

## Next Steps
- [ ] Validare fix `$PSScriptRoot` în toate contextele de execuție (standalone, ISE, dot-source, Task Scheduler)
- [ ] Test end-to-end pe workstation curat

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[12 Projects and Procedures Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
