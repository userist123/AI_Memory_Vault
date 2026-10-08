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
