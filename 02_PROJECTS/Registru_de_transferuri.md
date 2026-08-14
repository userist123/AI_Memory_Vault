---
id: "d2c10cab-0028-44c7-8f9e-a1d3d963c526"
type: project
lifecycle: REVIEW
category: projects.compliance_desktop_app
tags: [hg585, sqlcipher, pyqt6, wpf, dotnet8, air-gapped]
created: 2026-08-14
updated: 2026-08-14
provenance:
  source_type: ai_conversation
  source_ref: github.com/userist123/Registru-de-transferuri
  source_date: 2026-08-14
  original_path: not_applicable
  extraction_date: 2026-08-14
  redaction: not_applicable
confidence: medium
verification: unverified
relations: ["[[HG585_MS111_Compliance_Requirements]]", "[[Local_PIN_Auth_And_SQLCipher_Pattern]]"]
---

# Registru de Transferuri (Media Transfer Register, Air-Gapped)

## Objective

Aplicatie desktop pentru evidenta transferurilor pe medii de stocare, conforma cu HG 585/2002 si legislatia conexa (MS 111/2024, MS 172/191), destinata unui mediu air-gapped/militar.

## Success Criteria

- [ ] Autentificare locala sigura (PIN hash+salt) fara dependinte de retea.
- [ ] Criptare completa a bazei de date (SQLCipher + DPAPI) — implementat in v3.1.
- [ ] Semnatura electronica PAdES-LTA functionala cu token real (QSCD) — in curs.
- [ ] Stergere criptografica (Cryptographic Erase) pe langa Purge/Destroy pentru medii SED — in curs.

## Current State

Proiectul a trecut prin trei versiuni majore: v2.0.0 (Python/PyQt6, functionalitate de baza dar lacune de conformitate si securitate), v3.0 (Python, PIN hash+salt, imbunatatiri de conformitate), v3.1 (rescriere completa in C#/WPF/.NET 8, cu SQLCipher, DPAPI, PAdES-LTA partial). Repository: [github.com/userist123/Registru-de-transferuri](https://github.com/userist123/Registru-de-transferuri).

## Architecture

v3.1 (C#/.NET 8): straturi Data / Security / Hardware / Services / UI, plus tests si tools; migrare date din baza SQLite Python via script dedicat (`migrate_v31.py`).

## Tasks

### TODO
- [ ] Integrare reala `Pkcs11Interop` pentru `C_Login`/`C_Sign` pe token QSCD.
- [ ] Finalizare PAdES-LTA complet (BouncyCastle PdfSigner + TSAClient RFC 3161 + embed OCSP/CRL).
- [ ] Cryptographic Erase real pe SED prin `DeviceIoControl` (IOCTL_ATA_PASS_THROUGH / NVMe Format).
- [ ] `CardRemoved` real prin WinRT in `SmartCardRemovalMonitor`.

### IN PROGRESS
- [ ] Push si revizuire PR pe branch `v3.1-csharp` / `v3.1-update` pentru merge in main.

### DONE
- [x] Migrare completa Python -> C#/WPF/.NET 8 (v3.1).
- [x] Criptare baza de date cu SQLCipher AES-256-CBC (kdf_iter=256000) si cheie protejata DPAPI.
- [x] Curatare memorie sensibila (SecureBuffer, CryptographicOperations.ZeroMemory).

## Risks / Blockers

Functionalitatile care depind de hardware real (token QSCD, unitati SED) nu pot fi finalizate/testate fara accesul fizic la acel hardware.

## Decisions

- Trecere de la Python/PyQt6 la C#/WPF/.NET 8 pentru calitate si securitate superioara, conform cerintei explicite a utilizatorului din 2026-08-14.
- Autentificare locala simpla (PIN + hash/salt) in loc de solutii complexe (JWT/sesiuni), pentru un mediu air-gapped.

## Related

- [[HG585_MS111_Compliance_Requirements]]
- [[Local_PIN_Auth_And_SQLCipher_Pattern]]

## Retrospective

### What worked

Rescrierea modulara pe straturi (Data/Security/Hardware/Services/UI) a permis migrarea progresiva a functionalitatii critice de securitate.

### What failed

Un reset al mediului sandbox local a dus la pierderea fisierelor v3.1 pregatite pentru push, necesitand reconstructie si push pe branch nou cu PR pentru revizuire inainte de merge.

### Lessons

Pentru schimbari mari de arhitectura (Python -> C#), foloseste branch dedicat + PR de la inceput, nu doar la finalul lucrului, pentru a evita pierderea muncii in caz de reset de mediu.
