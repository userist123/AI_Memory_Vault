# GEMINI.md
# Citit automat de Gemini CLI (și de backend-ul Gemini din Google Antigravity)
# la fiecare sesiune, din rădăcina proiectului, cu traversare până la .git.

## Context proiect

Aplicație WPF (.NET 10, C#, MVVM) — registru militar de transferuri de date și control dispozitive air-gapped. Direcție vizuală: "Obsidian Tactical Command / Cyber Defense Operations Center". Standarde de conformitate: HG 585/2002, NATO AC/35-D/1022, EUCI 2013/488/UE, NIST SP 800-88r2. Cod guvernat de invariantele P0-P18 — nu le modifici fără a semnala explicit impactul.

## Stack & convenții

- C# WPF pe .NET 10, tipare MVVM, fără logică de business în code-behind.
- Temă centralizată: `Theme/ObsidianTactical.xaml`. Toate culorile din UI trebuie să vină de aici prin `StaticResource`, niciodată hardcodate.
- Controale native custom (ScrollBar 6px thumb, ComboBox fără chrome de sistem, TextBox 36-42px, DataGrid rând 40px minim) implementate ca `ControlTemplate`, nu doar `Style`.
- 7 module: Registru Transferuri · Înregistrare Transfer · Control Medii (P16-P18) · Seif Cognitiv & Oracol INFOSEC · Statistici & Conformitate · Jurnal Audit SHA-256 · Gestiune Operatori.

## Integrare cu vault-ul cognitiv (repo separat)

`AI_Memory_Vault_CODEX_READY` (Python) conține deja orchestrator.py, working_memory.py, recall.py, tool_router.py, audit_log.jsonl. Din acest proiect WPF construiești doar puntea:
- `Services/CognitiveVaultClient.cs` — apeluri HTTP către `127.0.0.1:{port}` (niciodată alt host).
- `Services/VaultProcessSupervisor.cs` — supervizor local de proces pentru `vault_api.py`.

Nu portezi logica cognitivă (attention/consolidation/reasoning) în C#.

## Comenzi utile

- Build: `dotnet build -c Release`
- Test: `dotnet test`
- Pornire sidecar cognitiv: `python vault_api.py` (din folderul vault-ului, venv local activ)

## Reguli de siguranță

- Zero trafic de rețea extern — aplicația rămâne air-gapped.
- Fără telemetrie ascunsă din librării terțe (dezactivezi explicit orice opțiune de acest tip la instalare).
- Contrast text/fundal minim WCAG AA (4.5:1) pe orice combinație de culori nouă.

## Subdirector specific (opțional)

Poți crea `src/Theme/GEMINI.md` sau `src/Modules/Module4_Oracol/GEMINI.md` pentru instrucțiuni specifice acelei zone — Gemini CLI le încarcă automat, cel mai specific fișier are prioritate peste cel din rădăcină.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
