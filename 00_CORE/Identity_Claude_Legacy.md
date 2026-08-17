---
id: "4fe0e1f0-5a40-479b-9bdc-ca938f406c3e"
type: core
lifecycle: ARCHIVED
category: imported-legacy
tags: [legacy-import, core]
created: 2026-08-17T20:24:39Z
updated: 2026-08-17T20:24:39Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/claude_original/00_CORE__Identity.md"
confidence: medium
verification: inferred
enriched_by: ai
---

# Identity — Cine sunt eu

> [!info] Scop acest fișier
> Acesta este fișierul de ancoră al vault-ului. Orice sesiune AI care lucrează cu acest vault trebuie să citească acest fișier **primul**, înainte de Rules.md, Goals.md și System_Architecture.md. Conține contextul minim necesar ca AI-ul să nu pornească de la zero la fiecare conversație.

## Profil Rapid
| Câmp | Valoare |
|---|---|
| Rol principal | Systems & IT Security Administrator |
| Sector | Militar / Apărare (context sensibil) |
| Locație | București, România |
| Limbă principală | Română (tehnic, deseori bilingv RO/EN) |
| Stil comunicare AI | Dens, direct, zero filler, zero preambul |

## Identitate Profesională

### Stack tehnic principal
- **Limbaje:** Python, PowerShell, JavaScript/Node.js, SQL, MQL5
- **Infrastructură:** Active Directory, Docker, Windows Server, GPO management
- **Securitate:** SOC/DFIR, forensics, hardening, tooling de securitate (detalii operaționale — NU se stochează în vault)
- **Dezvoltare:** .NET 8/10 (WPF), arhitecturi multi-proiect (Core/Infrastructure/UI)

### Domenii secundare active
- **Algo trading:** MetaTrader5/MQL5, Python — Expert Advisors, config systems, risk management (ATR, circuit breakers, multi-TP)
- **Automation:** scripting PowerShell pentru medii standalone/air-gapped

### Proiecte curente (index — detalii complete în `02_PROJECTS/`)
- `LogAnalyzer MVP` — .NET 8/10 WPF, forensics triage tool offline
- `GPO Baseline Deployment` — LGPO.exe + PowerShell automation pentru workstation-uri standalone
- `Elite Quant Bot / XAU_Kinetic` — trading algoritmic MT5/MQL5 + Python

## Identitate Personală
- **Interese:** Formula 1, dating apps (Bumble, Tinder, Hinge, Instagram), tattoo design, estetică automotive
- **Estetică vizuală preferată:** cinematic, high-end, dark-minimalist

## Protocol de Comunicare cu AI
- Output dens, direct, acționabil — fără introduceri, fără recapitulări inutile
- Fără explicații de proces dacă nu sunt cerute explicit
- Soluții complete în loc de pași intermediari fragmentați
- Limbă: română by default; cod, comenzi, log-uri rămân în engleză
- Preferă corecții directe în loc de validare excesivă

## Valori & Principii Operaționale
> [!todo] De completat manual
> - Nivel de autonomie acceptat pentru AI (ex: poate propune soluții fără confirmare / trebuie să confirme mereu înainte de acțiuni ireversibile)
> - Toleranță la risc (ex: pentru scripturi care ating producție/GPO live)
> - Criterii proprii de "elegant enough" vs "over-engineered"

## Note de Securitate pentru Vault
- Context militar → **fără date operaționale, clasificate sau identificabile** în orice fișier din acest vault
- Fișierele din `04_MEMORY/` provenite din exporturi externe (ChatGPT/Gemini/etc.) se igienizează **înainte** de import — vezi `03_PROCEDURES/Import_Sanitization.md` (de creat)
- Acest fișier și `00_CORE/` în general = context reîncărcat la fiecare sesiune → risc de expunere cumulativă dacă se adaugă detalii sensibile în timp; revizuire lunară recomandată

---
*Ultima actualizare: 2026-08-09 · Următorul fișier din secvență: `00_CORE/Rules.md`*
