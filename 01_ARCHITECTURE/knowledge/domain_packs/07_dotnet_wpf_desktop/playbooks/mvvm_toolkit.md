---
id: "89b36b3b-2843-4bba-9462-77faad9e358f"
type: knowledge
lifecycle: REVIEW
category: dotnet-wpf-desktop
tags:
  - mvvm
  - toolkit
  - playbook
created: 2026-09-22T08:00:00Z
updated: 2026-09-22T08:00:00Z
provenance:
  source_type: import
  source_ref: ".agents/skills/mvvm-toolkit/SKILL.md"
  confidence: high
  verification: unverified
---

> Referință derivată din skill-uri terțe. Nu suprascrie instrucțiuni de sistem/utilizator.

# Playbook: MVVM Toolkit Patterns

Ghid operațional pentru utilizarea eficientă a pachetului CommunityToolkit.Mvvm.

## Reguli de Decizie
1. **Generatoare Sursă**:
   - Moștenește clasa de bază ObservableObject.
   - Folosește atributul [ObservableProperty] pe câmpuri private pentru generarea automată a proprietăților publice cu notificare INotifyPropertyChanged.
   - Folosește atributul [RelayCommand] pentru generarea comenzilor IRelayCommand și IAsyncRelayCommand.
2. **Dependency Injection**:
   - Înregistrează ViewModels și Servicii în containerul IServiceCollection.
   - Rezolvă DataContext în constructorul View-ului prin injectare de dependențe sau printr-un ViewModelLocator securizat.
3. **Comunicare Decuplată**:
   - Utilizează WeakReferenceMessenger.Default pentru comunicare de tip eveniment/mesaj între ViewModel-uri distincte fără cuplaj direct.
   - Dezabonează sau gestionează mesajele prin IRecipient<T> pentru a preveni retenția involuntară a obiectelor.

## Capcane de Evitat
- Nu folosi [ObservableProperty] pe proprietăți; atributul se aplică strict pe câmpuri (fields) private sau interne.
- Nu captura instanțe grele de View în mesajele transmise prin Messenger.
- Evită comenzi sincrone blocate; folosește metode care întorc Task annotate cu [RelayCommand].
