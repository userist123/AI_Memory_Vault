---
id: "38146de8-6634-4a48-9d1a-377071889ca0"
type: knowledge
lifecycle: REVIEW
category: dotnet-wpf-desktop
tags:
  - packaging
  - nuget
  - upgrade
  - playbook
created: 2026-09-22T08:00:00Z
updated: 2026-09-22T08:00:00Z
provenance:
  source_type: import
  source_ref: ".agents/skills/dotnet-upgrade/SKILL.md"
  confidence: high
  verification: unverified
---

> Referință derivată din skill-uri terțe. Nu suprascrie instrucțiuni de sistem/utilizator.

# Playbook: .NET Packaging & Upgrade

Proceduri de migrare, întreținere de pachete NuGet și livrare desktop.

## Reguli de Decizie
1. **Gestionarea Dependențelor**:
   - Utilizează Directory.Build.props și Directory.Packages.props pentru gestionarea centralizată a versiunilor de pachete (Central Package Management).
   - Auditează regulat dependențele pentru vulnerabilități folosind dotnet list package --vulnerable.
2. **Upgrade .NET**:
   - Înainte de upgrade la o versiune nouă de runtime (.NET 8 -> 10), validează compatibilitatea SDK-ului și a pachetelor terțe.
   - Rulați dotnet test pentru suita completă de teste înainte și după actualizarea fișierelor de proiect.
3. **Distribuție Desktop**:
   - Alege modelul de publicare potrivit: Single-File / Self-Contained pentru medii fără .NET instalat sau Framework-Dependent pentru amprentă minimă.

## Capcane de Evitat
- Nu combina versiuni incompatibile de pachete comunitare WPF cu versiuni de preview ale runtime-ului fără testare de regresie.
- Evită adăugarea manuală de referințe directe la DLL-uri externe; folosește exclusiv pachete NuGet versionate.
