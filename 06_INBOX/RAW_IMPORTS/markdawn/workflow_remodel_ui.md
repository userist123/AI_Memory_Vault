# Workflow: /remodelUI
# Locație: .agents/workflows/remodel-ui.md (Google Antigravity)
# Invocare: scrii "/remodelUI" în Agent Manager

## Descriere

Execută, în ordine, cele 7 sarcini de remodelare UI/UX "Obsidian Tactical Command" pentru registrul militar de transferuri. Fiecare sarcină se planifică, se aprobă, se execută și se verifică separat — nu trece la următoarea fără confirmarea că build-ul e verde.

## Pași

1. **Sarcina 1 — Token-uri vizuale**: creează/actualizează `Theme/ObsidianTactical.xaml` cu toate culorile din skill-ul `ui-tokens`. Verifică skill-ul respectiv înainte de a scrie cod.
2. **Sarcina 2 — Controale custom**: implementează `ControlTemplate` complet pentru ScrollBar, ComboBox, TextBox/PasswordBox, DataGrid. Creează `DesignPreview/ControlGalleryWindow.xaml` pentru validare vizuală a tuturor stărilor.
3. **Sarcina 3 — Shell layout**: sidebar 280px↔64px colapsabil, header 68px, navigare cu bară luminoasă animată, card telemetrie air-gapped.
4. **Sarcina 4 — Oracol INFOSEC**: creează `Services/CognitiveVaultClient.cs` și `Services/VaultProcessSupervisor.cs` conform skill-ului `security-invariants`. UI split-view chat/inspector documente.
5. **Sarcina 5 — Jurnal audit**: `AuditChainVerifier.cs` care consumă `audit_log.jsonl` din vault-ul cognitiv, verifică lanțul SHA-256, marchează Genesis.
6. **Sarcina 6 — Wizard transfer**: TabControl fără chrome, 4 pași, validare instantă, semnare 4-Eyes.
7. **Sarcina 7 — Micro-grafice**: integrare LiveCharts2 pentru KPI-uri, donut, bar chart, paletă din skill `ui-tokens`.

## Verificare finală (obligatorie înainte de PR)

- `dotnet build -c Release` fără erori/warning-uri noi.
- `dotnet test` verde.
- Grep pentru culori hardcodate — trebuie zero rezultate în fișierele noi.
- Confirmă că orice apel de rețea nou e limitat la `127.0.0.1`.
- Generează `walkthrough.md` cu lista fișierelor modificate per sarcină.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
