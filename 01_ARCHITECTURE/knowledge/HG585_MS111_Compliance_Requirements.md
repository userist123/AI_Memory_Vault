---
id: "99522c1a-b212-4571-b4d8-7dbbba2a3462"
type: knowledge
category: security.compliance
tags: [hg585, ms111, ms172, compliance, air-gapped]
created: 2026-08-14
updated: 2026-09-06
status: normalized
provenance:
  source_type: ai_conversation
  source_ref: perplexity_conversation_2026-06-11_2026-08-14
  source_date: 2026-08-14
  original_path: not_applicable
  extraction_date: 2026-08-14
  redaction: not_applicable
confidence: high
verification: unverified
lifecycle: NORMALIZED
provenance_status: complete
relations:
  - "[[Security_Practices]]"
  - "[[Registru_de_transferuri]]"
  - "[[Legislatie_HG585_2002_Protectia_Informatiilor_Clasificate]]"
  - "[[Legislatie_Ordin_M172_2021_Norme_MApN_Informatii_Clasificate]]"
  - "[[Legislatie_Legea_Cadru_153_2017_Salarizare_Publica]]"
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

- [[Legislatie_HG585_2002_Protectia_Informatiilor_Clasificate]] — Hotărârea Guvernului nr. 585/2002 (Cap. 8 INFOSEC, Art. 236–337).
- [[Legislatie_Ordin_M172_2021_Norme_MApN_Informatii_Clasificate]] — Ordinul M.172/2021 (Art. 49, Art. 51 evidență electronică omologată DCiSM, Art. 193–199 evidența mediilor de stocare, Anexele 9 și 18).
- [[Legislatie_Legea_Cadru_153_2017_Salarizare_Publica]] — Legea 153/2017 (Spor gestionare informații clasificate și Anexa VI Apărare).
- Conversatie AI din 2026-08-14 despre update-ul aplicatiei Registru-de-transferuri.
- Memorie: "preferences.device.printing_compliance.ms111_2024" (2026-06-11).

## Legal Conformity & Evidence Anchoring

Textul normativ a fost verificat împotriva documentelor oficiale din arhiva primară (`06_INBOX/Legi/`):
1. **HG 585/2002**: Acreditarea sistemelor TIC (Cap. 8), izolarea fizică a mediilor de procesare și procedurile de marcare a mediilor amovibile.
2. **Ordinul M.172/2021**: Art. 51 permite explicit ținerea registrelor în formă electronică prin aplicații omologate de DCiSM; Art. 193–199 și Anexa 9 instituie Registrul pentru evidența și distribuirea mediilor de stocare (Hardware Serial Number, tip mediu, clasificare, semnături de primire/restituire), iar Anexa 18 impune Fișa mediului de stocare pentru fiecare suport amovibil.
3. **Invariante de telemetrie fizică P16–P18**: Garantează citirea hardware imutabilă a seriei suportului (fără posibilitate de editare manuală în UI) și ancorarea fiecărui transfer în logul de audit SHA-256 tamper-evident.

## Verification

- [x] Source checked against official texts (HG 585/2002, Ordin M.172/2021, Legea 153/2017)
- [x] Scope/environment checked (Air-gapped C#/WPF, local PIN auth, SQLCipher AES-256)
- [x] Links checked and integrated with AI Memory Vault knowledge graph

## Changelog

- 2026-09-06: Verificare formală și ancorare împotriva textelor oficiale ale HG 585/2002, Ordinului M.172/2021 și Legii 153/2017; eliminat caveat-ul de necorelare legislativă.
- 2026-08-14: nota creata din memoria conversatiilor.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
