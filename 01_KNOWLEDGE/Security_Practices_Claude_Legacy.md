---
id: "449eee1d-ee36-42d8-8653-1a4694e6709e"
type: knowledge
lifecycle: ARCHIVED
category: imported-legacy
tags: [legacy-import, knowledge]
created: 2026-08-17T20:24:39Z
updated: 2026-08-17T20:24:39Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/claude_original/01_KNOWLEDGE__Security_Practices.md"
confidence: medium
verification: inferred
enriched_by: ai
---

# Security Practices — Referință

> [!warning] Context militar
> Acest fișier conține doar practici generale, non-operaționale. Nicio informație specifică infrastructurii reale nu se documentează aici.

## Domenii
- SOC / DFIR — triage, analiză log-uri (EVTX), forensics offline/air-gapped
- Hardening — GPO baseline, LGPO.exe pentru workstation-uri standalone
- Automation securitate — scripting PowerShell pentru deployment repetabil

## Principii generale
- Mediu air-gapped → toate tool-urile trebuie să funcționeze offline, fără dependențe cloud
- GPO baseline → aplicat prin `LGPO.exe` + PowerShell, nu manual, pentru reproductibilitate
- Log analysis → prioritizare EVTX (Windows Event Log) ca sursă primară de triage

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
