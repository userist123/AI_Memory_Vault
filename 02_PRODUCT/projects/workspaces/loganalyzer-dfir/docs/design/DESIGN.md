# LogAnalyzer DFIR — reguli de design (tema „Registru”)

Tema este decizia proprietarului din 2026-10-11: hârtie, cerneală, un singur albastru. Regulile de mai jos o fac consecventă; nu o schimbă.

## Culoare (din `Themes/Tokens/ColorTokens.xaml`; nicio culoare scrisă direct în pagini sau în view-modele)

| Rol | Token | Valoare |
|---|---|---|
| Fundal fereastră | `CanvasBrush` | #F2F2EE |
| Benzi (antet, meniu) | `Surface1Brush` | #E9EAE4 |
| Carduri, câmpuri, tabele | `Surface2Brush` | #FFFFFF |
| Text | `TextPrimaryBrush` | #1C2024 |
| Text secundar | `TextSecondaryBrush` | #454B52 |
| Text estompat | `TextMutedBrush` | #61676E |
| Accent (o singură culoare de acțiune) | `PrimaryAccentBrush` | #1F4A73 |
| Critic, ridicat, mediu, scăzut, în regulă | `CriticalBrush` … `SuccessBrush` | #A3201C, #A84B0C, #7A5D00, #285F7E, #2E6638 |

Culorile de text și de stare sunt alese ca să treacă WCAG AA pe alb (testele de contrast existente rămân valabile). Starea se spune mereu
prin cuvânt și pictogramă vectorială, niciodată doar prin culoare. Accentul albastru marchează o singură acțiune principală pe ecran;
restul butoanelor sunt neutre.

## Tipografie

Segoe UI Variable pentru tot textul; Cascadia Mono doar pentru valori tehnice (hash, cale, cod de eveniment).
Scară modulară cu raportul 1,2: **11** (metadate) · **13** (corp dens, tabele) · **14** (corp) · **16** (titlu de panou) ·
**20** (titlu de secțiune) · **24** (titlu de pagină) · **29** (întrebarea de pe Acasă, o singură dată în aplicație).
Greutăți: Regular și SemiBold. Etichetele se scriu ca propoziții, fără majuscule integrale. Cifrele din tabele și carduri sunt tabulare.

## Spațiere și formă

Grilă de 4: 4, 8, 12, 16, 24, 32. Marginea paginii 24; distanța dintre carduri 12; interiorul cardului 16.
Raze: 4 (câmpuri, butoane, insigne) și 8 (carduri). O singură umbră, doar pentru ce plutește (dialog, notificare, meniu).

## Structură

- Fereastra încape întreagă la 1000 × 600: nimic nu se derulează pe orizontală, nimic nu iese din fereastră.
- Antet: identitatea (produs, ediție), contextul (mod, stație), căutarea, o acțiune principală, utilizatorul, nivelul de limbaj, ajutorul.
  Sub 1280 px etichetele auxiliare se strâng în pictograme cu nume accesibil și indiciu.
- Fiecare pagină începe la fel: titlu (24, SemiBold), un rând de explicație (14, text secundar), apoi conținutul. **Linia de registru** —
  o linie fină de cerneală sub titlul paginii — este singurul element decorativ al aplicației.
- Tabelele sunt dense (rând 32), cu antet SemiBold 12 și text care se trunchiază cu „…” și se citește întreg în indiciu sau la dublu clic.

## Mișcare

Doar ca răspuns la o acțiune (deschidere, extindere, confirmare), 120–160 ms. Nimic nu se mișcă singur. Se respectă setarea Windows
„Afișează animațiile”.

## Interzise

Emoji ca pictograme (se folosesc glifele Segoe Fluent Icons / Segoe MDL2 Assets), culori din tema veche întunecată (#121622, #38bdf8,
#00ff87, #ef4444 și altele), majuscule integrale pentru etichete, engleză în textul aplicației, mai mult de un buton accent pe ecran,
text tăiat fără indiciu, bare de derulare imbricate.
