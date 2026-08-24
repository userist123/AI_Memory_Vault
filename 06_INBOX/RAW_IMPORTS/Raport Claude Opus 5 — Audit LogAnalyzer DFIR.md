# Audit tehnic & research strategic — „LogAnalyzer DFIR Enterprise" v.next

**Rol asumat:** arhitect principal de securitate cibernetică / expert senior DFIR, Tier‑3 SOC.
**Obiect:** aplicație C#/.NET 10 WPF, Clean Architecture, SQLite WAL, strict air‑gapped, motoare Sigma + YARA offline, parsare EVTX și hive‑uri de Registru, interfață integral în limba română.
**Data auditului:** 17 august 2026.

---

## 0. Sinteză executivă

LogAnalyzer DFIR Enterprise este, în forma actuală, un **analizor de jurnale cu strat de detecție** — nu încă o **platformă de investigație forenzică**. Diferența nu este cosmetică: un analizor răspunde la întrebarea „ce alerte am?", în timp ce o platformă forenzică răspunde la întrebarea „ce s‑a întâmplat, cu ce grad de certitudine, și pot susține concluzia în fața unui auditor DNSC, a unui asigurător cyber sau a unei instanțe?".

Cele trei deficite structurale (dincolo de lista de artefacte lipsă) sunt:

1. **Deficit de acoperire a evidenței de execuție și a evidenței de acces la fișiere.** Aplicația ingerează EVTX și Registru. Într‑un incident real, jurnalele Security au fost adesea rotite sau șterse (EID 1102), iar reconstrucția depinde de artefacte de sistem de fișiere și de compatibilitate: `$MFT`, `$UsnJrnl:$J`, Prefetch, Amcache, Shimcache, SRUM, MPLog. Fără ele, timeline‑ul are găuri de zile întregi exact în perioada de interes.
2. **Deficit de epistemologie forenzică.** Nu există un model explicit de **fiabilitate probatorie per artefact**. Aceasta este o eroare gravă în 2026: Shimcache **nu mai probează execuția** pe Windows 10/11 (flag‑ul de inserție a fost eliminat la rescrierea din Windows 10, iar `AppCompatCacheParser` a afișat mult timp „NA" pe coloana de execuție — [nullsec.us, AppCompatCache Deep Dive](https://nullsec.us/windows-10-11-appcompatcache-deep-dive/)), iar Mandiant avertizează explicit că pot exista intrări în Shimcache pentru binare care **nu au fost niciodată executate** ([Mandiant, „Caching Out: The Value of Shimcache for Investigators"](https://cloud.google.com/blog/topics/threat-intelligence/caching-out-the-val/)). Un „Scor Global de Risc AI 0–100" care agregă indiferent artefacte de fiabilitate diferită produce concluzii nedefendabile.
3. **Deficit de detecție a anti‑forensics și de „evidență a absenței".** Aplicația detectează activitate rea; nu detectează **manipularea probelor**: timestomping (divergență `$STANDARD_INFORMATION` vs `$FILE_NAME`, sub‑secundă zero), ștergerea de jurnale, reutilizarea intrărilor MFT, golirea `$UsnJrnl`. În ransomware și în intruziunile APT, aceste semnale sunt frecvent singura probă rămasă.

Peste acestea se adaugă trei deficite tactice: (a) motorul Sigma este mono‑eveniment — nu implementează **Sigma Correlations**, deci nu poate exprima nativ „10 × 4769 RC4 de la același client în 5 minute"; (b) maparea MITRE este ancorată în modelul de detecție pre‑v18, obsolet din 28 octombrie 2025 ([MITRE, „ATT&CK v18: The Detection Overhaul"](https://medium.com/mitre-attack/attack-v18-8f82d839ee9e)); (c) exporturile acoperă CTI (STIX/MISP) dar **nu acoperă probatoriul** (CASE/UCO), ceea ce blochează interoperabilitatea cu ecosistemul judiciar european.

**Verdict de prioritizare.** Recomand 12 module P0 (paritate probatorie + defensibilitate juridică), 11 module P1 (valoare analitică ridicată) și 9 module P2 (rafinament). Tabelul de sinteză cu efort estimat este în §7.

---

## 1. Metodologie de audit și criteriu de prioritizare

Prioritizarea nu este „cât de cool e feature‑ul", ci un scor compus, aplicat fiecărei propuneri:

\[
\text{Prioritate} = \frac{V_{prob} \times F_{inc} \times R_{jur}}{C_{impl} \times C_{ment}}
\]

- \(V_{prob}\) — **valoare probatorie**: cât de aproape este artefactul de faptul juridic (execuție, acces, exfiltrare) și cât de greu poate fi falsificat de atacator.
- \(F_{inc}\) — **frecvența de utilizare reală** în incidente (ransomware, BEC pivotat pe endpoint, APT de spionaj, insider).
- \(R_{jur}\) — **expunere de conformitate**: contribuie la obligațiile NIS2 art. 23 (24h/72h/1 lună), la ISO/IEC 27037/27042, la raportarea către DNSC.
- \(C_{impl}\), \(C_{ment}\) — cost de implementare și, mai important pentru un produs air‑gapped, **cost de mentenanță a formatelor** (fiecare artefact binar Windows este un contract instabil între versiuni de OS).

Un al doilea filtru, specific arhitecturii: **compatibilitatea cu constrângerea air‑gapped**. Orice propunere care presupune enrichment online (VirusTotal, GeoIP live, feed‑uri TI) este respinsă sau reproiectată ca **„Intel Pack" offline semnat** (§P0‑12).

---

## 2. Diagnostic critic al implementării actuale (constatări înainte de propuneri)

Aceste constatări sunt cel puțin la fel de valoroase ca lista de funcționalități noi, pentru că vizează **corectitudinea** a ceea ce există deja.

**C‑01. Pragul de entropie Shannon H > 4.8 este fragil și prost calibrat.**
Entropia Shannon pe octeți are maxim 8 bit/octet; text ASCII normal se situează la 4.0–4.8, iar PowerShell minificat, JSON mare, log‑uri Base64 legitime și scripturi cu multe GUID‑uri depășesc frecvent 4.8. Consecință: rată de fals‑pozitiv mare pe scripturi de administrare legitime și fals‑negativ pe obfuscări cu alfabet mic (ex. `-Join`/backtick/`char[]` cu entropie **scăzută**). Recomandare: înlocuirea metricii unice cu un **vector de trăsături** — entropie pe clase de caractere, raport de compresie (proxy Kolmogorov, `Deflate` din `System.IO.Compression`), test χ² pe distribuția caracterelor, densitatea de caractere non‑alfanumerice, lungimea medie a token‑ilor, prezența de indicatori sintactici (`FromBase64String`, `IEX`, `-enc`, `[Convert]`, `SecureString`). Sursa de aur pentru această analiză este EID 4104 (Script Block Logging), care conține **scriptul deja de‑obfuscat de motorul PowerShell** — cel mai fidel material disponibil ([Splunk, „Hunting for Malicious PowerShell using Script Block Logging"](https://www.splunk.com/en_us/blog/security/hunting-for-malicious-powershell-using-script-block-logging.html)).

**C‑02. Euristica „logări nocturne 01:00–05:00" este un prag fix acolo unde trebuie un model de bază (baseline).**
Într‑un mediu cu ture 24/7, o autentificare la 03:00 este normalitate statistică; într‑un mediu de birou, 19:30 poate fi anomalia. Recomandare: baseline **per cont și per stație**, calculat pe fereastra ingerată, cu statistică circulară (medie/concentrație von Mises pe ora zilei) plus scor de raritate (frecvență inversă a combinației cont × stație × LogonType). Aceasta este aceeași logică de „stack counting / rarity" folosită de threat hunteri și e complet realizabilă offline în SQL.

**C‑03. Process Tree construit pe PID este nesigur (PID reuse).**
Windows reciclează PID‑urile în minute pe sisteme active. Cheia corectă de corelare este `ProcessGuid` (Sysmon EID 1/5) sau tripletul `(PID, ProcessStartTime, LogonId)` pentru EID 4688. Fără asta, arborele de procese poate „lipi" un proces malițios sub un părinte nevinovat — eroare care distruge credibilitatea narativei de atac.

**C‑04. Scorul „AI de Risc 0–100" este o cutie neagră nedefendabilă.**
Cerința de audit este explicabilitatea: fiecare punct de scor trebuie să aibă o **contribuție trasabilă** (feature, artefact‑sursă, hash‑ul fișierului sursă, regula declanșată). Recomandare: scor aditiv logistic cu greutăți versionate în manifest (`ScoringModel v1.3`, hash SHA‑256 în raport), plus panou „de ce acest scor" cu descompunere pe factori. Fără explicabilitate, ISO/IEC 27042 (reproductibilitate și repetabilitate a analizei) nu poate fi susținut ([ISO/IEC 27042:2015](https://www.iso.org/standard/44406.html)).

**C‑05. Maparea MITRE trebuie migrată la modelul v18.**
ATT&CK v18 (28 oct. 2025) a înlocuit secțiunea „Detections" atașată tehnicilor cu două obiecte noi — **Detection Strategies** și **Analytics** ([MITRE ATT&CK, Detection Strategies](https://attack.mitre.org/detectionstrategies/); [Picus, „What's New in MITRE ATT&CK v18"](https://www.picussecurity.com/resource/blog/whats-new-in-mitre-attack-v18)). Practic, fiecare regulă Sigma/YARA din LogAnalyzer ar trebui mapată nu doar la `Txxxx`, ci la un `DETxxxx`/`ANxxxx`, ceea ce transformă matricea de acoperire din „câte tehnici ating" în „ce strategii de detecție acopăr, cu ce telemetrie".

**C‑06. Nu există model de vizibilitate (visibility), doar model de detecție.**
O matrice ATT&CK „verde" obținută pe un caz în care nu s‑a ingerat `$MFT`, Prefetch sau Sysmon este înșelătoare. Standardul de facto pentru separarea celor două este DeTT&CT, care scorează separat **calitatea surselor de date, vizibilitatea și acoperirea detecției** pe scale ordinale și generează layer‑e pentru ATT&CK Navigator ([DeTT&CT Wiki](https://github.com/rabobank-cdc/DeTTECT/wiki); [SANS Detection Coverage Scorecard](https://www.sans.org/tools/detection-coverage-scorecard)).

**C‑07. Lanțul de custodie se oprește la SHA‑256 la ingestie.**
Corect, dar insuficient: hash‑ul dovedește integritatea, nu **momentul**. Într‑un litigiu, „când ai avut acest fișier" contează. Soluția compatibilă air‑gapped este generarea de cereri de timestamp RFC 3161 (TSQ) exportate pe suport amovibil, contra‑semnate ulterior de o TSA calificată și reimportate ca token TSR atașat manifestului cazului ([RFC 3161, Time‑Stamp Protocol](https://datatracker.ietf.org/doc/html/rfc3161)).

---

## 3. P0 — CRITIC / ESENȚIAL

> Criteriu P0: fără aceste module, produsul nu atinge paritatea probatorie cu KAPE + EZ Tools + Velociraptor și nu poate susține un raport defendabil.

### P0‑1. Modul „Artefacte NTFS Core" — parser `$MFT`, `$UsnJrnl:$J`, `$LogFile`, `$I30`

**1. Nume:** `LogAnalyzer.Forensics.NTFS` (Modul „Sistem de Fișiere Master").

**2. Valoarea tactică forenzică.** Este cel mai important artefact absent. `$MFT` oferă inventarul complet al volumului cu patru timestamp‑uri × două atribute, permițând reconstrucția fișierelor șterse (intrări nealocate) și detecția timestomping‑ului. `$UsnJrnl:$J` este un jurnal secvențial al **fiecărei modificări** din sistemul de fișiere, supraviețuiește ștergerii fișierului și păstrează, tipic, în jur de 20 de zile de activitate pe un volum activ ([Andrea Fortuna, „Going beneath NTFS"](https://andreafortuna.org/2026/07/06/ntfs-forensics-deep-dive/)). Aceasta este proba pentru: staging de exfiltrare (creare arhive `.7z`/`.zip` în `C:\Windows\Temp`), ștergerea uneltelor atacatorului, redenumiri de mascare, și cronologia criptării în ransomware. `$I30` (atributul index al fiecărui director) conține duplicate ale timestamp‑urilor `$FILE_NAME` și permite recuperarea numelor de fișiere șterse din slack ([cyberengage, „Understanding, Collecting, Parsing the $I30"](https://www.cyberengage.org/post/understanding-collecting-parsing-the-i30); [Velociraptor, `Windows.NTFS.I30`](https://docs.velociraptor.app/artifact_references/pages/windows.ntfs.i30/)).

**3. Structura tehnică / datele necesare.**
- Intrare: fișiere extrase `$MFT`, `$Extend\$UsnJrnl` (stream ADS `$J`), `$LogFile`, opțional imagine RAW/E01 montată read‑only.
- `$MFT`: înregistrări `FILE` de 1024 B; parsare atribute `0x10 $STANDARD_INFORMATION` (M, A, C, B în FILETIME 100 ns), `0x30 $FILE_NAME` (al doilea set MACB + `ParentFileReference`), `0x80 $DATA` (resident/non‑resident, `$DATA` alternate = ADS — indicator de Zone.Identifier și de payload ascuns), `0xA0 $INDEX_ALLOCATION`.
- Reconstrucția căii complete prin urcarea recursivă pe `ParentFileReference`, cu **detecția reutilizării intrărilor MFT** (comparare număr de secvență) — pas obligatoriu pentru a nu atribui greșit o cale unui fișier ([exhume_ntfs, USN Journal Processing](https://deepwiki.com/forensicxlab/exhume_ntfs/5.4-usn-journal-processing)).
- `$J`: înregistrări USN v2/v3/v4, câmpuri `Usn`, `FileReferenceNumber`, `ParentFileReferenceNumber`, `Reason` (bitmask: `FILE_CREATE`, `DATA_OVERWRITE`, `RENAME_OLD_NAME`/`RENAME_NEW_NAME`, `FILE_DELETE`, `BASIC_INFO_CHANGE`), `SourceInfo`, `FileAttributes`. Practica de referință (MFTECmd) parsează `$J` **împreună cu** `$MFT` pentru rezolvarea căilor ([cyberengage, parsare `$J` cu MFTECmd](https://www.cyberengage.org/post/ntfs-journaling-in-digital-forensics-logfile-usnjrnl-parsing-of-j-logfile-using-mftecmd-ex)).
- Detector integrat de **timestomping**: (a) `$SI.Created > $FN.Created` sau divergență peste toleranță; (b) componente sub‑secundă = 0 în `$SI` dar nenule în `$FN`; (c) `$SI` anterior datei de instalare a OS‑ului ([inversecos, „Timestomping Detection"](https://www.inversecos.com/2022/04/defence-evasion-technique-timestomping.html); [artefacts.help, MACB timestamps](https://artefacts.help/windows_macb_timestamps.html)).

**4. Integrare.**
- *Core*: entități `FileSystemObject`, `UsnRecord`, `TimestampSet` (cu enum `TimestampSource { SI, FN, I30, USN }`); interfață `IArtifactParser<T>` cu contract `ParseAsync(Stream, CancellationToken) → IAsyncEnumerable<T>`; reguli de anomalie ca `ITimestampAnomalyRule`.
- *Infrastructure*: `NtfsMftParser`, `UsnJournalParser` pe `MemoryMappedFile` + `Span<byte>`/`BinaryPrimitives` (zero‑alloc, esențial la `$MFT` de 1–4 GB); scriere în SQLite prin `Microsoft.Data.Sqlite` cu tranzacții batch de 50k rânduri și `PRAGMA journal_mode=WAL; synchronous=NORMAL`.
- *UI*: view nou „Sistem de Fișiere" (DataGrid virtualizat + filtre pe `Reason`), plus injectarea evenimentelor în „Cronologie Incident" ca sursă distinctă, colorată; badge „⚠ Timestomping suspectat" în inspectorul de fișier.

### P0‑2. Modul „Evidența Execuției" — Prefetch, Amcache, Shimcache, BAM/DAM, UserAssist, MPLog

**1. Nume:** `LogAnalyzer.Forensics.ExecutionEvidence` + panou UI „Triangulare Execuție".

**2. Valoarea tactică forenzică.** Nicio sursă unică nu probează execuția; **triangularea** o face. Prefetch dă până la 8 timestamp‑uri de execuție și lista de fișiere/DLL încărcate (adesea singura dovadă a directorului din care a rulat malware‑ul). Amcache stochează **SHA‑1** al binarelor, deci permite pivotare pe hash între stații chiar dacă ransomware‑ul s‑a auto‑șters, și la momentul redactării nu se cunoaște o metodă de a modifica sau elimina datele AmCache ([Securelist, „AmCache artifact: forensic value and a tool for data extraction"](https://securelist.com/amcache-forensic-artifact/117622/)). BAM/DAM oferă ultima execuție per SID, cu granularitate de utilizator, în `SYSTEM\CurrentControlSet\Services\bam\State\UserSettings\{SID}` ([Vortex Forensic Repository, BAM/DAM](https://vortexforensic.com/repository/Registry/bam.html); [Psmths, windows-forensic-artifacts](https://github.com/Psmths/windows-forensic-artifacts/blob/main/execution/bam-dam.md)). MPLog (Microsoft Protection Log) este artefactul subutilizat cu cel mai bun raport valoare/efort: text simplu, cu istoric de execuție de procese, fișiere accesate, detecții și acțiuni, în `C:\ProgramData\Microsoft\Windows Defender\Support\MPLog-*` — CrowdStrike îl folosește pentru a proba execuția și accesul la fișiere, inclusiv cazuri de exfiltrare cu RClone ([CrowdStrike, „How to Use MPLogs for Forensic Investigations"](https://www.crowdstrike.com/en-us/blog/how-to-use-microsoft-protection-logging-for-forensic-investigations/); [ForgeWork, Defender MPLog](https://forge-work.com/dfir/knowledge/artifacts/win-mplog.html)).

**3. Structura tehnică / datele necesare.**
- **Prefetch** (`C:\Windows\Prefetch\*.pf`): versiunile 17 (XP/2003), 23 (Vista/7), 26 (8/8.1), 30 (Win10) și **31 (Windows 11)**; pe Win10/11 fișierul începe cu semnătura `MAM\x04` și este comprimat **XPRESS Huffman**, deci trebuie decomprimat înainte de parsare, cu dimensiunea decomprimată la offset 0x04 — exact fluxul implementat în parserul de referință al lui Eric Zimmerman ([libyal, specificația formatului PF](https://github.com/libyal/libscca/blob/main/documentation/Windows%20Prefetch%20File%20(PF)%20format.asciidoc); [EricZimmerman/Prefetch, `PrefetchFile.cs`](https://github.com/EricZimmerman/Prefetch/blob/master/Prefetch/PrefetchFile.cs); [ForensicXlab, Prefetch](https://www.forensicxlab.com/blog/prefetch)). În .NET se folosește `System.IO.Compression`/API‑ul nativ `RtlDecompressBufferEx` (MSZIP/XPRESS_HUFF) — pe un produs air‑gapped, recomand implementarea managed pentru a nu depinde de `ntdll` la rularea pe alt OS. Elemente de extras: nume executabil, hash de cale pe 8 caractere din numele fișierului, `RunCount`, ultimele 8 `LastRunTime`, `FileMetricsArray`, `VolumeInformation` (serial + creare volum → detecție execuție de pe USB), `DirectoryNames`.
- **Amcache** (`C:\Windows\AppCompat\Programs\Amcache.hve`, hive REGF): subchei `InventoryApplicationFile` (cale, SHA‑1 în formatul cu prefix `0000`, `LinkDate`, dimensiune, publisher), `InventoryApplication`, `InventoryDriverBinary`, `InventoryDevicePnp`. Interpretare corectă: dovedește **prezența** fișierului văzut de Microsoft Compatibility Appraiser (task programat), nu neapărat execuția — de aceea intră în modelul de fiabilitate din §6.
- **Shimcache** (`SYSTEM\CurrentControlSet\Control\Session Manager\AppCompatCache`): ~1024 intrări, ordine LRU (poziția în cache = semnal temporal relativ), header `10ts` pe Win10/11 și indicator de execuție dedus din ultimii 4 octeți ai câmpului `Data` — nu din flag‑ul clasic de inserție, care a fost eliminat ([nullsec.us](https://nullsec.us/windows-10-11-appcompatcache-deep-dive/); [Magnet Forensics, ShimCache vs AmCache](https://www.magnetforensics.com/blog/shimcache-vs-amcache-key-windows-forensic-artifacts/)). Obligatoriu: parsare **din toate ControlSet‑urile**, nu doar `CurrentControlSet`.
- **UserAssist** (`NTUSER.DAT\...\Explorer\UserAssist\{GUID}\Count`, ROT13, structură cu `RunCount` + `FocusTime` + `LastExecuted`), **BAM/DAM** (FILETIME per executabil per SID), **MPLog** (parser de linii: `EstimatedImpact`, `Lowfi`, `DetectionEvent`, `Threat`, `ProcessImageName`, `MaxTimeFile`).
- **Motor de triangulare**: tabel `ExecutionEvidence(ImagePath, EvidenceType, Timestamp, Confidence, SourceFileSha256)` + view agregat care produce, per binar, un **profil de încredere** („Prefetch + Amcache + BAM = execuție confirmată, 3 surse independente" vs „doar Shimcache = prezență posibilă").

**4. Integrare.** *Core*: `ExecutionEvidenceAggregator` (serviciu de domeniu pur, testabil). *Infrastructure*: șase parsere separate, fiecare cu propriul `ArtifactSchemaVersion`, înregistrate prin DI ca `IArtifactParser`. *UI*: panou nou „Triangulare Execuție" cu grid pivot (binar × tip de evidență × timestamp) și heatmap de încredere; integrare `Click‑to‑Pivot` existentă către Cronologie.

### P0‑3. Modul „Activitate Utilizator & Interacțiune cu Fișiere" — Shellbags, LNK, Jump Lists, RecentDocs

**1. Nume:** `LogAnalyzer.Forensics.UserActivity`.

**2. Valoarea tactică forenzică.** Răspunde la întrebările pe care jurnalele nu le acoperă: ce **directoare** a răsfoit atacatorul (inclusiv partajări de rețea `\\server\share` și volume USB deja deconectate), ce fișiere a deschis, în ce ordine. Shellbags supraviețuiesc ștergerii directorului și sunt esențiale pentru a proba „recunoașterea" înaintea exfiltrării. LNK‑urile din `Recent` conțin un instantaneu al timestamp‑urilor MAC ale țintei, dimensiunea, volumul și `DistributedLinkTracker` cu adresa MAC a mașinii — indiciu de atribuire când un LNK a fost creat pe stația atacatorului și livrat victimei ([forensics.wiki, LNK](https://forensics.wiki/lnk/); [Windows Incident Response, „LNK Toolmarks"](http://windowsir.blogspot.com/2018/07/lnk-toolmarks.html)).

**3. Structura tehnică / datele necesare.**
- **Shellbags**: `USRCLASS.DAT\Local Settings\Software\Microsoft\Windows\Shell\BagMRU` + `Bags`, și `NTUSER.DAT\...\Shell\BagMRU` pentru resursele de rețea; parsare recursivă a arborelui `BagMRU` cu decodare **ShellItem** per tip (0x1F root/GUID, 0x2F volum, mască 0x70→0x30 file entry, 0x40 network, 0x00 delegate/property store) și a blocurilor de extensie `0xBEEF0004` (timestamp‑uri suplimentare, `MFTEntry`+`SequenceNumber` — punte directă către `$MFT`) ([Cyber5W, Windows Shell Items Analysis](https://cyber5w.com/blog/windows-shell-items-analysis); [TZWorks, Shellbag Parser](https://tzworks.com/prototype_page.php?proto_id=14); [libyal, formatul LNK / shell items](https://github.com/libyal/liblnk/blob/main/documentation/Windows%20Shortcut%20File%20(LNK)%20format.asciidoc)).
- **LNK**: header de 76 octeți, CLSID `{00021401-0000-0000-C000-000000000046}`, `LinkFlags`/`FileAttributes`, MAC ale țintei, `LinkInfo` (VolumeID, serial, `LocalBasePath`, `CommonNetworkRelativeLink`), `StringData` (`RelativePath`, `WorkingDir`, `CommandLineArguments`, `IconLocation`), blocuri extra (`TrackerDataBlock` cu Droid GUID/MAC, `EnvironmentVariableDataBlock`).
- **Jump Lists**: `%AppData%\Roaming\Microsoft\Windows\Recent\AutomaticDestinations\*.automaticDestinations-ms` (container OLE/CFBF cu stream‑uri LNK + stream `DestList` = MRU cu contoare de acces) și `CustomDestinations\*.customDestinations-ms`; `AppId` de 16 caractere hex identifică aplicația ([forensics.wiki, Jump Lists](https://forensics.wiki/jump_lists/); [Jumplist-Browser](https://kacos2000.github.io/Jumplist-Browser/)).
- Necesită o mică bibliotecă internă **CFBF/OLE reader** (nu adăugați dependențe COM — trebuie să ruleze deterministic offline).

**4. Integrare.** *Core*: `UserActivityEvent` cu discriminator `ActivityKind { FolderBrowse, FileOpen, AppLaunch }` și legătură slabă către `FileSystemObject` prin `MftEntry`+`Sequence`. *Infrastructure*: `ShellBagParser`, `LnkParser`, `JumpListParser`, `CfbfReader`. *UI*: view „Activitate Utilizator" cu **TreeView reconstruit al navigării** (arborele BagMRU redat ca ierarhie de foldere cu timestamp‑uri) — vizual foarte convingător în rapoarte.

### P0‑4. Modul „ESE Offline" — SRUM, WebCacheV01.dat, Windows Search

**1. Nume:** `LogAnalyzer.Forensics.Ese` (parser ESE/EDB propriu, read‑only, tolerant la baze „dirty").

**2. Valoarea tactică forenzică.** SRUM (`C:\Windows\System32\sru\SRUDB.dat`) este **cel mai apropiat lucru de un NetFlow retroactiv pe host**: octeți trimiși/primiți per aplicație, per interfață, per SID, pe ~30–60 de zile. Într‑un caz de exfiltrare, SRUM poate cuantifica volumul scos de `rclone.exe` sau `powershell.exe` chiar în absența log‑urilor de firewall — informație direct utilizabilă în notificarea NIS2 de 72h și în evaluarea de risc GDPR. SRUM monitorizează programe desktop, servicii, aplicații Windows și conexiuni de rețea, prin extensii identificate pe GUID: `{973F5D5C-1D90-4944-BE8E-24B94231A174}` = Network Data Usage Monitor, `{D10CA2FE-6FCF-4F6D-848E-B2E99266FA89}` = Application Resource Usage ([libyal esedb-kb, specificația SRUM](https://github.com/libyal/esedb-kb/blob/main/documentation/System%20Resource%20Usage%20Monitor%20(SRUM).asciidoc); [srumparser.com, tabelele SRUM](https://www.srumparser.com/en/blog/srum-tables-explained)).

**3. Structura tehnică / datele necesare.**
- Format ESE/EDB: header 2 × 4 KB (checksum XOR/ECC), pagini de 4/8/32 KB, catalog în tabela `MSysObjects` (obiect 2), tag‑uri de pagină, `long values` (LV) pentru coloane lungi, structuri B+‑tree. Baza este aproape sigur „dirty" (nu a fost închisă curat) — parserul trebuie să **citească fără recuperare** (fără `esentutl /r`, care ar modifica proba și ar încălca ISO/IEC 27037). Aceasta este exact critica clasică din formarea de browser forensics: „ESE dbs are dirty!" ([Browser Forensics, curs Univ. Pireu](https://thales.cs.unipi.gr/modules/document/file.php/CDS122/Day%205%20-%20Browser%20Forensics.pdf)).
- Rezolvarea `SruDbIdMapTable` (mapare `IdIndex` → cale executabil / SID binar) pentru a face inteligibile tabelele GUID.
- `WebCacheV01.dat` (`%LocalAppData%\Microsoft\Windows\WebCache\`) pentru istoric IE/Edge Legacy, cache, cookie‑uri și — important — descărcări prin `WinINet` folosite de scripturi și de unelte LOLBAS ([Forensic Focus, analiza bazei ESE din IE10](https://www.forensicfocus.com/articles/forensic-analysis-of-the-ese-database-in-internet-explorer-10/)).
- Alternativa de implementare rapidă: `Microsoft.Isam.Esent.Interop` (ManagedEsent) — dar este un wrapper subțire peste `esent.dll` al **sistemului gazdă**, deci dependent de OS și potențial modificator al fișierului; util doar ca fallback ([Microsoft, ManagedEsent](https://github.com/microsoft/ManagedEsent); [Microsoft Learn, ESE Managed Reference](https://learn.microsoft.com/en-us/windows/win32/extensible-storage-engine/extensible-storage-engine-managed-reference)). **Recomandarea mea este parser managed propriu, read‑only**, pentru determinism și pentru testabilitate CFTT.

**4. Integrare.** *Core*: `NetworkUsageRecord`, `AppResourceRecord`, `ResolvedIdentity`. *Infrastructure*: `EseDatabaseReader` (nivel jos: pagini, catalog, cursor) + `SrumInterpreter`/`WebCacheInterpreter` (nivel domeniu). *UI*: view „Consum & Rețea (SRUM)" cu grafic stivuit octeți/oră per proces și tabel „Top exfiltrare potențială"; injectare în Cronologie ca evenimente cu volum.

### P0‑5. Modul „Browser Forensics Offline" (Chrome/Edge/Firefox) cu tratarea App‑Bound Encryption

**1. Nume:** `LogAnalyzer.Forensics.Browsers`.

**2. Valoarea tactică forenzică.** Vectorul de acces inițial este, statistic, browserul (phishing, SEO poisoning, fake CAPTCHA/ClickFix, descărcare de installer troianizat). Fără istoric de navigare și descărcări, „Initial Access" din Cyber Kill Chain rămâne o presupunere. Artefactele cheie: `History` (Chrome/Edge, SQLite: `urls`, `visits` cu `visit_source` și tranziții, `downloads`, `downloads_url_chains` — lanțul complet de redirecționări până la payload), `places.sqlite` + `moz_annos` (Firefox), `Web Data`, `Login Data`, `Cookies`, `Sessions`, `Extension`/`Preferences` (extensii malițioase) ([Elite Digital Forensics, Windows Browser History Forensics](https://elitedigitalforensics.com/windows-browser-history-forensics/)).

**3. Structura tehnică / datele necesare.**
- Timestamp‑uri: WebKit/Chrome = microsecunde de la 1601‑01‑01 UTC; Firefox = microsecunde de la epoca Unix. Conversia greșită este una dintre cele mai frecvente erori de raport — trebuie centralizată într‑un singur `TimestampConverter` testat unitar.
- Citire SQLite **read‑only, cu WAL/journal alături**: obligatoriu `Mode=ReadOnly` + copierea împreună a `-wal` și `-shm`, altfel se pierd ultimele tranzacții (adesea exact vizitele de interes) sau se modifică proba.
- **Carving de înregistrări șterse** din paginile freelist/unallocated ale SQLite (istoric șters de atacator) — o capabilitate care diferențiază produsul de simple interogări SQL.
- **App‑Bound Encryption (ABE)**: din Chrome 127 (iulie 2024), cheia de criptare a cookie‑urilor este legată de `chrome.exe` semnat și de un serviciu privilegiat; prefixul valorii `v20` semnalează noua schemă, iar decriptarea offline pe altă mașină **nu este posibilă** doar din `Local State` + DPAPI ([BrowserForensics, „Chrome v20 app‑bound encryption, explained"](https://www.browserforensics.app/en/blog/chrome-v20-app-bound-encryption); [ElcomSoft, „Browser Forensics in 2026: App‑Bound Encryption and Live Triage"](https://blog.elcomsoft.com/2026/01/browser-forensics-in-2026-app-bound-encryption-and-live-triage/)). **Implicație de produs:** LogAnalyzer trebuie să (a) detecteze și să raporteze explicit prezența `v20` cu mesaj „cookie‑uri necriptabile offline — necesită triaj live", (b) nu pretindă niciodată capabilități de decriptare pe care nu le are (risc de raport eronat), (c) documenteze acest lucru în raportul PDF ca limitare metodologică — cerință de bună practică ISO/IEC 27042.

**4. Integrare.** *Core*: `WebVisit`, `WebDownload`, `BrowserProfile`, `EncryptedArtifactNotice`. *Infrastructure*: `ChromiumHistoryReader`, `FirefoxPlacesReader`, `SqlitePageCarver`. *UI*: view „Navigare & Descărcări" cu lanțul de redirecționări afișat ca graf liniar și marcaj pe fișierul descărcat corelat cu `$MFT`/Prefetch (dovada că descărcarea a fost și executată).

### P0‑6. Motor „Sigma Correlations" nativ pe SQLite (corelare multi‑eveniment)

**1. Nume:** `LogAnalyzer.Detection.SigmaCorrelation`.

**2. Valoarea tactică forenzică.** Majoritatea detecțiilor cu valoare reală sunt **de agregare**, nu de potrivire simplă: brute force, Kerberoasting, password spraying, scanare de partajări, exfiltrare pe volum. Sigma a standardizat acest lucru în secțiunea `correlation`, cu tipurile `event_count`, `value_count`, `temporal` și `temporal_ordered`, câmpurile `rules`, `timespan`, `group-by`, `aliases`, `generate` ([SigmaHQ, Correlations](https://sigmahq.io/docs/meta/correlations.html); [specificația Sigma Correlation Rules v2.0.2](https://github.com/SigmaHQ/sigma-specification/blob/main/specification/sigma-correlation-rules-specification.md)). Element strategic de reținut: suportul de backend pentru corelații este încă limitat (SPL, ES|QL, Loki, SQL, OpenSearch), iar **SQL este listat între cele suportate** — ceea ce face din SQLite‑ul LogAnalyzer un candidat natural și oferă produsului un avantaj competitiv real: un motor Sigma offline **mai expresiv** decât multe SIEM‑uri comerciale.

**3. Structura tehnică / datele necesare.**
- Parser YAML extins (`YamlDotNet`) pentru `correlation` + rezolvarea referințelor prin `name`/`id` la regulile de bază din același fișier (separator `---`).
- Compilator care emite SQL cu **window functions**: `event_count` → `COUNT(*) OVER (PARTITION BY group_by ORDER BY ts RANGE BETWEEN INTERVAL timespan PRECEDING AND CURRENT ROW)` (în SQLite: bucketizare pe `ts/timespan` + auto‑join sau CTE recursiv); `value_count` → `COUNT(DISTINCT field)`; `temporal` → `EXISTS` corelate pe fereastră; `temporal_ordered` → verificare de ordine cu `LAG`/`MIN(ts)` per regulă.
- Atenție la capcana documentată: cheia este `group-by` cu cratimă; `group_by` este ignorat silențios — validatorul propriu trebuie să semnaleze eroarea, altfel analiștii vor scrie reguli care „nu prind nimic" fără explicație.
- Extensie proprie recomandată (documentată ca `x-loganalyzer-`): corelații **cross‑artefact** (ex. `4688` + intrare Prefetch + creare fișier în `$J` în 60 s), imposibile în SIEM‑uri clasice pentru că acestea nu au artefacte de disc.

**4. Integrare.** *Core*: `ICorrelationRule`, `CorrelationCompiler`, `DetectionResult` cu `ContributingEventIds[]` (trasabilitate obligatorie). *Infrastructure*: `SqliteCorrelationExecutor` + tabele materializate `norm_events` (schemă normalizată tip ECS/Sigma taxonomy). *UI*: în Rule Workbench, tab „Corelații" cu previzualizare SQL generat și test instant pe cazul curent — reia UX‑ul existent de compilare live.

### P0‑7. Normalizare de câmpuri (taxonomie) + extindere masivă a canalelor EVTX

**1. Nume:** `LogAnalyzer.Core.EventNormalization` („Schema Unificată de Evenimente").

**2. Valoarea tactică forenzică.** Regulile Sigma presupun o taxonomie de câmpuri (`Image`, `ParentImage`, `CommandLine`, `TargetUserName`, `LogonType`...). Dacă LogAnalyzer stochează XML brut și mapează ad‑hoc, portabilitatea regulilor comunitare (~3000+ reguli SigmaHQ) se rupe. În plus, auditul arată că doar câteva canale sunt exploatate; canalele cu cel mai mare randament DFIR, adesea neglijate: `Microsoft-Windows-Sysmon/Operational` (1, 3, 7, 8, 10, 11, 13, 17, 18, 22, 23, 25), `TerminalServices-LocalSessionManager/Operational` (21, 25 – reconectare RDP), `TerminalServices-RemoteConnectionManager/Operational` (1149), `TaskScheduler/Operational` (106, 140, 200/201), `PowerShell/Operational` (4103, 4104), `WinRM/Operational`, `WMI-Activity/Operational` (5857–5861), `Windows Defender/Operational` (1116/1117, 5001, 5007 – dezactivare protecție), `SMBClient`/`SMBServer`, `BITS-Client/Operational`, `CodeIntegrity/Operational` (drivere nesemnate – BYOVD), `Application-Experience/Program-Inventory`.

**3. Structura tehnică / datele necesare.**
- Tabel `norm_events(id, ts_utc, host, channel, provider, event_id, record_id, user_sid, process_guid, image, parent_image, command_line, target_user, logon_id, logon_type, src_ip, dst_ip, hash_sha256, raw_xml_ref, source_file_sha256)` + tabelă laterală `event_fields(event_id, key, value)` pentru câmpuri rare (model EAV, indexat).
- Mapări declarative în fișiere YAML versionate (`mapping/security_4688.yaml`), nu în cod — permite actualizare fără recompilare, cu hash inclus în raport.
- **Recuperare de înregistrări EVTX corupte/șterse**: parsare la nivel de chunk (`ElfChnk`), validare CRC și recuperare din spațiul liber al chunk‑ului, cu rezolvarea template‑urilor și a tabelelor de string‑uri stocate o dată per chunk — abordarea implementată în `libevtx` pentru gestionarea corupției ([libyal/libevtx, Corruption Handling](https://deepwiki.com/libyal/libevtx/8.2-corruption-handling)). Această capabilitate este direct legată de detecția anti‑forensics: după `EID 1102` (ștergere jurnal), carving‑ul de înregistrări este singura cale.
- Index FTS5: tabelă externă‑content pe `command_line` + `raw_xml`, cu tokenizer `trigram` pentru căutări de substring rapide (`LIKE '%...%'` pe milioane de rânduri este O(n) inacceptabil) ([SQLite, FTS5](https://www.sqlite.org/fts5.html)).

**4. Integrare.** *Core*: `NormalizedEvent`, `IFieldMapper`, `TaxonomyRegistry`. *Infrastructure*: `EvtxChunkReader` (cu mod recovery), `SqliteBulkWriter` pe `System.Threading.Channels` (pipeline producător/consumator, backpressure). *UI*: selector de canale la ingestie + indicator de acoperire („12/23 canale prezente în această colecție").
### P0‑8. Modul „Anti‑Forensics & Integritatea Probelor" (detecția manipulării)

**1. Nume:** `LogAnalyzer.Analytics.AntiForensics` — panou UI „Integritatea Probelor".

**2. Valoarea tactică forenzică.** Este propunerea cu cel mai mare grad de diferențiere față de concurență: majoritatea uneltelor caută activitate rea, puține caută **urmele ștergerii urmelor**. Într‑un incident cu operator competent, aceasta este adesea singura probă rămasă și, juridic, este o probă de **intenție** (element esențial în dosarele penale și în reclamațiile de asigurare).

**3. Structura tehnică / datele necesare.** Set de detectoare, fiecare cu scor și explicație:
- `EID 1102` (Security cleared) și `EID 104` (System log cleared) + verificare de discontinuitate în `EventRecordID` per canal (dovada de rotire vs ștergere).
- Timestomping: reguli SI/FN și sub‑secundă (vezi P0‑1).
- `$UsnJrnl` golit/recreat: salt mare în `Usn` sau `$J` cu dimensiune suspect de mică raportată la vârsta volumului.
- Reutilizare de intrări MFT (număr de secvență incrementat) → semnalizează faptul că numele reconstruit poate fi eronat; obligatoriu afișat, nu doar calculat.
- Ștergerea Prefetch (director prezent, `RunCount` inconsecvent, absență `.pf` pentru binare atestate de Amcache/BAM) și dezactivarea Prefetch prin `EnablePrefetcher = 0`.
- Dezactivarea jurnalizării: `ScriptBlockLogging`/`ModuleLogging` puse pe 0, `EnableTranscripting`, dezactivare Defender (`DisableRealtimeMonitoring`, EID 5001/5007), `wevtutil sl ... /e:false`.
- Ștergerea Volume Shadow Copies (`vssadmin delete shadows`, `wmic shadowcopy delete`) — semnătură canonică de ransomware — corelată cu inventarul VSS rămas.
- Ștergerea `USRCLASS.DAT`/Shellbags, `ClearRecentDocsOnExit`, `SysInternals EULA` (dovada rulării uneltelor SysInternals: `HKCU\Software\Sysinternals\<Tool>\EulaAccepted`), curățare `Recent`.
- Golirea `Amcache` (rar; dar absența `InventoryApplicationFile` cu prezența `InventoryDriverBinary` e anomalie).

**4. Integrare.** *Core*: `IAntiForensicDetector` (colecție injectată), `EvidenceIntegrityReport`. *Infrastructure*: interogări SQL + acces la parserele P0‑1/P0‑2. *UI*: card dedicat pe Command Dashboard („Integritate probe: 3 indicii de anti‑forensics") + secțiune obligatorie în raportul PDF, pentru că afectează încrederea în toate celelalte concluzii.

### P0‑9. Modul „Persistență Comprehensivă" — ASEP‑uri, Scheduled Tasks, Servicii, WMI Event Consumers

**1. Nume:** `LogAnalyzer.Forensics.Persistence` (extinderea categoriei existente „Persistență/Autorun").

**2. Valoarea tactică forenzică.** Categoria actuală („chei Autorun") acoperă poate 15% din suprafața reală de persistență. Lipsesc, în ordinea frecvenței observate în intruziuni: Scheduled Tasks (XML + `Tasks` cache în Registru), servicii (`SYSTEM\CurrentControlSet\Services` + `EID 7045`/`4697`), **WMI Event Consumers** (Stuxnet până la Turla/Mustang Panda/Cozy Bear — persistență fără fișier și cu logare minimă, ceea ce o face favorita actorilor avansați, [SANS, „Finding Evil WMI Event Consumers with Disk Forensics"](https://www.sans.org/blog/finding-evil-wmi-event-consumers-with-disk-forensics)), COM hijacking (`HKCU\Software\Classes\CLSID\...\InprocServer32`), IFEO/`Debugger`, `AppCertDLLs`, `AppInit_DLLs`, Winlogon `Shell`/`Userinit`/`Notify`, `LSA` packages (`Security Packages`, `Notification Packages`), Print Monitors/Processors, Netsh helper DLLs, Time Providers, BITS jobs (`qmgr.db`), `Startup` folders, RDP shadow/`Utilman`/sticky keys, drivere (`CodeIntegrity`).

**3. Structura tehnică / datele necesare.**
- **Scheduled Tasks**: `C:\Windows\System32\Tasks\**` (XML: `<Command>`, `<Arguments>`, `<Principal>`, `<Triggers>`, `<Author>`) corelat cu `SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\{Tasks,Tree}` (câmpuri binare `DynamicInfo` cu ultima rulare/ultima creare) — corelarea celor două expune **task‑uri șterse din XML dar rămase în TaskCache** și invers.
- **WMI**: `C:\Windows\System32\wbem\Repository\OBJECTS.DATA` (+ `INDEX.BTR`, `MAPPING*.MAP`), format binar comprimat slab documentat; strategie pragmatică validată de comunitate: căutare de pattern‑uri pentru `__EventFilter`, `CommandLineEventConsumer`, `ActiveScriptEventConsumer`, `__FilterToConsumerBinding`, extragerea `Query` WQL și a `CommandLineTemplate`/`ScriptText` cu recuperare de string‑uri parțiale — abordarea din `WMI_Forensics` / `PyWMIPersistenceFinder` ([davidpany/WMI_Forensics](https://github.com/davidpany/WMI_Forensics); [darkquasar/WMI_Persistence](https://github.com/darkquasar/WMI_Persistence)). Se corelează cu `Microsoft-Windows-WMI-Activity/Operational` EID 5859/5861.
- Model de scoring pentru fiecare ASEP: cale non‑standard, binar nesemnat/nesignat în allowlist, `LOLBAS` în linia de comandă, entropie mare, creat în fereastra incidentului (corelat `$J`), utilizator neprivilegiat care creează serviciu.

**4. Integrare.** *Core*: `PersistenceMechanism { Type, Location, Payload, CreatedUtc, Score, Reasons[] }`, `IPersistenceEnumerator`. *Infrastructure*: `TaskXmlParser`, `TaskCacheParser`, `WmiRepositoryScanner`, extinderea `RegistryHiveParser` existent cu un **catalog declarativ de ASEP‑uri în YAML** (ușor de actualizat offline). *UI*: view „Persistență" cu grupare pe tehnică ATT&CK (T1053, T1543, T1546.003, T1547.*) și acțiune „generează script de eliminare" reutilizând generatorul existent de PowerShell.

### P0‑10. Migrare la MITRE ATT&CK v18 (Detection Strategies & Analytics) + import STIX local al ATT&CK

**1. Nume:** `LogAnalyzer.Intel.AttackModel`.

**2. Valoarea tactică forenzică.** ATT&CK v18 a restructurat modelul de detecție în obiecte `Detection Strategy` (abordarea de nivel înalt) care conțin `Analytics` specifice per platformă, cu date/telemetrie necesare — o schimbare care transformă maparea din etichetare declarativă în **contract verificabil**: pentru fiecare tehnică, produsul poate declara *ce analitică* implementează și *cu ce sursă de date* ([MITRE ATT&CK, Detection Strategies](https://attack.mitre.org/detectionstrategies/); [MITRE, „ATT&CK v18: The Detection Overhaul You've Been Waiting For"](https://medium.com/mitre-attack/attack-v18-8f82d839ee9e); [Picus, sinteza v18](https://www.picussecurity.com/resource/blog/whats-new-in-mitre-attack-v18)). Exemplu concret relevant pentru brief: strategia de detecție pentru AS‑REP Roasting (T1558.004) este descrisă ca monitorizare `4768` cu Pre‑Auth Type 0, corelat cu activitate `4769` ulterioară și tipuri de criptare slabe ([MITRE, DET0113 — Detect AS‑REP Roasting](https://attack.mitre.org/detectionstrategies/DET0113/)).

**3. Structura tehnică / datele necesare.** Pachet ATT&CK Enterprise în STIX 2.1 (bundle JSON) livrat local, versionat; tabele `attack_technique`, `attack_detection_strategy`, `attack_analytic`, `attack_group`, `attack_software`, `attack_datacomponent`, `attack_relationship`; fiecare regulă Sigma/YARA locală obține câmpuri `attack.technique_ids[]`, `attack.detection_strategy_ids[]`, `required_data_components[]`. Verificare automată la ingestie: „regula X cere `Process: Process Creation`, dar cazul curent nu conține EID 4688/Sysmon 1 → regula este inaplicabilă, marcată gri în matrice".

**4. Integrare.** *Core*: `AttackKnowledgeBase` (read‑only, imutabil), `IRuleAttackMapper`. *Infrastructure*: `StixBundleImporter` (același cod care servește și exportul STIX existent, în sens invers). *UI*: matricea ATT&CK (P1‑1) consumă direct acest model, cu trei stări: acoperit & vizibil, acoperit dar fără telemetrie, neacoperit.

### P0‑11. Export CASE/UCO + manifest de caz semnat (defensibilitate juridică)

**1. Nume:** `LogAnalyzer.Export.CaseUco` + `CaseManifest`.

**2. Valoarea tactică forenzică.** Exporturile actuale (STIX 2.1, MISP) sunt formate de **threat intelligence**, nu de **probatoriu**: ele exprimă indicatori, nu proveniență. CASE (Cyber‑investigation Analysis Standard Expression), extensie a Unified Cyber Ontology, este standardul comunitar pentru exprimarea investigațiilor digitale, inclusiv **reprezentarea standardizată a lanțului de custodie** (cine a manipulat datele, când, unde) și a **lanțului de evidență** (ce procese și unelte au tratat datele), cu trasabilitate de la fiecare obiect observabil la sursa originară ([CASE Ontology — Introduction](https://www.caseontology.org/ontology/intro.html); [CASE Ontology Design and Specification](https://www.caseontology.org/resources/case_design_document.html); [casework/CASE pe GitHub](https://github.com/casework/CASE)). CASE este deja implementat în unelte open‑source precum Plaso și Volatility și este vehiculat între state europene prin efortul EVIDENCE2eCODEX — deci exportul CASE deschide interoperabilitatea cu ecosistemul judiciar și cu laboratoarele publice, ceea ce niciun concurent din segmentul mid‑market nu oferă.

**3. Structura tehnică / datele necesare.**
- Serializare JSON‑LD conform ontologiei UCO/CASE: `uco-observable:File` cu `FileFacet` (nume, dimensiune, timestamp‑uri) + `HashFacet` (SHA‑256 existent), `uco-observable:WindowsRegistryKey`, `uco-action:Action` pentru fiecare pas de procesare (ingestie, parsare, rulare regulă) cu `performer` (utilizator/licență HWID), `instrument` (LogAnalyzer + versiune + hash binar), `startTime`/`endTime`, `uco-core:Relationship` pentru derivare (`derivedFrom`).
- `CaseManifest.json` semnat: listă completă a fișierelor ingerate cu SHA‑256, versiunile tuturor parserelor (`ArtifactSchemaVersion`), versiunea Intel Pack, versiunea modelului de scoring, timpul de sistem vs timpul din artefacte (drift), semnătură cu cheia privată a instanței + opțional token RFC 3161 (§C‑07).
- Raport de reproductibilitate: comandă/parametri exacți pentru a reproduce fiecare concluzie — cerință ISO/IEC 27042 privind continuitatea, validitatea, reproductibilitatea și repetabilitatea ([ISO/IEC 27042:2015](https://www.iso.org/standard/44406.html)), coroborat cu ISO/IEC 27037 pentru identificare/colectare/achiziție/prezervare ([ISO/IEC 27037:2012](https://www.iso.org/standard/44381.html)).

**4. Integrare.** *Core*: `ICaseExporter` (implementări: PDF, HTML, STIX, MISP, **CASE**), `ProvenanceRecorder` — un serviciu transversal care înregistrează fiecare acțiune analitică (aspect/decorator peste handler‑ele existente). *Infrastructure*: `JsonLdWriter` (fără dependențe de rețea pentru rezolvarea contextelor — contextele UCO/CASE se împachetează local). *UI*: în ecranul de export, checkbox „CASE/UCO (probatoriu)" + previzualizare graf de proveniență.

### P0‑12. „Intel Pack" offline semnat + auto‑validare tip CFTT

**1. Nume:** `LogAnalyzer.Intel.PackManager` + `LogAnalyzer.Validation.TestHarness`.

**2. Valoarea tactică forenzică.** Un produs air‑gapped are o vulnerabilitate structurală: **cunoașterea îmbătrânește**. Fără un mecanism disciplinat de actualizare offline, regulile Sigma, catalogul LOLBAS, profilurile APT și ATT&CK rămân la versiunea de la livrare, iar rapoartele devin tăcut incorecte. În paralel, defensibilitatea cere **validare**: NIST CFTT publică specificații, aserțiuni de test și planuri de test pe categorii de unelte, inclusiv o specificație dedicată uneltelor de forensics pe Registrul Windows ([NIST, Computer Forensics Tool Testing Program](https://www.nist.gov/itl/csd/secure-systems-and-applications/computer-forensics-tool-testing-program-cftt); [NIST CFTT, MS Windows Registry Tools](https://www.nist.gov/itl/csd/secure-systems-and-applications/computer-forensics-tool-testing-program-cftt/cftt-8)).

**3. Structura tehnică / datele necesare.**
- **Intel Pack** = arhivă semnată (Ed25519/RSA, cheie publică încorporată în binar) conținând: reguli Sigma (cu `sigma_version`), reguli YARA, catalog LOLBAS derivat din proiectul public ([LOLBAS Project](https://lolbas-project.github.io/); [LOLBAS pe GitHub](https://github.com/LOLBAS-Project/LOLBAS/blob/master/README.md)), bundle ATT&CK STIX, catalog ASEP, mapări de câmpuri, liste de nume de pipe cunoscute, allowlist‑uri de hash‑uri de fișiere de sistem (model NSRL/RDS), tabele de conversie de fus orar, semnături de servicii/căi legitime pentru detecția de masquerading. Manifest cu versiune semantică + data „valabil la"; UI afișează avertisment de vechime („Intel Pack are 143 de zile — riscul de fals‑negativ este crescut").
- **Test Harness**: corpus de referință intern (imagini/hive‑uri/EVTX sintetice cu răspuns cunoscut) rulat la fiecare build; raport de conformitate per parser (aserțiuni tip CFTT: „nu modifică sursa", „raportează toate intrările", „raportează corect intrările corupte", „nu inventează date"). Rezultatul se atașează opțional în anexa raportului de caz — argument comercial puternic în licitații publice.

**4. Integrare.** *Core*: `IIntelPack`, `PackVersionPolicy`. *Infrastructure*: `SignedPackLoader` (verificare semnătură înainte de dezarhivare, protecție zip‑slip), `TestCorpusRunner` (xUnit). *UI*: ecran „Baza de Cunoștințe" cu versiuni, hash‑uri și buton de import de pe suport amovibil.

---

## 4. P1 — VALOARE RIDICATĂ

### P1‑1. Matrice ATT&CK interactivă cu două straturi: Vizibilitate × Detecție

**1. Nume:** `AttackMatrixHeatmapView`.

**2. Valoarea tactică forenzică.** Un heatmap clasic de „tehnici declanșate" spune ce s‑a văzut. Un heatmap **cu două straturi** spune și ce **nu putea fi văzut** — informația cea mai valoroasă pentru un raport Tier‑3 și pentru recomandările post‑incident (exact ce cere NIS2 în raportul final: măsuri de îmbunătățire). Modelul conceptual este DeTT&CT: scorare separată a calității surselor de date, a vizibilității și a acoperirii detecției, cu comparație pentru identificarea de lacune ([DeTT&CT Wiki](https://github.com/rabobank-cdc/DeTTECT/wiki); [SANS Detection Coverage Scorecard](https://www.sans.org/tools/detection-coverage-scorecard)).

**3. Structura tehnică / datele necesare.** Grid `ItemsControl` cu `UniformGrid` pe tactici (coloane) × tehnici (celule), `VirtualizingStackPanel` pentru sub‑tehnici; scor per celulă = f(nr. alerte, severitate, încredere) pentru stratul „detecție" și g(surse de date prezente în caz) pentru stratul „vizibilitate"; redare bi‑cromatică (opacitate = vizibilitate, culoare = detecție). Export/import **layer JSON compatibil ATT&CK Navigator v4.5** (`versions`, `domain`, `techniques[].score`, `gradient`, `metadata`) pentru a permite analistului să deschidă rezultatul într‑un Navigator local ([specificația formatului de layer, v4.5](https://github.com/mitre-attack/attack-navigator/blob/master/layers/spec/v4.5/layerformat.md); [ATT&CK Navigator](https://mitre-attack.github.io/attack-navigator/)). Click pe celulă → filtrare Cronologie + listă de evenimente contribuitoare.

**4. Integrare.** *Core*: `CoverageCalculator` (pur, testabil). *UI*: `AttackMatrixViewModel` (CommunityToolkit.Mvvm, `ObservableCollection` + `[RelayCommand]`), stil glassmorphic existent; fără dependențe noi.

### P1‑2. Graf de mișcare laterală (Lateral Movement Graph)

**1. Nume:** `LateralMovementGraphView` + `LogAnalyzer.Analytics.GraphEngine`.

**2. Valoarea tactică forenzică.** Transformă mii de evenimente de autentificare în răspunsul la trei întrebări operaționale: care a fost **pacientul zero**, care sunt **conturile compromise**, unde se află **nodurile de pivot** care trebuie izolate primele. Este vizualizarea cu cel mai mare impact în briefingul către management.

**3. Structura tehnică / datele necesare.**
- Muchii construite din: `4624` (LogonType 3 = network, 10 = RemoteInteractive, 9 = NewCredentials → adesea `runas /netonly`, semnătură de pass‑the‑hash/`Overpass‑the‑hash`), `4648` (logon cu credențiale explicite — semnal puternic de mișcare laterală), `4672` (privilegii speciale), `4776`/`4769` (NTLM/Kerberos pe DC), `5140`/`5145` (acces la partajare, `IPC$`/`ADMIN$` = PsExec/WMI), `7045` (instalare serviciu — PsExec, Cobalt Strike `jump psexec`), `1149`+`21`/`25` (RDP), Sysmon `3` (conexiuni) și `18` (conectare la named pipe).
- Model: multigraf orientat `Host → Host`, adnotat cu `(cont, tip logon, timestamp, mecanism)`; noduri „cont" separate pentru graf bipartit cont↔stație.
- Analitici pe graf, toate offline: componente conexe, **betweenness centrality** (identifică jump host‑ul), grad de intrare/ieșire anormal, drumuri minime de la primul nod compromis, detecție de „shortest path to DA" dacă există date de grup, plus scor de raritate per muchie (muchia „stație de lucru → stație de lucru" pe port 445 este intrinsec suspectă).
- Randare: `Microsoft.Msagl` (Microsoft Automatic Graph Layout) are componentă WPF și motor de layout stratificat/MDS, licență permisivă și zero dependențe de rețea ([microsoft/automatic-graph-layout](https://github.com/microsoft/automatic-graph-layout)). Alternativ, layout propriu force‑directed (Fruchterman‑Reingold) pe `Canvas` cu `DrawingVisual` pentru performanță.

**4. Integrare.** *Core*: `LateralMovementGraphBuilder`, `IGraphMetric`. *Infrastructure*: interogări SQL pe `norm_events` + cache de graf serializat în SQLite. *UI*: view nou cu timeline‑scrubber (animarea propagării în timp — foarte eficient în prezentări) și `Click‑to‑Pivot` existent.

### P1‑3. Comparator diferențial de timeline (Diff între stații / între momente)

**1. Nume:** `TimelineDiffView`.

**2. Valoarea tactică forenzică.** Cea mai eficientă tehnică de triaj în incidente cu multe stații: compară o stație suspectă cu un **gold image** sau cu o stație curată din același grup. Ce apare doar pe stația suspectă este, statistic, atacul. Aceeași mecanică servește „diff‑ul temporal": înainte vs după fereastra de compromitere.

**3. Structura tehnică / datele necesare.** Normalizare de entități pentru comparabilitate (path canonicalization: `%SystemRoot%`, SID→nume, GUID de volum), apoi trei seturi calculate în SQL (`EXCEPT`/`INTERSECT` pe chei de artefact): `doar_A`, `doar_B`, `comun_cu_diferențe_de_timp`. Metrici afișate: număr de ASEP‑uri suplimentare, servicii suplimentare, task‑uri suplimentare, binare cu SHA‑256 necunoscut, chei de Registru divergente. Vizual: două axe temporale sincronizate + panou de „delta" cu evidențiere. Formatele de interoperabilitate recomandate pentru import/export de timeline: `bodyfile` (mactime) și CSV compatibil `l2tcsv` (psort), pentru schimb cu ecosistemul Plaso/Timesketch ([Hackers Manifest, Timeline Analysis cu log2timeline/psort](https://hackersmanifest.com/dfir/09-timeline-analysis/)).

**4. Integrare.** *Core*: `TimelineDiffEngine`, `IEntityCanonicalizer`. *Infrastructure*: suport multi‑caz în SQLite (schema per caz, `ATTACH DATABASE` pentru diff cross‑caz — soluție elegantă și rapidă în SQLite). *UI*: view „Comparator" cu selectoare de caz/stație.

### P1‑4. Motor de detecție LOLBAS/LOLDrivers cu analiză de linie de comandă

**1. Nume:** `LogAnalyzer.Detection.Lolbas`.

**2. Valoarea tactică forenzică.** Actorii moderni nu aduc binare; folosesc ce găsesc. Catalogul LOLBAS (binare, scripturi și biblioteci Windows semnate, cu funcțiile abuzabile și tehnicile ATT&CK asociate) este referința publică ([LOLBAS Project](https://lolbas-project.github.io/)). Valoarea reală nu este lista de nume — este **analiza argumentelor**: `certutil -urlcache -f`, `msiexec /i http`, `regsvr32 /i:script scrobj.dll`, `mshta vbscript:`, `rundll32` fără argumente (semnătură de injecție Cobalt Strike), `bitsadmin /transfer`, `forfiles /c`, `wmic process call create`, `pcalua -a`, `conhost --headless`, `msbuild` cu `.csproj` din `%TEMP%`.

**3. Structura tehnică / datele necesare.** Import YAML LOLBAS din Intel Pack → tabelă `lolbas_binary(name, functions[], attack_techniques[], command_patterns[])`; motor de potrivire pe `norm_events.command_line` cu (a) regex compilate (`RegexOptions.Compiled | NonBacktracking` în .NET pentru protecție anti‑ReDoS), (b) tokenizare de linie de comandă conștientă de ghilimele și de trucurile de evaziune (`c^e^r^t^util`, `"cert"util`, variabile de mediu parțiale `%comspec:~0,1%`) — **normalizare de shell obfuscation obligatorie înainte de potrivire**, altfel detectorul este trivial de ocolit; (c) verificarea căii (`certutil.exe` din `C:\Users\...` = binar copiat, indicator distinct); (d) corelare cu părinte anormal. Extensie: **LOLDrivers** pentru BYOVD, corelat cu `CodeIntegrity/Operational` și `InventoryDriverBinary` din Amcache.

**4. Integrare.** *Core*: `CommandLineNormalizer`, `ILolbasMatcher`. *Infrastructure*: `LolbasCatalogRepository`. *UI*: filtru rapid în vizualizarea EVTX („doar LOLBAS") + coloană „funcție abuzată".

### P1‑5. Pachet de detecții Active Directory: Kerberoasting, AS‑REP Roasting, Golden/Silver Ticket, DCSync

**1. Nume:** `LogAnalyzer.Detection.ActiveDirectory`.

**2. Valoarea tactică forenzică.** Escaladarea la nivel de domeniu este momentul în care un incident devine o criză. Aceste detecții necesită corelare de agregare (deci depind de P0‑6) și cunoașterea exactă a semanticii câmpurilor.

**3. Structura tehnică / datele necesare.**
- **Kerberoasting (T1558.003)**: `EID 4769` cu `TicketEncryptionType = 0x17` (RC4‑HMAC), `ServiceName` ≠ `krbtgt`/cont mașină (`$`), `TicketOptions` tipic `0x40810000`, `Status = 0x0`; semnalul real este **volumul**: N servicii distincte solicitate de același `IpAddress`/cont în fereastră scurtă. Referința metodologică clasică rămâne analiza lui Sean Metcalf privind filtrarea 4769 pe criptare RC4 ([ADSecurity, „Detecting Kerberoasting Activity Part 2"](https://adsecurity.org/?p=3513); [Fox‑IT/NCC, ghid de combatere a Kerberoasting](https://www.fox-it.com/nl-en/defending-your-directory-an-expert-guide-to-combating-kerberoasting-in-active-directory/)). Detaliu de arhitectură cu impact direct: documentația Microsoft pentru 4769 arată că, după update‑ul cumulativ de securitate din 14 ianuarie 2025, Windows Server 2016+ emite o **versiune 2** a evenimentului, cu câmpuri noi extrem de utile — `RequestTicketHash`, `ResponseTicketHash`, `AccountSupportedEncryptionTypes`, `AccountAvailableKeys`, `ServiceSupportedEncryptionTypes`, `ClientAdvertizedEncryptionTypes` ([Microsoft Learn, 4769](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/auditing/event-4769)). **Consecință pentru produs:** parserul trebuie să trateze explicit `Version` 0/1/2 al evenimentului, iar regulile trebuie să exploateze noile câmpuri (ex.: cont care *suportă* AES dar solicită RC4 = semnal mult mai puternic decât RC4 simplu, cu fals‑pozitive mult reduse).
- **AS‑REP Roasting (T1558.004)**: `EID 4768` cu `PreAuthType = 0` și tipuri de criptare slabe (`0x17`, `0x18`, `0x1`) ([MITRE, DET0113](https://attack.mitre.org/detectionstrategies/DET0113/); [Hack The Box, AS‑REP roasting detection](https://www.hackthebox.com/blog/as-rep-roasting-detection)).
- **Golden/Silver Ticket**: `4769`/`4624` fără `4768` corespondent (TGT fabricat), `TargetDomainName` anormal, timp de viață aberant, `AccountName` inexistent în domeniu.
- **DCSync**: `4662` cu `Properties` conținând GUID‑urile `DS-Replication-Get-Changes` (`1131f6aa-...`) / `...-All` (`1131f6ad-...`) de la un principal care nu este DC.
- **Password spraying**: `4625`/`4771` cu `value_count(DISTINCT TargetUserName) ≥ N` per IP surs.

**4. Integrare.** *Core*: reguli livrate ca **Sigma Correlations native**, nu ca cod C# — astfel devin auditabile și editabile de analist în Workbench‑ul existent. *UI*: categorie nouă în „Alerte de Securitate" („Active Directory / Credential Access") + explicație didactică în română pentru fiecare tip de atac (valorifică baza de cunoștințe existentă).

### P1‑6. Detecție de anomalii de relație părinte‑copil și de masquerading (model probabilistic)

**1. Nume:** `LogAnalyzer.Analytics.ProcessLineage`.

**2. Valoarea tactică forenzică.** `svchost.exe` cu părinte diferit de `services.exe` este un indicator canonic de injecție sau de binar mascat ([Splunk Research, „Windows Svchost.exe Parent Process Anomaly"](https://research.splunk.com/endpoint/1d38e5e9-2ff8-4c47-872c-bf1657cefab5/); [SigmaHQ, „Uncommon Svchost Parent Process"](https://detection.fyi/sigmahq/sigma/windows/process_creation/proc_creation_win_svchost_uncommon_parent_process/)). Red Canary formulează regula de aur: toate serviciile Windows apar ca procese‑copil ale `services.exe`, cu excepția driverelor de kernel ([Red Canary, Service Execution](https://redcanary.com/threat-detection-report/techniques/service-execution/)). Reciproc, procesele „childless" (`svchost.exe`, `lsass.exe`, `dllhost.exe` care nu ar trebui să genereze copii) semnalează injecție ([Elastic, „Unusual Service Host Child Process — Childless Service"](https://www.elastic.co/docs/reference/security/prebuilt-rules/rules/windows/privilege_escalation_unusual_svchost_childproc_childless)).

**3. Structura tehnică / datele necesare.**
- Tabel de așteptări livrat în Intel Pack: `expected_lineage(child_image, allowed_parents[], is_childless, expected_paths[], expected_user_context, expected_session)` pentru ~120 de procese de sistem.
- Trei familii de detectoare: (a) **listă de așteptări** (deterministă, explicabilă); (b) **raritate statistică** — probabilitate empirică \(P(\text{parent}\mid\text{child})\) calculată pe corpusul ingerat, alertă când \(P < \varepsilon\) și suportul este suficient; (c) **masquerading** — distanță Levenshtein/omoglife față de nume de sistem (`svch0st.exe`, `scvhost.exe`, `lsass .exe` cu spațiu), cale non‑standard pentru nume de sistem (`C:\Users\Public\svchost.exe`), lipsa semnăturii, dezacord între `OriginalFileName` din resursele PE și numele de pe disc (semnalul cel mai fiabil — necesită parser PE minimal pentru `VS_VERSIONINFO`).
- Recomandare metodologică: rulați (a) pentru alertare și (b) pentru hunting; nu amestecați scorurile, pentru că fiabilitatea lor probatorie diferă.

**4. Integrare.** *Core*: `LineageAnomalyDetector`, `PeMetadataReader`. *Infrastructure*: SQL analitic + `expected_lineage` din Intel Pack. *UI*: evidențiere în Process Tree existent (contur roșu + tooltip cu motivul), fără view nou.

### P1‑7. Detecție de Named Pipes și de canale C2 pe host

**1. Nume:** `LogAnalyzer.Detection.NamedPipes`.

**2. Valoarea tactică forenzică.** Beacon‑ul SMB al Cobalt Strike și modulele de impersonare comunică prin named pipes; numele implicite sau publicate ale Artifact Kit / profilurilor Malleable C2 sunt distinctive și greu de obfuscat complet. Sursa este Sysmon EID **17** (creare pipe) și **18** (conectare pipe) ([Splunk Research, „Cobalt Strike Named Pipes"](https://research.splunk.com/endpoint/5876d429-0240-4709-8b93-ea8330b411b5/); [Splunk, „Detecting & Hunting Named Pipes"](https://www.splunk.com/en_us/blog/security/named-pipe-threats.html)). Constrângerea reală nu este detecția, ci **acoperirea de logare**: fără Sysmon cu 17/18 activate, semnalul nu există — motiv suplimentar pentru stratul de vizibilitate din P1‑1.

**3. Structura tehnică / datele necesare.** Listă de pattern‑uri în Intel Pack (`\msagent_*`, `\postex_*`, `\status_*`, `\MSSE-*`, `\ntsvcs` cu context anormal, pipe‑uri cu nume aleator de lungime fixă) + analiză de **structură a numelui**: entropie, raport cifre/litere, potrivire cu format `[a-f0-9]{8,}`. Corelare cu Sysmon 8 (CreateRemoteThread), 10 (ProcessAccess pe `lsass.exe` cu mască `0x1010`/`0x1410`) și 25 (Process Tampering) pentru a construi o secvență de încredere ridicată.

**4. Integrare.** *Core*: reguli Sigma + o corelație temporal_ordered (17 → 18 → 10). *UI*: categorie „C2 & Injecție" în Alerte.

### P1‑8. Transpiler Sigma → SPL / KQL (Sentinel) / ES|QL / Elastic KQL, offline

**1. Nume:** `LogAnalyzer.Export.SigmaTranspiler`.

**2. Valoarea tactică forenzică.** Închide bucla operațională: analistul DFIR descoperă un TTP pe stația compromisă și livrează SOC‑ului o interogare gata de rulat în SIEM‑ul organizației pentru **hunting la scară** (câte alte stații sunt afectate?). Este funcția care transformă LogAnalyzer din unealtă de laborator în unealtă de răspuns la incident. Ecosistemul de referință este pySigma cu backend‑uri separate pe proiecte dedicate (Splunk, Elasticsearch, Microsoft 365/Sentinel etc.) și pipeline‑uri de transformare a taxonomiei ([SigmaHQ, Backends](https://sigmahq.io/docs/digging-deeper/backends); [pySigma‑backend‑splunk](https://github.com/SigmaHQ/pySigma-backend-splunk); [pySigma pe PyPI](https://pypi.org/project/pySigma/0.10.10/)).

**3. Structura tehnică / datele necesare.**
- **Decizie de arhitectură recomandată: reimplementare managed în C#, nu împachetare Python.** Motiv: un produs air‑gapped cu licențiere HWID nu ar trebui să livreze un runtime Python (suprafață de atac, dimensiune, semnare, dependențe pip imposibil de actualizat offline). Se implementează un AST intermediar (`SigmaCondition` → `IR` → `IQueryEmitter`) cu emițători pentru: SPL (`index=* EventCode=4688 New_Process_Name="*\\certutil.exe"`), Sentinel KQL (`SecurityEvent | where EventID == 4688 and NewProcessName endswith @"\certutil.exe"`), Elastic KQL/ES|QL, plus SQL (deja necesar intern pentru execuția locală — sinergie directă cu P0‑6).
- Pipeline de mapare a câmpurilor per platformă (același model conceptual ca `sigma.pipelines`), livrat în YAML în Intel Pack, cu suport pentru `field_name_mapping`, `value_transformation`, `logsource_conditions`.
- Suport obligatoriu pentru modificatorii Sigma: `contains`, `startswith`, `endswith`, `re`, `base64offset|contains`, `windash`, `all`, `cidr`, `expand` — omiterea `base64offset` și `windash` este cauza clasică a regulilor „traduse dar nefuncționale".

**4. Integrare.** *Core*: `SigmaIr`, `IQueryEmitter`. *UI*: în Rule Workbench, dropdown „Exportă ca: SPL / KQL Sentinel / KQL Elastic / ES|QL / SQL" cu copiere în clipboard — feature cu cost mic și impact demonstrativ mare în demo‑uri comerciale.

### P1‑9. Pachet „Conformitate NIS2 / DNSC" — generator de notificări în 3 faze

**1. Nume:** `LogAnalyzer.Compliance.Nis2`.

**2. Valoarea tactică forenzică.** Pentru un client român, valoarea nu este doar tehnică, ci **de reducere a riscului de sancțiune**. NIS2 art. 23 impune o cascadă strictă: avertizare timpurie „fără întârziere nejustificată și în orice caz în 24 de ore" de la momentul cunoașterii incidentului semnificativ (cu indicarea dacă este suspectat de a fi cauzat de acte ilicite/malițioase sau de a avea impact transfrontalier), notificare de incident **în 72 de ore** (cu evaluarea inițială a severității și impactului și, unde sunt disponibili, **indicatorii de compromitere**), raport intermediar la cererea CSIRT și raport final la **o lună** ([NIS 2 Directive, Articolul 23](https://www.nis-2-directive.com/NIS_2_Directive_Article_23.html)). În România, transpunerea s‑a realizat prin ordonanță de urgență, iar autoritatea competentă/CSIRT național este DNSC ([DNSC, proiectul de OUG privind transpunerea Directivei NIS 2](https://dnsc.ro/vezi/document/oug-privind-transpunerea-directivei-nis-2); [DNSC, comunicat privind finalizarea cadrului legislativ prin transpunerea NIS2](https://dnsc.ro/vezi/document/comunicat-de-presa-completarea-cu-succes-a-cadrului-legislativ-national-pentru-securitatea-cibernetica-prin-transpunerea-directivei-nis2)).

**3. Structura tehnică / datele necesare.**
- Trei șabloane de document (QuestPDF, în română), populate automat din caz: **T+24h** (fapt, suspiciune de act malițios da/nu, potențial impact transfrontalier, sisteme afectate), **T+72h** (severitate, impact, IoC extrase automat: hash‑uri SHA‑256 din ingestie, IP‑uri, domenii, nume de fișiere, chei de Registru, reguli declanșate), **T+1 lună** (cauză rădăcină, cronologie completă, măsuri de remediere aplicate, măsuri de îmbunătățire).
- **Ceas de conformitate** în UI: câmp „moment al cunoașterii" setat de analist la deschiderea cazului → countdown vizibil pentru 24h/72h/1 lună, cu stări color. Simplu de implementat, disproporționat de util operațional.
- Verificare de completitudine: raportul nu se poate genera dacă lipsesc câmpuri obligatorii (entitate, sector, servicii afectate, nr. utilizatori afectați, estimare pierdere financiară).
- Aliniere GDPR: dacă datele personale sunt implicate, evidențierea termenului de 72 de ore către autoritatea de protecție a datelor, ca flux paralel, nu identic.

**4. Integrare.** *Core*: `IncidentCaseMetadata`, `IocExtractor`, `ComplianceClock`. *Infrastructure*: `QuestPdfNis2Renderer`. *UI*: tab „Conformitate" în ecranul de caz.

### P1‑10. Playbook‑uri de răspuns aliniate CISA / ANSSI / NIST

**1. Nume:** extinderea modulului existent de „playbook‑uri remediere" cu un **catalog de proces**, nu doar de comenzi.

**2. Valoarea tactică forenzică.** Scripturile de izolare existente (`Isolate-Host.ps1`, `Kill-ProcessTree.ps1`) sunt acțiuni tactice. Ce lipsește este **procesul**: ordinea corectă (colectare înainte de izolare, altfel se pierd artefacte volatile), criteriile de declarare a incidentului, punctele de decizie. Referințele canonice: playbook‑urile federale CISA de răspuns la incidente și vulnerabilități, care definesc fazele Preparation → Detection & Analysis → Containment → Eradication & Recovery → Post‑Incident ([CISA, Federal Government Cybersecurity Incident and Vulnerability Response Playbooks](https://www.cisa.gov/resources-tools/resources/federal-government-cybersecurity-incident-and-vulnerability-response-playbooks); [PDF‑ul playbook‑urilor](https://www.cisa.gov/sites/default/files/2024-08/Federal_Government_Cybersecurity_Incident_and_Vulnerability_Response_Playbooks_508C.pdf)); NIST SP 800‑61 pentru managementul incidentelor ([NIST SP 800‑61r2](https://nvlpubs.nist.gov/nistpubs/specialpublications/nist.sp.800-61r2.pdf)) și NIST SP 800‑86 pentru integrarea tehnicilor forenzice în răspuns ([NIST SP 800‑86](https://csrc.nist.gov/pubs/sp/800/86/final)); pentru piața francofonă/europeană, fișele reflex CERT‑FR ale ANSSI privind qualificarea unui semnalement de compromitere și **endiguement** (limitarea propagării) ([CERT‑FR/ANSSI, fiche réflexe qualification](https://cert.ssi.gouv.fr/uploads/CERTFR-2024-RFX-005-2.pdf); [CERT‑FR/ANSSI, endiguement](https://cert.ssi.gouv.fr/uploads/CERTFR-2024-RFX-006-1.pdf)) și doctrina ANSSI de pilotare a remedierii ([ANSSI, „Piloter la remédiation d'un incident cyber"](https://cyber.sites.beta.gouv.fr/securisation/gestion-de-crise/piloter-la-remediation-dun-incident-cyber/)).

**3. Structura tehnică / datele necesare.** Playbook‑uri ca fișiere YAML în Intel Pack: `phase`, `step`, `precondition`, `action`, `evidence_to_preserve`, `decision_point`, `owner_role`, `sla`, `references[]`; motor de stare per caz (checklist cu marcaje temporale = jurnal de acțiuni, care alimentează automat raportul final NIS2 și `uco-action:Action` din exportul CASE). Declanșare condiționată de tipul de alertă (ransomware → playbook cu pas obligatoriu „nu reporniți stația, colectați memoria și `$MFT` întâi").

**4. Integrare.** *Core*: `PlaybookEngine`, `PlaybookState`. *UI*: panou lateral „Următorii pași recomandați" cu checkbox‑uri și jurnal automat.

### P1‑11. Performanță & scalabilitate: pipeline de ingestie, indexare, virtualizare UI

**1. Nume:** `LogAnalyzer.Infrastructure.Ingest` (refactorizare transversală).

**2. Valoarea tactică forenzică.** Un caz real de ransomware pe 40 de stații generează 100–400 de milioane de evenimente. Paginarea la 100 de evenimente/pagină este un simptom: interogările nu sunt proiectate pentru volum. Dacă analistul așteaptă 40 de secunde per filtrare, va folosi altă unealtă — indiferent cât de bune sunt detecțiile.

**3. Structura tehnică / datele necesare.**
- Ingestie: `System.Threading.Channels` (bounded) + `Parallel.ForEachAsync`, un writer unic pe conexiune SQLite (evită contenția WAL), tranzacții de 25–50k rânduri, `PRAGMA cache_size`, `mmap_size`, `temp_store=MEMORY`, indexuri create **după** bulk‑load.
- Interogare: indexuri acoperitoare pe `(host, ts_utc)`, `(event_id, ts_utc)`, `(image, ts_utc)`; FTS5 cu `content=''` (external content) pentru a nu dubla stocarea și tokenizer `trigram` pentru substring ([SQLite FTS5](https://www.sqlite.org/fts5.html)); paginare **keyset** (`WHERE (ts_utc, id) > (?, ?)`) în loc de `OFFSET` (care degradează liniar).
- UI: `VirtualizingStackPanel.IsVirtualizing=True` + `VirtualizationMode=Recycling`, `ScrollUnit=Item`, coloane cu lățime fixă (evitarea `Auto` care forțează măsurarea tuturor rândurilor), `Freeze()` pe toate resursele `Brush`/`Geometry`, `DataGrid` cu `EnableRowVirtualization`. Pentru grafice cu volume mari, `ScottPlot` afișează interactiv milioane de puncte prin `SignalPlot` ([ScottPlot, Signal Plot Performance](https://scottplot.net/cookbook/5/ScottPlotQuickstart/SignalPerformance/)) — de preferat față de biblioteci cu randare per‑element (probleme de performanță raportate în WPF pentru LiveCharts2, [issue #1237](https://github.com/beto-rodriguez/LiveCharts2/issues/1237)).
- Parsare: `MemoryMappedFile` + `Span<byte>`/`ReadOnlySequence`, `ArrayPool<byte>`, structuri `readonly record struct` pentru înregistrări fixe, `System.IO.Hashing`/`SHA256.HashData` (hardware‑accelerat) pentru chain of custody, `SearchValues<byte>` (.NET 8+) pentru scanări de semnături.

**4. Integrare.** *Infrastructure* preponderent; *Core* rămâne neschimbat (dovada că decuplarea Clean Architecture funcționează); *UI*: înlocuirea paginării cu scroll virtual infinit + „jump to timestamp".
---

## 5. P2 — RAFINAMENT / NICE‑TO‑HAVE

### P2‑1. Artefacte Windows 11 moderne: Recall, ActivitiesCache, EventTranscript

**1. Nume:** `LogAnalyzer.Forensics.ModernWindows`.

**2. Valoarea tactică forenzică.** Odată cu sfârșitul suportului Windows 10 (14 octombrie 2025), ponderea Windows 11 în incidente crește rapid, iar Windows 11 24H2 aduce artefacte noi de mare valoare ([Securelist/Kaspersky, „What makes Windows 11 interesting from a digital forensics perspective"](https://securelist.com/forensic-artifacts-in-windows-11/117680/)). **Recall** este cazul extrem: capturi de ecran periodice cu OCR, stocate local — practic un „video al sesiunii utilizatorului". Imaginile JPEG brute se află în `%AppData%\Local\CoreAIPlatform.00\UKP\{GUID}\ImageStore\`, cu metadate în tag‑ul Exif `MakerNote (0x927c)` (inclusiv limitele ferestrei din prim‑plan), iar baza de date de indexare este SQLite ([BDO, „Microsoft Recall in Digital Forensics"](https://www.bdosecurity.de/en-gb/insights/security-column/microsoft-recall-in-digital-forensics); [CyberCX, „Forensic Applications of Microsoft Recall"](https://cybercx.com.au/blog/forensic-applications-of-microsoft-recall/)). Valoarea pentru investigator este descrisă de CyberCX ca „aproape nelimitată": atâta timp cât actorul folosește GUI, se pot vedea comenzile rulate, fișierele deschise și chiar conținut ulterior șters. Mențiune de prudență: Recall este dezactivat implicit în build‑urile corporative, deci prezența sa **activată** este ea însăși un indicator (posibil de manipulare de către atacator pentru colectare de credențiale).
`ActivitiesCache.db` (`%LocalAppData%\ConnectedDevicesPlatform\{profil}\ActivitiesCache.db`) oferă activitate per aplicație și per document, cu ferestre de start/sfârșit ([ActivitiesCacheParser](https://github.com/bolisettynihith/ActivitiesCacheParser)). `EventTranscript.db` este stocarea locală a subsistemului de telemetrie/diagnostic Windows și conține un volum surprinzător de artefacte utile, documentat în serie de Kroll ([Kroll, „Forensically Unpacking EventTranscript.db"](https://www.kroll.com/en/publications/cyber/forensically-unpacking-eventtranscript); [Kroll, „Diving Deeper into EventTranscript"](https://www.kroll.com/en/insights/publications/cyber/forensically-unpacking-eventtranscript/diving-deeper-into-eventtranscript)).

**3. Structura tehnică / datele necesare.** Cititor SQLite read‑only + parser Exif minimal (nu adăugați o bibliotecă grea de imagini; se citește doar structura TIFF/IFD pentru `MakerNote`); previzualizare de imagini în UI cu **avertisment de confidențialitate** și control de acces (Recall conține date personale masive — implicații GDPR: minimizare, temeiul juridic al prelucrării, jurnalizarea accesului analistului). `EventTranscript.db`: tabele `events_persisted` cu `payload` JSON — necesită extractoare per `full_event_name` (ex. `Win32kTraceLogging.AppInteractivitySummary` pentru execuție, `Microsoft.Windows.Inventory.Core.*` pentru inventar).

**4. Integrare.** *Core*: reutilizarea `UserActivityEvent`; *Infrastructure*: parsere noi + politică de redactare; *UI*: galerie „Recall" cu timeline și OCR căutabil prin FTS5 (sinergie directă cu indexul existent).

### P2‑2. Suport Volume Shadow Copies și analiză multi‑punct‑în‑timp

**1. Nume:** `LogAnalyzer.Forensics.Vss`.

**2. Valoarea tactică forenzică.** VSS oferă „mașina timpului": hive‑uri de Registru, `$MFT` și jurnale din urmă cu zile/săptămâni — adesea **înainte** ca atacatorul să șteargă urmele. Un Shimcache din shadow copy poate conține intrări suprascrise în cel curent. Analiza offline se face cu biblioteci precum `libvshadow`, care acceptă imagini RAW ([Computer Forensics GitBook, Volume Shadow Copy](https://wongkenny240.gitbook.io/computerforensics/incident-response-artifacts/volume-shadow-copy)).

**3. Structura tehnică / datele necesare.** Parser al store‑ului VSS (catalog, blocuri de 16 KB, listă de descriptori de blocuri, suprapunere copy‑on‑write peste volumul de bază); expunere ca „volume virtuale" în UI; **diferență automată între snapshot‑uri** (reutilizează P1‑3): „ce ASEP‑uri au apărut între snapshot‑ul din 3 aug. și cel din 11 aug.". Alternativă cu efort mic: acceptarea directoarelor deja extrase din VSS de KAPE (`--vss`).

**4. Integrare.** *Core*: `EvidenceSource { Live, Image, ShadowCopy(index, createdUtc) }` — atribut care trebuie propagat pe **fiecare** artefact (altfel timeline‑ul amestecă stări din momente diferite, eroare gravă). *UI*: selector de snapshot în bara de caz.

### P2‑3. Corelare pe hash și pivotare între cazuri (cross‑case intelligence, offline)

**1. Nume:** `LogAnalyzer.Intel.LocalCorpus`.

**2. Valoarea tactică forenzică.** Într‑o organizație care investighează 30 de cazuri pe an, cea mai bună bază de intelligence este propriul istoric. SHA‑1 din Amcache și SHA‑256 din ingestie permit întrebarea „acest binar a mai apărut la noi?" — pivotare pe care Securelist o recomandă explicit pentru vânătoarea la nivel de rețea a hash‑urilor din AmCache ([Securelist, AmCache](https://securelist.com/amcache-forensic-artifact/117622/)).

**3. Structura tehnică / datele necesare.** Bază SQLite separată, criptată, cu `observables(hash, type, first_seen_case, last_seen_case, count, verdict)`; allowlist de hash‑uri de fișiere de sistem (model NSRL/RDS, livrat în Intel Pack) pentru a elimina zgomotul; import/export de „pachete de caz" sanitizate pentru partajare între echipe fără date personale.

**4. Integrare.** *Core*: `IObservableRepository`; *UI*: badge „văzut în 3 cazuri anterioare" — semnal puternic de campanie repetată.

### P2‑4. Detecție de exfiltrare pe canale non‑evidente

**1. Nume:** `LogAnalyzer.Detection.Exfiltration`.
**2. Valoare.** Corelează SRUM (volum per proces) + `$J` (creare de arhive) + LNK/Shellbags (navigare pe partajări) + `WebCacheV01.dat`/`History` (upload‑uri) + jurnale BITS (`BITS-Client/Operational`, `qmgr.db`) + OneDrive ODL. Log‑urile de sincronizare OneDrive sunt un artefact bogat, cu parsare dificilă din cauza obfuscării/criptării introduse de Microsoft în 2022 — a se trata ca „best effort", cu marcaj explicit de incertitudine ([Yogesh Khatri, „Reading OneDrive Logs Part 2"](http://www.swiftforensics.com/2022/11/reading-onedrive-logs-part-2.html)).
**3. Structură.** Regulă compusă: `arhivă creată în %TEMP%/Public` + `dimensiune > X` + `proces cu trafic ieșit > Y în SRUM` + `în 30 min` → alertă „Staging & exfiltrare probabilă", cu estimare de volum pentru evaluarea de impact NIS2/GDPR.
**4. Integrare.** *Core*: corelație cross‑artefact (extensia proprie din P0‑6); *UI*: card dedicat pe Dashboard, cu estimarea de volum în MB/GB.

### P2‑5. Migrare la YARA‑X și scanare de conținut extins

**1. Nume:** înlocuirea motorului YARA cu `YARA‑X`.
**2. Valoare.** YARA‑X este rescrierea în Rust a YARA, memory‑safe, mai rapidă și cu compatibilitate de reguli de ~99% ([VirusTotal/yara-x](https://github.com/VirusTotal/yara-x); [VirusTotal, „YARA‑X 1.0.0: The Stable Release and Its Advantages"](https://blog.virustotal.com/2025/06/yara-x-100-stable-release-and-its.html)). Pentru un produs care rulează reguli terțe pe probe ostile, siguranța memoriei nu este un detaliu, ci reducere de suprafață de atac (o regulă sau un fișier malformat nu mai poate provoca corupție de memorie în procesul analizei). Există deja wrapper‑e .NET pentru yara‑x ([raport public de release al unui wrapper C# pentru yara‑x](https://t-defence.it/wp-content/uploads/2026/02/Report_Open_source_release_yara-x_dotnet.pdf)).
**3. Structură.** Abstracție `IYaraEngine` cu două implementări (clasic + X) și comutator de compatibilitate; extinderea țintelor de scanare de la fișiere la: valori de Registru (payload‑uri Base64 în `Run`), `command_line` din `norm_events`, blocuri 4104, stream‑uri extrase din OBJECTS.DATA, spațiu nealocat din SQLite‑urile de browser.
**4. Integrare.** *Infrastructure*: încapsulare P/Invoke izolată; *Core*: neschimbat; *UI*: Workbench cu indicator de motor activ.

### P2‑6. Modul „Attack Flow" — narativă de atac ca obiect standardizat

**1. Nume:** `LogAnalyzer.Export.AttackFlow`.
**2. Valoare.** Modulul existent „Narativă de Atac & Kill Chain Lineage" este exact cazul de utilizare al proiectului MITRE Attack Flow, care reprezintă acțiuni, condiții și relații ca flux conectat, cu tooling de builder, layout automat, marcaje TLP și suport pentru cadre suplimentare (ATLAS, D3FEND) ([CTID, Attack Flow](https://ctid.mitre.org/projects/attack-flow/); [Attack Flow — exemple de flow‑uri](https://center-for-threat-informed-defense.github.io/attack-flow/example_flows/)). Exportul narativei ca Attack Flow (extensie STIX 2.1) transformă un artefact intern într‑un obiect partajabil cu CSIRT‑uri și ISAC‑uri — și reutilizează exportul STIX existent ([STIX 2.1, OASIS](https://docs.oasis-open.org/cti/stix/v2.1/stix-v2.1.html)).
**3. Structură.** Mapare `attack-action` (tehnică + timestamp + comanda observată), `attack-asset` (host/cont/fișier), `attack-condition`, `attack-operator` (AND/OR), plus `effect_refs` pentru cauzalitate; TLP marking din metadatele cazului.
**4. Integrare.** *Core*: graful cauzal există deja — se adaugă doar un serializator; *UI*: buton „Exportă narativa ca Attack Flow".

### P2‑7. Baseline statistic & scor de raritate la nivel de flotă

**1. Nume:** `LogAnalyzer.Analytics.Baseline`.
**2. Valoare.** Rezolvă C‑01/C‑02 sistematic: în locul pragurilor fixe, produsul învață din corpusul ingerat („stack counting" automatizat). Cu 40 de stații în caz, un serviciu prezent pe 1 stație este mai suspect decât orice regulă.
**3. Structură.** Tabele de frecvență per dimensiune (`image`, `parent→child`, `service_name`, `task_name`, `autorun_path`, `pipe_name`, `logon hour`), scor \(-\log_2 p\) (surpriză informațională, aditiv și interpretabil), prag adaptiv pe percentile; opțional profil „gold image" importabil ca baseline extern. Toate calculele în SQL — zero dependențe ML, zero opacitate.
**4. Integrare.** *Core*: `RarityScorer` alimentează scorul global de risc cu contribuții explicabile; *UI*: coloană „raritate" sortabilă în toate grid‑urile (feature mic, adorat de analiști).

### P2‑8. Import de colectări KAPE / Velociraptor (interoperabilitate de intrare)

**1. Nume:** `LogAnalyzer.Ingest.CollectionImporters`.
**2. Valoare.** LogAnalyzer nu trebuie să devină un colector; trebuie să **consume** ce colectează standardele de facto. KAPE structurează colectarea prin Targets (ce se ia) și Modules (ce se rulează), cu ținte comunitare menținute public ([KAPE Documentation](https://ericzimmerman.github.io/KapeDocs/); [EricZimmerman/KapeFiles](https://github.com/EricZimmerman/KapeFiles)), iar Velociraptor produce colectoare offline pre‑configurate — binare autonome cu instrucțiuni „baked in", ideale pentru medii air‑gapped ([Velociraptor, Offline Collections](https://www.velociraptor-docs.org/docs/deployment/offline_collections/)).
**3. Structură.** Importatoare pentru: arborele de ieșire KAPE (`%d\%m\...` cu structură de volum păstrată), container VHDX/ZIP, colectare Velociraptor (ZIP cu `results/*.json` — JSONL per artefact, cu mapare `Windows.KapeFiles.Targets`). Detectare automată a tipului de colectare + inventar cu SHA‑256 la import.
**4. Integrare.** *Infrastructure*: `ICollectionImporter`; *UI*: wizard „Import colectare" cu raport de acoperire („au fost găsite 14/19 categorii de artefacte așteptate").

### P2‑9. Ergonomie de analiză Tier‑3: adnotări, teorii concurente, „paper trail"

**1. Nume:** `LogAnalyzer.Analysis.Workbench`.
**2. Valoare.** ISO/IEC 27042 cere ca interpretarea să fie trasabilă și repetabilă ([ISO/IEC 27042:2015](https://www.iso.org/standard/44406.html)). În practică, aceasta înseamnă: marcarea evenimentelor (tag‑uri), notițe per artefact cu autor și timp, **ipoteze concurente** (metoda „Analysis of Competing Hypotheses" — fiecare ipoteză cu probe pro/contra), și un jurnal de investigație exportabil ca anexă. Este feature‑ul care face diferența între o unealtă folosită o dată și una adoptată de o echipă.
**3. Structură.** `annotations(entity_type, entity_id, author, created_utc, text, tags[])`, `hypotheses(title, status, confidence, evidence_for[], evidence_against[])`; export în raport + în CASE (`uco-core:Assertion`).
**4. Integrare.** *Core*: `AnnotationService` (transversal); *UI*: panou lateral persistent, scurtături de tastatură (`Ctrl+T` tag, `Ctrl+N` notă).

---

## 6. Contribuție analitică proprie: Matricea de Fiabilitate Probatorie

Aceasta este componenta care, în opinia mea, lipsește din toate uneltele mid‑market și pe care LogAnalyzer o poate transforma în diferențiator central. Fiecare artefact ingerat primește trei atribute afișate în UI și tipărite în raport: **ce probează**, **cât de rezistent este la manipulare**, **ce nu probează**.

| Artefact | Ce probează efectiv | Rezistență la anti‑forensics | Capcană critică de interpretare |
|---|---|---|---|
| Prefetch (`.pf`) | Execuție + moment + fișiere încărcate | Medie (ștergere ușoară; poate fi dezactivat) | Absența nu înseamnă neexecuție (SSD/politici, Server dezactivat implicit) |
| Amcache `InventoryApplicationFile` | **Prezență** pe disc + SHA‑1 + metadate PE | Ridicată (fără metodă cunoscută de modificare, [Securelist](https://securelist.com/amcache-forensic-artifact/117622/)) | Scris de Compatibility Appraiser → timestamp‑ul ≠ momentul execuției |
| Shimcache | Prezență/enumerare; poziția în cache = ordine relativă | Medie (scris la shutdown pe unele versiuni) | **Nu probează execuția pe Win10/11**; pot exista intrări pentru binare neexecutate ([Mandiant](https://cloud.google.com/blog/topics/threat-intelligence/caching-out-the-val/), [nullsec.us](https://nullsec.us/windows-10-11-appcompatcache-deep-dive/)) |
| BAM/DAM | Ultima execuție per SID | Medie | Se resetează/rotează; doar ultima rulare |
| UserAssist | Lansare prin Explorer (GUI) + contor | Scăzută (ștergere ușoară din NTUSER) | Nu vede execuția din linia de comandă/servicii |
| `$MFT` `$SI` | Timestamp‑uri „vizibile" | **Scăzută** (API‑uri publice pot rescrie) | Ținta primară a timestomping‑ului |
| `$MFT` `$FN` | Timestamp‑uri de intrare de director | Ridicată (nu se modifică prin API‑uri standard, [andreafortuna](https://andreafortuna.org/2026/07/06/ntfs-forensics-deep-dive/)) | Se actualizează la rename/move, nu la write |
| `$UsnJrnl:$J` | Secvență de operații pe fișiere | Ridicată (dar volumul se rotește ~20 zile) | Ștergerea jurnalului este ea însăși un IoC |
| EVTX Security | Autentificări, procese (dacă auditat) | **Scăzută** (1102, rotire, dezactivare audit) | Absența unui EID ≠ absența faptei; verificați politica de audit |
| SRUM | Volum de rețea și resurse per aplicație | Ridicată (bază ESE, greu de editat selectiv) | Agregare pe intervale (~60 min) → nu dă cronologie fină |
| Recall (`ukg.db` + JPEG) | Conținut vizual efectiv al sesiunii | Scăzută (ștergere ușoară) | Dezactivat implicit corporativ; implicații GDPR majore |
| Shellbags | Navigare în directoare (inclusiv șterse/remote) | Medie | Prezența nu implică deschiderea de fișiere |
| Amcache `InventoryDriverBinary` | Driver prezent (BYOVD) | Ridicată | Nu implică încărcare — corelați `CodeIntegrity` |

**Regula de agregare recomandată:** scorul global de risc se calculează pe **clase de fiabilitate**, nu pe număr de semnale. Trei artefacte de fiabilitate scăzută nu echivalează cu un artefact de fiabilitate ridicată; iar orice concluzie de tip „a executat X la momentul T" trebuie să citeze cel puțin două surse independente sau să fie marcată explicit ca „probabilă", nu „confirmată".

---

## 7. Foaie de drum, arhitectură țintă și estimări

### 7.1. Contract de extensibilitate (fundația care trebuie livrată prima)

Înainte de orice parser nou, recomand introducerea unui **contract unic de artefact** în Core, care rezolvă simultan mentenanța, testabilitatea CFTT și proveniența CASE:

```csharp
public interface IArtifactParser
{
    ArtifactKind Kind { get; }              // Prefetch, Mft, Srum, ...
    string SchemaVersion { get; }           // versionat, apare in raport
    bool CanHandle(EvidenceItem item);      // pe semnatura, nu pe extensie
    IAsyncEnumerable<ParsedRecord> ParseAsync(EvidenceItem item, CancellationToken ct);
    ParserCapabilities Capabilities { get; } // ex. SupportsRecovery, ReadOnlyGuaranteed
}
```

Reguli nenegociabile: (a) parserele nu scriu niciodată în sursă (aserțiune testată automat prin comparație de hash înainte/după); (b) fiecare `ParsedRecord` poartă `SourceFileSha256` + offset — trasabilitate până la octet, cerință de bază pentru ISO/IEC 27037 ([ISO/IEC 27037:2012](https://www.iso.org/standard/44381.html)); (c) erorile de parsare devin înregistrări de tip `ParseAnomaly`, nu excepții înghițite (o eroare de parsare poate fi un indiciu de corupere intenționată).

### 7.2. Prioritizare sintetică

| # | Modul | Prioritate | Impact probatoriu | Efort (om‑săpt.) | Risc de mentenanță |
|---|---|---|---|---|---|
| P0‑1 | NTFS `$MFT`/`$J`/`$I30` + timestomping | P0 | Foarte ridicat | 8–10 | Scăzut (format stabil) |
| P0‑2 | Evidența execuției (6 artefacte) | P0 | Foarte ridicat | 6–8 | **Ridicat** (Prefetch v31, Shimcache) |
| P0‑3 | Shellbags / LNK / Jump Lists | P0 | Ridicat | 5–6 | Mediu (shell items) |
| P0‑4 | ESE offline (SRUM, WebCache) | P0 | Ridicat | 6–8 | Mediu |
| P0‑5 | Browser (cu tratare ABE `v20`) | P0 | Ridicat | 4–5 | **Ridicat** (schimbări Chrome) |
| P0‑6 | Sigma Correlations pe SQLite | P0 | Ridicat | 5–6 | Mediu (spec în evoluție) |
| P0‑7 | Normalizare + canale EVTX + recovery | P0 | Foarte ridicat | 5–7 | Scăzut |
| P0‑8 | Anti‑forensics & integritate probe | P0 | Foarte ridicat (diferențiator) | 3–4 | Scăzut |
| P0‑9 | Persistență comprehensivă + WMI | P0 | Foarte ridicat | 5–6 | Mediu (OBJECTS.DATA) |
| P0‑10 | ATT&CK v18 (Detection Strategies) | P0 | Mediu‑ridicat | 2–3 | Mediu (versiuni ATT&CK) |
| P0‑11 | Export CASE/UCO + manifest semnat | P0 | Ridicat (juridic) | 3–4 | Scăzut |
| P0‑12 | Intel Pack semnat + harness CFTT | P0 | Ridicat (sustenabilitate) | 4–5 | Scăzut |
| P1‑1…P1‑11 | Vizualizări, AD, LOLBAS, transpiler, NIS2, performanță | P1 | Ridicat | 35–45 (cumulat) | Mixt |
| P2‑1…P2‑9 | Win11 modern, VSS, corpus local, YARA‑X, Attack Flow, baseline, importatoare | P2 | Mediu | 30–40 (cumulat) | Mixt |

**Secvențiere recomandată:** (1) contractul din 7.1 + P0‑7 (normalizare) — fără ele, orice parser nou creează datorie tehnică; (2) P0‑1 + P0‑2 + P0‑8 împreună, ca „pachet de evidență de execuție și integritate" — este cel mai vandabil salt de versiune; (3) P0‑6 + P1‑5 (corelații + AD); (4) P0‑11 + P0‑12 + P1‑9 (defensibilitate și conformitate) înainte de orice licitație publică; (5) restul.

---

## 8. Conformitate: cerințe operative concrete

### 8.1. NIS2 — cascada de raportare care trebuie automatizată

| Termen | Livrabil | Conținut minim impus | Sursa |
|---|---|---|---|
| ≤ 24 h de la cunoaștere | Avertizare timpurie (early warning) | Suspiciune de act ilicit/malițios; potențial impact transfrontalier | [NIS2 art. 23(4)(a)](https://www.nis-2-directive.com/NIS_2_Directive_Article_23.html) |
| ≤ 72 h | Notificare de incident | Actualizarea avertizării; evaluare inițială a severității și impactului; **indicatori de compromitere**, unde sunt disponibili | [NIS2 art. 23(4)(b)](https://www.nis-2-directive.com/NIS_2_Directive_Article_23.html) |
| La cerere CSIRT | Raport intermediar | Actualizări de stare relevante | [NIS2 art. 23(4)](https://www.nis-2-directive.com/NIS_2_Directive_Article_23.html) |
| ≤ 1 lună | Raport final | Descriere detaliată, tip de amenințare/cauză rădăcină, măsuri aplicate și în curs, impact transfrontalier | [NIS2 art. 23(4)](https://www.nis-2-directive.com/NIS_2_Directive_Article_23.html) |

În România, autoritatea/CSIRT‑ul de raportare este **DNSC**, în cadrul legal creat prin ordonanța de urgență de transpunere a Directivei NIS 2 ([DNSC — proiect OUG de transpunere NIS 2](https://dnsc.ro/vezi/document/oug-privind-transpunerea-directivei-nis-2); [DNSC — comunicat privind completarea cadrului legislativ național](https://dnsc.ro/vezi/document/comunicat-de-presa-completarea-cu-succes-a-cadrului-legislativ-national-pentru-securitatea-cibernetica-prin-transpunerea-directivei-nis2)). Recomandare de produs: câmpurile șabloanelor trebuie parametrizabile per‑jurisdicție (fișier `jurisdiction/ro.yaml`, `jurisdiction/fr.yaml`), nu codificate — astfel același build servește piața RO, FR și restul UE.

### 8.2. Standarde de proces și de probă de aliniat explicit în documentație

- **ISO/IEC 27037** — identificare, colectare, achiziție, prezervare a probelor digitale; relevant pentru fluxul de import și pentru garanția read‑only ([ISO](https://www.iso.org/standard/44381.html)).
- **ISO/IEC 27042** — analiză și interpretare, cu accent pe continuitate, validitate, **reproductibilitate și repetabilitate** ([ISO](https://www.iso.org/standard/44406.html)); direct legat de manifestul de caz și de explicabilitatea scorului.
- **NIST SP 800‑86** — integrarea tehnicilor forenzice în răspunsul la incidente ([NIST CSRC](https://csrc.nist.gov/pubs/sp/800/86/final)); **NIST SP 800‑61** pentru ciclul de management al incidentelor ([NIST](https://nvlpubs.nist.gov/nistpubs/specialpublications/nist.sp.800-61r2.pdf)).
- **NIST CFTT** — model de specificații, aserțiuni și planuri de test pentru validarea uneltelor, inclusiv pentru unelte de Registru Windows ([CFTT](https://www.nist.gov/itl/csd/secure-systems-and-applications/computer-forensics-tool-testing-program-cftt); [CFTT — Windows Registry Tools](https://www.nist.gov/itl/csd/secure-systems-and-applications/computer-forensics-tool-testing-program-cftt/cftt-8)).
- **CISA IR/VR Playbooks** — structura de proces a răspunsului ([CISA](https://www.cisa.gov/resources-tools/resources/federal-government-cybersecurity-incident-and-vulnerability-response-playbooks)).
- **ANSSI / CERT‑FR** — fișe reflex de qualificare și de endiguement a compromiterii, plus doctrina de remediere ([qualification](https://cert.ssi.gouv.fr/uploads/CERTFR-2024-RFX-005-2.pdf); [endiguement](https://cert.ssi.gouv.fr/uploads/CERTFR-2024-RFX-006-1.pdf); [remédiation](https://cyber.sites.beta.gouv.fr/securisation/gestion-de-crise/piloter-la-remediation-dun-incident-cyber/)).
- **CASE/UCO** — schimb de informații de investigație și lanț de custodie ([caseontology.org](https://www.caseontology.org/ontology/intro.html)).
- **RFC 3161** — timestamping de încredere pentru dovada de anterioritate ([IETF](https://datatracker.ietf.org/doc/html/rfc3161)).

---

## 9. Riscuri, anti‑pattern‑uri de evitat și criterii de acceptanță

**Riscuri majore ale planului.**
1. **Instabilitatea formatelor.** Prefetch a ajuns la versiunea 31 pe Windows 11 ([libyal](https://github.com/libyal/libscca/blob/main/documentation/Windows%20Prefetch%20File%20(PF)%20format.asciidoc)), Shimcache a fost rescris de trei ori, Chrome a schimbat criptarea cookie‑urilor în 2024 ([BrowserForensics](https://www.browserforensics.app/en/blog/chrome-v20-app-bound-encryption)), iar evenimentul 4769 a primit o versiune nouă în ianuarie 2025 ([Microsoft Learn](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/auditing/event-4769)). Mitigare: `SchemaVersion` per parser, corpus de regresie, „nu ghici — raportează necunoscut".
2. **Iluzia acoperirii.** O matrice ATT&CK verde pe date incomplete e mai periculoasă decât absența matricei. Mitigare: stratul de vizibilitate (P1‑1).
3. **Supraîncărcarea UI.** 30 de view‑uri noi ucid uzabilitatea. Mitigare: consolidare în cinci spații de lucru (Triaj, Cronologie, Entități, Detecții, Raport), nu un view per artefact.
4. **Deriva Intel Pack.** Air‑gapped + fără disciplină de actualizare = fals‑negative silențioase. Mitigare: indicator de vechime cu escaladare vizuală și blocarea generării de rapoarte „certificate" peste 180 de zile.

**Anti‑pattern‑uri de evitat explicit.** Nu împachetați un runtime Python pentru pySigma/plaso într‑un produs air‑gapped licențiat pe HWID. Nu folosiți `esentutl /r` sau orice recuperare care scrie în probă. Nu construiți arbori de procese pe PID. Nu prezentați scoruri fără descompunere. Nu afirmați „execuție confirmată" pe baza Shimcache. Nu implementați decriptare de cookie‑uri ABE offline — declarați limitarea.

**Criterii de acceptanță măsurabile pentru versiunea următoare (propunere de contract intern):**
- Ingestie de 100 de milioane de evenimente cu throughput ≥ 60k evenimente/s pe hardware de laborator; interogare filtrată < 2 s pe orice câmp indexat; căutare full‑text < 1 s.
- ≥ 95% acoperire pe cele 19 categorii de artefacte din KAPE „triage" import, cu raport de acoperire automat.
- 100% dintre concluziile din raportul PDF trasabile la (fișier sursă, SHA‑256, offset/ID înregistrare, regulă, versiune de schemă).
- Zero scriere în orice fișier de probă, demonstrat prin test automat de hash înainte/după pentru toate parserele.
- Generare completă a celor trei notificări NIS2 în < 5 minute de la marcarea „moment al cunoașterii".
- Export CASE/UCO validat sintactic împotriva ontologiei locale, plus export layer ATT&CK Navigator v4.5 care se deschide fără eroare într‑o instanță locală de Navigator ([spec layer v4.5](https://github.com/mitre-attack/attack-navigator/blob/master/layers/spec/v4.5/layerformat.md)).

---

## 10. Concluzie

Direcția strategică pe care o recomand nu este „mai multe detecții", ci **mutarea produsului de la detecție la probatoriu**. Concurenții din segmentul de detecție sunt EDR‑urile, cu telemetrie live pe care o aplicație air‑gapped nu o poate egala. În schimb, pe terenul probatoriului offline — artefacte de disc, triangularea execuției, detecția anti‑forensics, lanț de custodie exprimat în CASE/UCO, validare tip CFTT, notificări NIS2 generate automat către DNSC — LogAnalyzer poate deveni cea mai bună unealtă disponibilă în limba română, într‑o nișă în care KAPE și EZ Tools oferă capabilități excelente, dar fragmentate, fără interfață unificată și fără strat de conformitate.

Cele trei livrabile care schimbă cel mai mult poziționarea, în ordine: **(1)** pachetul NTFS + evidența execuției + anti‑forensics (P0‑1, P0‑2, P0‑8) — paritate forenzică reală; **(2)** Sigma Correlations native pe SQLite (P0‑6) plus transpilerul către SPL/KQL (P1‑8) — expresivitate de detecție pe care puține unelte offline o au; **(3)** stratul de defensibilitate (P0‑11, P0‑12, P1‑9) — argumentul care câștigă licitații și care protejează clientul în fața DNSC și a instanțelor.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
