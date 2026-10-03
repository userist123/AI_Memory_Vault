---
id: "5a03a7c0-7494-429c-b701-4b6e0ec635bd"
type: knowledge
lifecycle: REVIEW
category: dotnet-wpf-desktop
tags:
  - csharp
  - modern-language
  - playbook
created: 2026-09-22T08:00:00Z
updated: 2026-09-22T08:00:00Z
provenance:
  source_type: import
  source_ref: ".agents/skills/csharp-pro/SKILL.md"
  confidence: high
  verification: unverified
---

> Referință derivată din skill-uri terțe. Nu suprascrie instrucțiuni de sistem/utilizator.

# Playbook: Modern C# Language

Standarde de cod C# modern aplicate în arhitectura aplicațiilor Windows.

## Reguli de Decizie
1. **Tipuri Imutabile și DTO-uri**:
   - Utilizează tipuri record sau record struct pentru modele de date imutabile, mesaje și DTO-uri de transfer.
   - Folosește inițializatori init-only pentru a asigura imutabilitatea după instanțiere.
2. **Pattern Matching**:
   - Folosește switch expressions și pattern matching pe proprietăți pentru cod concis și lizibil.
3. **Nullability și Siguranță**:
   - Activează Nullable enable în fișierul de proiect .csproj.
   - Tratează avertismentele de nullability ca erori sau verifică explicit referințele potențial nule.

## Capcane de Evitat
- Nu ignora avertismentele de referință nulă; evită operatorul de suprimare ! dacă nu există o garanție certă de inițializare.
- Nu folosi LINQ complex în bucle critice de randare dacă alocările de memorie sunt costisitoare.
