# Cercetare: ce așteaptă practicienii de la un instrument de triaj DFIR pe Windows și ce lipsește din LogAnalyzer

Data: 2026-10-11. Tip: cercetare web, fără modificări de cod. Statut: `DOCUMENT_VERIFIED` pentru afirmațiile cu
sursă; `CLAIMED_ONLY` pentru afirmațiile producătorilor despre propriile produse (sunt marcate ca atare).
Afirmațiile pe care nu le-am putut confirma dintr-o sursă primară sunt marcate **[NESIGUR]**.

Metodă și limite:

- Surse: documentația și notele de versiune ale instrumentelor, bloguri tehnice ale producătorilor și ale
  practicienilor (2019-2026), ghiduri publice. Reddit (r/computerforensics, r/blueteamsec) **nu a putut fi
  accesat**: domeniul refuză agentul de căutare. De aceea secțiunea 3 se sprijină pe ce scriu public autorii
  instrumentelor și practicienii pe bloguri, nu pe fire de discuții. Este o lacună de metodă, nu o dovadă că
  utilizatorii nu se plâng de altceva.
- Afișele SANS (Windows Forensic Analysis, Hunt Evil) sunt PDF-uri care cer cont; le-am citat prin pagina
  publică a afișului, nu prin conținutul lor.
- Starea LogAnalyzer a fost luată din descrierea produsului primită și dintr-o verificare scurtă în cod:
  `LogAnalyzer.Dfir.Core/Analysis/ParserCapabilities.cs` declară parsere pentru EVTX, Prefetch, Amcache,
  hive-ul SYSTEM (BAM/ShimCache), hive-urile utilizator și SOFTWARE, servicii, USB, LNK, JumpList, USN,
  sarcini programate, SRUM (doar rețea), PCAPNG, istoric de browser (Chromium și Firefox), plus canale EVTX
  (Security, System, Application, Sysmon, WinRM, OpenSSH, TerminalServices LSM/RCM, DNS-Client,
  NetworkProfile, Partition/Diagnostic, Kernel-PnP, DriverFrameworks, WLAN, DHCP, PowerShell/Operational).
  Există și `SigmaLite`, `YaraLite`, `LolbasEngine`, `MftParser`, detecția golurilor de RecordID
  (`AntiForensics.cs` AF04) și, în stratul vechi `LogAnalyzer.Infrastructure`, extragerea parțială din MPLog
  și din istoricul PSReadLine (prin CSV de triaj). Nu am verificat cât de complet funcționează
  fiecare parser: lista de lacune de mai jos este relativă la această declarație, nu la teste rulate
  (`CODE_VERIFIED` parțial, nu `TEST_VERIFIED`).

---

## 1. Ce oferă instrumentele comparabile

| Instrument | Rol | Ce face bine (după sursă) | Ce nu face sau limitări | Sursă |
|---|---|---|---|---|
| **KAPE + EZ Tools** | Colectare țintită (Targets) și procesare (Modules) | Copiază fișiere blocate prin acces brut NTFS; ținta compusă `KapeTriage` + modulul `!EZParser` rulează toate EZ Tools pe ce s-a colectat; ieșire CSV | Licență gratuită doar pentru uz intern, forțe de ordine și mediul academic; consultanții au nevoie de licență comercială | [Kroll Q3 2021](https://www.kroll.com/en/publications/cyber/kape-quarterly-update-q3-2021), [Cyber Triage despre KAPE](https://www.cybertriage.com/blog/analyzing-kape-dfir-artifacts-in-cyber-triage/), [Cado](https://cadosecurity.com/blog/dfir-with-kape-and-cado-community-edition) |
| **EvtxECmd** | Parsare EVTX | Normalizează toate evenimentele prin „Maps” (câmpuri comune pentru toate canalele); `--dedupe` pe SHA-1 pentru fișiere și shadow copies; `--met` pentru metrici | Fără date publice de performanță pe seturi mari | [SANS, afișul EZ Tools](https://www.sans.org/blog/want-fast-dfir-results-learn-how-with-the-ez-tools-command-line-poster/), [Kroll webcast](https://kroll.com/en/insights/events/2021/webcast-event-log-analysis-with-evtxecmd-using-kape) |
| **Velociraptor** | Agent și colector offline | Colector offline = un singur executabil, fără instalare, ZIP opțional criptat X.509; `Windows.Triage.Targets` derivă din KapeFiles și adaugă surse volatile; din 2025 „colecții adaptive”: parsează pe stație și colectează și binarele indicate de Prefetch/LNK/sarcini/servicii, cu politici `ExcludeSigned`/`HashOnly`, deduplicare și consemnarea fișierelor lipsă | Curba de învățare (VQL); pentru triaj de jurnale recomandă stivuirea rezultatelor pe titlul regulii, pentru că regulile complete produc mii de rânduri | [Colector offline](https://docs.velociraptor.app/knowledge_base/tips/offline_collector_tool/), [Triage collector](https://github.com/Velocidex/velociraptor-triage-collector), [Colecții adaptive 2025](https://docs.velociraptor.app/blog/2025/2025-09-28-adaptive-collections/), [Playbook triaj jurnale](https://docs.velociraptor.app/training/playbooks/triage-logs/) |
| **Hayabusa** | Cronologie și vânătoare pe EVTX | Peste 4.000 de reguli Sigma + peste 170 reguli proprii; pachete Core/Core+/Core++ + Emerging Threats; comenzi `logon-summary`, `eid-metrics`, `log-metrics`, `computer-metrics`, `pivot-keywords-list`, `extract-base64`, `level-tuning`, `config-critical-systems`; raport HTML; afișare în ora locală sau UTC (`-U`) și ISO-8601; multi-thread (până la 5x); filtrare pe canal (20%-10x mai rapid); reguli „zgomotoase” oprite implicit; suport corelații Sigma v2; `sort-csv` (2026) deduplică detecțiile | Antivirusurile marchează fișierele `.yml` ale regulilor; pentru analiză live recomandă pachetul cu reguli codificate; sortarea consumă mult mai multă memorie | [README](https://github.com/Yamato-Security/hayabusa), [OLD-README (comenzi)](https://github.com/Yamato-Security/hayabusa/blob/main/OLD-README.md), [Releases](https://github.com/Yamato-Security/hayabusa/releases), [v4.1.0](https://newreleases.io/project/github/Yamato-Security/hayabusa/release/v4.1.0) |
| **Chainsaw** | Căutare și vânătoare | `search` (șir, regex, expresii Tau), `hunt` cu Sigma + reguli proprii, `analyse shimcache` (cronologie de execuție îmbogățită cu Amcache), `analyse srum` (cere hive-ul SOFTWARE), `analyse gaps` (goluri de RecordID și de timp = ștergere selectivă), `dump` pentru MFT/hive/ESE | Fără benchmark-uri publicate de autori | [README](https://github.com/WithSecureLabs/chainsaw) |
| **Zircolite** | Sigma pe EVTX/JSON/XML/Auditd prin SQLite | Fiecare regulă devine o interogare SQL; pachet `--package` cu vizualizator HTML offline (prezentare, detecții, tabel de evenimente, cronologie, matrice ATT&CK, entități, arbori de procese, consolă SQL); export către Timesketch, Elastic, Splunk, ATT&CK Navigator, SARIF; transformări (Base64, extragere IOC, LOLBin) | Folosește mai multă memorie; seturile implicite „pot conține reguli zgomotoase sau lente” | [README + benchmark](https://github.com/wagga40/Zircolite) |
| **Plaso / Timesketch** | Supercronologie și analiză colaborativă | Plaso fuzionează MACB din sistemul de fișiere, registru, EVTX, Prefetch, LNK, `$UsnJrnl` într-o singură cronologie; `psort` normalizează fusul orar; Timesketch: etichetare, stele, căutări salvate, analizoare (Sigma, „chain” care leagă descărcarea de execuția fișierului) | Infrastructură grea (server) | [Plaso](https://github.com/log2timeline/plaso), [plaso.readthedocs.io](https://plaso.readthedocs.io/), [Timesketch](https://timesketch.org/), [concepte Timesketch](https://github.com/hnhdev/timesketch/blob/master/docs/guides/user/basic-concepts.md) |
| **Cyber Triage** | Triaj comercial cu scor | Fiecare artefact etichetat Bad/Suspicious/Good/Unknown; scorul se propagă pe alte stații din același incident după hash; încadrare MITRE în raportul HTML; din 3.14: integrare Hayabusa automată și **„baseline” dintr-o imagine de referință** ca să nu mai marcheze artefactele cunoscute; „dată de referință” pentru colecții întârziate; criptare cu cheie publică la colector | Afirmațiile de rată de fals-pozitive sunt ale producătorului (`CLAIMED_ONLY`) | [Scoruri](https://docs.cybertriage.com/en/latest/chapters/scores.html), [Analiză](https://docs.cybertriage.com/en/v3.9.0/chapters/analysis.html), [3.14](https://www.cybertriage.com/blog/3-14-release-brings-new-uis-hayabusa-baselining-and-much-more/), [propagare](https://www.cybertriage.com/?p=26751) |
| **Autopsy** | Analiză de imagine disc | Module de ingest: Recent Activity (browser), Hash Lookup cu NSRL marcat „Known” și seturi „known bad”, Data Source Integrity (verifică hash-ul imaginii) | Orientat pe imagine completă, nu pe triaj rapid; sursele găsite sunt secundare **[NESIGUR]** pentru numele exacte ale modulelor | [forensicfocus, NSRL în Autopsy](https://www.forensicfocus.com/forums/general/tsk-autopsy-2-24-working-with-nsrl-hashes/), [rezumat flux](https://skillselion.com/skills/mukul975/anthropic-cybersecurity-skills/analyzing-disk-image-with-autopsy) |
| **Magnet RESPONSE / AXIOM** | Colectare gratuită + analiză comercială | Un singur executabil de pe stick USB, „pornește captura”, aproape fără instruire; RAM (DumpIt, cu RAM Capture ca rezervă), pagefile, EVTX, hive-uri, JumpList; hash pentru ZIP și pentru RAM | Analiza se face în AXIOM (comercial) | [Magnet RESPONSE](https://www.magnetforensics.com/resources/magnet-response/), [anunț](https://www.magnetforensics.com/blog/magnet-response-new-free-tool-for-ir-investigations/) |
| **Belkasoft X / Belkasoft T** | Analiză comercială + triaj gratuit | Fereastră separată „Incident investigation”: Amcache, ShimCache, Syscache, BAM/DAM, AppInit, sarcini, pornire automată, extensii de browser, jurnale Defender, conexiuni RDP/TeamViewer cu IP și oră; mecanismele de persistență grupate într-un loc; Belkasoft T rulează de pe stick, oprește colectarea când ajunge | Modulul de incident e doar în ediția Corporate; afirmațiile sunt ale producătorului | [Incident Investigation](https://belkasoft.com/incident_investigation), [EVTX la Belkasoft](https://belkasoft.com/windows-event-log-forensics), [Belkasoft T](https://platform.softwareone.com/product/belkasoft-triage/PCP-4049-9255) |
| **DFIR-ORC (ANSSI)** | Colector Windows fără instalare | Hive-uri, EVTX, MFT, Prefetch, stare rețea, procese, memorie într-o arhivă structurată; conceput pentru stații unde nu se poate instala agent | Am găsit doar surse secundare pentru lista exactă **[NESIGUR]** | [index cod public FR](https://data.code.gouv.fr/hosts/GitHub/topics/collection), [descriere secundară](https://knogin.com/en/developers/digital-forensics-dfir-orc) |
| **CyLR** | Colector live (C#, GPL-3.0) | Citire brută fără API Windows (fișiere blocate), ținte proprii cu glob/regex, ZIP, opțional SFTP | Doar colectează; ultima activitate în depozit în 2022 | [ecosyste.ms](https://awesome.ecosyste.ms/projects/321075), [comparație colectoare](https://speakerdeck.com/soji256/forensic-artifacts-collecting-tools) |
| **THOR Lite / Aurora Lite (Nextron)** | Scaner IOC/YARA gratuit; agent Sigma pe ETW | THOR Lite: semnăturile open-source (din LOKI) criptate ca să nu declanșeze antivirusul; actualizare prin `thor-lite-util`; Aurora Lite: reconstruiește evenimente de tip Sysmon din ETW și aplică Sigma, gratuit și comercial | Lite = reguli netestate pe goodware; fără Sigma în THOR Lite **[NESIGUR]**; nicio procedură documentată de actualizare offline găsită | [THOR Lite](https://www.nextron-systems.com/thor-lite/), [frecvență actualizări](https://www.nextron-systems.com/2020/03/20/thor-lite-free-yara-and-ioc-scanner/), [Aurora](https://www.nextron-systems.com/aurora), [Aurora Lite](https://www.nextron-systems.com/2022/04/04/aurora-lite-agent-v1-0-release/) |

Performanță comparată (singurul benchmark găsit, publicat de autorul Zircolite, deci `CLAIMED_ONLY`, seturi de
reguli diferite): 8 EVTX / 13,3 GB pe 10 nuclee: Zircolite 104,8 s, Chainsaw 206,3 s, Hayabusa 518,8 s
([Zircolite README](https://github.com/wagga40/Zircolite)). Concluzia utilă pentru LogAnalyzer: ordinul de
mărime acceptat de practicieni pentru zeci de GB de EVTX este de minute, nu de ore, și se obține prin
paralelism și filtrare pe canal înainte de a aplica regulile
([Hayabusa OLD-README](https://github.com/Yamato-Security/hayabusa/blob/main/OLD-README.md)).

---

## 2. Lista de artefacte: ce dovedește fiecare, dificultate, stare în LogAnalyzer

Dificultate = efort de implementare a unui parser fiabil în .NET (S mic, M mediu, L mare). Stare: **DA** =
declarat în `ParserCapabilities.cs`; **PARȚIAL**; **NU** = negăsit.

| Artefact | Ce dovedește (și ce NU) | Dificultate | Stare LogAnalyzer | Sursă |
|---|---|---|---|---|
| **Prefetch** | Execuție, număr de rulări, ultima rulare și pe Win10/11 până la 7 rulări anterioare. Poate fi dezactivat pe servere | M (compresie MAM pe Win10+) | DA | [EC-Council ghid](https://open-exam-prep.com/study-guides/ec-council-ecih/endpoint-iot-ot-incidents/sec-11-1-windows-endpoint-forensics-artifacts) |
| **Amcache.hve** | Prezența fișierului pe disc, inclusiv după ștergere, cu SHA-1; **nu** dovedește execuția, nu are utilizator, argumente sau oră de rulare | M | DA | [Cyber Triage 2026](https://www.cybertriage.com/blog/shimcache-and-amcache-forensic-analysis-2026/), [ANSSI 2019](https://cyber.gouv.fr/sites/default/files/2019/01/anssi-coriin_2019-analysis_amcache.pdf) |
| **ShimCache (AppCompatCache)** | Prezență/vizibilitate pentru subsistemul de compatibilitate; pe Win10+ **nu** dovedește execuția; ora e a ultimei modificări a fișierului; se scrie în hive doar la oprire corectă | M | DA | [Cyber Triage](https://www.cybertriage.com/blog/shimcache-and-amcache-forensic-analysis-2025/), [Magnet](https://www.magnetforensics.com/blog/shimcache-vs-amcache-key-windows-forensic-artifacts/) |
| **BAM/DAM** | Calea completă și ultima execuție per SID (UTC, FILETIME); există din Win10 1709 | S | DA | [Velociraptor, dovezi de execuție](https://docs.velociraptor.app/docs/forensic/evidence_of_execution/) |
| **LNK** | Deschiderea unui fișier: calea originală, ore, seria volumului, numele NetBIOS și MAC-ul gazdei; rămâne după ștergerea țintei; literă de unitate E:/F: + seria volumului leagă de un mediu amovibil | M | DA | [ghid LNK/JumpList](https://tessl.io/registry/skills/github/mukul975/Anthropic-Cybersecurity-Skills/analyzing-lnk-file-and-jump-list-artifacts), [SANS playbook](https://www.sans.org/posters/windows-forensic-analysis-playbook) |
| **Jump Lists** | Fișiere recente/frecvente per aplicație; aplicațiile UWP/MSIX pot să nu le creeze | M (OLE CFB + DestList) | DA | același ghid |
| **ShellBags** (UsrClass.dat) | Că un folder a fost răsfoit în Explorer, inclusiv pe rețea sau mediu amovibil, chiar după ștergerea folderului | M-L (structuri shell item variate) | **NU** (există doar textul explicativ în `ItemMeaning.cs`, fără parser) | același ghid; [SANS playbook](https://www.sans.org/posters/windows-forensic-analysis-playbook) |
| **RecentDocs / OpenSave MRU / UserAssist** | Ordinea accesărilor recente în registru | S | PARȚIAL (hive utilizator declarat; acoperirea cheilor neverificată) | [SANS playbook](https://www.sans.org/posters/windows-forensic-analysis-playbook) |
| **SRUM** (SRUDB.dat, ESE) | Per aplicație și per SID: octeți trimiși/primiți, CPU, rețele și durata conectării; ~30-60 zile; aplicațiile șterse rămân | M (ESE) | PARȚIAL (doar rețea) | [SANS ISC](https://isc.sans.edu/diary/21927), [Velociraptor SRUM](https://docs.velociraptor.app/artifact_references/pages/windows.forensics.srum/), [Magnet](https://magnetforensics.com/blog/srum-forensic-analysis-of-windows-system-resource-utilization-monitor) |
| **Windows Timeline** (ActivitiesCache.db) | Activitate per utilizator; datele există local și pe Win11; nu poate fi singura probă | S (SQLite) | **NU** | [Elcomsoft](https://pt.elcomsoft.com/help/en/esr/esr_forensic.html), [studiu 2026](https://infokripto.poltekssn.ac.id/index.php/infokripto/article/download/157/123), [DFRWS](https://dfrws.org/presentation/an-incomplete-tour-of-the-forensic-implications-of-the-windows-10-activity-timeline-2/) |
| **Istoric browser** | Navigare, descărcări (vector inițial) | S-M | DA (Chromium, Firefox) | [Belkasoft](https://belkasoft.com/incident_investigation) |
| **PSReadLine** `ConsoleHost_history.txt` | Comenzi PowerShell interactive per utilizator, uneori parole în clar; lipsa lui nu dovedește nimic (nu se scrie din shell-uri non-interactive; `Clear-History` nu îl șterge; atacatorul poate seta `SaveNothing`) | S | PARȚIAL (doar prin `TriageCsvParser` din stratul vechi, din CSV de triaj; fără parser direct al fișierului în `ParserCapabilities`) | [Binalyze](https://kb.binalyze.com/air/features/acquisition/acquisition-profiles/windows-collections/powershell-consolehost-history), [Splunk](https://research.splunk.com/endpoint/a203040e-f8fd-49bb-8424-d2fabf277322/), [Sigma](https://detection.fyi/sigmahq/sigma/windows/file/file_delete/file_delete_win_delete_powershell_command_history/) |
| **Sarcini programate** (XML în `System32\Tasks`, `TaskCache` în SOFTWARE) | Persistență; nepotrivirea XML ↔ TaskCache ↔ evenimente e un semnal | S-M | PARȚIAL (parserul XML există; `ScheduledTaskParser.cs` declară explicit că nu citește TaskCache, deci sarcinile fără XML scapă) | [Velociraptor](https://docs.velociraptor.app/artifact_references/pages/windows.eventlogs.scheduledtasks/), [MCSI](https://mcsi-library.readthedocs.io/articles/2022/05/windows-scheduled-tasks-in-digital-forensics/windows-scheduled-tasks-in-digital-forensics.html) |
| **Persistență WMI** (`wbem\Repository\OBJECTS.DATA`) | Abonamente filter/consumer/binding; `CommandLineEventConsumer` și `ActiveScriptEventConsumer` execută cod; există abonamente benigne (SCCM, AV) | L (format CIM nedocumentat oficial) | **NU** | [Velociraptor WMI](https://docs.velociraptor.app/blog/2022/2022-01-12-wmi-eventing), [HTB](https://hackthebox.com/blog/perseverance-biz-ctf-2022-forensics-writeup) |
| **Cache bitmap RDP** (`Terminal Server Client\Cache`) | Fragmente 64x64 din ecranul văzut de cel care a pornit RDP; uneori singura urmă rămasă după ștergerea jurnalelor; reconstrucția e imperfectă | M | **NU** | [Pentest Partners](https://www.pentestpartners.com/security-blog/the-remote-desktop-puzzle-dfir-techniques-for-dealing-with-rdp-bitmap-cache/), [Palo Alto](https://www.paloaltonetworks.com/blog/security-operations/playbook-of-the-week-uncover-your-rdp-secrets/) |
| **Defender MPLog** (`ProgramData\Microsoft\Windows Defender\Support\MPLog-*`) | Execuție de procese, acces la fișiere, amenințări, versiuni de semnături; formatul s-a schimbat în timp | M (text cu formate variabile) | PARȚIAL (`AntiForensicsArtifactsParser` din stratul vechi extrage doar liniile de detecție cu hash; execuția de procese și accesul la fișiere nu; nu e în `ParserCapabilities`) | [CrowdStrike](https://www.crowdstrike.com/en-us/blog/how-to-use-microsoft-protection-logging-for-forensic-investigations/), [Intrinsec mplog_parser](https://www.intrinsec.com/?p=223236) |
| **Defender DetectionHistory** | Hash, utilizator, cale, proces inițiator, uneori linia de comandă; se actualizează când utilizatorul permite/carantinează | M (binar) | **NU** | [SANS](https://www.sans.org/blog/uncovering-windows-defender-real-time-protection-history-with-dhparser), [Orange Cyberdefense](https://www.orangecyberdefense.com/global/blog/cybersecurity/digging-into-windows-defender-detection-history-wddh), [Plaso](https://plaso.readthedocs.io/en/latest/_modules/plaso/parsers/windefender_history.html) |
| **Canal Defender/Operational** (1006-1008, 1116-1119) | Detecții și acțiuni | S | **NU ca canal** (controlul de stație verifică Defender, dar canalul nu e în listă) | [glosar Defender](https://ayinedjimi-consultants.fr/glossaire/windows-defender-forensics), [Intrinsec](https://www.intrinsec.com/?p=231730) |
| **$MFT** | Toate fișierele, inclusiv marcate șterse; comparația $SI/$FN pentru timestomping (indiciu, nu dovadă: ambele pot fi modificate) | L | PARȚIAL (`MftParser`) | [inversecos](https://inversecos.com/2022/04/defence-evasion-technique-timestomping.html), [Fortuna 2026](https://andreafortuna.org/2026/07/06/ntfs-forensics-deep-dive/) |
| **USN Journal** (`$Extend\$UsnJrnl:$J`) | Creare/redenumire/ștergere, ~20 de zile pe volum activ; coroborează timestomping | M | DA | [Fortuna 2026](https://andreafortuna.org/2026/07/06/ntfs-forensics-deep-dive/) |
| **USB** (USBSTOR, Enum\SCSI pentru USB3/UASP, MountedDevices, MountPoints2, `setupapi.dev.log`, Partition/Diagnostic 1006) | Dispozitiv recunoscut/montat, serie, prima/ultima conectare; **nu** lista fișierelor copiate; `setupapi.dev.log` e în ora locală | M | DA (USB, Partition/Diagnostic, Kernel-PnP); Enum\SCSI neverificat | [it-connect](https://www.it-connect.tech/forensic-windows-part-15-tracing-usb-device-history/), [Magnet USB](https://www.magnetforensics.com/blog/artifact-profile-usb-devices/) |
| **EVTX TaskScheduler/Operational** (106, 140, 141, 200/201) | Înregistrare/modificare/ștergere/rulare de sarcini; canalul poate fi dezactivat implicit; 4698 cere auditul „Other Object Access” | S | **NU ca canal** | [NXLog](https://docs.nxlog.co/integrate/windows-task-scheduler.html), [Velociraptor](https://docs.velociraptor.app/artifact_references/pages/windows.eventlogs.scheduledtasks/) |
| **EVTX RDP** (RCM 1149, LSM 21/22/23/24/25, RdpCoreTS 131/98, RDPClient 1024 pe sursă, Security 4624 tip 10, 4778/4779) | Mișcare laterală; 1149 = sesiune de rețea reușită, nu autentificare; sensul LSM 24 diferă între surse **[NESIGUR]** | S | PARȚIAL (LSM, RCM da; RdpCoreTS și RDPClient nu) | [FRSecure](https://frsecure.com/blog/rdp-connection-event-logs/), [Expel](https://expel.com/blog/mdr-insights-tracking-lateral-movement-of-bad-actors-in-a-windows-environment-part-i/), [Sophos](https://www.sophos.com/en-us/blog/remote-desktop-protocol-queries-for-investigation) |
| **EVTX BITS-Client, WMI-Activity, PowerShell 4104** | Descărcări BITS, WMI, script block | S | PARȚIAL (PowerShell/Operational da; BITS și WMI-Activity nu) | [Velociraptor playbook](https://docs.velociraptor.app/training/playbooks/triage-logs/), [CERT Bournemouth](https://cert.bournemouth.ac.uk/most-common-windows-event-ids-to-hunt-mind-map/) |
| **Sysmon** (1, 3, 11, 19-21, 22, 23/26) | Procese cu linie de comandă și hash, WMI, ștergeri; doar dacă era instalat și configurat înainte | S | PARȚIAL (1, 4, 16, 22) | [Splunk](https://research.splunk.com/endpoint/a203040e-f8fd-49bb-8424-d2fabf277322/), [Velociraptor WMI](https://docs.velociraptor.app/blog/2022/2022-01-12-wmi-eventing) |
| **Volume Shadow Copies** | Versiuni anterioare ale hive-urilor și EVTX-urilor | L (citire VSS) | NU **[NESIGUR]** (doar detecția ștergerii VSS e confirmată) | [KAPE `--vss`](https://moltchat-agent-commons.onrender.com/wiki/triaging-windows-with-kape_skill_(Anthropic-Cybersecurity-Skills)) |

---

## 3. Dureri recurente și funcții lăudate (rezumate fără citate, cu legături)

### 3.1 Dureri recurente

1. **Zgomotul regulilor.** Seturile Sigma complete produc mii de rânduri; Velociraptor recomandă stivuirea pe
   titlul regulii, Hayabusa oprește regulile zgomotoase implicit, Zircolite avertizează că seturile implicite
   pot fi zgomotoase sau lente. Regulile SIEM nu sunt adaptate mediului local: ce e normal într-o rețea e
   suspect în alta. ([Velociraptor](https://docs.velociraptor.app/training/playbooks/triage-logs/),
   [Hayabusa](https://github.com/Yamato-Security/hayabusa/blob/main/OLD-README.md),
   [Zircolite](https://github.com/wagga40/Zircolite))
2. **Întreținerea manuală a listelor „bune”.** Cyber Triage a introdus baseline din imagine de referință ca să
   scape analiștii de adăugarea manuală în Good Lists.
   ([Cyber Triage 3.14](https://www.cybertriage.com/blog/3-14-release-brings-new-uis-hayabusa-baselining-and-much-more/))
3. **Interpretarea greșită a artefactelor de „execuție”.** Amcache și ShimCache sunt citite des ca dovadă de
   rulare; sursele de specialitate insistă că arată prezența.
   ([Cyber Triage](https://www.cybertriage.com/blog/shimcache-and-amcache-forensic-analysis-2026/),
   [Magnet](https://www.magnetforensics.com/blog/shimcache-vs-amcache-key-windows-forensic-artifacts/))
4. **Fusuri orare.** Unele ore sunt stocate în UTC, altele în ora locală (ex. `setupapi.dev.log`); instrumentele
   le afișează diferit; o zonă greșit configurată pe stația cercetată a dus la acuzații de alterare a probelor.
   ([Digital Detective](https://www.digital-detective.net/manual-identification-of-suspect-computer-time-zone-2/),
   [Hexacorn](https://www.hexacorn.com/blog/?p=6051),
   [it-connect USB](https://www.it-connect.tech/forensic-windows-part-15-tracing-usb-device-history/))
5. **Jurnale suprascrise sau oprite.** Jurnalele se rotesc după mărime, nu după timp; un jurnal plin fie se
   oprește, fie suprascrie; atacatorul poate genera evenimente ca să-și acopere urmele. Canalul TaskScheduler
   poate fi dezactivat implicit; 4698 depinde de politica de audit.
   ([CIS prin Tenable](https://www.tenable.com/audits/items/CIS_MS_Windows_8.1_v2.4.1_Level_1_Bitlocker.audit:9605809157b76adc31ea0d310255b80f),
   [Netwrix](https://docs.netwrix.com/docs/auditor/10_7/configuration/activedirectory/securitylog),
   [NXLog](https://docs.nxlog.co/integrate/windows-task-scheduler.html)). Dimensiunea implicită exactă a
   jurnalului Security pe versiunile actuale **[NESIGUR]**.
6. **O singură șansă la colectare.** Stația e adesea reinstalată; operatorul poate să nu fie specialist; listele
   fixe de căi ratează binarele indicate de artefacte; a doua colectare e scumpă.
   ([Velociraptor 2025](https://docs.velociraptor.app/blog/2025/2025-09-28-adaptive-collections/))
7. **Colectarea de la distanță și colaborarea cu IT.** 71% din 350+ practicieni spun că se luptă cu colectarea
   de la distanță; restricțiile de acces și munca repetitivă sunt printre principalele probleme.
   ([Magnet, State of Enterprise DFIR 2025](https://www.magnetforensics.com/blog/state-of-enterprise-dfir-report-remote-collections-the-surge-in-ai-usage-and-the-growth-of-saas-based-tools))
8. **Antivirusul blochează instrumentul.** Regulile Sigma în clar declanșează Defender; Hayabusa și THOR Lite
   livrează reguli codificate/criptate. LogAnalyzer are deja o problemă documentată de acest tip în memorie
   (fals pozitiv Defender pe DLL-ul de test).
   ([Hayabusa releases](https://github.com/Yamato-Security/hayabusa/releases),
   [THOR Lite](https://www.nextron-systems.com/thor-lite/))
9. **Actualizarea regulilor fără internet.** `update-rules` (Hayabusa), `-U` (Zircolite) și `thor-lite-util`
   presupun rețea; nicio sursă găsită nu documentează o procedură oficială pentru stații izolate.
   SigmaHQ publică pachete ZIP pe pagina de versiuni, ceea ce face posibilă copierea manuală.
   ([SigmaHQ](https://github.com/sigmahq/sigma), [Zircolite](https://github.com/wagga40/Zircolite)).
   Procedura offline a fiecărui instrument **[NESIGUR]**.
10. **Ștergerea selectivă de evenimente** (nu doar 1102) e greu de văzut fără analiza golurilor de RecordID.
    ([Chainsaw `analyse gaps`](https://github.com/WithSecureLabs/chainsaw))
11. **Fals-pozitive pe hash-uri „cunoscute”.** NSRL înseamnă „software cunoscut”, nu „sigur”: conține și
    instrumente de criptare, steganografie, hacking.
    ([NIST NSRL FAQ](https://www.nist.gov/itl/ssd/software-quality-group/national-software-reference-library-nsrl/about-nsrl/nsrl-frequently))

### 3.2 Funcții lăudate sau copiate de la un instrument la altul

- **Supercronologie unică** din sistem de fișiere, registru, EVTX, Prefetch, LNK, USN
  ([Plaso](https://github.com/log2timeline/plaso)).
- **Legarea automată a evenimentelor** (descărcare → execuție) ([Timesketch](https://github.com/hnhdev/timesketch/blob/master/docs/guides/user/basic-concepts.md)).
- **Rezumate „la prima vedere”**: logon-summary, eid-metrics, log-metrics, computer-metrics, pivot keywords
  ([Hayabusa](https://github.com/Yamato-Security/hayabusa/blob/main/OLD-README.md)).
- **Scor de triaj Bad/Suspicious cu propagare după hash** și **baseline din imagine de referință**
  ([Cyber Triage](https://docs.cybertriage.com/en/latest/chapters/scores.html)).
- **Raport HTML offline autonom** cu matrice ATT&CK, arbori de procese, entități
  ([Zircolite](https://github.com/wagga40/Zircolite)); raport HTML cu MITRE la Cyber Triage și Hayabusa.
- **Export către ATT&CK Navigator, Timesketch (JSONL), CSV pentru Timeline Explorer**
  ([Zircolite](https://github.com/wagga40/Zircolite), [Hayabusa](https://github.com/Yamato-Security/hayabusa)).
- **Persistența grupată într-un singur loc** ([Belkasoft](https://belkasoft.com/incident_investigation)).
- **Colector „un singur executabil, un buton”** cu hash pe arhivă ([Magnet RESPONSE](https://www.magnetforensics.com/resources/magnet-response/),
  [Velociraptor](https://docs.velociraptor.app/knowledge_base/tips/offline_collector_tool/)).
- **Consemnarea fișierelor lipsă** ca rezultat util, nu ca eroare ([Velociraptor 2025](https://docs.velociraptor.app/blog/2025/2025-09-28-adaptive-collections/)).
- **Ridicarea severității pentru sisteme critice** (controlere de domeniu, servere de fișiere)
  ([Hayabusa `config-critical-systems`](https://github.com/Yamato-Security/hayabusa/blob/main/OLD-README.md)).
- **Hash lookup offline cu NSRL**, cu semnătură digitală pentru verificarea setului descărcat
  ([NIST NSRL](https://www.nist.gov/itl/ssd/software-quality-group/national-software-reference-library-nsrl/about-nsrl/nsrl-frequently)).

### 3.3 Standarde de colectare pe care le invocă practicienii

- Ordinea volatilității (RFC 3227): memoria înaintea discului, starea rețelei înainte de oprire; NIST SP 800-86
  pentru echipe IT/securitate (colectare, examinare, analiză, raportare); ISO/IEC 27037 pentru identificare,
  colectare, achiziție, conservare, inclusiv date volatile
  ([TrueScreen, comparație](https://truescreen.io/articles/digital-evidence-preservation-standards/),
  [CyberDefenders](https://cyberdefenders.org/cybersecurity-glossary/dfir/)). Sursele găsite sunt secundare;
  textele standardelor nu au fost citite **[NESIGUR]** pentru cerințe exacte.
- Hash la achiziție (ex. SHA-256), păstrat separat, analiza pe copie verificată, reverificare înainte de
  analiză ([Petronella](https://petronellatech.com/data-breach-forensics/how-it-works/collection-and-preservation/)).
- Triajul selectiv înlocuiește imaginea completă când discurile sunt mari, cu compromisul că a doua colectare e
  costisitoare ([Velociraptor triage collector](https://github.com/Velocidex/velociraptor-triage-collector)).

---

## 4. Lacune prioritizate pentru LogAnalyzer

Legendă public: **C** = controlor fără pregătire IT; **A** = analist CSIRT. Efort: S (zile), M (1-3 săptămâni),
L (peste 3 săptămâni). Toate estimările sunt **[NESIGUR]**, fără măsurare pe codul real.

### P1: lipsuri care schimbă concluzia unei investigații sau încrederea în ea

| # | Lacună | De ce | Efort | Public |
|---|---|---|---|---|
| 1 | **Fusul orar al stației** (citit din `TimeZoneInformation` din hive-ul SYSTEM, negăsit în cod), afișat în antetul cazului și al raportului lângă orele UTC (pe care codul le folosește deja); marcarea și convertirea surselor stocate în ora locală (`setupapi.dev.log`, unele câmpuri de text) | Durerea 4; erorile de fus au dus la acuzații de alterare a probelor | S-M | C + A |
| 2 | **Verificarea „jurnalul acoperă perioada?”**: mărimea maximă și politica de retenție a fiecărui canal, cel mai vechi eveniment păstrat vs. perioada cercetată, canale dezactivate (TaskScheduler), politici de audit lipsă pentru 4698/4688. Golurile de RecordID sunt deja detectate (`AntiForensics.cs` AF04, `Correlation.cs`) | Durerile 5 și 10 | S-M | A (C vede doar verdictul „jurnalul acoperă / nu acoperă perioada”) |
| 3 | **Canalele EVTX lipsă**: Defender/Operational, TaskScheduler/Operational, RdpCoreTS, RDPClient (pe stația sursă), BITS-Client, WMI-Activity, Sysmon 3/11/19-21/23/26 | Sunt în ghidurile de RDP, persistență și exfiltrare; parserul EVTX există, lipsesc hărțile și explicațiile | S per canal | A; C beneficiază prin explicații |
| 4 | **Defender MPLog complet + DetectionHistory** în pipeline-ul nou (azi doar liniile de detecție, în stratul vechi) | Pe stații izolate fără Sysmon, Defender e adesea singura telemetrie de execuție; LogAnalyzer verifică deja starea Defender | M | A; C: „Defender a găsit X la ora Y” |
| 5 | **Parser direct PSReadLine `ConsoleHost_history.txt` pentru fiecare profil** (azi doar din CSV de triaj, strat vechi), cu avertisment că lipsa nu dovedește nimic | Artefact ieftin, valoare mare | S | A |
| 6 | **Avertisment de „putere probatorie” pe fiecare artefact** în rapoartele pentru controlori: prezență vs. execuție vs. acces | Durerea 3; `ParserCapabilities` are deja textele „ce dovedește / ce nu”, trebuie aduse în cardul de constatare și în PDF | S | C |
| 7 | **Actualizare offline a regulilor**: pachet semnat (Sigma, LOLBAS, IOC, hărți EVTX) importat de pe mediu amovibil, cu SHA-256 și versiune afișate, refuz la semnătură invalidă, istoric al versiunilor folosite în fiecare caz | Durerea 9; publicul e explicit air-gapped | M | A administrează, C vede „reguli din data X” |
| 8 | **Reguli stocate codificat** pe disc (ca Hayabusa/THOR Lite) | Durerea 8; evită blocarea aplicației de Defender pe stația controlată | S | C + A |

### P2: lipsuri care cresc mult viteza sau acoperirea

| # | Lacună | De ce | Efort | Public |
|---|---|---|---|---|
| 9 | **Baseline „cunoscut bun”** dintr-o stație de referință (servicii, sarcini, programe, drivere, chei Run, hash-uri) și diferența față de stația controlată | Funcție lăudată (Cyber Triage); se potrivește controalelor repetate pe parcuri de stații identice | M | C (cel mai mare câștig) + A |
| 10 | **Hash lookup offline**: NSRL RDS (importat, verificat cu semnătura NIST) ca „cunoscut”, plus liste locale „rău cunoscut”; etichetă clară că NSRL ≠ sigur | Funcție lăudată; reduce zgomotul în inventarul de programe | M (setul e mare: indexare locală) | A; C vede „program cunoscut / necunoscut” |
| 11 | **Stivuire și rezumate**: hit-uri grupate pe regulă, logon-summary, eid-metrics, computer-metrics, pivot pe utilizator/IP/proces | Durerea 1; practica recomandată de Velociraptor și Hayabusa | S-M | A |
| 12 | **ShellBags** | Singura dovadă de răsfoire a folderelor, inclusiv pe stick-uri și share-uri, relevantă pentru registrul de medii | M-L | A; C prin registrul de medii |
| 13 | **SRUM complet** (Application Resource Usage, nu doar rețea) | Aplicații șterse, per SID, octeți trimiși pentru exfiltrare | M | A |
| 14 | **Export interoperabil**: JSONL pentru Timesketch, CSV compatibil Timeline Explorer, strat ATT&CK Navigator | CSIRT-ul lucrează cu alte instrumente; exportul de supercronologie există, formatul trebuie verificat | S | A |
| 15 | **Colecție adaptivă**: la colectarea de pe stație, hash (și opțional copiere) pentru binarele indicate de Prefetch, sarcini, servicii, chei Run, LNK; consemnarea celor lipsă | Durerea 6; „o singură șansă” pe stația izolată | M | C rulează, A primește |
| 16 | **Severitate ridicată pentru stații critice** (configurabilă în cazul de control) | Funcție Hayabusa; ajută ordonarea constatărilor | S | C + A |
| 17 | **Corelarea sarcinilor programate XML ↔ TaskCache ↔ 4698/106** cu semnalarea nepotrivirilor | Semnal de persistență ascunsă | S-M | A |

### P3: utile, dar costisitoare sau de nișă

| # | Lacună | De ce | Efort | Public |
|---|---|---|---|---|
| 18 | **Persistență WMI din `OBJECTS.DATA`** offline | Valoare mare, dar format greu și abonamente benigne frecvente | L | A |
| 19 | **Cache bitmap RDP** (extragere tile-uri + colaj) | Uneori singura urmă; reconstrucție imperfectă, cere revizie umană | M | A |
| 20 | **Windows Timeline (ActivitiesCache.db)** | Probă de sprijin, nu singulară; SQLite simplu | S | A |
| 21 | **Citirea Volume Shadow Copies** pentru hive/EVTX mai vechi | Recuperează jurnale curățate; complexitate mare pe .NET | L | A |
| 22 | **Timestomping complet**: $SI vs $FN + coroborare USN/$LogFile, prezentat ca indiciu | Există deja „indicii”; sursele avertizează că ambele atribute pot fi falsificate | M | A |
| 23 | **Performanță pe zeci de GB EVTX**: filtrare pe canal înainte de reguli, paralelism, metrică afișată (evenimente/s) | Ordinul de mărime al practicienilor: minute pentru 13 GB | M (măsurare întâi) | A |
| 24 | **RAM / pagefile** la colectare | Ordinea volatilității; instrumentele gratuite o fac deja | L (driver/semnare) | A; nu pentru C pe stații clasificate fără aprobare |

---

## 5. Riscuri și necunoscute

- **Metodă**: Reddit inaccesibil; afișele SANS necitite direct; textele ISO 27037 / NIST SP 800-86 necitite.
  Durerile din 3.1 provin din documentația instrumentelor și bloguri, deci pot reflecta prioritățile
  autorilor mai mult decât ale utilizatorilor.
- **Afirmații de producător**: Cyber Triage, Belkasoft, Magnet, Nextron și benchmark-ul Zircolite sunt
  `CLAIMED_ONLY`.
- **Starea LogAnalyzer**: dedusă din `ParserCapabilities.cs` și din căutări de nume, nu din teste. Un parser
  declarat poate fi incomplet; o lacună marcată „NU” poate exista sub alt nume.
- **Licențe**: regulile Hayabusa sunt DRL 1.1, Sigma are licențe mixte (DRL 1.1, GPL 3.0, CC0); includerea lor
  într-un produs distribuit cere atribuire și verificare juridică
  ([Hayabusa](https://github.com/Yamato-Security/hayabusa), [Zircolite](https://github.com/wagga40/Zircolite)).
  KAPE nu poate fi redistribuit de consultanți fără licență comercială
  ([Cyber Triage despre KAPE](https://www.cybertriage.com/blog/analyzing-kape-dfir-artifacts-in-cyber-triage/)).
- **Puncte contradictorii între surse**: sensul LSM 24; ID-urile 200/201 la TaskScheduler; GUID-ul tabelei SRUM
  Application Resource Usage ([it-connect SRUM](https://www.it-connect.tech/windows-forensics-part-12-analyzing-srum/),
  [TROOPERS19](https://troopers.de/downloads/troopers19/TROOPERS19_AD_Beyond_Windows_Forensics.pdf)). Trebuie
  verificate pe stații de laborator înainte de a fi scrise în explicațiile pentru controlori.
