---
id: "5d7b785f-221c-48ba-9e7e-557998c9fefa"
type: project
lifecycle: ACTIVE
category: soc-tooling
tags: [project, dotnet, wpf, dfir, active]
created: 2026-08-09
updated: 2026-08-12
provenance:
  source_type: user
  source_ref: "C:\Users\user\Desktop\LogAnalyzer.MVP"
confidence: very_high
verification: verified
relations: []
---

# LogAnalyzer MVP

## Descriere
Aplicație .NET 10 WPF pentru analiză forensics și colectare date audit (SOC-DFIR), optimizată pentru medii offline și PC-uri individuale. Proiectul folosește o arhitectură curată divizată în: `LogAnalyzer.Core`, `LogAnalyzer.Infrastructure`, `LogAnalyzer.UI` și `LogAnalyzer.UI.Tests`.

## Status curent
🟢 **Functional & Build Clean**
- Proiectul se compilează cu succes pe .NET 10.
- Interfața grafică este modernizată în stil "Cyber Command Center" cu tematică space-blue neon, carduri translucide (Glassmorphism) și scrollbar-uri subțiri luminate în cyan.
- Suport complet pentru analiză forenzică locală (EVTX, Registru offline).
- Colectorul local de date (PowerShell) este configurat corect pentru PC, Server, NAS și rulează asamblat de WPF.

## Caracteristici Implementate & Securizate
1. **Elevare Drepturi UAC (`app.manifest`):**
   - Configurat cu `requireAdministrator` la nivel de executabil pentru a asigura permisiunile necesare exportării log-urilor securizate (`wevtutil epl Security`) în mod automat fără erori de tip "Access is denied".
2. **Chain of Custody (`ChainOfCustodyService`):**
   - Importurile de dovezi digitale sunt semnate, hashes-uite cu SHA-256 și înregistrate într-un jurnal securizat de tip hash chain (NDJSON) pentru a asigura integritatea și non-repudierea probelor.
3. **Cale Securizată & Validare (`SecurePathService`, `EvidenceIntakeService`):**
   - Protecție împotriva atacurilor de tip symlink traversal / reparse points. Fișierele de log sunt inspectate înainte de încărcare.
4. **Criptare Bază de Date & DPAPI (`DatabaseService`):**
   - Database-ul SQLite folosește SQLCipher (pachetul `SQLitePCLRaw.bundle_e_sqlcipher`) cu pooling dezactivat pentru securizarea stocării.
   - Cheia de criptare a bazei este generată aleatoriu (32 octeți) și salvată pe disc utilizând DPAPI (`CurrentUser` scope) prin `DpapiEncryptionService`.
5. **Suita de Testare Automată:**
   - 15 unit tests adăugate în `LogAnalyzer.UI.Tests` ce validează migrarea bazei de date legacy, integritatea lanțului de custodie, validarea căilor și funcționalitatea licenței.
   - Toate testele din soluție (inclusiv testele din `LogAnalyzer.Tests`) trec cu succes (16 teste în total).

## Arhitectură Actuală
- **LogAnalyzer.Core:** Conține modelele (ParsedEvent, RegistryArtifact, TimelineItem, etc.) și serviciile generice (LicenseService, DpapiEncryptionService).
- **LogAnalyzer.Infrastructure:** Implementează parserul EVTX, parserul de registru offline și serviciul de bază de date criptat cu SQLCipher.
- **LogAnalyzer.UI:** WPF Application conținând MVVM ViewModels, view-urile interactive threat hunting (MITRE Heatmap, arborele de procese, Sigma Editor) și serviciile de securitate forenzică (Chain of Custody, Evidence Intake, Secure Paths).
- **LogAnalyzer.UI.Tests:** Suită de teste XUnit axată pe integrare și funcțiile noi de securitate.

## Next Steps
- Monitorizarea rulării pe sistemele air-gapped pentru validarea performanței pe seturi masive de loguri.
- Extinderea regulilor Sigma integrate implicit în panoul de alertare offline.
