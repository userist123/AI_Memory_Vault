---
id: "b492d001-c841-4e12-b5e1-8890471b8991"
type: knowledge
lifecycle: ACTIVE
category: DFIR & AI Architecture
tags: [dfir, windows-forensics, explainable-ai, nis2, case-uco, iso-27042, iso-27037]
created: 2026-08-17T22:30:00Z
updated: 2026-08-17T22:30:00Z
provenance:
  source_type: execution
  source_ref: "LogAnalyzer.Core / LogAnalyzer.Infrastructure / LogAnalyzer.UI"
confidence: very_high
verification: verified
relations:
  - related_to: "[[Memory Protocol]]"
  - implements: "[[Confidence Model]]"
---

# LogAnalyzer DFIR Enterprise — Arhitectura Sistemului Forenzic de Ultimă Generație

## 1. Rezumat Arhitectural
Platforma LogAnalyzer DFIR Enterprise este o suită completă, air-gapped și offline destinată analizei forenzice a sistemelor Windows, concepută pe baza recomandărilor din **Consensul Consiliului de Modele**.

## 2. Piloni Tehnici și Module Integrate

### A. Evaluarea Riscului Explicabil (ISO/IEC 27042)
* **Motor:** `ExplainableAiRiskEngine`
* Înlocuiește evaluările opace printr-o descompunere matematică a riscului (0-100) în factori de pondere trasabili până la nivelul sursei de probă, asociind fiecare factor cu tehnica MITRE corespunzătoare și justificarea legală în limba română.

### B. Jurnal de Proveniență Imutabil & Lanț de Custodie (ISO/IEC 27037)
* **Motor:** `ProvenanceLedgerService`
* Jurnal append-only hash-chained SHA-256 (`EntryHash = SHA256(PrevHash || Entry)`), garantând integritatea probatorie matematică a oricărei acțiuni efectuate pe parcursul investigației.

### C. Motoare de Detecție Offline Specializate
* **Kerberos / Active Directory:** `KerberosAdAttackEngine` (detectează Kerberoasting RC4 0x17, AS-REP Roasting și Pass-the-Hash LogonType 9).
* **Binare LOLBAS:** `LolbasEngine` (monitorizează certutil, bitsadmin, mshta, rundll32, regsvr32 și anomalii de proces părinte-copil pentru Web Shells).
* **Corelare Multi-Eveniment Temporală:** `SigmaCorrelationEngine` (corelații complexe în ferestre temporale glisante).
* **Matrice Vizuală MITRE & DeTT&CT:** `MitreMatrixCoverageEngine` (heatmap pe 14 tactici cu scor de acoperire senzorială).

### D. Conformitate NIS2 & Formate Legale de Export
* **Modul DNSC / NIS2:** `Nis2NotificationService` (generare automată a celor 3 stadii obligatorii conform OUG 155/2024: Avertizare Timpurie 24h, Notificare Incident 72h, Raport Final 1 lună).
* **CASE / UCO 1.3 JSON-LD:** `CaseUcoExportService` (ontologie standardizată pentru tribunale).
* **Super-Timeline:** `SuperTimelineExportService` (format Plaso / Timesketch MACB CSV).
* **Sigilare Dosar Caz Forenzic:** `DfirCasePackagingService` (creare arhivă `.dfirbundle.zip` cu `MANIFEST.json` și `CERTIFICATE_OF_AUTHENTICITY.txt`).

## 3. Clasificarea Forței Probatorii (Consens Mandiant / NIST)
* `ExecutionProven`: Prefetch, Amcache.hve, BAM/DAM, UserAssist.
* `ExecutionPossible`: Shimcache (AppCompatCache) pe Windows 10/11.
* `FileExistenceOnly`: NTFS $MFT (oferă detecție timestomping $SI vs $FN).
* `ConfigurationOnly`: Chei de registru pasive.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
