---
id: "b8bd05ab-076f-429d-a742-d1bc2ba0c212"
type: knowledge
lifecycle: REVIEW
category: windows.hardening
tags: [windows11, hardening, powershell, cve]
created: 2026-08-14
updated: 2026-08-14
provenance:
  source_type: external_documentation
  source_ref: "https://github.com/nullvaluez/Windows11-Hardener"
  source_date: 2024-06-12
  original_path: not_applicable
  extraction_date: 2026-08-14
  redaction: not_applicable
confidence: medium
verification: unverified
relations: []
---

# Windows11-Hardener — CVE-Focused Windows 11/10 Hardening Script

## Summary

Windows11-Hardener este un script PowerShell care aplică măsuri de hardening pentru Windows 11/10, vizând prevenirea atacurilor bazate pe CVE-uri cunoscute, reducerea suprafeței de atac și dezactivarea funcțiilor inutile.

## Core Concept

Scriptul configurează setări de sistem pentru a aborda vulnerabilități cunoscute, dezactivează servicii/protocoale inutile și aplică politici de securitate pentru a întări OS-ul împotriva atacurilor comune.

## Key Points

- Aplică modificări la setări de sistem, politici și funcții pentru a reduce vulnerabilitățile.
- Necesită rulare cu privilegii de administrator și respectarea instrucțiunilor de backup/restaurare.
- Se concentrează pe prevenția CVE-urilor cunoscute și pe reducerea suprafeței de atac.

## Caveats

- Scriptul modifică multe setări; trebuie testat în VM/medii de test înainte de producție.
- Nu este bazat direct pe Security Baselines Microsoft, ci pe o selecție proprie de măsuri.

## Verification

- [ ] Source checked
- [ ] Scope/environment checked
- [ ] Links checked

## Changelog

- 2026-08-14: Nota creată din README-ul Windows11-Hardener.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
