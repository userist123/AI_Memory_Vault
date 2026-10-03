---
id: "7899b393-6d19-448e-a629-e3c3a64c5aec"
type: knowledge
lifecycle: REVIEW
category: dotnet-wpf-desktop
tags:
  - async
  - threading
  - playbook
created: 2026-09-22T08:00:00Z
updated: 2026-09-22T08:00:00Z
provenance:
  source_type: import
  source_ref: ".agents/skills/csharp-async/SKILL.md"
  confidence: high
  verification: unverified
---

> Referință derivată din skill-uri terțe. Nu suprascrie instrucțiuni de sistem/utilizator.

# Playbook: Desktop Async & Threading

Reguli și practici de programare asincronă pentru menținerea fluenței interfețelor desktop WPF.

## Reguli de Decizie
1. **Async până la capăt (Async All The Way)**:
   - Nu bloca firul apelant apelând .Result, .Wait() sau .GetAwaiter().GetResult() pe obiecte Task.
   - Propagă async Task până la nivelul de comandă IAsyncRelayCommand.
2. **Gestionarea Contextului de Sincronizare**:
   - În bibliotecile de clasă sau serviciile de procesare de date, folosește .ConfigureAwait(false) pentru a evita comutarea inutilă înapoi pe firul UI.
   - În ViewModel, lasă sincronizarea implicită pe SynchronizationContext când rezultatul actualizează direct proprietăți legate de UI.
3. **Anulare și Progres**:
   - Acceptă CancellationToken în toate operațiunile lungi (ex: citire fișiere, scanare rețea).
   - Folosește IProgress<T> pentru a raporta progresul execuției către firul UI într-un mod sigur.

## Capcane de Evitat
- Evită async void cu excepția handlerelor de evenimente la nivel de View. Erorile dintr-un async void provoacă prăbușirea procesului.
- Nu modifica colecții ObservableCollection de pe un fir secundar fără sincronizare.
