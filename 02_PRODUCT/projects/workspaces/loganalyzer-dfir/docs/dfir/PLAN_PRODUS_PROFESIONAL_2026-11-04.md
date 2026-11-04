# LogAnalyzer DFIR — planul produsului profesional (module, licență, autentificare, DFIR complet)

Data: 04.11.2026. Cerința proprietarului: aplicația să arate și să funcționeze ca un produs DFIR real, nu ca un prototip. Concret:
- alegerea modulului din licență, cu nume potrivite pentru „AirGap”, „AirGap Network” și „Network Complete”;
- conectare, creare de cont și autentificare profesionale;
- în fiecare modul apar doar opțiunile lui;
- funcționalitate DFIR completă.

Surse folosite:
- **(a)** inventarul codului actual (ramura `claude/loganalyzer-f1f3-hg585`, cu fișierele citate mai jos);
- **(b)** cercetarea publică despre Tenable Nessus, Belkasoft X, X-Ways, EnCase, Magnet AXIOM, Nuix, Autopsy, Velociraptor, KAPE, CIS-CAT, DISA STIG Viewer, Fluent / PatternFly / Carbon. URL-urile sunt în anexa A.

Ce nu e verificat este marcat **NEVERIFICAT**.

## 1. Diagnostic: de ce arată „neprofesional” azi

| Zonă | Ce este acum (din cod) | Problema |
|---|---|---|
| Ediții | Trei executabile construite separat (`StationPosture` la compilare: Standalone, IsolatedNetwork, Internet; `LogAnalyzer.Core/Services/Edition/StationPosture.cs`) | Corect ca securitate, dar utilizatorul vede trei produse fără identitate comună; numele „Standalone / Rețea air-gap / Internet” sunt descrieri tehnice, nu nume de produs |
| Licență | Cheie `LA1…` semnată ECDSA P-256 cu `id`, `hw`, `exp`, `product`; **fără ediție și fără drepturi** (`LogAnalyzer.Core/Services/LicenseService.cs:120-125`); fereastra de activare are doar ID hardware + câmp de cheie | Licența nu decide nimic despre modul; nu există activare offline cu fișier de cerere/răspuns; nu există ecran „Despre licență” |
| Cheia publică | `LogAnalyzer.Core/Licensing/license-public-keys.txt` lipsește din checkout, deci orice licență e refuzată pe un build din sursă | Build-ul de dezvoltare nu trece de activare; lipsește un mod de dezvoltare documentat |
| Cod mort | Un al doilea `LicenseService` (RSA-PSS, cu câmp `Edition`) în `LogAnalyzer.App/Services`, nefolosit; un al doilea arbore UI la rădăcină (`Views/`, `Services/`, `LogAnalyzer.UI.csproj`, `START_AirGapped.bat`) | Confuzie, risc ca cineva să-l folosească |
| Autentificare | Administrator principal cu parolă (PBKDF2 600 000, minim 14 caractere, blocare progresivă); ceilalți cu card + PIN; roluri doar Operator / Administrator (`LogAnalyzer.Dfir.Core/Auth`) | Fond bun, formă slabă: niciun banner de notificare și consimțământ, nicio informație despre modul / licență / versiune pe ecranul de intrare, nu există roluri de auditor sau investigator, nu există domeniu (AD) în modulele de rețea |
| Navigare | Bară laterală cu ~24 de pagini, o comandă duplicată (`MainWindow.xaml:199-200`), pagini ascunse după mod prin sufixe `;air` / `;net` | Lista e plată, nu urmează fluxul unui caz; ascunderea după modul e făcută pe bucăți, nu dintr-o singură sursă |
| Aspect | Temă proprie „Obsidian”, 56 de chei de culoare, dar 38 de culori hex în XAML și 22 în cod, trei familii de stiluri de butoane, `Consolas` scris direct de 12 ori | Lipsă de consecvență, care e exact ce se percepe ca „ieftin” |

**Notă:** capturile de ecran ale aplicației instalate nu au fost făcute în această sesiune, pentru că accesul de control al ecranului a fost refuzat. Evaluarea vizuală de mai sus vine din cod, nu din imagini (**NEVERIFICAT pe ecran**).

## 2. Numele modulelor (decizie)

Recomandarea cercetării, pe care o adopt: un **nume de modul** plus un **descriptor tehnic**, ambele afișate mereu împreună.

| Provizoriu | Nume modul | Descriptor (RO / EN) | Ce înseamnă |
|---|---|---|---|
| AirGap | **Insula** | Stație izolată / Standalone | Un singur calculator, fără nicio rețea |
| AirGap Network | **Arhipelag** | Rețea izolată / Enclave | Stații într-o rețea închisă, cu domeniu, fără internet |
| Network Complete | **Continent** | Rețea conectată / Connected | Rețea cu acces la internet, cu interconectare aprobată |

Afișare: „LogAnalyzer DFIR · Insula — Stație izolată”.

De ce: metafora spune exact granița de securitate pe care o verifică acreditarea (o insulă nu are pod spre continent). Funcționează la fel în română și în engleză. Nu am găsit un produs de securitate cu aceste nume.

Alternative respinse:
- „Cetate / Citadel”: în engleză e numele unui troian bancar cunoscut.
- „Outpost”: a fost marca unui firewall.
- „Straja”: e luat de o platformă românească anti-fraudă.

**Înainte de folosirea comercială** trebuie verificate mărcile la OSIM și EUIPO (**NEVERIFICAT**).

## 3. Licența decide modulul, fără să slăbească izolarea

**Constrângere de securitate existentă, care se păstrează:**
- Modulul Insula nu trebuie să conțină deloc cod de rețea sau de internet; o politică semnată nu poate schimba asta (decizia 13, `StationPostures.ClampMode`).
- De aceea **nu** facem un singur executabil cu toate funcțiile ascunse după licență, cum face Nessus.

Facem varianta care păstrează izolarea:

1. **Un singur kit de instalare** „LogAnalyzer DFIR” care conține cele trei module construite separat.
2. **Licența semnată conține modulul și drepturile**, ca listă de capabilități. Exemple:
   - `module=insula`, `features=[control.station, case.local, report.pv, registers.media]`
   - `module=arhipelag`, `features=[…, collect.lan, auth.ad]`
   - `module=continent`, `features=[…, intel.online, update.online]`
   - plus `seats`, `holder`, `exp`.
   - Semnătura rămâne ECDSA P-256, ca acum. Câmpurile noi intră într-o versiune nouă a formatului (`LA2`). Cheile `LA1` vechi rămân valabile ca Insula, cu setul de bază.
3. **Instalarea**: se introduce sau se importă licența, iar kitul instalează **doar** modulul licențiat.
4. **Fiecare executabil acceptă doar licența modulului său.** De exemplu, Insula refuză o licență Continent, cu mesaj clar.
5. **Verificarea drepturilor se face acolo unde rulează capabilitatea, nu doar în interfață.** Ascunderea unui buton e prezentare, nu protecție.
   - Ce un modul **nu poate avea niciodată** e ascuns complet (de exemplu, căutarea online în Insula).
   - Ce e licențiat dar temporar indisponibil e afișat inactiv, cu motivul (de exemplu „Controlerul de domeniu nu răspunde”).
6. **Activare offline în trei pași**, după modelul Nessus / Belkasoft:
   - stația scrie un fișier de cerere (ID hardware + numărul licenței) pe suport amovibil înregistrat;
   - emitentul (LicenseManager) produce fișierul de licență;
   - fișierul se importă pe stație.
   - Rămâne posibilă și lipirea cheii, ca acum.
7. **Licență expirată**: aplicația pornește în **mod doar citire**. Cazurile, rapoartele și registrele rămân accesibile, dar nu se colectează nimic nou. Nu se blochează complet, după modelul CIS-CAT, care trece în varianta Lite.
8. **Ecran „Licență și modul”** în Administrare: titular, modul, drepturi, expirare, ID hardware, sursa licenței, istoricul activărilor (din jurnalul sigilat).

## 4. Pornirea și autentificarea (fluxul nou)

1. **Ecranul de pornire**: siglă, „LogAnalyzer DFIR”, modulul cu descriptorul, versiunea și build-ul, starea verificărilor de integritate (biblioteci native, lanțul jurnalului de audit), fără text de umplutură.
2. **Licența**:
   - dacă lipsește, se deschide asistentul de activare (cheie, fișier sau cerere offline);
   - dacă există, se afișează discret în bara de stare.
3. **Bannerul de notificare și consimțământ**, obligatoriu înainte de autentificare:
   - textul e stabilit de funcționarul de securitate (din Profil) și se acceptă explicit;
   - acceptarea se scrie în jurnalul sigilat;
   - modelul este DoD STIG V-203596;
   - textul românesc conform HG 585 îl dă unitatea (**NEVERIFICAT**).
4. **Autentificarea**, pe un ecran împărțit:
   - **în stânga**, identitatea produsului: modul, unitate, stație, nivelul stației (SSv–SSID), licența;
   - **în dreapta**, metoda: card + PIN (implicit), parolă (doar administratorul principal), cont de domeniu (doar Arhipelag / Continent).
   - Se păstrează blocarea progresivă și blocarea la inactivitate.
   - Se afișează „ultima autentificare reușită / eșuată” a contului, după modelul Windows.
5. **Conexiunea**, doar în modulele de rețea:
   - Arhipelag: controlerul de domeniu, depozitul comun de cazuri (dacă există) și starea lor;
   - Continent: în plus, sursa de actualizări și de informații despre amenințări.
   - Insula nu are acest pas.
6. **Prima pornire**: un asistent în pași, cu bară de progres:
   - licență;
   - administrator principal;
   - unitate și stație: nume după convenție, nivel, funcționar de securitate;
   - bannerul;
   - cardurile;
   - primul profil de proceduri.
   - La final: „Gata pentru primul control”.

**Roluri noi:**
- **Auditor**: doar citire.
- **Analist**: examinează și etichetează.
- **Investigator**: în plus colectează și rulează controale.
- **Ofițer de securitate**: semnează PV, gestionează registrele de medii și bannerul.
- **Administrator**: conturi, licență, setări.

Drepturile se verifică în serviciul de autorizare, nu doar în meniu. Modelul de roluri urmează Velociraptor (reader / analyst / investigator / administrator) plus rolul cerut de HG 585.

## 5. Navigarea: doar ce ține de modulul și rolul curent

Structura principală urmează fluxul unui caz, nu lista de unelte:

| Secțiune | Conținut | Insula | Arhipelag | Continent |
|---|---|---|---|---|
| Acasă | Starea stației, sarcini, neconformități cu termen, ultimul control | da | da | da |
| Control stație (HG 585) | Asistentul în 6 pași, istoric, comparație, registrul de neconformități | da | da (și pe mai multe stații) | da |
| Cazuri | Asistent caz nou (număr, tip, examinator, unitate, temei, foldere), lista cazurilor | da | da (depozit comun) | da |
| Probe și colectare | Surse de probe, colectare live, colectare triaj în pachet, verificarea integrității | locală | locală + din rețea | locală + din rețea |
| Examinare | Artefacte pe categorii, cronologie unificată, conexiuni, căutare | da | da | da |
| Registre | Medii semnate, transferuri, utilizatori și abilitări, intervenții, copii de siguranță, incidente | da | da | da |
| Rapoarte | PV de control, raport de investigație, raport de incident, export pentru revizor | da | da | da |
| Rețea și domeniu | Active Directory, Kerberos, investigație de domeniu, izolare | — (absent) | da | da |
| Informații despre amenințări | Actualizări online, IOC | — (absent) | import offline | da |
| Administrare | Licență și modul, conturi și roluri, carduri, banner, profil de proceduri, politici, jurnal de audit | da | da | da |

- **Sursă unică de adevăr**: un catalog `ModuleFeatures` care leagă fiecare pagină și comandă de capabilitatea ei. Meniul, comenzile și serviciile citesc același catalog. Sufixele `;air` / `;net` din XAML dispar.
- **Simplu / Expert** rămâne ca nivel de limbaj, separat de modul și de rol.

## 6. Ce lipsește pentru un DFIR „complet”, în ordinea valorii

1. **Înregistrarea fiecărei colectări** (cine, când, versiunea uneltei, amprentele sursei), legată de lanțul de custodie existent. Model: „scan information” din AXIOM.
2. **Cronologie unificată** a tuturor artefactelor, cu filtre de timp și sursă. Există piese separate: `IncidentTimeline` și cronologia din investigație.
3. **Seturi de amprente**: NSRL importat offline și liste „cunoscut bun / cunoscut rău” ale unității.
4. **Căutare indexată** după cuvinte cheie, peste toate artefactele unui caz.
5. **Scanare YARA** pe fișierele colectate. Regulile Sigma există deja în `SigmaRuleEngine` și se leagă în același modul de procesare.
6. **Colector de triaj autonom**: un pachet semnat care rulează pe o stație fără aplicație și produce o arhivă criptată. Model: colectorul offline Velociraptor.
7. **Caz pentru revizor**: export doar citire, în care un revizor adaugă etichete și comentarii; acestea se reîntorc în caz. Model: AXIOM portable case.
8. **Șabloane de raport**: PV, incident (Anexa 18), investigație, inventar suporturi, PV de distrugere. Exporturi PDF, HTML și CSV.
9. **Registrele HG 585 lipsă**, din matricea din 23.10:
   - intervenții cu martor (#20, #23, #29);
   - copii de siguranță (#30);
   - incidente (#32, #33);
   - instruirea personalului (#31);
   - acreditarea, cu termen și alertă (#34).
10. **Analiza memoriei**: doar importul unei capturi și rularea unor analize offline. **De decis** dacă intră în produs.

## 7. Aspectul vizual (reguli)

- **Temă**: Fluent (.NET 9 `ThemeMode`, deocamdată experimental, sau biblioteca WPF UI, licență MIT), cu temă întunecată și luminoasă și un singur set de stiluri.
  - Toate cele 38 + 22 de culori scrise direct se mută în tokeni.
  - Rămân trei stiluri de buton: principal, secundar și pericol.
- **Tipografie**: Segoe UI Variable, scara Windows (12/14/20/28), Semibold în loc de Bold, nimic sub 12 px. Monospațiu doar prin `CodeFontFamily`.
- **Densitate**: grilă de 4 px; densitate compactă pentru tabele.
- **Severitate**: culoare + simbol + text, niciodată doar culoare (PatternFly / Carbon). Scara: critic, ridicat, mediu, scăzut, informativ.
- **Bara de stare permanentă**: modul, nivelul stației, utilizator și rol, licență (zile rămase), conexiune (doar rețea), integritatea jurnalului.

## 8. Planul de lucru (o fază pe sesiune; fiecare se termină cu teste și capturi de ecran)

| Fază | Conținut | Criteriu de acceptare |
|---|---|---|
| R1 | Curățenie: arborele UI vechi și al doilea `LicenseService` se mută în `_recovered` (nu se șterg); catalogul `ModuleFeatures`; numele modulelor în titlu și în bara de stare | Build verde; o căutare după `;air` / `;net` dă zero rezultate; un test care pune fiecare pagină față de catalog |
| R2 | Licența `LA2` cu modul + drepturi; LicenseManager emite `LA2`; activare offline cu fișier de cerere/răspuns; mod doar citire la expirare; ecranul „Licență și modul” | Teste: licență pe modul greșit refuzată, drept lipsă = funcție indisponibilă în serviciu, `LA1` acceptat ca Insula de bază |
| R3 | Pornire și autentificare noi: ecranul de pornire, bannerul, ecranul împărțit, ultima autentificare, asistentul de primă pornire | Teste pe ViewModel + capturi pe stația reală |
| R4 | Rolurile Auditor / Analist / Investigator / Ofițer; autorizare în servicii; AD pentru Arhipelag / Continent | Matrice rol × comandă testată |
| R5 | Restilizare Fluent: tokeni, butoane, tipografie, severitate, bara de stare | Zero culori hex în afara temei (test); capturi |
| R6 | Navigarea pe flux de caz (secțiunea 5) | Capturi în toate cele trei module |
| R7 | DFIR: înregistrarea colectărilor, cronologia unificată, seturile de amprente | Teste pe corpusul sintetic |
| R8 | DFIR: căutarea indexată, YARA, colectorul de triaj, cazul pentru revizor | Teste + o colectare reală pe stația de laborator |
| R9 | Registrele HG 585 rămase (#20, #23, #29–#34) | Teste + PV cu noile secțiuni |

**Deciziile luate implicit** (proprietarul a cerut să nu fie întrebat; toate sunt reversibile):
- numele Insula / Arhipelag / Continent;
- module construite separat, cu licență pe modul;
- mod doar citire la expirare;
- codul vechi mutat, nu șters.

**Rămân la proprietar:**
- verificarea mărcilor;
- textul bannerului de la funcționarul de securitate;
- dacă analiza memoriei intră în produs.

## Anexa A — surse publice (selecție)

- Nessus, licențiere și activare offline: https://docs.tenable.com/nessus/Content/LicensingRequirements.htm ; https://docs.tenable.com/nessus/Content/UpdateLicenseOffline.htm
- Belkasoft, activare offline: https://belkasoft.com/electronic-license-activation
- X-Ways, protecție la copiere: https://www.x-ways.net/copy_protection.html
- Nuix, alegerea sursei de licență: https://documentation.nuix.com/en/content/server/general/projects_frameless/Nuix%20Workstation%20Online%20Help/installation_and_configuration/use_nuix_cloud_license_server_to_start_nuix_workstation.htm
- CIS-CAT, trecerea în varianta Lite: https://ciscat-assessor.docs.cisecurity.org/en/latest/Installation%20and%20License%20Management/
- Velociraptor, roluri și colectorul offline: https://docs.velociraptor.app/blog/2020/2020-03-29-velociraptors-acl-model-7f497575daee/ ; https://docs.velociraptor.app/docs/offline_triage
- AXIOM, cronologie, caz portabil, detaliile cazului: https://www.magnetforensics.com/docs/axiom/html/Content/en-us/reviewing-evidence/explorers/viewing-timeline.htm ; https://www.magnetforensics.com/docs/axiom/html/Content/en-us/sharing-evidence/portable-case/features-portable-case.htm
- Autopsy, module de procesare: https://sleuthkit.org/autopsy/docs/user-docs/4.21.0/
- Banner de notificare și consimțământ (STIG V-203596): https://www.stigviewer.com/stigs/general_purpose_operating_system_security_requirements_guide_v3/2024-12-04/finding/V-203596
- Tipografie Windows: https://learn.microsoft.com/windows/apps/design/signature-experiences/typography ; WPF .NET 9 Fluent: https://learn.microsoft.com/dotnet/desktop/wpf/whats-new/net90
- Severitate: https://www.patternfly.org/patterns/status-and-severity/ ; https://www.carbondesignsystem.com/patterns/status-indicator-pattern
- Conflicte de nume: Citadel https://www.malwarebytes.com/blog/news/2012/11/citadel-a-cyber-criminals-ultimate-weapon ; Straja https://www.revistabiz.ro/scamy-io-devine-straja-io-platforma-de-identificare-a-fraudelor/
