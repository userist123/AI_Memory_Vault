---
id: "c1a01101-7291-49fa-9481-22904c10d002"
type: procedure
lifecycle: REVIEW
category: ui-remodel
tags:
  - registru-transferuri
  - obsidian-tactical
  - wpf
  - ui-remodel
  - workflow
created: 2026-08-17T23:20:00Z
updated: 2026-08-17T23:20:00Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/markdawn/workflow_remodel_ui.md"
confidence: high
verification: inferred
enriched_by: ai
enrichment_date: 2026-08-17T23:20:00Z
relations:
  - target: "[[02_PROJECTS/Registru_de_transferuri]]"
    type: implements
  - target: "[[01_KNOWLEDGE/Registru_Transferuri_Development_Standards]]"
    type: depends_on
  - target: "[[01_KNOWLEDGE/Registru_Multi_Agent_Contracts]]"
    type: related_to
---

# Procedură: Workflow Remodelare UI „Obsidian Tactical Command" (/remodelUI)

## TL;DR
Workflow secvențial de 7 sarcini pentru remodelarea completă a interfeței WPF a Registrului de Transferuri. Fiecare sarcină se planifică, se aprobă, se execută și se verifică separat — nu se trece la următoarea fără confirmarea că build-ul e verde.

## Key Facts
- **Invocare**: `/remodelUI` în Agent Manager (Antigravity).
- **Dependențe**: Skill-urile `security-invariants` și `ui-tokens` trebuie instalate și active.
- **Criteriu de finalizare per sarcină**: `dotnet build -c Release` fără erori/warning-uri noi.

---

## Pași de Execuție

### Sarcina 1 — Token-uri vizuale
- Creează/actualizează `Theme/ObsidianTactical.xaml` cu toate culorile din skill-ul `ui-tokens`.
- Verifică skill-ul respectiv înainte de a scrie cod.

### Sarcina 2 — Controale custom
- Implementează `ControlTemplate` complet pentru: **ScrollBar** (thumb 6px), **ComboBox** (fără chrome de sistem), **TextBox/PasswordBox** (36–42px), **DataGrid** (rând minim 40px).
- Creează `DesignPreview/ControlGalleryWindow.xaml` pentru validare vizuală a tuturor stărilor (default, hover, focus, disabled, error).

### Sarcina 3 — Shell layout
- Sidebar colapsabil 280px ↔ 64px.
- Header fix 68px.
- Navigare cu bară luminoasă animată.
- Card telemetrie air-gapped (status indicator).

### Sarcina 4 — Oracol INFOSEC (Modulul 4)
- Creează `Services/CognitiveVaultClient.cs` și `Services/VaultProcessSupervisor.cs` conform skill-ului `security-invariants`.
- UI split-view: chat (interacțiune cu Vault-ul cognitiv) + inspector documente.

### Sarcina 5 — Jurnal audit (Modulul 6)
- `AuditChainVerifier.cs`: consumă `audit_log.jsonl` din Vault-ul cognitiv.
- Verifică lanțul SHA-256, marchează blocul Genesis.
- Afișare cronologică cu filtrare și status de integritate.

### Sarcina 6 — Wizard transfer (Modulul 2)
- TabControl fără chrome nativ, 4 pași secvențiali.
- Validare instantă per pas.
- Implementare semnare 4-Eyes (2 operatori distincți).

### Sarcina 7 — Micro-grafice (Modulul 5)
- Integrare LiveCharts2 pentru KPI-uri.
- Tipuri: donut chart, bar chart.
- Paletă din skill-ul `ui-tokens` (exclusiv `StaticResource`).

---

## Verificare Finală (obligatorie înainte de PR)

1. `dotnet build -c Release` fără erori/warning-uri noi.
2. `dotnet test` verde.
3. Grep pentru culori hardcodate în fișierele noi — trebuie **zero** rezultate.
4. Confirmă că orice apel de rețea nou este limitat la `127.0.0.1`.
5. Generează `walkthrough.md` cu lista fișierelor modificate per sarcină.
