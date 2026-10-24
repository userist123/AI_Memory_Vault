# Roluri de stație și interfața pentru utilizatori fără pregătire IT (WP18)

Cerințele și deciziile proprietarului D1–D8 sunt în `PROMPT_WP18_ROLURI_STATIE_UI_SIMPLA.md`. Documentul de față descrie ce există în cod.

## 1. Patru axe, separate

| Axă | Valori | Cine decide | Cod |
|---|---|---|---|
| Ediție | P1 clasificat / P2-P3 neclasificat | build-ul | `LogAnalyzer.Core/Services/Edition/EditionProfile.cs` |
| Mod | izolat (air-gap) / conectat | politica semnată; P1 mereu izolat | `EditionPolicy.cs` |
| Rol de cont | administrator global / operator | administratorul | `AUTHENTICATION.md` |
| **Rol de stație** | `control` / `csirt` | **PC-ul**, prin politica semnată; P1 mereu `control` | `LogAnalyzer.Core/Services/Edition/StationRole.cs` |

Rolul de stație nu lărgește niciodată ce permit ediția și modul. WP18 nu adaugă roluri de cont (decizia D6). „Stație” înseamnă PC; serverul centrului este un PC cu rolul `csirt` (D8).

## 2. Cum se decide rolul

- Câmpul `role` din `%ProgramData%\LogAnalyzer\LogAnalyzer.policy` este ultima linie, opțională, a textului semnat (`EditionPolicy.Payload`). O politică emisă înainte de WP18 rămâne valabilă și înseamnă `control`. Un rol nu poate fi adăugat sau schimbat fără o semnătură nouă.
- Lanțul de decizie (`StationRoleResolver.Decide`): ediție → politică (semnătură, valabilitate, stație, versiune) → `role` → verificări de consistență → rol efectiv.
- **Fail-closed la `control`**:
  - nu există politică, semnătura e greșită, politica a expirat sau e pentru altă stație, versiunea e mai veche;
  - valoarea `role` e necunoscută; în acest caz întreaga politică devine invalidă, deci și modul revine la izolat;
  - P1 este mereu `control`; o cerere `csirt` este respinsă cu motiv (P1 nu are cheia de verificare, deci fișierul doar se citește, nu se acordă nimic din el).
- `csirt` cu `mode: airgapped` este acceptat și afișat „CSIRT fără rețea”.
- Decizia apare în: `startup_debug.log`, linia de audit `station.role`, auditul de autentificare `session.context` al sesiunii deschise, `case.json` → `StationRole` (gol pentru cazurile vechi, citit ca necunoscut, niciodată ca `control`) și tooltip-ul insignei „ROL:” din antet.
- Nu există comutator în interfață, în linia de comandă sau în vreun fișier. Schimbarea cere o politică nouă cu versiune mai mare:

```powershell
pwsh release-gate/Sign-EditionPolicy.ps1 -Mode connected -Role csirt -Version 2 -NotBefore 2026-10-10 -Audience CSIRT-PC01 -Signer owner -PrivateKeyPath <cheia privată> -Out .\LogAnalyzer.policy
```

## 3. Profilul rolului (date, nu `if`-uri)

`LogAnalyzer.Core/Services/Edition/RoleProfile.cs` construiește, din rol, ediție și mod, cel mult cinci intenții principale pentru Acasă, restul sub „Mai multe”, și paginile rolului din bara laterală. O intenție indisponibilă rămâne vizibilă, dezactivată, cu motivul („necesită modul conectat…”, „nu este disponibil în ediția clasificată”). Toate paginile vechi rămân sub „Avansat (toate paginile)” (decizia 5). Aplicația pornește pe Acasă în orice mod.

| Stație de control | Stație de sprijin răspuns la incidente |
|---|---|
| Verifică această stație | Primește probe de la o stație |
| Verifică un suport (USB, CD/DVD) | Ce s-a întâmplat? |
| Cine a lucrat și când | Ce fac acum? |
| Deschide un control anterior | Raport de incident |
| Raport pentru proces-verbal | Cazuri deschise |

## 4. Limbajul

- Nivelul **Simplu** este implicit pentru **orice** cont, administratori incluși. Rolul de cont nu spune nimic despre pregătirea persoanei (D5). **Expert** este un comutator în antet („LIMBAJ”), reținut per cont în `%LOCALAPPDATA%\LogAnalyzer\preferences\`.
- Nivelul schimbă cuvintele, nu datele. Termenii din glosar (`{views:Term …}`) se recalculează la comutare: Simplu arată expresia umană, Expert numele tehnic lângă ea.
- Glosarul unic (`LogAnalyzer.Dfir.Core/Language/Glossary.cs`) are și termenii liniei de control: Hardware ID, NetworkList, USBSTOR, auditpol, RecordID, CRL, CA, SHA-256, SRUM, RDP, GPO. „Ce înseamnă?” din antet îl deschide, cu căutare.
- Pe paginile rolurilor, un termen tehnic apare doar în paranteză, după numele simplu. Testul de lint `Wp18LanguageTests` impune regula.

## 5. Fluxurile ghidate și ecranul de rezultat

- `GuidedFlow` (`LogAnalyzer.Dfir.Core/Flow/`): pași cu verificare la „Înainte”; „Înapoi” și „Oprește” nu pierd nimic.
- **Verifică această stație** (`StationControlView`) are trei pași:
  1. stația;
  2. perioada: de la ultimul control (plus o zi de suprapunere), 30 de zile, 90 de zile sau un interval ales;
  3. procedurile: secțiunile nedefinite ale profilului sunt arătate, pentru că nu pot da CONFORM.

  Urmează un singur ecran de rezultat (`ControlResultScreen`), cu cele cinci răspunsuri, fără stare SAFE. „Nimic neconform găsit” apare mereu împreună cu sursele citite și cele necitite.
- **Compară cu controlul anterior** (`ControlComparison`): verificările înrăutățite, îmbunătățite, noi și dispărute față de un control salvat în `Control/CONTROL_*/control_report.json`.
- **Primește probe de la o stație** (`InvestigationView`) are trei pași:
  1. folderul;
  2. ce s-a găsit și ce lipsește (`IncomingEvidence.Scan`, după aceeași regulă ca importul: orice fișier este probă, cele fără analizor dedicat merg la indicatori și reguli pe conținut; nimic nu se copiază înainte de import);
  3. scopul cazului.

  Un export de control adus de la o stație de control se importă ca probă (D2).

## 6. Rapoartele

- **Raport pentru proces-verbal** (`ControlReportPdf` cu `ControlReportHeader`). Conținutul primei părți:
  - antetul unității: unitate, structură, stație, Hardware ID, perioadă, cine a controlat și funcția, rolul stației, numărul de înregistrare;
  - cele cinci răspunsuri;
  - golurile de probă și sursele necitite, puse **înaintea** verificărilor.

  Pe fiecare pagină apare marcajul introdus manual, cu nota „marcaj introdus manual, neverificat de aplicație” (D4). Raportul se încheie cu două linii de semnătură. Antetul unității se configurează în „Profil de proceduri” → „Antetul rapoartelor unității”, în `%ProgramData%\LogAnalyzer\profile\report_header.json` (D3).
- **Raport de incident** (`InvestigationReportPdf.Write(audience)`). Întrebarea „Pentru cine este raportul?” alege secțiunile, nu faptele. Pentru orice public apar: lanțurile incidentului, golurile de probă (tipărite înaintea constatărilor), toate constatările (pe scurt pentru conducere și audit, dar cu ce lipsește, ce contrazice și ce limitează) și rezultatele anti-forensics. Antetul arată rolul stației.

## 7. Accesibilitate

Fiecare element interactiv din cele cinci ecrane principale are nume pentru cititorul de ecran, iar fonturile nu coboară sub 11. Fiecare ecran a fost așezat la 150 % într-o fereastră de 1366 px, fără depășire orizontală și fără etichete tăiate. Testul este `Wp18AccessibilityTests`, care încarcă tema aplicației.

## 8. Ce nu este verificat

- **Proba cu o persoană reală** (`WP18_USABILITY_SCRIPT.md`, D7) nu a fost rulată. S6 rămâne `PARTIAL` până la consemnarea ei.
- Scriptul `Sign-EditionPolicy.ps1 -Role` a fost doar parsat; nu a fost rulat cap-coadă (necesită PowerShell 7).
- Așezarea a fost verificată prin măsurare WPF în test, nu prin captură de ecran a aplicației pornite.
- Dependențe lăsate pentru alte pachete:
  - un rol de cont „ofițer de securitate” și regula celor doi (D6);
  - marcajul preluat din clasificarea cazului (WP13);
  - transformarea rezultatelor de control în constatări (WP13/WP14/WP16).
