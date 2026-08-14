---
id: "99522c1a-b212-4571-b4d8-7dbbba2a3462"
type: knowledge
category: security.compliance
tags: [hg585, ms111, ms172, compliance, air-gapped]
created: 2026-08-14
updated: 2026-08-14
status: review
provenance:
  source_type: ai_conversation
  source_ref: perplexity_conversation_2026-06-11_2026-08-14
  source_date: 2026-08-14
  original_path: not_applicable
  extraction_date: 2026-08-14
  redaction: not_applicable
confidence: medium
verification: unverified
lifecycle: REVIEW
provenance_status: incomplete
relations: ["[[Security_Practices]]", "[[Registru_de_transferuri]]"]
---

# HG 585/2002, MS 111/2024, MS 172/191 — Cerinte de Conformitate

## Summary

Aplicatiile pentru gestionarea documentelor si transferurilor pe medii de stocare in mediul institutional al utilizatorului trebuie sa respecte simultan HG 585/2002 (protectia informatiilor clasificate) si standardele conexe MS 111/2024, MS 172, MS 191, chiar si atunci cand documentele gestionate sunt declarate neclasificate.

## Core Concept

Aceste reglementari impun cerinte specifice pentru printare/scanare, evidenta transferurilor pe medii de stocare, si trasabilitate/audit, in special in medii air-gapped (fara conectivitate externa).

## Key Points

- Solutiile de printare/scanare trebuie sa respecte MS111/2024, HG585 si MS172/191, chiar pentru documente neclasificate.
- Aplicatiile de evidenta a transferurilor (ex. `Registru-de-transferuri`) trebuie sa functioneze complet offline/air-gapped, fara dependente cloud.
- Autentificarea operatorilor trebuie sa fie simpla si locala (ex. PIN numeric hash-uit cu salt), nu solutii complexe de tip JWT/sesiuni web, considerate overkill pentru mediul air-gapped.
- Pentru versiuni avansate (ex. v3.1 in C#/.NET 8), s-au adaugat: criptare baza de date cu SQLCipher AES-256-CBC, cheie protejata prin DPAPI (`ProtectedData.Protect(LocalMachine)`), semnaturi PAdES-LTA, si stergere criptografica (Cryptographic Erase) pe langa Purge/Destroy.

## Examples

Proiectul `Registru-de-transferuri` (PyQt6 -> C#/WPF/.NET 8) este exemplul concret de aplicatie construita pentru a respecta aceste cerinte.

## Related Concepts

- [[Security_Practices]]
- [[Registru_de_transferuri]]
- [[PaperCut_Xerox_Secure_Print_Setup]]

## References

- Conversatie AI din 2026-08-14 despre update-ul aplicatiei Registru-de-transferuri.
- Memorie: "preferences.device.printing_compliance.ms111_2024" (2026-06-11).

## Caveats

Textul exact al articolelor din HG 585/2002 si ordinele MS nu a fost verificat impotriva documentatiei oficiale in aceasta nota; foloseste doar ca referinta operationala.

## Verification

- [ ] Source checked
- [ ] Scope/environment checked
- [ ] Links checked

## Changelog

- 2026-08-14: nota creata din memoria conversatiilor.
