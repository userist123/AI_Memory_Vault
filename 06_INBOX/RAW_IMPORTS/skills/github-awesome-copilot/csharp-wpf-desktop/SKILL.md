---
name: csharp-wpf-desktop
description: Încarcă acest skill când dezvolți aplicații desktop C#/.NET WPF (LogAnalyzer.UI, Registru-de-transferuri, unelte interne Windows). Impune MVVM curat, async corect pe UI thread și pattern-uri pentru unelte enterprise/guvernamentale.
---

# C# WPF Desktop

Aplicațiile tale WPF sunt unelte serioase (log analysis, registre oficiale) — stabilitatea și trasabilitatea bat feature-urile.

## MVVM strict

- View = doar XAML + code-behind minimal (fără logică de business în code-behind).
- ViewModel: `INotifyPropertyChanged` prin `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`) — nu boilerplate manual.
- Model/Services: toată logica testabilă, injectată prin DI (`Microsoft.Extensions.DependencyInjection` merge și în WPF).
- Zero `MessageBox.Show` din ViewModel — printr-un `IDialogService` injectat (testabilitate).

## Async și UI thread (sursa nr. 1 de freeze-uri)

- Orice I/O (fișiere EVTX, parsare, DB, rețea) = `async`/`await`; NICIODATĂ `.Result`/`.Wait()` pe UI thread (deadlock clasic).
- Operații lungi: `IProgress<T>` pentru progres + `CancellationToken` pentru anulare — orice operație > 2s trebuie să fie anulabilă.
- Colecții mari în UI: virtualizare activată (`VirtualizingStackPanel`), încărcare paginată; un DataGrid cu 500k rânduri nevirtualizat omoară aplicația.
- `ObservableCollection` se modifică DOAR pe UI thread (`Dispatcher` sau `EnableCollectionSynchronization`).

## Pattern-uri pentru unelte enterprise/gov

- **Audit trail:** orice acțiune a utilizatorului asupra datelor se loghează (cine, ce, când) — cerință implicită în mediul tău.
- **Fail loud, save state:** handler global de excepții (`DispatcherUnhandledException`) care loghează + salvează starea de lucru înainte de crash.
- **Fără dependențe exotice:** mediul țintă poate fi offline/restricționat; preferă BCL + pachete Microsoft. Self-contained deploy sau Native AOT unde se poate.
- Configurație lângă executabil sau în `%APPDATA%`, niciodată hardcodată; căile UNC tratate explicit.

## Performanță la parsare de loguri

- Streaming, nu load-all: `IAsyncEnumerable`/pipe pentru fișiere EVTX/CSV mari.
- Parsarea pe `Task.Run`, UI-ul primește batch-uri (nu item cu item — flood de notificări).
- Măsoară înainte să optimizezi: `Stopwatch` + coloană de timing în log.

## Calitate

- `nullable enable` pe tot proiectul. Warnings as errors la release.
- Un `.editorconfig` comun; naming: `_camelCase` pentru câmpuri private, `PascalCase` rest.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[10 Imports and Sources Map]]
- [[Master_Skills_Catalog_251]]
- [[Knowledge Graph Home]]
