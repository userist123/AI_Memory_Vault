---
name: ui-tokens
description: Încarcă acest skill la orice task care implică fișiere `.xaml`, `Theme/*.xaml`, `Styles/*.xaml` sau `ControlTemplate`. Impune utilizarea exclusivă a tokeni-lor din ObsidianTactical.xaml prin StaticResource.
---

# UI Tokens — Obsidian Tactical

Toate valorile de culoare din UI trebuie să vină din `Theme/ObsidianTactical.xaml`, referențiate prin `StaticResource`. Nu se introduc culori hex direct în fișiere de layout sau view.

## Paletă de referință (nu se modifică fără cerere explicită)

| Token | Hex | Utilizare |
|---|---|---|
| BgDeep | #080C14 | Canvas principal |
| BgBase | #0D1322 | Sidebar & Header |
| BgCard | #121A2D | GroupBox / carduri |
| BgElevated | #18233C | Inputuri / dropdowns / butoane |
| BgHighlight | #223254 | Hover / focus |
| BorderDefault | #1E2C48 | Bordură 1px standard |
| BorderSubtle | #2D3F66 | Bordură hover |
| FocusViolet | #7C3AED | Focus/selecție principală |
| FocusCyan | #00E5FF | Caret, badge tehnice |
| Emerald | #10B981 / #064E3B | Stare air-gapped, audit integru |
| Amber | #F59E0B / #78350F | Secret de Serviciu / NATO Confidential |
| Crimson | #EF4444 / #7F1D1D | Strict Secret, operațiuni distructive |
| TextPrimary | #F8FAFC | Text principal |

## Verificare obligatorie înainte de a marca task-ul complet

1. Grep pentru culori hex hardcodate în fișierele modificate — dacă găsești, înlocuiește cu resursa corespunzătoare.
2. Verifică contrast text/fundal ≥ 4.5:1 (WCAG AA) pe orice pereche nouă.
3. Confirmă că niciun control nu afișează text alb pe fundal alb implicit de sistem (ex: ComboBox/DataGrid nestilizate).
