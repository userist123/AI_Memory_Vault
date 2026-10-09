# WP16a — Cercetare urmărire tipărire (decizia 26)

Data: 2026-10-08. Tip: cercetare, fără cod. Context: `CONTRACT_AUDIT_STAGE1.md` §8 decizia 26.
Reguli: fiecare afirmație are sursă (URL) sau este marcată **neverificat**. Starea „verificat" înseamnă că textul a fost citit direct din sursa citată în această sesiune. „Sursă terță" înseamnă articol de producător/blog, nu documentație Microsoft. Eșantioanele locale sunt în scratchpad (`print_samples/`), nu în depozit.

Ediții: **P1** (clasificat, izolat, fără cod de rețea, nu schimbă setări ale gazdei), **P2** (LAN izolat, rețea permisă), **P3** (internet).

---

## A. Jurnalele de evenimente Windows pentru tipărire

### A.1 Canale și activare
| Fapt | Stare | Sursă |
|---|---|---|
| `Microsoft-Windows-PrintService/Operational` este **dezactivat implicit**; istoricul începe doar de la activare. | sursă terță (concordantă în 4 surse) | https://www.ninjaone.com/blog/enable-or-disable-event-viewer-printer-logs/ ; https://winaero.com/enable-print-logging-in-event-viewer-in-windows-10/ |
| Activare: `wevtutil sl Microsoft-Windows-PrintService/Operational /e:true` (nu `wmic`). | verificat (sintaxa `sl /e`) | https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/wevtutil |
| Dimensiune: `wevtutil sl <canal> /ms:<octeți>` (minim 1048576, multiplu de 64 KB); retenție `/rt:false` = suprascrie cele mai vechi, `/rt:true` = elimină evenimentele noi când e plin; `/ab` autobackup. Configurare și prin fișier XML `sl /c:config.xml`. | verificat | același URL wevtutil |
| Dimensiunea implicită a jurnalului Operational este 1 MB. | sursă terță | https://winaero.com/enable-print-logging-in-event-viewer-in-windows-10/ (via căutare) |
| Fișier: `C:\Windows\System32\Winevt\Logs\Microsoft-Windows-PrintService%4Operational.evtx`. | sursă terță | ninjaone (de mai sus) |
| Politica „Allow job name in event logs" (Computer Configuration > Administrative Templates > Printers), valoare `ShowJobTitleInEventLogs`=1 sub `HKLM\Software\Policies\Microsoft\Windows NT\Printers`. Fără ea numele documentului apare ca „Print Document". Nu cere repornirea spooler-ului. | sursă terță; calea exactă a cheii **neverificat** (o sursă dă și varianta Wow6432Node) | https://www.eventsentry.com/kb/491-print-tracking-in-the-web-reports-shows-either-print-document-or-an-incorrect-document-name |
| Activare la scară: **nu există** o setare ADMX dedicată „activează canalul"; variante: (1) script de pornire GPO cu `wevtutil`; (2) GPP Registry care setează `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WINEVT\Channels\Microsoft-Windows-PrintService/Operational` valoarea `Enabled`=1. | varianta 2 **neverificat** pe un sistem real (calea cheii și numele valorii confirmate doar indirect, prin regula Sigma care urmărește dezactivarea) | https://detection.fyi/sigmahq/sigma/windows/registry/registry_set/registry_set_disable_winevt_logging/ |
| Windows Server 2012/2012 R2 pot cere actualizări (KB2919355/KB2934016) — în afara perimetrului suportat (Server 2016+). | sursă terță, irelevant | eventsentry (de mai sus) |

Decizia 13/26: aplicația **nu** activează jurnalul. Raportează „jurnal de tipărire dezactivat" când canalul lipsește sau are `Enabled=0`, citind starea doar în citire (fișier EVTX / API de evenimente), fără a modifica gazda. În P1 se importă un `.evtx` exportat (`wevtutil epl`).

### A.2 Evenimente relevante (Operational, dacă nu e specificat altfel)
Ciclul normal al unui job: 800 → (308/309 dacă e oprit și reluat) → 801 → 805 → 842 → 307 (sursă terță: 7–8 evenimente/job; https://www.papercut.com/kb/Main/LogPrintJobsInEventViewer/ — pagina nu a putut fi citită integral, rezumat prin căutare).

| ID | Semnificație | Ce dovedește | Ce NU dovedește | Câmpuri | Stare |
|---|---|---|---|---|---|
| **307** | „Document N, <nume> owned by <user> on <mașină> was printed on <imprimantă> through port <port>"; include dimensiune și pagini. | Spooler-ul a terminat trimiterea jobului către port/monitor de port. Structura sursă `RPC_BranchOfficeJobDataPrinted` are: `Status` (cod eroare implementare), `pDocumentName`, `pUserName`, `pMachineName`, `pPrinterName`, `pPortName`, `Size` (octeți), `TotalPages`. | **Nu** dovedește hârtie ieșită: „printed" înseamnă predat portului. Fără hârtie, cu imprimantă offline/oprită, sau port `nul`/fișier, evenimentul apare. `TotalPages` este numărul de pagini ale documentului, nu al celor ieșite. | `Param1`=id job, `Param3`=proprietar, `Param4`=mașină, `Param5`=imprimantă, `Param6`=port, `Param8`=pagini (numerotare din sursă terță; ordinea structurii din MS-RPRN este verificată) | structura **verificat**: https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-rprn/b4a22048-7837-460e-b1bd-8d34f4270f29 ; numerotarea Param: https://eventlogxp.com/blog/?p=394 |
| **805** | Randare job (`RenderJobDiag`). | Setările de randare: `Size`, `ICMMethod`, `Color`, `PrintQuality`, `YResolution`, `Copies`, `TTOption`. | `Color=2` descrie setarea jobului, nu ieșirea fizică; apare și pe imprimante monocrome. | câmpurile din structura `RPC_BranchOfficeJobDataRendered` | **verificat**: https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-rprn/e90ee32f-12f4-4310-9238-71aca7dd9cb1 ; caveat Color: https://learn.microsoft.com/en-au/answers/questions/1608906/print-server-log-is-indicating-the-print-job-was-c |
| **800** | Diagnostic: începutul spool-ării jobului. | Jobul a fost creat în coadă. | Nimic despre imprimare. | id job, imprimantă, utilizator (**neverificat** câmpurile exacte) | sursă terță (PaperCut) |
| **801** | Diagnostic: etapa de tipărire. | Spooler-ul a început procesarea pentru imprimare. | idem | **neverificat** | sursă terță |
| **842** | Print processor și mod de izolare driver; cod Win32 de la print processor. | Dacă procesorul a returnat 0x0, nu a raportat eroare. | Nu dovedește că imprimanta a primit date. | **neverificat** | sursă terță |
| **308** | Job în pauză (analiză/oprit); nu se tipărește până la reluare. | Starea PAUSED. | — | **neverificat** | sursă terță (PaperCut) |
| **309** | Job reluat. | Ieșire din PAUSED. | — | **neverificat** | sursă terță |
| **310** | Document șters (sursă terță) / „document eșuat" (altă sursă) — **sursele diferă**. | Nu se poate folosi fără verificare pe eșantion real. | — | **neverificat** | conflict: https://insiderthreatmatrix.org/detections/DT007 vs PaperCut |
| **372** | Eroare de job în Operational (exemplu HP: „failed to print"); în Admin: „spooler failed to delete the file". | Indiciu de eșec, semnificația depinde de canal. | — | **neverificat** | https://h30434.www3.hp.com/t5/Printers-Archive-Read-Only/Event-ID-372-Print-Service-message-while-printing-on-Windows/td-p/2909873 |
| **812** | Spooler-ul nu a putut șterge fișierul SHD; PaperCut: inofensiv. | Nimic despre rezultatul jobului. | — | **neverificat** | https://learn.microsoft.com/en-us/archive/msdn-technet-forums/960c0a6b-a6de-44b7-93b6-5fd3d5410c98 (titlu) |
| 354, 808, 316, 300, 301, 823, 848 | Evenimente Admin/Operational de instalare driver/port (apar în eșantioanele PrintNightmare). | Relevante pentru securitate (instalare driver), nu pentru ciclul jobului. | — | existența în canale **verificat** pe eșantioane (secțiunea E); semnificația **neverificat** | eșantioane Hayabusa |

Secvență observată pe un job care „nu a ieșit": 800, 308, 309, 801, 307 (fișier 0 octeți), 310 — o postare de forum; **neverificat** ca model general.

Concluzie operațională: **nu există în Windows nicio dovadă de hârtie**. Cel mai înalt nivel obținut doar din Windows este SENT_TO_PRINTER (307). Starea PRINTED_CONFIRMED cere dovadă de pe dispozitiv (secțiunea C).

### A.3 Server de tipărire vs stație
- Pe un **server de tipărire** cu cozi partajate, 307 este înregistrat de spooler-ul serverului pentru toate joburile primite (utilizatorul = proprietarul jobului, mașina = clientul).
- Pe **stații** cu imprimantă locală/IP direct, evenimentele sunt în jurnalul stației. Pentru imprimare prin server cu randare pe client, mecanismul „Branch Office Print Remote Log" (MS-RPRN §3.1.1) transferă intrările de log la server; comportamentul exact (care mașină înregistrează ce) este **neverificat** în această sesiune. Referință: https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-rprn/b4a22048-7837-460e-b1bd-8d34f4270f29
- Consecință pentru aplicație: deduplicare după (id job, imprimantă, mașină sursă, timp) când se importă EVTX de la server și de la stație.

---

## B. Fișiere spool

### B.1 „Keep printed documents"
| Fapt | Stare | Sursă |
|---|---|---|
| Atribut per imprimantă `PRINTER_ATTRIBUTE_KEEPPRINTEDJOBS`: „jobs are kept after they are printed. If unset, jobs are deleted". Câmpul `Attributes` din `PRINTER_INFO_2`. Valoarea numerică (0x100) **neverificat** în această sesiune (tabelul citit nu o afișează). | verificat (semnificația) | https://learn.microsoft.com/en-us/windows/win32/printdocs/printer-info-2 |
| PowerShell: `Set-Printer -Name <p> -KeepPrintedJobs $true` (modulul PrintManagement; cere administrator). | verificat | https://github.com/MicrosoftDocs/windows-powershell-docs/blob/main/docset/winserver2012r2-ps/printmanagement/Set-Printer.md |
| La scară: GPO/GPP nu are o setare ADMX dedicată pentru acest atribut (**neverificat**); abordări: script PowerShell de pornire (`Set-Printer`), sau GPP Printers/Registry pe cheia imprimantei în `HKLM\SYSTEM\CurrentControlSet\Control\Print\Printers\<nume>` valoarea `Attributes` (OR cu bitul KEEPPRINTEDJOBS) — **neverificat**, risc de a strica alte atribute. Se recomandă script cu `Set-Printer`. | neverificat | — |
| Pe un server de tipărire, setarea se face pe coada serverului, nu pe clienți. | raționament, neverificat | — |
| Din GUI: Proprietăți imprimantă > Advanced > „Keep printed documents". | sursă terță | https://www.ninjaone.com/blog/keep-printed-documents-in-windows-11-print-queue/ |

Consecințe: (1) aplicația nu setează atributul (decizia 13/26); poate doar detecta, dintr-un export de configurație, că atributul lipsește și raporta „conținut tipărit indisponibil — «Keep printed documents» dezactivat". (2) Joburile păstrate rămân vizibile în coadă până sunt șterse; politica de ștergere/rotire a fișierelor este **neverificat** (nu există documentație Microsoft găsită); trebuie testat pe un sistem real (reținere la repornirea spooler-ului, limită de număr). (3) Conținut sensibil: fișierul spool conține exact ce s-a tipărit, inclusiv clasificat; aplicația îl copiază doar în cazul criptat (decizia 26a).

### B.2 Locație și structură
- Director implicit: `%SystemRoot%\System32\spool\PRINTERS` (sursă terță: https://www.forensicfocus.com/forums/general/printing-spool-shadow-files-what-else ; schimbabil prin `DefaultSpoolDirectory`, **neverificat**).
- Pentru fiecare job: `*.SPL` (datele) și `*.SHD` (umbra: utilizator, nume document, imprimantă, port, driver, DEVMODE, procesor, datatype). Pentru EMF, pot exista și fișiere per pagină `.EMF`/`FP*.SPL` (sursă terță, forensicfocus). Denumirea cu 5 cifre a ID-ului jobului: **neverificat** documentat.
- Fișierele se șterg după imprimare dacă atributul nu e setat (sursă terță, forensicfocus).

**Antet SHD** — nu există specificație Microsoft. Cea mai bună sursă găsită este șablonul WinHex al lui Costas Katsavounidis (licență necunoscută; referă articol CodeProject și undocprint.org). Layout descris (little-endian, offseturi la șiruri UTF-16LE terminate cu nul):
- semnătură 4 octeți, variază pe versiune (Windows 10: citită ca `0x23510000`; Win2003 `0x68490000`; Win2000/XP `0x67490000`; NT `0x66490000`; Win98 `0x4B490000`);
- dimensiune antet; flag-uri de stare; **ID job**; prioritate;
- offseturi pentru: **Username**, NotifyName, **DocumentName**, PrinterPort, PrinterName, DriverName, DEVMODE, PrintProcessorName, **DataType**;
- `SYSTEMTIME` (an, lună, zi-săptămână, zi, oră (UTC), minut, secundă, ms) — momentul trimiterii;
- dimensiune SPL (octeți) și **număr de pagini**.
Surse: https://github.com/kacos2000/WinHex_Templates/blob/master/SHD%20spool%20shadow%20file.tpl ; context: https://i.blackhat.com/USA-20/Thursday/us-20-Hadar-A-Decade-After-Stuxnet-Printer-Vulnerability-Printing-Is-Still-The-Stairway-To-Heaven-wp.pdf (SHD citit ca structură serializată `INIJOB`, nedocumentată). **Neverificat** pe fișiere reale Win11/Server 2025; implementarea trebuie să fie defensivă (verificare offseturi în limitele fișierului, șiruri limitate, timeout), fiindcă SHD-ul este intrare ostilă (Black Hat: offseturile contează pentru parser).

### B.3 Datatype-uri și drivere
- **EMF** (`NT EMF 1.00x`): instrucțiuni GDI; procesorul de tipărire le redă prin driver (care produce RAW). Joburile locale pe server sunt spool-ate ca EMF; clienții NT trimit EMF la servere NT. https://learn.microsoft.com/en-us/windows-hardware/drivers/print/emf-data-type — **verificat**. Formatul fișierului EMF-spool: specificație deschisă MS-EMFSPOOL (înregistrări: antet, pagini, fonturi, offset-uri de pagină): https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-emfspool/ed79f9d8-31fb-46cb-950e-ea7682f50c70
- **RAW**: date deja în limbajul imprimantei (PCL, PostScript, PJL, ESC/P…). Variantele `RAW [FF appended]` / `RAW [FF auto]` și `XPS_PASS`: numele datatype-urilor print processor-ului `winprint` — pagina Microsoft pentru winprint nu a putut fi citită (404) → **neverificat** aici.
- **XPS**: driverele XPSDrv (v3) și **v4** folosesc XPS ca format de spool; v4 „continues to support XPSDrv, GPD, PPD": https://learn.microsoft.com/en-us/windows-hardware/drivers/print/v4-printer-driver ; Microsoft recomandă acum driverul clasă IPP inbox + PSA pentru Windows 10/11 (aceeași pagină). Pentru driverul IPP clasă, spool-ul este intermediar (XPS) iar filtrul de randare produce de obicei PWG Raster (discuție PWG, nu documentație Microsoft): https://pwg.org/archives/ipp/2024/021562.html — **neverificat**.
- Implicație: cu drivere v3 PCL6/PS pe Windows, spool-ul păstrat este de regulă **EMF** (decodabil), nu RAW; RAW apare la „Print directly", cozi RAW-only, clienți non-Windows (CUPS/SMB) sau aplicații care scriu RAW. Raționamentul pornește din documentul EMF de mai sus; frecvența reală este **neverificat**.

### B.4 Detectarea limbajului într-un spool RAW (secvențe cunoscute din manualele HP)
| Marcaj | Limbaj |
|---|---|
| `ESC %-12345X` (UEL) urmat de `@PJL ...` | înveliș PJL (poate conține `@PJL ENTER LANGUAGE=PCL / POSTSCRIPT / PCLXL`) |
| `ESC E` (reset) / secvențe `ESC &l`, `ESC (`, `ESC *` | PCL5 |
| linia `) HP-PCL XL;<major>;<minor>;...` (antet binding PCL XL) | PCL XL (PCL 6 Enhanced) |
| `%!PS-Adobe-...` sau `%!` | PostScript |
| început `0x01000000` (EMR_HEADER) + ` EMF` la offset 40 | EMF simplu (rar în RAW) |
| `PK\x03\x04` | pachet XPS/OPC (spool XPS) |
Sursă pentru PCL/PJL: manualele de referință folosite de pclbox: https://github.com/michaelknigge/pclbox (README) și PCL XL: https://pic.hallikainen.org/techref/language/pcl/6/xl_refsup30r089.pdf (supliment, marcat „HP Confidential", statut de distribuire neclar). Tabelul de mai sus este reconstituit din cunoștințe generale + aceste surse; octeții exacți trebuie validați pe eșantioane (E) — **parțial neverificat**.

### B.5 Conversie în .NET, in-process, doar licențe permisive
| Format | Opțiune | Licență | Verdict |
|---|---|---|---|
| EMF | `System.Drawing` (GDI+ `Metafile`, redare pe `Graphics`) — Windows-only în .NET 6+, parte din runtime Windows Desktop | MIT (dotnet/runtime) — **neverificat** în sesiune | redare vizuală: DA. Nu extrage text direct. |
| EMF | Parser propriu al înregistrărilor EMF conform MS-EMF (open spec); text din `EMR_EXTTEXTOUTW` (și `EMR_POLYTEXTOUTW`), poziții, fonturi | cod propriu | **recomandat**: extragere text exactă, independent de platformă. Atenție: textul poate fi glife/indecși dacă fontul e subset (ETO_GLYPH_INDEX) → text indisponibil, atunci rămâne imaginea. |
| EMF spool | parser propriu MS-EMFSPOOL (despachetare pagini EMF din SPL) | cod propriu | necesar înaintea EMF |
| XPS | `System.IO.Packaging` (OPC) + parsare `FixedPage`/`Glyphs@UnicodeString` pentru text; `System.Windows.Xps.Packaging` (WPF) pentru redare | .NET/WPF sunt MIT (dotnet/wpf) — **neverificat** în sesiune | text: simplu și permisiv. Redare: doar Windows/WPF. |
| XPS → PDF | nu există bibliotecă .NET permisivă găsită. Variante: (a) redare XPS la bitmap prin WPF + asamblare PDF cu PDFsharp; (b) text invizibil + imagine. Comercial: Aspose/Syncfusion etc. (nu permisiv). | — | (a) fezabil, fără interpreter terț. |
| PDF (generare) | **PDFsharp**, MIT: https://raw.githubusercontent.com/empira/PDFsharp/master/LICENSE (citit: „MIT License", © empira 2001–2026) | MIT | folosit pentru ieșire PDF din imagini + text. |
| PDF (citire/verificare) | **PdfPig** (UglyToad), Apache-2.0 (LICENSE citit) | Apache-2.0 | verificare/extragere text din PDF-urile generate; opțional. |
| PDF (generare) | QuestPDF: licență duală („Community"/comercială, condiționată de venit) | NU permisiv necondiționat | de evitat fără aprobare. |
| PDF | iText 7/8 | AGPL/comercial | exclus. |
| PCL5 | pclbox (Java, Apache-2.0; parser, fără redare, PCL până la v5 + PJL + HP-GL/2; fără dependențe) https://github.com/michaelknigge/pclbox | Apache-2.0 | doar ca **referință/portare**; nu e .NET. |
| PCL5 | sigram/pcl-parser (Java, Apache-2.0; „parser and renderer", scopul inițial: extragere text; ultimul commit 2011) https://github.com/sigram/pcl-parser | Apache-2.0 | referință; veche. |
| PCL5 + PCL XL | **PCL Paraphernalia** (C#/.NET 4.x, WPF) conține analizor `PrnParse*` pentru PCL, PCL XL, HP-GL/2, PJL; LICENȚĂ: Unlicense (domeniu public, text citit) https://github.com/michaelknigge/pclparaphernalia ; ultimul commit 2023-12-20 | public domain | **cea mai utilă bază**: tabele de operatori/atribute PCL XL (`PCLXLOperators`, `PCLXLAttributes`) pot fi portate. Este analizor de protocol, nu randor; nu extrage text gata făcut (**neverificat** capacitatea de extragere text). |
| PCL XL | specificație HP „PCL XL Feature Reference Protocol Class 3.0" — document oficial greu de găsit; supliment 0.90 pe site terț | — | spec necesară pentru implementare proprie. |
| PostScript | **niciun interpretor PostScript permisiv de nivel producție găsit** (căutare în sesiune). Ghostscript: AGPL sau licență comercială Artifex https://ghostscript.com/faq ; GhostPCL și GhostXPS: AGPL/comercial https://ghostscript.com/download/gxpsdnld.html ; MuPDF: AGPL/comercial https://mupdf.readthedocs.io/en/1.27.0/license.html . Fonturile URW din GhostPCL au licență separată cu restricții (sursa căutării). | AGPL | **Nu există interpretor PCL/PS/PCL XL permisiv utilizabil direct.** |

Spus clar: **nu există** un interpretor PCL XL sau PostScript cu licență MIT/BSD/Apache/MS-PL pe care să-l putem integra în .NET (verificat prin căutări, nu dovedit negativ cu certitudine). Singurele componente permisive sunt **parsere** PCL5 (Java, Apache-2.0) și analizorul C# PCL Paraphernalia (public domain), nu motoare de randare.

**Rezerva: extragere de text prin euristici** (fără randare):
- **PCL5**: text în clar între secvențe `ESC`; filtrare secvențe `ESC <param>` (de ex. `ESC &a#R/C` pozițiere cursor, `ESC (…` set de simboluri). Pentru text simplu (jobul din Notepad/ERP în mod text) fezabil; pentru PCL5 generat de drivere (text ca fonturi descărcate, bitmap) **nefezabil**. Fezabilitate: medie; pierderea ordinii pe pagină.
- **PCL XL**: operatorul `Text`/`TextPath` poartă șiruri de glife cu indecși de font (de regulă fonturi descărcate cu mapare proprie) → text cu valoare incertă; imaginile sunt rastere comprimate (RLE/JPEG/DeltaRow). Fezabilitate: scăzută fără tabele de font.
- **PostScript**: text în operatorii `show/ashow/xshow` și variantele; cu fonturi Type 3/Type 42 codificate și compresie (`/FlateDecode`, `ASCII85`) → doar parțial. Fezabilitate: scăzută–medie.
Toate aceste euristici trebuie raportate ca „conținut parțial/nedecodabil fără interpretor", conform deciziei 26a, cu marcaj de încredere; nu se afirmă „ce s-a tipărit" fără o redare completă.

---

## C. Confirmarea ieșirii de hârtie

Principiu: Windows nu poate dovedi hârtia (A.2, 307). Dovada vine **doar de pe dispozitiv**. Trei familii de surse, plus intrare manuală.

### C.1 SNMP — Printer MIB (RFC 3805) și Job Monitoring MIB (RFC 2707)
Texte RFC citite integral local (rfc-editor.org): https://www.rfc-editor.org/rfc/rfc3805.txt , https://www.rfc-editor.org/rfc/rfc2707.txt

| Obiect | Fapt verificat | Valoare pentru aplicație |
|---|---|---|
| `prtMarkerLifeCount` (`prtMarkerEntry` 4; OID `1.3.6.1.2.1.43.10.2.1.4.<hrDeviceIndex>.<markerIndex>`; `printmib`=`mib-2 43`, `prtMarker`=`printmib 10`, `prtMarkerTable`=`prtMarker 2`) | `Counter32`, „numărul de unități de măsură numărate în viața imprimantei", unitatea dată de `prtMarkerCounterUnit` (include `impressions(7)`); „ar trebui implementat ca obiect persistent". | **Contor de pagini**: două citiri (înainte/după) arată dacă dispozitivul a marcat pagini. Dovedește că *s-au marcat pagini* într-un interval, nu că *aceste* pagini aparțin jobului X (alte joburi, copii, fax pot incrementa). Corelație probabilistică. |
| `prtAlertTable` (`prtAlert`=`printmib 18`) | alertele de dispozitiv (hârtie blocată, fără hârtie, capac deschis…). | Explică „nimic ieșit": jam/out-of-paper în intervalul jobului. Alertele sunt actuale/recente, nu arhivă garantată (**neverificat** retenția). |
| `hrPrinterStatus` (Host Resources MIB, RFC 2790) | nu a fost citit în sesiune | stare generală (idle/printing/warmup); **neverificat**. |
| Job Monitoring MIB: `jmJobState` (stări pending/processing/completed/canceled/aborted; „valoarea finală trebuie să fie completed, canceled sau aborted"; durata de păstrare = `jmGeneralJobPersistence`) | verificat în RFC 2707 | stare de job la dispozitiv, **dacă dispozitivul implementează MIB-ul**. |
| `jmJobImpressionsCompleted` | „impresii terminate până acum; pentru dispozitive de tipărire include interpretare, marcare și stivuire" | dovadă puternică de hârtie ieșită per job. |
| Susținere de către producători | **neverificat**: nu s-a găsit în sesiune o listă oficială de modele care implementează Job MIB; în practică implementarea e inegală (afirmație nedovedită). HP documentează că Web Jetadmin colectează din „Job Information Table" a dispozitivului și că datele de utilizare pe utilizator „nu înlocuiesc contabilizarea" (https://kaas.hpcloud.hp.com/pdf-public/pdf_6881691_en-US-1.pdf — rezumat din căutare). |

Disponibilitate în rețea: SNMP rulează pe UDP/161 către imprimantă — necesită rețea între stație și dispozitiv. **P1: cod de rețea absent → interzis.** P2 (LAN izolat): permis, ca interogare *opțională* declarată în politică semnată; SNMPv3 cu autentificare/criptare recomandat (SNMPv1/v2c „community" = text clar; recomandare, nu citare). P3: permis.
Caveat de securitate: orice interogare de rețea modifică amprenta dispozitivului în jurnalul lui; documentat în politică.

### C.2 IPP (RFC 8011)
Verificat în RFC 8011 (https://www.rfc-editor.org/rfc/rfc8011.txt):
- `Get-Job-Attributes` = cod operație `0x0009`; `Get-Jobs` = `0x000a`; `Get-Printer-Attributes` = `0x000b`.
- `job-state` (REQUIRED): pending, pending-held, processing, processing-stopped, canceled, aborted, completed; „starea finală trebuie să fie completed/canceled/aborted înainte ca imprimanta să elimine jobul"; „completed" se atinge „după ce toate activitățile s-au terminat, **inclusiv stivuirea mediei de ieșire**" (§5.3.7).
- `job-impressions-completed` (RECOMMENDED): „include interpretarea, marcarea și stivuirea ieșirii"; `job-media-sheets-completed` (RECOMMENDED): foi marcate și stivuite.
- `Get-Jobs` cu `which-jobs=completed` returnează joburile terminate/anulate/abandonate **doar dacă** imprimanta le reține; „dacă implementarea nu păstrează joburi completed… întoarce niciunul" (§4.2.6) — deci istoricul poate lipsi.
- Valoare probatorie: `completed` + `job-impressions-completed ≥ 1` + `job-media-sheets-completed` = cea mai puternică dovadă via rețea. `job-state-reasons` (ex. `job-completed-successfully`, `job-completed-with-errors`) rafinează rezultatul (valori exacte din RFC 8011 §5.3.8 — **neverificat** în sesiune).
- Suport de producători: nu s-a verificat model cu model. Joburile create de Windows prin port TCP/IP standard (RAW 9100) **nu** sunt joburi IPP la dispozitiv; `Get-Job-Attributes` funcționează numai pentru joburi trimise prin IPP sau dacă imprimanta expune joburile tuturor canalelor (**neverificat**). Pentru joburi RAW pe 9100 rămân SNMP/contor/jurnal vendor.
- Rețea: IPP = TCP/631 (HTTP/HTTPS) → **P2/P3 doar**. Nu pentru P1.

### C.3 Jurnale de job ale dispozitivului (vezi secțiunea D)
Singura cale **offline** reală pentru P1: operatorul exportă jurnalul din interfața web a dispozitivului (sau de pe USB/panou) pe un mediu autorizat și îl importă în aplicație. Jurnalul poate confirma pagini imprimate per job/utilizator/nume fișier (Ricoh, Canon, Sharp, Konica…). Mapare pe stări: vezi C.5.

### C.4 Introducere manuală (P1 fără export)
Operatorul introduce două valori de contor de pagini (de pe panou/pagină de configurare) cu marcaje de timp, plus „nimic ieșit" ca observație. Aplicația le stochează ca dovadă de tip `OPERATOR_ATTESTED` (nu `DEVICE_CONFIRMED`), cu cine/când. Este dovadă slabă, dar auditabilă.

### C.5 Propunere de model de stare
Stări (job de tipărire): `SUBMITTED, SPOOLED, SENT_TO_PRINTER, PRINTING, FAILED, CANCELLED/DELETED, PAUSED, PRINTED_CONFIRMED, NOT_CONFIRMED`.
Reguli: (1) stările sunt rezultate de analiză, **niciodată erori**; (2) `PRINTED_CONFIRMED` numai din dovadă de dispozitiv; (3) lipsa dovezii = `NOT_CONFIRMED` (inclusiv „nimic ieșit din imprimantă" ca rezultat de primă clasă, când există dovadă negativă: alertă jam/out-of-paper, contor neschimbat, jurnal dispozitiv fără job); (4) `NOT_CONFIRMED` ≠ `FAILED`.

| Sursă / eveniment | Stare rezultată | Notă |
|---|---|---|
| Windows 800 (job început) | SUBMITTED / SPOOLED | 800 = spool-are (sursă terță) |
| Fișier SPL/SHD prezent | SPOOLED | SHD dă user/document/datatype/pagini |
| Windows 308 (pauză) | PAUSED | neverificat |
| Windows 309 | revine la SPOOLED | neverificat |
| Windows 801/805/842 (cod 0x0) | PRINTING (în curs de procesare) | diagnostice |
| Windows 307 | SENT_TO_PRINTER | **limita maximă din Windows** |
| Windows 372 / 842 cu cod ≠ 0 / 307 cu `Status` ≠ 0 | FAILED (cu motiv) | 372/842: neverificat |
| Windows 310 (dacă se confirmă „șters") sau job absent din coadă fără 307 | CANCELLED/DELETED | 310 contradictoriu între surse |
| SNMP `prtMarkerLifeCount` crește în fereastra jobului, fără alt job activ | PRINTED_CONFIRMED cu încredere *medie* („contor") | etichetă separată: `PAGE_COUNTER_DELTA` |
| SNMP `prtAlert` jam / fără hârtie / capac în fereastră | NOT_CONFIRMED + motiv „nimic ieșit probabil" | dovadă negativă |
| `jmJobState=completed` + `jmJobImpressionsCompleted>0` | PRINTED_CONFIRMED (dispozitiv) | doar dacă MIB implementat |
| `jmJobState` canceled/aborted | CANCELLED sau FAILED (după `jmJobStateReasons`) | |
| IPP `job-state=completed` + `job-impressions-completed>0` | PRINTED_CONFIRMED (dispozitiv) | |
| IPP `job-state=aborted` | FAILED | |
| IPP `job-state=canceled` | CANCELLED | |
| IPP `processing` / `processing-stopped` / `pending-held` | PRINTING / PAUSED | |
| Jurnal vendor import (Ricoh/Canon/Sharp…): rând job, rezultat OK, nume fișier/utilizator potrivite, pagini>0 | PRINTED_CONFIRMED (dispozitiv, offline) | corelare după utilizator + nume document + timp ±toleranță |
| Jurnal vendor rezultat NG/eroare | FAILED | |
| Contoare introduse manual | PRINTED_CONFIRMED *operator* sau NOT_CONFIRMED | nivel de încredere separat |
| Nicio dovadă de dispozitiv | NOT_CONFIRMED (după 307) | |

Câmp suplimentar obligatoriu: `evidence_level` ∈ {WINDOWS_ONLY, PAGE_COUNTER_DELTA, OPERATOR_ATTESTED, DEVICE_JOB_LOG, DEVICE_PROTOCOL(IPP/JMP)} — pentru a nu se amesteca dovada slabă cu cea tare (în linie cu invariantele anti-overclaim din WP2).

### C.6 Ce merge în fiecare ediție
| Mecanism | P1 | P2 | P3 |
|---|---|---|---|
| EVTX import (offline) | da | da | da |
| SPL/SHD import (copiere offline) | da | da | da |
| Export jurnal dispozitiv + import | **da (singura dovadă de dispozitiv)** | da | da |
| Contor introdus manual | da | da | da |
| SNMP poll | **nu** (fără cod de rețea) | opțional, politică semnată | opțional |
| IPP Get-Job-Attributes | **nu** | opțional | opțional |

---

## D. Jurnale MFP (scanare/copiere/fax/tipărire)

Notă generală: formatele publice sunt rare. Numai Ricoh, Canon (parțial) și Sharp au câmpuri documentate pe site-ul public în sesiune; HP, Xerox, Kyocera, Lexmark, Epson: **câmpurile exacte nu sunt documentate public** (de verificat cu eșantion exportat de pe dispozitiv; proprietarul a spus că va furniza eșantioane — decizia 26f). Un parser trebuie să fie **bazat pe antet** (citește rândul de antet, nu poziții fixe) și versionat pe model/firmware.

| Producător | Metodă de export | Câmpuri documentate | Fișier / utilizator / pagini / destinație | Documentat public? | Sursă |
|---|---|---|---|---|---|
| **Ricoh** | Web Image Monitor > Configuration > Logs > descărcare CSV (Job Log, Access Log, sau combinat); sau server de colectare/Streamline NX. UTF-8 sau JIS, antet pe primul rând, sortat după Log ID. Nume: `<Machine>_joblog.csv`, `_accesslog.csv`, `_log.csv`, `_ecolog.csv`. | Comune: Start/End Date/Time, Log Type, Result, Operation Method, Status, User Entry ID, User Code/User Name, Log ID. Intrare (Source): Source, Start/End, Stored File, Stored File Name, Folder Number/Name, Print File Name. Ieșire (Target): Target, Start/End, **Destination Name**, **Destination Address**, Stored File ID/Name, Folder. Rânduri multiple per job pentru surse/ținte multiple. | nume fișier: da (Stored File Name / Print File Name); utilizator: da; **pagini: nu apare coloană** în tabelul citit; destinație scan-to-email/folder: da (Destination Name/Address). Tipuri: copiere, tipărire, scanare, fax, rapoarte. | **da** | https://support.ricoh.com/services/device/ccmanual/IM550/en-GB/setting/int/logfiles.htm ; https://support.ricoh.com/services/device/ccmanual/IM550/en-GB/setting/int/loglist.htm |
| **Canon** imageRUNNER ADVANCE | Remote UI > Settings/Registration > Device Management > Export/Clear Audit Log > Export (CSV; admin; max 20 000 intrări; înlocuiește cele mai vechi); tip log 1001/8193 = Job. Separat: Status Monitor > Job Log > „Store in CSV Format" (ultimele 100 joburi; `tx.csv`/`rx.csv` pentru fax). | Dată/oră, nume utilizator, tip operație, rezultat (OK/NG); pentru Job: tip job (copy, fax, scan, send, print). Lista completă a coloanelor: **neverificat**. | utilizator da; fișier/pagini/destinație: **neverificat** (manualul rezumat nu le listează) | parțial | https://oip.manual.canon/USRMA-0099-zz-CS-enUS/contents/1T0002196156.html ; https://oip.manual.canon/USRMA-0099-zz-CS-enUS/contents/1T0002196126.html |
| **Konica Minolta** bizhub | Web Connection (admin) > Security > Job Log Settings > Job Log Usage Set = ON (implicit OFF; efect după repornire); apoi Maintenance > Job Log > Create Job Log → descărcare pe PC sau SMB (mod manual = **XML**; mod auto = syslog). Jurnalul nedescărcat se pierde la crearea unuia nou. | Manualul: „utilizare, consum hârtie, operațiuni, istoric joburi per utilizator/cont"; **schema XML nu e descrisă** — „contactați reprezentantul de service". | **neverificat** | **nu** (schema) | https://manuals.konicaminolta.eu/bizhub-451i/EN/contents/WC_12_03_06.html ; https://manuals.konicaminolta.eu/bizhub-650i-550i-450i-UD/EN/contents/id08-_104679563.html |
| **Sharp** MX/BP | Pagina web a dispozitivului: Job Log (selectare perioadă, Show, salvare, ștergere); Audit Log separat (BP-1360M: export, ghid de referință). Câmpuri contabilitate/OSA: Job ID, Account Job ID, Job Mode, Computer Name, User Name, Login Name, Card ID, Main/Sub Code, Starting/Completing Date-Time, contoare pagini (color/mono, pe format de hârtie). | da (job log); audit: dată, oră, ID eveniment, utilizator, descriere (syslog severitate fixă 6) | utilizator da; nume fișier **neverificat**; pagini da; destinație **neverificat**. Export CSV al jurnalului de job: **neverificat** (CSV găsit doar pentru agendă/utilizatori). | parțial–da | https://global.sharp/restricted/print/manuals/5/bp70m65/en/contents_09-07_021.html ; https://business.sharpusa.com/portals/0/downloads/Manuals/BP-1360M_1250M-Audit-Log-Reference-Guide.pdf |
| **HP** (LaserJet Enterprise/FutureSmart) | EWS > Information > Job Log (poate fi ascuns; se activează la Security > EWS options „Display Job log on Information tab" — sursă comunitate). Contabilitate: „Serverless Job Accounting" (FutureSmart ≥ 4.6.1): EWS > Security > Accounting Methods > Usage History > **Export**; contorizează fețe copiate/tipărite/scanate per utilizator. Web Jetadmin: rapoarte HTML/CSV, dar nu e contabilitate (HP). | Job Log (comunitate): JobName, User, Status, Date. SJA: contoare, nu job-uri individuale. | nume job da (în Job Log); utilizator da; pagini/destinație: **neverificat** | **slab** (nu există listă oficială de câmpuri găsită) | https://support.hp.com/us-en/document/ish_7598494-7598479-16 ; https://support.hp.com/gb-en/document/c06529554 ; https://h30434.www3.hp.com/t5/LaserJet-Printing/Job-Log/m-p/6367268 |
| **Xerox** | Standard Accounting: Interfață web > Properties > Accounting > Report and Reset > Usage Report > Download (.csv), opțiune „Show User ID in Report" (contor pe utilizator, nu per job). VersaLink: System > Logs > export jurnal audit (`auditlog.txt`); jurnalul de debug/job este criptat, decriptabil doar de Xerox (Tungsten). | coloanele nu sunt documentate în sursele găsite | **neverificat** | **nu** | https://procurement.ufl.edu/wp-content/uploads/2021/08/AltaLink-Setup-Standard-Accounting.pdf ; https://docshield.tungstenautomation.com/ControlSuite/en_US/help/clients/DRS/XeroxUC/ControlSuite_XeroxUC/t_debuglogs.html |
| **Kyocera** | Command Center RX: istoric joburi/„Job Accounting"/„Job Box"; ghidul oficial CCRX există (PDF) dar secțiunea de jurnal nu a putut fi extrasă în sesiune. | **neverificat** | **neverificat** | de verificat în ghid | https://downloads.kyoceradocumentsolutions.com.au/Documentation/CommandCenterRX_EN_2020.pdf |
| **Lexmark** | EWS > Security > Security Audit Log: export (fișier) și syslog (UDP/514, Stunnel; severitate 0–7); „Job Accounting Statistics" activabil, jurnal oprit la 3 MB. | câmpurile exportului audit **neverificat** | **neverificat** | parțial (meniuri), nu câmpuri | https://support.lexmark.com/content/support/guides/en/v55522091/use-printer-menus/security/security-audit-log-v50415213.html ; https://support.lexmark.com/content/support/guides/en/kb20220203102749873/setup-installation-and-configuration-issues/how-to-enable-job-accounting-ho3221.html |
| **Brother** | „Store Print Log to Network": scrie pe un server CIFS un fișier TXT sau CSV cu ID, tip job, nume job, utilizator, dată, oră, pagini tipărite și color, pentru tipărire PC, USB direct, copiere. Necesită rețea către share (P2/P3), nu offline. | ID, tip job, nume job, nume utilizator, dată/oră, pagini, pagini color | fișier = nume job da; utilizator da; pagini da; destinație scanare: **neverificat** | parțial | https://download.brother.com/welcome/doc002572/cv_dcp8080n_eng_nug_log.pdf |
| **Epson** | Web Config exportă *configurația*, nu istoricul joburilor (FAQ Epson). Istoric: Epson Device Admin (**neverificat**). | — | — | **nu** | https://epson.com/faq/SPT_C31CD54011~faq-0000716-shared |

Observații: (1) exporturile de dispozitiv sunt **date de la sursă ostilă/necunoscută**: parser defensiv, limite de dimensiune, antet validat, fără execuție. (2) Fusul orar și ceasul dispozitivului se pot abate; aplicația stochează fusul declarat și o toleranță de corelare. (3) Syslog (Lexmark, Konica, Sharp) cere rețea → P2/P3; în P1 se folosește exportul de fișier.

---

## E. Date publice de test

Descărcate (doar în scratchpad, **nu** în depozit, tratate ca nesigure; nimic executat; citite doar cu `python3 -I`): `/tmp/claude-0/-home-user-AI-Memory-Vault-CODEX-READY/866eaee9-546d-505f-a536-6c7b061fb922/scratchpad/print_samples/`

| Fișier (subdirector) | SHA-256 | Dimensiune | Origine / licență | Note |
|---|---|---|---|---|
| `evtx_keep/ID354-808-Mimispool printer installation (PrintNightmare).evtx` | `02b442fdc39b768672c12213637f33e75edf7101c3bcff402283848e40f8e226` | 69 632 | Yamato-Security/hayabusa-sample-evtx (colectat din EVTX-to-MITRE-Attack, mdecrevoisier). Fără fișier LICENSE în depozitele citite → **licență necunoscută, nu se redistribuie**. | conține canalul `Microsoft-Windows-PrintService/Operational` (șir verificat); evenimente 354, 808 (instalare driver) — **nu** evenimente de job |
| `evtx_keep/ID316,300,301,316,823,848-Mimispool printer server instal.evtx` | `1d183b689bd85596f6eaefa220e5afcb18d7dd1090dc03d5e0c2ad8039641adf` | 69 632 | idem | canalul `Microsoft-Windows-PrintService/Admin`; evenimente 316, 300, 301, 823, 848 |
| `pcl_keep/fonts.pcl` | `421fc9f49d5c43cba3f8899fd785dea6b17f68916fea64bdf294fa37c47e753b` | 7 731 | ArtifexSoftware/ghostpdl `pcl/examples/` — AGPL-3.0 / comercial (LICENSE depozit: directoarele pcl/xps/examples fac parte din GPL Ghostscript) | PCL5, începe cu `ESC E` (verificat) |
| `pcl_keep/fills.pcl` | `433f0dc992d3884952dcb471d097091455ecefbb5e14f7b04e5556f9ebc05d5b` | 123 | idem | începe cu UEL `ESC %-12345X` (verificat) |
| `pcl_keep/fonts.pxl` | `24289143071e18cabd94916d65a97b5f13be999d18858a7fe051e2e5271d5407` | 6 009 | idem | PJL înveliș `@PJL SET DUPLEX=OFF…` (verificat) urmat de PCL XL |
| `ps_keep/alphabet.ps` | `0c726c53d1050db449bb0af85d1ad1472ed5473e672000a0dcd2907216cae8d3` | 1 966 | ghostpdl `examples/` — AGPL-3.0 | PostScript, începe cu `%!` |
| `xps_keep/colorcirc.xps` | `cbe7075c8d1e8b2030423150ab7a339fe41aa58766a43d18dbb2fdc7070555ee` | 50 588 | ghostpdl `xps/tools/` — AGPL-3.0 | arhivă zip (OPC) verificat |
| `xps_keep/tiger.xps` | `685143e809d9d14542289b41707a1d29b98242e6a3b6f0a396ca24d55fc3fd0a` | 62 775 | idem | arhivă zip verificat |

Repozitorii identificați, nedescărcați integral: `sbousseaden/EVTX-ATTACK-SAMPLES` (**GPL-3.0**, LICENSE citit; fișiere de tip spooler/PrintNightmare, fără evenimente 307), `Yamato-Security/hayabusa-sample-evtx` (agregă mai multe depozite; licență necunoscută). Nu s-au găsit: EVTX public cu evenimente 307/805/801 de job, fișiere `.SPL`/`.SHD` publice (există doar eșantioane în arhive de aplicații CodeProject, licență neclară: https://codeguru.com/?p=12938), exporturi de jurnal MFP publice.

**Recomandare teste**:
1. **EVTX de job**: construit sintetic din șabloanele de mesaj documentate (A.2). Generarea unui EVTX real cere o mașină Windows cu canalul activat; fixtures în XML (`wevtutil qe /f:xml`) sunt proprietatea proiectului. Verificarea parserului se face în plus pe cele două EVTX PrintService de mai sus (licență nesigură → rulate doar local, nu comise).
2. **SPL/SHD**: generate pe o VM de test cu „Keep printed documents" (procedură CodeGuru, sursă terță) și comise doar dacă conținutul este inofensiv; aplicația are nevoie de aceste fișiere reale pentru a valida layout-ul SHD (**neverificat**).
3. **PCL/PCL XL/PS/XPS**: PCL Paraphernalia (Unlicense) poate *genera* joburi PCL și PCL XL cu licență permisivă — mai curat decât eșantioanele AGPL. XPS: generate cu `XpsDocument` (WPF) sau „Microsoft XPS Document Writer". PS: scrise de mână (texte scurte). Eșantioanele ghostpdl (AGPL) rămân de utilizat **doar local**, ca verificare de încredere a detectării limbajului, nu în depozit.
4. **Jurnale MFP**: lipsesc public; proprietarul le furnizează (decizia 26f). Până atunci: fixtures sintetice după câmpurile documentate Ricoh (D) — marcate explicit „sintetic".

---

## F. Standardul de denumire a documentelor

Model dat: `yyyymmdd_litera_litera-litera-literacifra/cifre-denumire document-[cifre]/cifre(4/5 cifre).ext`.

Observații:
- `/` este **ilegal** în nume de fișier Windows (și `\ : * ? " < > |`). Pattern-ul trebuie să accepte un **caracter substitut configurabil** pentru fiecare `/` (implicit `+`; alternative legale: `~`, `=`, `#`, `@`). Aplicația nu redenumește niciodată (decizia 26c); raportează „nume neconform".
- Interpretarea „litera" = o literă (inclusiv diacritice românești prin `\p{L}`); „cifre" = una sau mai multe cifre; `[cifre]` — **ambiguu**: paranteze literale sau doar notație pentru „cifre"? Regex-ul le acceptă pe amândouă (paranteze opționale). Cere confirmare proprietar.
- „literacifra/cifre" interpretat ca literă + cifră(e), separator, cifre (ex. `D12+345`). Ambiguu dacă după literă e exact o cifră; parametru `regLetterDigits`.
- Prima zonă se validează și ca **dată calendaristică reală** în cod (regex singur acceptă 20260231).

### F.1 Regex implicit (.NET, `RegexOptions.CultureInvariant`, cu timeout; șablonul se compune din parametri)
```
^(?<date>(?<yyyy>\d{4})(?<mm>0[1-9]|1[0-2])(?<dd>0[1-9]|[12]\d|3[01]))
_(?<unit1>\p{L})
_(?<unit2>\p{L})-(?<unit3>\p{L})
-(?<regletter>\p{L})(?<regdigits>\d+)(?<sep1>\+)(?<regnumber>\d+)
-(?<title>[^\\/:*?"<>|]+?)
-\[?(?<ref>\d+)\]?(?<sep2>\+)(?<serial>\d{4,5})
\.(?<ext>[A-Za-z0-9]{1,8})$
```
(scris pe mai multe linii doar pentru lizibilitate; în configurare este un singur șir, fără `IgnorePatternWhitespace` sau cu el și spații escape-uite). Parametri configurabili: caracterul substitut (`\+` → `[+=~#@]`), clasa de litere (ex. numai majuscule: `\p{Lu}`), lungimea seriei (`{4,5}`), prezența parantezelor `[]` (obligatorii/opționale/interzise), lista extensiilor, set de unități acceptate (listă albă pentru `unit1..3`).
Titlul acceptă cratime și spații; backtracking-ul leneș cu `-\[?\d+…` la final dezambiguizează; timeout obligatoriu (intrare ostilă).

### F.2 Trei exemple acceptate de regex-ul implicit
1. `20260314_A_B-C-D12+345-Raport de inspectie-[07]+1234.pdf`
2. `20251231_M_S-T-X9+10-Nota informare-12+54321.docx`
3. `20260101_Ă_Ț-I-K1+7-Plan de masuri-[3]+0042.xlsx`
Exemple respinse: `20260314_A_B-C-D12/345-...` (nu poate exista pe disc), `2026-03-14_A_...` (dată), `20260314_A_B-C-D12+345-Raport-[07]+123.pdf` (serie 3 cifre).
Neverificat: exemplele nu sunt reale; nu au fost rulate pe un motor .NET în această sesiune (doar raționament pe sintaxă).

---

## Propunere de implementare WP16a (ordinea pașilor)

| # | Pas | Ediție | Licență / componente |
|---|---|---|---|
| 1 | Model de stare job tipărire (C.5) + `evidence_level`; contract „stări, nu erori"; „jurnal de tipărire dezactivat" | P1, P2, P3 | cod propriu |
| 2 | Parser EVTX PrintService (307, 805, 800, 801, 842, 308, 309, 310, 372, 812; defensiv; EVTX offline, fără API gazdă în P1) + detectare canal dezactivat/mic (starea din EVTX/config exportat) | P1, P2, P3 | cod propriu; EVTX reader existent al proiectului |
| 3 | Deduplicare server/stație; corelare job (id, imprimantă, mașină, timp) | P1, P2, P3 | cod propriu |
| 4 | Import SPL/SHD (copiere offline → caz criptat): parser SHD defensiv (B.2) + detectare datatype/limbaj (B.4) | P1, P2, P3 | cod propriu |
| 5 | EMF-spool (MS-EMFSPOOL) → pagini EMF → extragere text (`EMR_EXTTEXTOUTW`) + redare imagine (GDI+ doar Windows) → PDF cu PDFsharp (imagine + text) | P1, P2, P3 | cod propriu + PDFsharp (MIT); System.Drawing (MIT, Windows) |
| 6 | XPS → text (OPC + `Glyphs`) și PDF prin redare WPF + PDFsharp | P1, P2, P3 | .NET/WPF (MIT), PDFsharp (MIT) |
| 7 | RAW PCL/PS/PCL XL: **numai** detectare și raport „conținut nedecodabil fără interpretor" + metadate din SHD; opțional euristică de text PCL5 (portare din pclbox/PCL Paraphernalia) cu marcaj „parțial". Decizie de licență: **interpretor AGPL (GhostPCL/Ghostscript) NU se integrează**; poate fi oferit ca *instrument extern opțional*, instalat de proprietar, apelat ca proces separat — dar aceasta contravine regulii „fără executarea de programe externe" din P1 și ar cere decizia proprietarului. | P1, P2, P3 | Apache-2.0 / Unlicense (referință); AGPL exclus |
| 8 | Import jurnal MFP (Ricoh CSV întâi — singurul cu câmpuri publice; apoi Canon, Sharp; restul când vin eșantioane), parser bazat pe antet, versionat; corelare job Windows ↔ rând dispozitiv | P1, P2, P3 | cod propriu |
| 9 | Introducere manuală contoare (C.4) | P1, P2, P3 | cod propriu |
| 10 | Verificare standard de denumire (F), configurabil, raportează fără redenumire | P1, P2, P3 | cod propriu (.NET Regex) |
| 11 | SNMP poll (prtMarkerLifeCount, prtAlert, jmJob*) | **P2, P3 numai**; exclus din build-ul P1 | bibliotecă SNMP permisivă — de evaluat (neverificat; nu a fost cercetat în această sesiune) |
| 12 | IPP Get-Job-Attributes / Get-Jobs | **P2, P3 numai** | client IPP propriu pe HTTP (RFC 8011) sau bibliotecă permisivă — neverificat |
| 13 | Lanț clasificat → tipărire → scanare → PDF → USB (decizia 26e) | P1, P2, P3 | corelație cu parserele existente |
| 14 | Teste: fixtures sintetice + generare cu PCL Paraphernalia (Unlicense); EVTX/SPL reale de pe VM de test | toate | — |

Condiții transversale: conținutul tipărit capturat este copie de informație posibil clasificată → doar în cazul criptat, marcat cu clasificarea documentului, sub custodie și control de acces (decizia 26a); nu se adaugă cod de rețea în P1 (pașii 11–12 sunt compilați condiționat sau în ansamblu separat, absent din P1).

---

## Neverificat (rezumat)
1. ID-urile 800, 801, 842, 308, 309, 310, 372, 812: semnificații și câmpuri exacte (nu există listă oficială Microsoft citită); 310 are definiții contradictorii; ordinea câmpurilor `Param1..8` pentru 307 provine din sursă terță.
2. Activarea la scară prin GPP Registry (`WINEVT\Channels\...\Enabled`), calea exactă a cheii `ShowJobTitleInEventLogs`, `Attributes` registry pentru KeepPrintedJobs, valoarea numerică 0x100.
3. Layout SHD pe Windows 10/11/Server 2016–2025 (șablon WinHex terț, licență necunoscută, semnături per versiune); denumirea fișierelor spool; rotația/retenția fișierelor păstrate; efectul repornirii spooler-ului.
4. Numele exacte ale datatype-urilor `winprint` (`RAW [FF appended]`, `XPS_PASS`, `NT EMF 1.00x`) — pagina Microsoft nu s-a putut citi.
5. Formatul de spool al driverului IPP clasă (XPS intermediar → PWG Raster): doar discuție PWG.
6. Comportamentul „Branch Office Print Remote Log" (care mașină înregistrează 307).
7. Suportul real pentru Job Monitoring MIB, `hrPrinterStatus`, valorile `job-state-reasons`, `Get-Jobs` pe joburi RAW/9100 per model.
8. Câmpurile jurnalelor: HP, Xerox, Konica Minolta (XML), Kyocera, Lexmark, Epson, Canon (coloane complete), Sharp (export CSV de job), Brother (coloane exacte).
9. Licența MIT pentru System.Drawing.Common și dotnet/wpf (cunoscută, dar nelecturată în sesiune); evaluare biblioteci SNMP/IPP.
10. Capacitatea PCL Paraphernalia de a extrage text (este analizor); acuratețea euristicilor de text PCL/PS.
11. Octeții de recunoaștere PCL/PCL XL/PS/XPS din B.4: parțial confirmați pe eșantioane (`ESC E`, UEL+PJL, `%!`, zip pentru XPS); antetul PCL XL (`) HP-PCL XL;…`) neconfirmat pe fișier (fonts.pxl începe cu PJL; antetul urmează după) .
12. Regex-ul din F: neexecutat; interpretarea modelului (paranteze, „literacifra") cere confirmarea proprietarului.
13. Nu s-a găsit niciun EVTX public cu evenimente 307, nicio pereche SPL/SHD publică, niciun jurnal MFP public.
