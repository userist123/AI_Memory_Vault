---
type: project
category: sysadmin
tags: [project, gpo, powershell, active]
created: 2026-08-09
updated: 2026-08-09
status: review
priority: medium
related: ["[[Tech_Stack]]", "[[Security_Practices]]"]
id: "b5b8ed33-da3a-42cc-8e94-4dde6b1edb6d"
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
