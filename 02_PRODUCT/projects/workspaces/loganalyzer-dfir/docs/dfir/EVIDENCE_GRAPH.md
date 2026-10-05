# LogAnalyzer — Graful de probe (P4)

Starea la 2026-10-05. Cod: `LogAnalyzer.Dfir.Core/Graph/EvidenceGraph.cs`. Fiecare caz scrie `Analysis/graph.json`, cu SHA-256 trecut în custodie.

## Regula de bază

O relație (muchie) are **fie** o probă, **fie** o derivare explicită:
- **probă:** `EvidenceId` + `Locator` ale înregistrării care arată legătura;
- **derivare:** constatarea sau regula care a dedus-o.

Constructorul `Relationship` refuză orice muchie fără niciuna dintre ele. Legăturile deduse prin corelare sunt `CORRELATED`, iar cele văzute direct într-o probă sunt `DIRECT`.

## Entități

`Host`, `User`, `File` (cale normalizată: fără `\VOLUME{…}` și fără literă de unitate), `Domain`, `IpAddress`, `Service`, `Task`, `RegistryKey`, `Device`, `Threat`, `Finding`.

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

DOWNLOADED, OPENED și DETECTED sunt extensii explicite față de lista minimă din spec §7. Restul tipurilor din spec există în enum și se folosesc când o sursă le poate dovedi (de exemplu LOGGED_ON, MEMBER_OF pentru domeniu).

## Interogări

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

## Limite

- Fluxurile SRUM nu au adresă de destinație, deci nu produc CONNECTED_TO.
- LOGGED_ON / AUTHENTICATED din jurnalul Security nu sunt încă trase în graf.
- Graful nu are încă o vizualizare în aplicație. Există doar JSON-ul și interogările din cod.
