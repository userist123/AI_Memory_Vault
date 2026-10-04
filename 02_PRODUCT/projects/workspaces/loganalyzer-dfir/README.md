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
