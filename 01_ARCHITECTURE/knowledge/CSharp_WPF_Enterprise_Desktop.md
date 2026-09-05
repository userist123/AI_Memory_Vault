---
id: "c1a01101-7291-49fa-9481-22904c10b001"
type: knowledge
lifecycle: REVIEW
category: desktop-development
tags:
  - csharp
  - dotnet
  - wpf
  - mvvm
  - enterprise
created: 2026-08-17T23:00:00Z
updated: 2026-08-17T23:00:00Z
provenance:
  source_type: import
  source_ref: "06_INBOX/RAW_IMPORTS/skills/coding/csharp-wpf-desktop/SKILL.md"
confidence: high
verification: inferred
enriched_by: ai
enrichment_date: 2026-08-17T23:00:00Z
relations:
  - target: "[[LogAnalyzer_DFIR_Enterprise_Architecture]]"
    type: implements
  - target: "[[UI_UX_Heuristic_Review]]"
    type: supports
---

# Dezvoltare Aplicații Desktop C# / .NET WPF (Enterprise & Guvernamental)

## TL;DR
Pentru instrumentele enterprise și guvernamentale (LogAnalyzer, Registru Transferuri), stabilitatea, trasabilitatea și decuplarea MVVM primează în fața adăugării de funcționalități nevalidate. Orice operațiune I/O sau parsare masivă de loguri EVTX/CSV se execută asincron prin streaming (`IAsyncEnumerable`), protejând UI Thread-ul împotriva blocajelor.

## Key Facts
- **MVVM Strict cu CommunityToolkit.Mvvm**: View-ul conține exclusiv XAML și code-behind declarativ; starea și comenzile se gestionează prin `[ObservableProperty]` și `[RelayCommand]`.
- **Decuplare Dialoguri & Servicii**: Interzis apelul direct `MessageBox.Show` din ViewModel; interacțiunea se injectează prin `IDialogService` cu Dependency Injection (`Microsoft.Extensions.DependencyInjection`).
- **Async & UI Thread Protection**: Interzis apelul blocant `.Result` sau `.Wait()`; operațiunile > 2s impun `CancellationToken` și raportare prin `IProgress<T>`.
- **Optimizare Colecții Masive**: Virtualizare obligatorie a controalelor (`VirtualizingStackPanel`); modificarea `ObservableCollection` pe thread-uri secundare necesită `BindingOperations.EnableCollectionSynchronization`.
- **Integritate & Audit Trail**: Toate acțiunile utilizatorului asupra datelor se înregistrează în jurnal de audit (cine, ce, când); excepțiile globale se capturează prin `DispatcherUnhandledException` salvând starea de lucru.

---

## 1. Reguli Arhitecturale pentru Medii Restricționate (INFOSEC)

1. **Dependențe Minimaliste**:
   - Fără librării terțe exotice; se preferă BCL (Base Class Library) și pachete oficiale Microsoft.
   - Livrare *Self-Contained* sau *Native AOT* pentru funcționare complet offline fără conexiune la internet.
2. **Streaming la Parsarea Fișierelor EVTX/CSV**:
   - Prelucrarea evenimentelor se face în flux continuu pe `Task.Run`, transmițând către UI pachete (batch-uri) pentru a evita supraîncărcarea mecanismului de notificare.
3. **Calitate & Standarde de Cod**:
   - `Nullable enable` activat pe tot proiectul; avertismentele tratate ca erori la compilare (`WarningsAsErrors`).

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
