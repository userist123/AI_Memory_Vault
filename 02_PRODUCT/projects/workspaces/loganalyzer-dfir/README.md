# LogAnalyzer

Aplicație desktop WPF (.NET 10) pentru analiză DFIR locală pe Windows: achiziție și import de probe, parsere de artefacte, timeline, corelare, findings, rapoarte și export — cu SHA-256, chain of custody și raportarea explicită a golurilor de probă.

O singură aplicație, `LogAnalyzer.exe`, care își alege singură modul de lucru la pornire:

| Mod | Când | Ce face |
|---|---|---|
| **Network** | Windows raportează conexiune la Internet | Monitorizare live, fluxul SOC, audit Active Directory / domeniu, receptor Syslog, integrări online |
| **AirGapped** | Fără Internet: doar rețea locală, nicio rețea sau stare nedeterminată | Analiză offline, audit local SAM & USB, conformitate pentru stații izolate. Orice funcție care ar deschide o conexiune este blocată (`NetworkPolicy`) |

Cum se decide:
- **Detectarea este pasivă.** Aplicația citește starea din Windows Network List Manager (rezultatul verificării pe care o face deja sistemul de operare) și nu trimite niciun pachet.
- **La nesiguranță alege AirGapped.** Dacă starea nu poate fi determinată, se folosește modul sigur.
- **Modul se poate forța:** `LogAnalyzer.exe --mode=airgapped|network|auto` sau fișierul `LogAnalyzer.mode` lângă executabil, care conține `airgapped`, `network` sau `auto`. Ordinea de prioritate: argumentul din linia de comandă, apoi fișierul, apoi detectarea.
- **Modul nu se schimbă singur în timpul rulării.** Dacă o stație pornită în AirGapped primește conexiune, aplicația afișează avertizarea „STAȚIE IZOLATĂ CONECTATĂ LA REȚEA”, iar funcțiile de rețea rămân blocate.

Aplicația este licențiată per stație (Hardware ID).

## Funcționalități principale

- **Deep Triage**: import EVTX/CSV, artefacte Prefetch / LNK / Shimcache / Registry
- **Platforma DFIR** (`LogAnalyzer.Dfir.*`, în lucru): model de caz și probe cu statusuri explicite (SUCCESS / EMPTY / FAILED / NOT_AVAILABLE / PARTIAL / SKIPPED_BY_DESIGN), parsere reale validate pe un caz real: Prefetch MAM, SRUM (ESE), EVTX brut, PCAPNG (inclusiv Wi-Fi 802.11), DNS / TLS SNI / HTTP
- **Detecție**: Sigma, YARA, anomalii (entropie Shannon)
- **Raportare**: HTML și PDF (QuestPDF), export STIX 2.1 / MISP
- **Criminalistică**: SHA-256 la intake, chain of custody append-only, probe brute read-only
- **Securitate**: SQLCipher pentru date la rest, Windows DPAPI pentru secrete

## Roluri de stație și interfața simplă (WP18)

Pe lângă mod, fiecare PC are un **rol de stație**, stabilit tot de politica semnată (câmpul `role`):

| Rol | Pentru cine | Ce oferă pe Acasă |
|---|---|---|
| **Stație de control** (implicit) | controalele pe linia protecției informațiilor clasificate, pe stații izolate | Verifică această stație · Verifică un suport · Cine a lucrat și când · Deschide un control anterior · Raport pentru proces-verbal |
| **Stație de sprijin răspuns la incidente** (`csirt`) | centrul de răspuns la incidente de securitate cibernetică | Primește probe de la o stație · Ce s-a întâmplat? · Ce fac acum? · Raport de incident · Cazuri deschise |

- Fără politică validă, rolul este „Stație de control”. Ediția clasificată este mereu stație de control. Rolul nu se schimbă din aplicație.
- Butoanele și textele sunt scrise pentru oameni fără pregătire IT, **administratori incluși**. Nivelul „Simplu” este implicit pentru orice cont, iar „Expert” este un comutator în antet. Datele sunt aceleași în ambele niveluri.
- „Ce înseamnă?” din antet deschide glosarul. Paginile tehnice rămân toate sub „Avansat”.
- Detalii: `docs/dfir/STATION_ROLES.md`.

## Izolarea proceselor suspecte

Fila **„Izolare procese suspecte”** (sau `LogAnalyzer.exe --tab=13`) funcționează în ambele moduri, fără să deschidă nicio conexiune:

1. **Caută procese suspecte.** Încredere mare înseamnă un program nesemnat, care rulează dintr-o locație în care orice utilizator poate scrie (AppData, Temp, ProgramData, Downloads) și are conexiuni în Internet. Este tiparul din cazul NanAgent.
2. **Izolează și scanează.**
   - O regulă Windows Firewall (ieșire și intrare) blochează **doar programul**; restul PC-ului rămâne conectat.
   - Opțional, procesul se suspendă.
   - Urmează scanarea, fără să se modifice nimic: SHA-256, semnătura (inclusiv cea din cataloagele Windows), PE și importuri, IOC-uri din conținut, procesul părinte și linia de comandă, conexiunile active, autostart, Prefetch și Defender (cu remedierea dezactivată, ca proba să rămână intactă).
3. **Unde a încercat să meargă.** După activarea auditului „Filtering Platform Connection”, butonul „Reîmprospătează încercările blocate” citește evenimentele Security 5157 ale programului.
4. **Incidentul** se salvează în cazul live (`%LOCALAPPDATA%\LogAnalyzer\Cases\LIVE-<stație>\Incidents\INC-nnnn`), cu copia programului ca probă (SHA-256 și custodie), jurnalul acțiunilor și constatările. **Export PDF** produce raportul complet.
5. **Ridică izolarea** anulează regulile și suspendarea.

Câteva reguli de funcționare:
- **Izolarea automată** (comutatorul din antet) acționează la fiecare minut, doar pe procesele cu încredere mare care nu sunt aprobate. Nu suspendă procese.
- **„E de încredere”** aprobă un program după SHA-256. Dacă fișierul se schimbă, aprobarea trebuie confirmată din nou.
- Pentru firewall, Prefetch și autostart complet, aplicația trebuie rulată ca administrator.

## Controlul stației (utilizatori și administratori)

Fila **„Control stație”** (`LogAnalyzer.exe --tab=14`) pregătește verificările de conformitate. Funcționează pe stații izolate (AirGapped) și pe stații conectate. Aplicația doar citește din stație; nu modifică nimic.

- **Ce verifică:**
  - conturi și administratori, Guest, politica de parole și de blocare;
  - UAC, autologon, RDP, firewall, Defender (inclusiv excluderile), BitLocker, SMBv1, WDigest, blocarea USB;
  - politica de audit și dimensiunea jurnalelor;
  - jurnale șterse, politica de audit sau ora schimbate;
  - modificări în grupul Administrators și în conturi;
  - autentificări locale și RDP, serii de parole greșite, sesiuni privilegiate;
  - dispozitive USB (USBSTOR și Partition/Diagnostic 1006, fără discurile interne);
  - **conectările la rețele ale unei stații care ar trebui să fie izolată** (NetworkList, WLAN, NetworkProfile);
  - software instalat, servicii (separat de drivere), task-uri.
- **Rezultatul fiecărei verificări:**
  - **CONFORM** sau **NECONFORM**;
  - **DE VERIFICAT**: faptele sunt prezentate, dar numai organizația poate spune dacă au fost autorizate;
  - **NEDETERMINAT**: sursa nu a putut fi citită. Nu înseamnă că activitatea nu a avut loc.
- **Raportul** conține verificările, cu probele fiecăreia (jurnal, RecordID, ora), activitatea pe utilizator, cronologia acțiunilor (cine, ce, când) și golurile de probă. Se salvează în cazul stației (`Control/CONTROL_<dată>/`), ca JSON și PDF înregistrate cu SHA-256.
- Pentru jurnalul Security și politica de audit, aplicația trebuie rulată ca administrator.
## Investigație în domeniu și e-mail

Fila **„Investigație domeniu și e-mail”** (`LogAnalyzer.exe --tab=15`) are trei părți:

- **Domeniu.** Inventar LDAP doar în citire, făcut cu contul Windows curent. Verificările sunt implementate de noi, după modelul PingCastle:
  - conturi cu SPN (Kerberoasting) și conturi fără pre-autentificare (AS-REP roasting);
  - PASSWD_NOTREQD, criptare reversibilă, DES;
  - membrii recursivi ai grupurilor privilegiate (dezactivați, neutilizați, cu parolă care nu expiră);
  - conturi și calculatoare neutilizate, vechimea parolei krbtgt, delegare neconstrânsă, adminCount orfan;
  - sisteme de operare fără suport, politica de parole și de blocare.
- **Utilizator.** Profilul din AD, grupurile privilegiate și traseul autentificărilor din jurnalele Security ale controlerelor de domeniu (4624/4625/4740/4768/4769/4771/4776 și modificările de cont), plus sursele de autentificare.
- **E-mail.** Aplicația generează scriptul oficial Microsoft: Exchange Online (`Get-MessageTraceV2`, `Get-InboxRule`, redirecționări) sau Exchange local (`Get-MessageTrackingLog`). Administratorul de e-mail îl rulează și se autentifică el însuși, deci aplicația nu primește parole. Exportul se importă și se analizează pentru:
  - rafale de mesaje trimise (cont compromis folosit pentru spam);
  - adresele IP de pe care s-a trimis;
  - reguli de inbox care redirecționează, șterg sau ascund mesaje;
  - redirecționarea automată a cutiei poștale;
  - mesaje trimise în afara organizației.

Toate interogările de rețea trec prin `NetworkPolicy`. În modul AirGapped sunt blocate; pe o rețea izolată cu domeniu, porniți aplicația cu `--mode=network`. Rezultatele se salvează în cazul stației (`Investigations/`), ca probe, și se pot exporta ca PDF.
## Investigația completă (caz)

Fila **„Investigație completă (caz)”** (`LogAnalyzer.exe --tab=16`) reface cap-coadă o investigație ca cea de pe MARIUS-PC:

1. **Colectare de pe stație.**
   - Jurnalele de evenimente sunt exportate brut cu `wevtutil` (17 canale).
   - Prefetch se copiază.
   - SRUM se copiază prin VSS, pentru că fișierul e blocat de sistem.
   - Se fotografiază starea live: procese cu părinte, linie de comandă și semnătură; conexiuni; servicii; task-uri; autostart.
   - Pe lângă colectare sau în locul ei, se pot **importa** probe dintr-un folder (EVTX, `.pf`, `SRUDB.dat`, `.pcapng`), de exemplu de pe o stație izolată.
2. **Parsare** cu parserele reale (EVTX, Prefetch MAM, SRUM ESE, PCAPNG) într-o singură cronologie: `Analysis/timeline.csv`.
3. **Corelare.** Fiecare constatare are clasificarea DIRECT / CORRELATED / CANDIDATE și probele pe care se sprijină (dovadă și locator):
   - detecții Defender și dezactivarea lui;
   - jurnale șterse și goluri de RecordID;
   - servicii și task-uri din locații scriabile;
   - scripturi rulate prin unelte Windows abuzate frecvent;
   - trafic de rețea al acestor unelte (SRUM, confirmat de Prefetch);
   - încercări repetate de ghicire a parolei, RDP de pe IP-uri publice, PowerShell suspect.
4. **Lanțul incidentului (INCIDENT-CHAIN).** Constatările grave apropiate în timp sunt legate într-o poveste ordonată.
5. **Raport PDF**, cu lanțurile la început, apoi constatările cu probe, starea colectării și golurile de probă.

Validare pe cazul real NanAgent: 348.000 de evenimente procesate în circa 19 secunde. Lanțul din 19.09.2026, 14:53–15:21 UTC (`SETUP.EXE` SamFW → detecții Defender → comportament `NanAgent32.exe` → `msiexec` cu `bootstrap_7d57.cmd` → trafic `msbuild.exe`) este găsit automat. Testul de regresie `InvestigationTests` verifică acest rezultat.
## Cerințe

- Rulare: Windows 10/11 x64. Pachetul este self-contained și nu necesită .NET instalat.
- Dezvoltare: .NET SDK 10.x, Visual Studio 2022+ sau VS Code.

## Build și testare

```powershell
dotnet build LogAnalyzer.slnx -c Release
dotnet test LogAnalyzer.slnx -c Release
```

Testele pe corpusul real (`LogAnalyzer.Dfir.Tests`, `[CorpusFact]`) rulează doar dacă variabila `LADFIR_CORPUS` indică un corpus local. Altfel sunt sărite. Corpusul nu se pune niciodată în repository.

## Publicare

```powershell
dotnet publish LogAnalyzer.App/LogAnalyzer.App.csproj -c Release -p:PublishProfile=win-x64-singlefile
```

Rezultatul: `publish\LogAnalyzer\win-x64\` — `LogAnalyzer.exe` (~150 MB, single-file, self-contained) plus `Categories\`, `Data\` și `LatoFont\`.
CI-ul (`.github/workflows/loganalyzer-dfir-build.yml`, la rădăcina vault-ului) produce același artefact la PR-uri și la push pe `main`.

## Licențe

Toate instrumentele folosesc aceeași logică, cea din `LogAnalyzer.Core/Services/LicenseService.cs`:

- **Hardware ID** = primele 16 caractere hex din SHA-256(ProcessorId + serialul plăcii de bază);
- **licența** are forma `CHEIE|YYYY-MM-DD`;
- aplicația o verifică la activare și la fiecare pornire și o păstrează în `license.lic`, lângă executabil.

Emiterea unei licențe:

1. Clientul pornește aplicația. Fereastra de activare afișează Hardware ID-ul stației.
2. Pe stația de administrare, alegi unul dintre generatoare:
   - **`LogAnalyzer.LicenseManager`** (recomandat, interfață grafică). Introduci clientul, Hardware ID-ul, valabilitatea (1/3/10 ani sau o dată aleasă) și note. Licența este verificată automat cu logica aplicației și trecută în registrul `%APPDATA%\LogAnalyzer\LicenseManager\issued_licenses.csv`. O poți copia sau salva direct ca `license.lic`.
   - `LogAnalyzer.KeyGen` (consolă): `LogAnalyzer.KeyGen <HWID> <YYYY-MM-DD>` sau interactiv.
   - `Generate-LicenseKey.ps1 -HardwareId <HWID> -ExpiryDate <YYYY-MM-DD>` (fără build).
3. Clientul lipește șirul `CHEIE|YYYY-MM-DD` în fereastra de activare sau pune `license.lic` lângă `LogAnalyzer.exe`.

> Limitare: schema actuală este hash cu salt inclus în binar. Oprește copierea ocazională, dar nu și pe cineva care decompilează aplicația. Trecerea la semnătură asimetrică (RSA-PSS, cu cheia privată ținută doar pe stația de emitere) este planificată. Vezi `Documentation/MVP-DECISIONS.md`, decizia 9. Atenție: această trecere invalidează licențele deja emise.

## Structura

| Cale | Rol |
|---|---|
| `LogAnalyzer.App/` | Aplicația WPF (Views, ViewModels, Services, Themes, Categories); modul AirGapped/Network se alege la pornire |
| `LogAnalyzer.Core/` | Modele, interfețe, servicii de domeniu, licențiere, detectarea modului (`Services/Connectivity`) |
| `LogAnalyzer.Infrastructure/` | Parsere, motoare de detecție, acces la date |
| `LogAnalyzer.Dfir.Core/`, `LogAnalyzer.Dfir.Windows/` | Platforma DFIR: caz, probe, custodie, parsere reale, colectoare |
| `LogAnalyzer.Dfir.Tests/`, `LogAnalyzer.UI.Tests/` | Teste (xUnit) |
| `LogAnalyzer.LicenseManager/`, `LogAnalyzer.KeyGen/` | Emiterea licențelor (doar pentru stația de administrare; nu se livrează clienților) |
| `docs/dfir/` | Audit de arhitectură DFIR |
| `Documentation/` | Status faze, decizii, checklist de release, playbook |

## Securitate și limitări

- Controalele de integritate și trasabilitate sunt la nivel de aplicație. Nu constituie certificare ORNISS / ISO/IEC 27037 / Common Criteria.
- Raportarea vulnerabilităților: `SECURITY.md`.
- Înainte de orice distribuție: `Documentation/RELEASE-CHECKLIST.md`.
