---
id: "c1a01101-7291-49fa-9481-22904c10c001"
type: knowledge
lifecycle: REVIEW
category: project-standards
tags:
  - registru-transferuri
  - wpf
  - dotnet10
  - air-gapped
  - obsidian-tactical
  - infosec
  - mvvm
  - p0-p18
created: 2026-08-17T23:15:00Z
updated: 2026-08-17T23:15:00Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/AGENTS.md"
confidence: very_high
verification: inferred
enriched_by: ai
enrichment_date: 2026-08-17T23:15:00Z
relations:
  - target: "[[02_PROJECTS/Registru_de_transferuri]]"
    type: implements
  - target: "[[01_KNOWLEDGE/CSharp_WPF_Enterprise_Desktop]]"
    type: depends_on
  - target: "[[03_PROCEDURES/PowerShell_SecOps_Forensic_Standard]]"
    type: related_to
  - target: "[[01_KNOWLEDGE/Local_AI_Integration_Architecture]]"
    type: supports
---

# Standarde Operaționale — Registru Militar de Transferuri & Device Control (C# WPF .NET 10)

## TL;DR
Contract de dezvoltare obligatoriu pentru aplicația **Registru de Transferuri** (WPF .NET 10, air-gapped, conformitate HG 585/2002 + NATO AC/35-D/1022 + EUCI 2013/488/UE + NIST SP 800-88r2). Zero apeluri de rețea externe; tema vizuală `ObsidianTactical.xaml` cu resurse centralizate; 7 module; comunicare cu Vault-ul cognitiv strict pe `127.0.0.1`.

## Key Facts
- **Stack & Arhitectură**: WPF pe .NET 10, MVVM strict (zero logică de business în code-behind), 7 module funcționale.
- **Temă Vizuală `ObsidianTactical.xaml`**:
  - Bg Deep `#080C14`, Bg Base `#0D1322`, Bg Card `#121A2D`, Bg Elevated `#18233C`
  - Accent Violet `#7C3AED`, Cyber Blue `#00E5FF`, Emerald `#10B981`, Amber `#F59E0B`, Crimson `#EF4444`
  - Text primar `#F8FAFC`
- **Constrângeri Air-Gapped Nederogabile**:
  - Zero apeluri de rețea externă — aplicația funcționează complet offline.
  - Comunicarea cu Vault-ul cognitiv (Modulul 4) exclusiv pe `127.0.0.1` via `Services/CognitiveVaultClient.cs`.
  - Fără dependențe cu telemetrie încorporată (sau dezactivare explicită documentată).
  - Invariantele P0–P18 nu pot fi șterse/rescrise fără aprobare umană explicită.
- **Cele 7 Module**:
  1. Registru Transferuri
  2. Înregistrare Transfer
  3. Control Medii (invariante P16–P18)
  4. Seif Cognitiv & Oracol INFOSEC
  5. Statistici & Conformitate
  6. Jurnal Audit SHA-256
  7. Gestiune Operatori

---

## 1. Reguli de Cod (Code Style)

- **Zero culori hardcodate în XAML** — toate culorile referențiază `StaticResource` / `DynamicResource` din `ObsidianTactical.xaml`.
- **ControlTemplate complet** pentru controale native (ScrollBar, ComboBox, TextBox/PasswordBox, DataGrid) — nu doar `Style` cosmetic.
- **ScrollViewer obligatoriu**: orice formular sau tabel care poate deborda este încapsulat în `ScrollViewer VerticalScrollBarVisibility="Auto"`.
- **Accesul la Vault** trece exclusiv prin `Services/CognitiveVaultClient.cs` — interzis accesul direct la fișierele Python din C#.

## 2. Comenzi de Build & Test

```bash
# Restore și Build
dotnet restore && dotnet build -c Release

# Rulare locală
dotnet run --project src/<NumeProiect>.csproj

# Teste
dotnet test

# Sidecar Cognitiv (Modulul 4) — STRICT pe 127.0.0.1
python vault_api.py
```

## 3. Reguli PR & Commit

- **Format titlu PR**: `[Modul N] Descriere scurtă` (ex: `[Modul 4] Punte HttpClient către vault cognitiv`).
- **Conținut descriere**: fișierele XAML/C# modificate + captură de ecran sau descriere text a stărilor UI afectate.
- **Semnalizare invariante**: `⚠️ IMPACT INVARIANTĂ P{n}` obligatoriu la orice modificare care afectează P0–P18.

## 4. Testare Obligatorie

- `dotnet test` înainte de orice commit.
- Verificare manuală în `DesignPreview/ControlGalleryWindow.xaml` a tuturor stărilor (default, hover, focus, disabled, error).
- Validare contrast WCAG AA (4.5:1) pe orice combinație nouă de culori.

## 5. Standarde de Conformitate Aplicabile

| Standard | Domeniu |
|---|---|
| HG 585/2002 | Clasificare documente și medii de stocare RO |
| NATO AC/35-D/1022 | Controlul informațiilor clasificate NATO |
| EUCI 2013/488/UE | Securitatea informațiilor clasificate UE |
| NIST SP 800-88r2 | Sanitizare medii de stocare |
| Invariante P0–P18 | Controlul trasabilității și integrității interne |

## 6. Relații și Sinapse Cognitive
- `implements`: [[02_PROJECTS/Registru_de_transferuri]] — Proiectul activ care aplică aceste standarde.
- `depends_on`: [[01_KNOWLEDGE/CSharp_WPF_Enterprise_Desktop]] — Baza MVVM, async și audit trail.
- `related_to`: [[03_PROCEDURES/PowerShell_SecOps_Forensic_Standard]] — Procedurile de colectare forensic compatibile.
- `supports`: [[01_KNOWLEDGE/Local_AI_Integration_Architecture]] — Protocolul de comunicare cu Ollama pe 127.0.0.1.
