# LogAnalyzer — Detecție (P5)

Starea la 2026-10-05. Cod: `LogAnalyzer.Dfir.Core/Detection/` (`IocMatcher`, `YaraLite`, `SigmaLite`, `DetectionEngine`).

## Rezultatul unei detecții (`DetectionResult`, spec §10)

| Câmp | Semnificație |
|---|---|
| `RuleId`, `RuleVersion`, `RuleSha256` | regula exactă care s-a potrivit: identitate, versiune, hash-ul textului |
| `Kind` | `IOC`, `HASH`, `YARA`, `SIGMA` |
| `EvidenceId`, `Locator`, `EvidenceSha256` | proba și locul din ea; hash-ul sursei de la achiziție |
| `Match` | ce s-a potrivit (câmp, valoare, offset) |
| `Classification`, `Confidence`, `Explanation` | prezența criteriului (DIRECT); încrederea din regulă sau listă |

O potrivire arată că proba îndeplinește criteriile regulii. Nu dovedește singură intenția sau rezultatul unei activități. Aplicația nu declară „malware” doar dintr-un șir sau un nume de proces.

## Surse de reguli

`DetectionEngine.Load` citește:
- regulile livrate cu aplicația (`Rules/` lângă executabil);
- folderul `Rules/` al cazului: `ioc/*.csv`, `yara/*.yar`, `sigma/*.yml`.

Un fișier de reguli care nu se încarcă devine gol de probă (`Regulă …`) cu eroarea exactă. Nu oprește analiza și nu e sărit în tăcere. Fiecare caz scrie:
- `Analysis/rules.json` — regulile aplicate, cu hash;
- `Analysis/detections.json` — potrivirile.

## IOC (`IocMatcher`)

- **Format:** CSV, fie formatul inventarului din investigație (`Type, Value, Context, Classification, Confidence, Source`), fie `type, value, description`.
- **Tipuri:**
  - `sha256` (comparat și cu hash-ul fișierelor achiziționate), `sha1`, `md5`;
  - `ip`;
  - `domain` (inclusiv subdomenii);
  - `path` (`…\` = prefix de folder; `*` și `?`; cale normalizată);
  - `filename`, `task`, `registry`.

## YARA-lite (`YaraLite`) — subset implementat aici, nu libyara

- **Șiruri:**
  - text cu `ascii`, `wide`, `nocase`, `fullword`;
  - hex cu `??` și salturi `[n-m]`;
  - `/regex/` (regex .NET peste octeți citiți ca Latin-1).
- **Condiții:** `$a`, `#a op N`, `any|all|N of them`, `any|all|N of ($p*)`, `filesize op N[KB|MB]`, `and`, `or`, `not`, paranteze.
- **Orice altceva e eroare de parsare:** `xor`, `base64`, `at`, `in`, module, `for … of`.
- **Ce se scanează:** doar probele de tip `file` (fișiere importate fără parser dedicat), după un preflight care confirmă că nu s-au schimbat. Limita este de 256 MB per fișier; peste ea, fișierul devine gol de probă.

## Sigma-lite (`SigmaLite`) — subset implementat aici, nu pySigma

- **logsource:** `product: windows` și `service` ∈ security, system, application, powershell, windefend, taskscheduler, sysmon, firewall. `category` nu este acceptat.
- **Selecții:** hartă (AND între câmpuri, OR între valorile unei liste) sau listă de hărți (OR).
- **Modificatori:** `contains`, `startswith`, `endswith`, `all`, `re`.
- **Condiții:** nume, `and`, `or`, `not`, paranteze, `1|all of nume*`, `1|all of them`.
- **Câmpuri:** numele din EventData. `EventID` și `Provider_Name` vin din antetul evenimentului. Comparațiile de șiruri ignoră majusculele, ca în Sigma.

## Regulile livrate și validarea lor

| Regulă | Validare pe date reale |
|---|---|
| YARA `MSBuild_PropertyFunction_EntityObfuscation` (T1127.001) | se declanșează pe `NetworkMonitor.targets` din incident și pe niciunul dintre fișierele vecine; 0 potriviri pe cele 496 de `.targets`/`.props` din SDK-ul .NET (90 folosesc legitim funcții de proprietate) |
| Sigma Defender 1116/1117 | aceleași 418 înregistrări ca `wevtutil qe` cu XPath, pe jurnalul real |
| Sigma System 104 | aceleași înregistrări ca `wevtutil` (3) |
| Sigma Security 1102 | 0 în corpus (nicio ștergere a jurnalului de audit) |
| Sigma 7045 din AppData/Temp/Public | aceleași înregistrări ca `wevtutil` + ImagePath; în corpus e driverul GPU-Z extras în Temp: semantica regulii e respectată, contextul e benign |
| IOC (inventarul investigației) | toate cele 9 probe cu SHA-256 în inventar au hash identic la import; folderul loader-ului apare în Prefetch-ul MSIEXEC |

## Limite

- Regulile Sigma și YARA publice (SigmaHQ, colecții YARA) nu sunt incluse și nu sunt descărcate automat. Se pot pune în folderul `Rules/` al cazului; cele care depășesc subsetul sunt raportate ca neîncărcate.
- Nu există corelare temporală Sigma (`timeframe`, `count`).
