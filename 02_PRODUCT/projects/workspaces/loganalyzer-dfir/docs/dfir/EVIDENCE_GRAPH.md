# LogAnalyzer — Graful de probe (P4)

Starea la 2026-10-05. Cod: `LogAnalyzer.Dfir.Core/Graph/EvidenceGraph.cs`. Fiecare caz scrie `Analysis/graph.json`, cu SHA-256 trecut în custodie.

## Regula de bază

O relație (muchie) are **fie** o probă, **fie** o derivare explicită:
- **probă:** `EvidenceId` + `Locator` ale înregistrării care arată legătura;
- **derivare:** constatarea sau regula care a dedus-o.

Constructorul `Relationship` refuză orice muchie fără niciuna dintre ele. Legăturile deduse prin corelare sunt `CORRELATED`, iar cele văzute direct într-o probă sunt `DIRECT`.

## Entități

`Host`, `User`, `File` (cale normalizată: fără `\VOLUME{…}` și fără literă de unitate), `Domain`, `IpAddress`, `Service`, `Task`, `RegistryKey`, `Device`, `Threat`, `Finding`;
din inventarul de domeniu (P9): `AdDomain`, `Account`, `Group`, `Control` și `Host` pentru conturile de calculator.

## Relații produse azi

| Sursă | Relație | Clasificare |
|---|---|---|
| Prefetch | Host EXECUTED File; File LOADED File (doar scripturi și executabile din locații scriabile, referite în Prefetch) | DIRECT |
| BAM / UserAssist | User / Host EXECUTED File | DIRECT |
| Descărcare Chromium / Firefox | Domain (URL final) DOWNLOADED File; Domain (pagina tab-ului) DOWNLOADED File, cu motiv diferit | DIRECT |
| Serviciu / Task / Run / Winlogon / IFEO | File PERSISTED Service / Task / RegistryKey | DIRECT |
| USB | Device INSTALLED / CONNECTED_TO Host | DIRECT |
| LNK / Jump List | Host OPENED File | DIRECT |
| PCAP | Domain RESOLVED IpAddress; IpAddress CONNECTED_TO IpAddress | DIRECT |
| Defender 1116/1117 | Threat DETECTED File | DIRECT |
| Constatări | entitatea produsă de o probă SUPPORTS Finding | DIRECT (cu proba) |
| INCIDENT-CHAIN | pas PART_OF lanț | CORRELATED (derivare) |
| DOWNLOAD-THEN-EXEC | programul DERIVED_FROM fișierul descărcat | CORRELATED (derivare) |
| Inventar AD (P9) | Account / Host / Group PART_OF AdDomain; Account / Host MEMBER_OF Group (membri recursivi, cum i-a întors LDAP) | DIRECT (proba = inventarul salvat, locatorul = DN-ul sau interogarea LDAP) |
| Verificări de domeniu NECONFORM (P9) | Account / Host VIOLATES Control, doar pentru conturile numite de evaluator | DIRECT, cu derivarea `DomainEvaluator DMxx` |

DOWNLOADED, OPENED și DETECTED sunt extensii explicite față de lista minimă din spec §7. Restul tipurilor din spec există în enum și se folosesc când o sursă le poate dovedi.

## Interogări

În aplicație (fila „Graf de probe”, cod `GraphExplorer`): căutarea unei entități, relațiile ei cu direcția, ora, clasificarea și
proba (EvidenceId + locator) sau derivarea, plus drumul cel mai scurt între două entități. Clasificarea folosește termenii din
spec §26: OBSERVED (probă directă), CORRELATED, INFERRED (candidat), UNPROVEN. Aceiași termeni apar și în grila de constatări.


- `Edges(id)` — relațiile unei entități.
- `Neighbors(id)` — vecinii.
- `Path(a, b)` — cel mai scurt drum, ignorând direcția.
- `Find(type, text)` — căutare de entitate.
- `Snapshot(caseId)` — JSON determinist, cu hash.

## Ce arată pe cazul NanAgent (test de corpus)

- `mailjilq.tzd4is.cyou` (pagina) și `…192169503.com` (URL-ul final `blob:`) → DOWNLOADED → `SamFw_FRP_Tool_…_302044.zip` (DIRECT, Chrome).
- `…\DOWNLOADS\SAMFW_FRP_TOOL_V5.9_SETUP_DOWNLOAD_LATES_ARCHIVE_FILE_302044\SETUP.EXE` → DERIVED_FROM → arhiva (CORRELATED, DOWNLOAD-THEN-EXEC).
- MSIEXEC.EXE → LOADED → BOOTSTRAP_7D57.CMD (DIRECT, Prefetch).
- `Behavior:Win32/GenCodeInjected.H` → DETECTED → NanAgent32.exe (DIRECT, Defender).

**Nu există** o muchie directă SETUP.EXE → NanAgent32.exe: nicio probă din corpus nu o arată (Prefetch-ul lui SETUP.EXE nu face referire la NanAgent32.exe). Cele două sunt legate doar prin lanțul de incident, adică o corelare în timp. Graful păstrează această diferență.

## Domeniu (P9)

`DomainGraph.Build` (`Dfir.Windows/Domain/DomainGraph.cs`) primește inventarul LDAP, verificările de domeniu și `EvidenceId`-ul
inventarului înregistrat în caz; refuză un inventar neînregistrat. Pagina „Investigație domeniu și e-mail” salvează după fiecare
inventar și `domain_graph_*.json`, înregistrat ca probă derivată (părinte = inventarul).

- Conturile pe care le vizează o verificare vin din evaluator (`ControlCheck.Subjects`), nu din textul probelor.
- Doar verificările NECONFORM produc VIOLATES; DE VERIFICAT nu este o încălcare.
- Un cont de calculator devine `Host:<nume>`, același id pe care graful cazului îl dă stației cu acel nume: cele două grafuri se
  unesc pe el (`DomainGraph.Build(..., into: graful cazului)`).
- Validat pe un inventar sintetic; **nu a fost rulat pe un domeniu real** (stația de dezvoltare nu face parte dintr-un domeniu).

## Limite

- Fluxurile SRUM nu au adresă de destinație, deci nu produc CONNECTED_TO.
- LOGGED_ON / AUTHENTICATED din jurnalul Security și din cronologia utilizatorului de domeniu nu sunt încă trase în graf.
- Vizualizarea din aplicație este tabelară, nu un desen al grafului: fila „Graf de probe” a paginii „Investigație completă”.
