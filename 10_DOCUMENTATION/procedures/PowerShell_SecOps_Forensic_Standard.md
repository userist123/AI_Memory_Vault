---
id: "c1a01101-7291-49fa-9481-22904c10b003"
type: procedure
lifecycle: REVIEW
category: secops
tags:
  - powershell
  - secops
  - forensic
  - evtx
  - infosec
created: 2026-08-17T23:00:00Z
updated: 2026-08-17T23:00:00Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/skills/coding/powershell-secops/SKILL.md"
confidence: very_high
verification: inferred
enriched_by: ai
enrichment_date: 2026-08-17T23:00:00Z
relations:
  - target: "[[LogAnalyzer_DFIR_Enterprise_Architecture]]"
    type: implements
  - target: "[[00_GOVERNANCE/rules/Rules]]"
    type: supports
---

# Procedură Operațională: PowerShell SecOps, Colectare Forensic și Audit INFOSEC

## TL;DR
Standarde riguroase pentru scripturile de administrare și colectare forensic în medii securizate și reglementate (MApN / INFOSEC). Orice script de producție trebuie să fie idempotent, complet trasabil (`Start-Transcript`), non-destructiv asupra originalelor și verificat prin hash-uri SHA-256 imediate.

## Key Facts
- **Standard de Script**: `#Requires -Version 5.1`, `Set-StrictMode -Version Latest`, `$ErrorActionPreference = 'Stop'`, suport obligatoriu `-WhatIf` / `-Confirm` pentru operațiuni cu efecte secundare.
- **Validare Parametri**: Restricționare strictă a intrărilor prin `[ValidateSet]` și `[ValidateScript]`; fără credențiale în clar.
- **Colectare Forensic de Integritate**:
  - Export EVTX prin `wevtutil epl` fără a șterge jurnalul sursă; interzis apelul `Clear-EventLog`.
  - Calcul SHA-256 imediat la salvare și generarea fișierului `manifest.csv` (Fișier, Hash, Timestamp, Operator, Sursă).
  - Salvare hive-uri registru (`reg save` sau Volume Shadow Copy pentru fișiere blocate).
  - Tratarea explicită a fusului orar (UTC și Timp Local).
- **Format Ieșire**: Dublă generare — format structurat (CSV/JSON) pentru prelucrare automată + sumar lizibil executiv.

---

## 1. Structura Canonică de Script SecOps

```powershell
<#
.SYNOPSIS
    Colectare evenimente securitate Windows (EVTX) cu generare manifest forensic.
.DESCRIPTION
    Script non-destructiv pentru colectare jurnale și calcul hash-uri SHA-256.
.PARAMETER OutputDirectory
    Calea folderului destinație validat.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path $_ -PathType Container })]
    [string]$OutputDirectory
)

#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$transcriptPath = Join-Path $OutputDirectory "Execution_Transcript_$(Get-Date -Format 'yyyyMMdd_HHmmss').log"
Start-Transcript -Path $transcriptPath -Append

try {
    Write-Host "[*] Initiere colectare forensic conform standardelor INFOSEC..." -ForegroundColor Cyan
    # Operatiuni de export non-distructiv
}
catch {
    Write-Error "[!] Eroare critica in timpul executiei: $_"
    exit 1
}
finally {
    Stop-Transcript
}
```

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[12 Projects and Procedures Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
