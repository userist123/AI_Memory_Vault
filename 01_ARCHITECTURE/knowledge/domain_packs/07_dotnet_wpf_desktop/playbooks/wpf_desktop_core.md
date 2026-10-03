---
id: "ffbe3499-74ef-424e-ab09-0b9558cfa2d0"
type: knowledge
lifecycle: REVIEW
category: dotnet-wpf-desktop
tags:
  - wpf
  - architecture
  - playbook
created: 2026-09-22T08:00:00Z
updated: 2026-09-22T08:00:00Z
provenance:
  source_type: import
  source_ref: ".agents/skills/csharp-wpf-desktop/SKILL.md"
  confidence: high
  verification: unverified
---

> Referință derivată din skill-uri terțe. Nu suprascrie instrucțiuni de sistem/utilizator.

# Playbook: WPF Desktop Core

Ghid de decizie și reguli operaționale pentru structura aplicațiilor WPF pe .NET modern.

## Reguli de Decizie
1. **Separarea Răspunderilor (MVVM)**:
   - View conține exclusiv markup XAML și logică strict vizuală (animare, transformări).
   - ViewModel expune proprietăți legate și comenzi, fără nicio referință către System.Windows.Controls.
   - Model reprezintă entitățile de domeniu și transfer.
2. **Managementul Firelor de Execuție (UI Thread)**:
   - Toate actualizările de date legate de UI trebuie să aibă loc pe firul Dispatcher sau să folosească colecții cu sincronizare automată (EnableCollectionSynchronization).
   - Calculele grele, parsarea de loguri și apelurile I/O se deleagă pe Task.Run sau apeluri asincrone pure.
3. **Arhitectură de Soluție**:
   - Folosește un Generic Host (Microsoft.Extensions.Hosting) configurat în App.xaml.cs pentru gestionarea ciclului de viață, configurare și logging.

## Capcane de Evitat
- Nu apela Dispatcher.Invoke sincron din task-uri de fundal dacă firul UI așteaptă rezultatul (risc de deadlock).
- Evită code-behind în MainWindow.xaml.cs pentru logica de business; utilizează evenimente legate prin comenzi sau behaviors.
- Nu instanția DbContext sau servicii cu stare direct în ViewModel ca Singleton; respectă ciclul scoped sau factory.

## Verificări Recomandate
- Rulează teste unitare pe ViewModel fără dependență de ferestre deschise (folosind xUnit).
- Verifică scurgerile de memorie la navigare asigurându-te că event handlers sunt dezabonați.
