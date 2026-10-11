# Research 2026-10-11 — interfață care nu pare generată, politicile GPO ale proprietarului, defecte găsite la rulare

Cerut de proprietar în sesiunea WP18 („trebuie să-mi placă aplicația și construcția ei de design UI/UX ca să continuăm”; „tot ce faci research trebuie să ajungă și în git”).
Depozitul este public: din materialele proprietarului au fost scoși identificatorii organizației (numele complete ale GPO-urilor, coduri interne, numele conturilor redenumite, valori de marcaj intern).

## 1. De ce o interfață pare „făcută de AI”

Sursele sunt bloguri de designeri și dezvoltatori din 2026, nu cercetare formală. Concordă asupra cauzei și a semnelor.

**Cauza.** Modelele produc „centrul statistic” al exemplelor de antrenare: alegerile sigure, valabile peste tot. Aspectul generat nu este un stil, ci **lipsa deciziilor**: unde se rupe grila, care element contează pe ecran, ce se lasă deoparte.

**Semne recunoscute:**
- grile de fișe egale, fără un element dominant; fiecare ecran este o variantă a aceluiași tablou (meniu lateral, bară sus, grilă de fișe);
- fișe rotunjite cu **bordură groasă colorată pe o singură latură** (folosită de noi pe butoanele de pe Acasă în varianta „Registru”: de scos);
- aceeași rotunjire mare pe tot, gradienți violet-albastru, strălucire, insigne „Live/New”, etichete cu MAJUSCULE și spațiere mare;
- controale lăsate pe stilul implicit al unui kit; butonul principal, cel secundar și linkul arată aproape la fel;
- stări goale/încărcare/eroare lipsă sau doar un indicator de așteptare; texte generice („Welcome back!”, „Something went wrong”).

**Remedii propuse de surse:**
- un singur element dominant și o singură acțiune principală pe ecran;
- aranjament ales după sarcină (editor, listă-și-detaliu, configurare ghidată, ecran de decizie), nu același tablou peste tot;
- ritm de spațiere: strâns în interiorul grupurilor, generos între ele;
- stări goale care spun pasul următor, erori care spun cauza și remedierea;
- texte specifice produsului, nu generice;
- referințe reale (capturi din produse livrate) date ca exemplu, nu doar o descriere.

## 2. Ce funcționează în produse reale

**Aspect nativ Windows 11 (WPF Fluent).** De la .NET 9, WPF include tema Fluent (aspectul aplicațiilor Windows 11), cu mod deschis/închis și culoarea de accent aleasă de utilizator în Windows. Se aplică prin `ThemeMode` (Light, Dark, System, None) pe `Application`/`Window` sau prin dicționarul `pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml`. Schimbarea `ThemeMode` din cod este experimentală (eroarea WPF0001 trebuie suprimată). Culorile de accent se citesc din `SystemColors.AccentColor*` și trebuie folosite cu `DynamicResource`. Risc raportat: o excepție `XamlParseException` la pornire când Fluent e activ și Windows are contrast ridicat (dotnet/wpf#10043): de testat.

**O întrebare pe ecran (GOV.UK).** Ghidul serviciilor guvernamentale britanice recomandă să se pornească de la un singur lucru pe pagină (o informație, o decizie, o întrebare) și să se grupeze doar când cercetarea cu utilizatori o justifică. Testele lor: utilizatorii cu încredere scăzută completează mai ușor, erorile și ramificările se tratează mai bine. Excepția documentată: personalul care repetă și comută des între sarcini poate avea întrebări grupate, ceea ce corespunde ecranului de analist (listă-și-detaliu).

## 3. Decizii pentru LogAnalyzer (aprobate de proprietar 2026-10-11: „acum hai să facem asta”)

1. Tema nativă Windows 11 (Fluent, mod deschis); din tema proprie rămân doar culorile de stare (NECONFORM, DE VERIFICAT, NEDETERMINAT, CONFORM) și dimensiunile textului.
2. Acasă: o întrebare și o listă scurtă de acțiuni (nu grilă de fișe, fără bordură pe o latură).
3. Controlul: o întrebare pe ecran; rezultatele ca listă-și-detaliu pentru analist.
4. Fiecare tabel gol spune ce urmează; fiecare eroare spune cauza și ce se poate face.
5. Întâi Acasă și primul pas al controlului, cu capturi pentru proprietar; restul doar după aprobare.

**Stare 2026-10-11 (primul pas, trimis proprietarului spre aprobare):**
- tema Fluent luminoasă (`Fluent.Light.xaml` explicit: `Fluent.xaml` urmează modul închis din Windows, activ pe stația proprietarului, și ar pune fundal închis sub textul nostru închis); stilurile implicite proprii pentru butoane, câmpuri, liste, grile și bare de derulare au fost scoase; stilurile cu cheie se bazează pe cele native;
- Acasă: numele stației, întrebarea „Ce doriți să faceți?” și rânduri de acțiuni în stilul aplicației Setări din Windows (fără grilă de fișe, fără bordură pe o latură); fără caz deschis nu se mai afișează cele patru răspunsuri „niciun caz deschis”;
- Verifică această stație: în timpul pașilor doar Înapoi, Înainte, Oprește; raportul, comparația, folderul și „Control nou” apar în rezultat; tabelele brute doar după un control;
- testele WPF rulează pe un singur fir STA comun (resursele Fluent aparțin firului aplicației).

## 4. Politicile GPO ale proprietarului (3 backup-uri, stații Intranet W11)

Hash-urile arhivelor primite (SHA-256): computer `509FF895…7484`, user `2C706C1C…758E`, AppLocker `E01F1B38…8F1A`. Defender: nicio detecție. Scripturile din backup au fost citite ca text, nu rulate.

- **Importatorul era deja validat pe aceleași trei politici** (5 octombrie); revalidarea pe codul curent a dat aceleași cifre (AppLocker 27 controale, Computer 637, User 7).
- **Defect găsit: preferințele GPO și scripturile erau ignorate fără să apară în raport.** În preferințe sunt: blocarea stocării USB (`HKLM\SYSTEM\CurrentControlSet\Services\USBSTOR\Start = 4`), serviciile Bluetooth (`bthserv`) și Wi-Fi (`WlanSvc`) dezactivate și două chei care marchează versiunea politicii de bază aplicate. Reparat: devin controale; scripturile, acțiunile de oprire a serviciilor, preferințele de alt tip și orice fișier necitit apar nominal ca neacceptate. Computer: 642 controale, 52 neacceptate (`GpoPreferencesImportTests`, `POLICY_ENGINE.md`).
- **Ce conțin politicile și aplicația încă nu citește:** `[Privilege Rights]` (38 drepturi de utilizator), 9 chei `[System Access]` (complexitatea parolei, redenumirea contului de administrator și a contului Guest, starea lor, căutarea anonimă de nume, parola reversibilă).
- **Ce merită verificat de controlul stației:** jurnalul `Microsoft-Windows-PrintService/Operational` activat la 100 MB de un script de pornire (necesar urmăririi tipăririlor); cheile de marcaj al politicii de bază (ce versiune de politică are efectiv stația); categoriile cu cele mai multe setări: Internet Explorer (≈140), acces la stocare amovibilă (15), restricții de instalare a dispozitivelor (5), imprimante (8), Defender (≈18), Desktop la distanță (8), WinRM (7), dimensiunea jurnalelor (9), LAPS (2), audit avansat (40).

## 5. Defecte găsite la rularea aplicației pe PC-ul proprietarului (toate reparate, cu test)

| Simptom | Cauză | Reparație / test |
|---|---|---|
| Crash la „Creează administratorul principal” | `AccountBox.Text` citit pe un fir de fundal (WP-AUTH) | citire pe firul ferestrei; `UiThreadAccessTests` |
| Crash la investigație („Eroare critică internă”) | `IPEndPoint` serializat în JSON (citirea `ScopeId` IPv4 aruncă excepție) | convertor text; `LiveStateSerializationTests` |
| Jurnalul Security mereu gol de probă („The specified query is invalid”) | 25 de coduri de eveniment într-un XPath; Windows acceptă cel mult 22 (măsurat) | grupuri de cel mult 20; `EventLogXPathTests` |

## Surse

- [Why everything your AI builds looks the same](https://geekyants.com/blog/why-everything-your-ai-builds-looks-the-same)
- [AI design slop: overused UI patterns and how to fix them](https://noqta.tn/en/blog/ai-design-slop-overused-ui-patterns-fix-2026)
- [SaaS UI looks AI generated](https://www.saasui.design/blog/saas-ui-looks-ai-generated)
- [Why AI design looks generic](https://superdesign.dev/blog/why-ai-design-looks-generic)
- [GOV.UK design notes: One thing per page](https://designnotes.blog.gov.uk/2015/07/03/one-thing-per-page/)
- [GOV.UK Service Manual: Structuring forms](https://www.gov.uk/service-manual/design/form-structure)
- [Scottish Government Design System: Question page](https://designsystem.gov.scot/patterns/question-page)
- [Microsoft Learn: What's new in WPF for .NET 9](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90)
- [dotnet/wpf issue 10043](https://github.com/dotnet/wpf/issues/10043)
