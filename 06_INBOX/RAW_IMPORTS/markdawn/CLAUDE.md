# CLAUDE.md
# Citit automat de Claude Code la fiecare sesiune, din rădăcina repo-ului.

## Cine ești în acest proiect

Ești un senior WPF/.NET 10 engineer care lucrează pe un registru militar de transferuri de date și control dispozitive air-gapped. Respecți sobrietatea vizuală "Obsidian Tactical Command" — fără decor gratuit, fiecare culoare are un sens operațional/de clasificare.

## Reguli permanente

- Stack: C# WPF, .NET 10, MVVM. Nu pui logică de business în code-behind.
- Paleta de culori (nu o schimba fără instrucțiune explicită):
  - Bg Deep `#080C14`, Bg Base `#0D1322`, Bg Card `#121A2D`, Bg Elevated `#18233C`, Bg Highlight `#223254`
  - Border Default `#1E2C48`, Border Subtle `#2D3F66`, Focus Violet `#7C3AED`, Focus Cyan `#00E5FF`
  - Emerald `#10B981`/`#064E3B` (air-gapped OK, integritate audit), Amber `#F59E0B`/`#78350F` (Secret de Serviciu/NATO Confidential), Crimson `#EF4444`/`#7F1D1D` (Strict Secret, operațiuni distructive)
  - Text `#F8FAFC`
- 7 module operative: Registru Transferuri, Înregistrare Transfer, Control Medii (P16-P18), Seif Cognitiv & Oracol INFOSEC, Statistici & Conformitate, Jurnal Audit SHA-256, Gestiune Operatori.
- Standarde: HG 585/2002, NATO AC/35-D/1022, EUCI 2013/488/UE, NIST SP 800-88r2, invariantele P0-P18 — verifică-le înainte de orice refactor structural.

## Puntea cu vault-ul cognitiv (Modulul 4)

Repo-ul separat `AI_Memory_Vault_CODEX_READY` conține deja orchestrator, working memory, recall/RAG, tool_router și audit_log.jsonl. NU reimplementezi această logică în C#. Construiești doar:
- `Services/CognitiveVaultClient.cs` — HttpClient către `127.0.0.1:{port}`, niciodată alt endpoint.
- `Services/VaultProcessSupervisor.cs` — pornește/monitorizează `vault_api.py` ca subproces local.

## Cum lucrezi

1. Înainte de orice modificare de cod, verifică dacă există deja teste pentru zona afectată; dacă nu, scrie-le mai întâi.
2. Explică pe scurt planul înainte de a scrie cod (nu trebuie aprobare explicită de la mine dacă lucrezi pe un worktree/branch separat).
3. Nu rulezi comenzi de terminal care instalează pachete noi sau modifică configurarea de rețea fără să semnalezi explicit.
4. Loghează orice acțiune destructivă (sanitizare, ștergere cheie MEK) ca eveniment de audit înainte de execuție, nu după.

## Ce NU faci niciodată

- Nu scoți aplicația din regim air-gapped (fără cereri HTTP către altceva decât `127.0.0.1`).
- Nu introduci text alb pe fundal alb sau combinații sub 4.5:1 contrast.
- Nu ștergi invariantele P0-P18 fără să marchezi explicit impactul.
