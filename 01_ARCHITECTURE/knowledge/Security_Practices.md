---
type: knowledge
category: security
tags: [knowledge, security, soc, dfir]
created: 2026-08-09
updated: 2026-08-09
status: review
id: "aa5076f2-fd31-4d11-aa4b-23ad1cf89a4e"
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
