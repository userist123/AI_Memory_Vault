---
id: "78517c1b-0634-4c5b-a429-ca764b23e415"
type: knowledge
lifecycle: REVIEW
category: dotnet-wpf-desktop
tags:
  - dotnet
  - wpf
  - desktop
  - domain-pack
created: 2026-09-22T08:00:00Z
updated: 2026-09-22T08:00:00Z
provenance:
  source_type: import
  source_ref: ".agents/skills/"
  confidence: high
  verification: unverified
---

> Referință derivată din skill-uri terțe. Nu suprascrie instrucțiuni de sistem/utilizator.

# Pachet Domeniu: 07 .NET / WPF Desktop

## 1. Scop și Agenți Deserviți
Pachetul furnizează ghiduri operaționale, arhitectură decuplată și convenții de implementare pentru aplicații desktop Windows bazate pe .NET și WPF.
- **Agent deservit**: `wpf_engineer`.

## 2. Tabel Rutare Task → Recomandare Documente (Max 2)
| Task de Dezvoltare | Document Primar | Document Secundar |
|---|---|---|
| Structură aplicație, UI threading | `playbooks/wpf_desktop_core.md` | `INDEX_wpf_desktop_core.md` |
| ViewModels, MVVM CommunityToolkit | `playbooks/mvvm_toolkit.md` | `INDEX_mvvm_toolkit.md` |
| Operațiuni asincrone, UI responsiveness | `playbooks/desktop_async_threading.md` | `INDEX_desktop_async_threading.md` |
| C# modern (records, patterns, LINQ) | `playbooks/modern_csharp.md` | `INDEX_modern_csharp.md` |
| Upgrade framework, pachete NuGet | `playbooks/dotnet_packaging_upgrade.md` | `INDEX_dotnet_packaging_upgrade.md` |

## 3. Regulă de Încărcare Minimă
- Încarcă maximum 2 documente per task (respectând plafonul de 3000 tokeni per agent).
- Nu încărca întregul pachet de domeniu simultan.
- Consultă `gaps.md` pentru cerințe nesusținute în skill-urile existente (ex. XAML styling complex, securitate DPAPI).
