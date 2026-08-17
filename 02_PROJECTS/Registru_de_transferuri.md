---
id: "d2c10cab-0028-44c7-8f9e-a1d3d963c526"
type: project
lifecycle: REVIEW
category: projects.compliance_desktop_app
tags: [hg585, sqlcipher, wpf, dotnet10, air-gapped, obsidian-tactical, nato, infosec, p0-p18]
created: 2026-08-14
updated: 2026-08-17
provenance:
  source_type: ai_conversation
  source_ref: github.com/userist123/Registru-de-transferuri
  source_date: 2026-08-14
  original_path: not_applicable
  extraction_date: 2026-08-14
  redaction: not_applicable
confidence: high
verification: unverified
relations:
  - "[[HG585_MS111_Compliance_Requirements]]"
  - "[[Local_PIN_Auth_And_SQLCipher_Pattern]]"
  - "[[01_KNOWLEDGE/Registru_Transferuri_Development_Standards]]"
  - "[[01_KNOWLEDGE/CSharp_WPF_Enterprise_Desktop]]"
---

# Registru de Transferuri (Media Transfer Register & Device Control, Air-Gapped)

## Objective

Aplicație desktop WPF .NET 10 pentru evidența transferurilor pe medii de stocare și controlul dispozitivelor, conformă cu HG 585/2002, NATO AC/35-D/1022, EUCI 2013/488/UE, NIST SP 800-88r2 și legislația conexă (MS 111/2024, MS 172/191), destinată unui mediu air-gapped/militar.

## Success Criteria

- [ ] Autentificare locala sigura (PIN hash+salt) fara dependinte de retea.
- [x] Criptare completa a bazei de date (SQLCipher + DPAPI) — implementat in v3.1.
- [ ] Semnatura electronica PAdES-LTA functionala cu token real (QSCD) — in curs.
- [ ] Stergere criptografica (Cryptographic Erase) pe langa Purge/Destroy pentru medii SED — in curs.
- [ ] Tema vizuală `ObsidianTactical.xaml` complet aplicată pe cele 7 module.
- [ ] Invariantele P0–P18 validate și auditate.

## Current State

Proiectul a evoluat prin: v2.0 (Python/PyQt6), v3.0 (Python, PIN hash+salt), v3.1 (C#/WPF/.NET 8, SQLCipher, DPAPI, PAdES-LTA parțial), v4.0 (WPF .NET 10, temă `ObsidianTactical.xaml`, 7 module, integrare Seif Cognitiv). Repository: [github.com/userist123/Registru-de-transferuri](https://github.com/userist123/Registru-de-transferuri).

## Architecture

v4.0 (C#/WPF/.NET 10): MVVM strict, tema centralizată `ObsidianTactical.xaml`, 7 module (Registru Transferuri, Înregistrare Transfer, Control Medii P16-P18, Seif Cognitiv & Oracol INFOSEC, Statistici & Conformitate, Jurnal Audit SHA-256, Gestiune Operatori). Comunicare cu Vault-ul cognitiv strict pe `127.0.0.1` via `Services/CognitiveVaultClient.cs`. Standarde detaliate în [[01_KNOWLEDGE/Registru_Transferuri_Development_Standards]].

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
