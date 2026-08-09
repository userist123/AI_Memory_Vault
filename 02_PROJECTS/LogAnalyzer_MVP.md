---
type: project
category: soc-tooling
tags: [project, dotnet, wpf, dfir, active]
created: 2026-08-09
updated: 2026-08-09
status: active
priority: high
related: ["[[Tech_Stack]]", "[[Security_Practices]]"]
---

# LogAnalyzer MVP

## Descriere
Aplicație .NET 8/10 WPF pentru analiză forensics/SOC-DFIR a log-urilor, gândită pentru medii offline/air-gapped. Soluție multi-proiect: `Core`, `Infrastructure`, `UI`.

## Status curent
🔴 Build blocat — lipsesc implementări concrete pentru:
- `EvtxParser`
- `AnalysisEngine`
- `RegistryParser`

## Arhitectură
- **Core** — interfețe, modele, logică de business pură
- **Infrastructure** — implementări concrete (parsers, I/O)
- **UI** — WPF, binding, prezentare

## Istoric relevant
- Compilat inițial din două documente de specificație (markdown)
- Stub-uri generate pentru tipuri nedefinite + fișier de referință pentru fragmente neintegrate
- Cicluri anterioare de build errors: versionare NuGet, definiții WPF Application lipsă, probleme de namespace

## Next Steps
- [ ] Implementare concretă `EvtxParser` (Infrastructure)
- [ ] Implementare concretă `AnalysisEngine` (Core → orchestrare)
- [ ] Implementare concretă `RegistryParser` (Infrastructure)
- [ ] Build clean end-to-end

## Lecții legate (vezi 04_MEMORY/Lessons)
- 🔲 De completat pe măsură ce apar corecții
