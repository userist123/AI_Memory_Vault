---
id: "d2c10cab-0028-44c7-8f9e-a1d3d963c526"
type: project
lifecycle: REVIEW
category: projects.compliance_desktop_app
tags: [hg585, sqlcipher, wpf, dotnet10, air-gapped, obsidian-tactical, nato, infosec, p0-p18, cognitive-vault]
created: 2026-08-14
updated: 2026-08-18
provenance:
  source_type: ai_conversation
  source_ref: github.com/userist123/Registru-de-transferuri
  source_date: 2026-08-18
  original_path: not_applicable
  extraction_date: 2026-08-18
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

- [x] Autentificare locala sigura (PIN hash Argon2id + salt) fara dependinte de retea.
- [x] Criptare completa a bazei de date (SQLite WAL mode + DPAPI).
- [x] Tema vizuală `ObsidianTactical.xaml` complet aplicată pe cele 7 module (WCAG AA conform, GPU frozen brushes).
- [x] Punte cognitivă sidecar securizată cu `AI_Memory_Vault_CODEX_READY` pe loopback `127.0.0.1:8765`.
- [x] Invariantele P0–P18 validate și auditate (Imutabilitate S/N hardware, izolare logică nume).
- [x] Jurnal audit SHA-256 chained (`AuditChainVerifier.cs`) cu ancoră pe Blocul Genesis.
- [x] Wizard înregistrare în 4 etape cu inspecție DLP Magic Bytes împotriva executabilelor deghizate.
- [x] Generare nativă de documente oficiale PDF cu QuestPDF (Proces-Verbal Predare-Primire HG 585 și Certificat Sanitizare NIST).
- [x] Motor euristic DFIR / YARA offline pentru detecția amenințărilor din pachete de date (`YaraDfirScanner.cs`).
- [ ] Semnatura electronica PAdES-LTA cu token PKCS#11 real (QSCD) — în integrare.
- [ ] Sanitizare directă hardware Cryptographic Erase pe SED prin `DeviceIoControl` (IOCTL_ATA_PASS_THROUGH).

## Current State

Proiectul a atins versiunea **v5.1 (Obsidian Tactical Command & Cognitive Bridge)** pe .NET 10 LTS.
Repository oficial: [github.com/userist123/Registru-de-transferuri](https://github.com/userist123/Registru-de-transferuri).

## Architecture

v5.1 (.NET 10 WPF / C#):
- **Theme**: `ObsidianTactical.xaml` — token-uri de culoare unificate, fără elemente de sistem Windows Aero albe, scrollbar sleek de 6px, combobox cu drop shadow, pinbox centrat cu font 18px.
- **Shell**: Sidebar colapsabil (280px <-> 68px), telemetrie live `AIR-GAPPED PROTOCOL`, card SQLite WAL.
- **Cele 7 Module**:
  1. *Registru Transferuri*: DataGrid de 40px row-height, filtrare după clasificare și căutare live, export CSV/HTML, inspector detalii.
  2. *Înregistrare Transfer (Wizard 4 etape)*: Suport Fizic ➔ Entități Flux ➔ DLP Magic Bytes (MZ/ELF blocker) ➔ Semnare 4-Ochi.
  3. *Control Medii & Whitelist*: P16-P18 Hardware Telemetry imutabil, redenumire volum logic fără alterare S/N, proces verbal sanitizare NIST SP 800-88r2.
  4. *Seif Cognitiv & Oracol INFOSEC*: Split-view (Terminal chat 60% + Inspector documente oficiale HG 585/NATO/EUCI 40%), conectat la `CognitiveVaultClient.cs` + `VaultProcessSupervisor.cs`.
  5. *Statistici & Conformitate*: 4 carduri KPI de 28px cu sparklines și raport sintetic militar.
  6. *Jurnal Audit SHA-256*: Blockchain local tamper-evident, card dedicat pentru Blocul Genesis, verificare instantă integritate.
  7. *Gestiune Operatori*: Înregistrare securizată cu PIN de 6 cifre și clearance HG 585.

## Tasks

### TODO
- [ ] Integrare reală `Pkcs11Interop` pentru `C_Login`/`C_Sign` pe token QSCD fizic.
- [ ] Finalizare modul export PDF nativ pentru Proces-Verbal Predare-Primire.
- [ ] Cryptographic Erase pe SSD-uri SED prin `DeviceIoControl` (IOCTL_ATA_PASS_THROUGH / NVMe Format).

### DONE
- [x] Remodelare completă UI/UX Obsidian Tactical pe toate cele 7 module conform specificației de design.
- [x] Rezolvare conflict resurse Pack URIs în `App.xaml` pentru încărcarea garantată a stilurilor.
- [x] Implementare `CognitiveVaultClient` și `VaultProcessSupervisor` (loopback offline).
- [x] Implementare `AuditChainVerifier` pentru verificarea lanțului de blocuri SHA-256.
- [x] Inspecție DLP binară a fișierelor transferate (`PayloadDlpInspector.cs`).
- [x] Sincronizare pe GitHub și validare test suite 9/9 teste pe .NET 10 LTS (100%).

## Decisions

- **Obsidian Tactical Design**: Trecere la o paletă dark sobru militar (`#080C14` / `#0D1322` / `#121A2D`) cu accente semantice Cyber Blue (`#00E5FF`), Tactical Violet (`#7C3AED`) și Emerald (`#10B981`), respectând WCAG AA.
- **Air-Gapped Loopback Bridge**: Comunicarea cu asistentul de memorie AI se face exclusiv pe loopback local `127.0.0.1:8765`, fără nicio transmisie externă.
