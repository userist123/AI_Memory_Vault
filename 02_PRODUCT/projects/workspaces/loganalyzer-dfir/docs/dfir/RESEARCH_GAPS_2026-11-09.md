# Cercetare: ce mai lipsește aplicației (2026-11-09)

Două cercetări pe web, cu sursele de mai jos. Fiecare propunere a fost verificată în cod, ca să rămână doar lipsurile reale. Sursele au fost citite din rezultatele căutării; ce nu s-a putut confirma este marcat NEVERIFICAT.

## A. Controlul stației față de STIG Windows 11 (categoria I)

Controlul are deja 90 de verificări. Printre ele sunt BitLocker, Secure Boot și TPM, Defender (activ, semnături, excluderi, ultima scanare), WDigest, protecția LSA, Credential Guard, VBS, SMBv1, jurnalizarea PowerShell și PowerShell 2.0, AppLocker și WDAC, AutoPlay/AutoRun, camera și microfonul, Asistența la distanță, WinRM, blocarea ecranului, sursa de timp și versiunea de Windows.

Lipsesc regulile STIG de categoria I de mai jos. Ele sunt implementate ca P40–P47:

| Cod | Verificare | Regula STIG (Windows 11 V2R3) |
|---|---|---|
| P40 | Acces anonim la conturi și partajări | WN11-SO-000145, -000150, -000165 |
| P41 | Hash LM și NTLMv2 obligatoriu | WN11-SO-000195, -000205 |
| P42 | Drepturi periculoase (Act as part of OS, Create token, Debug) | WN11-UR-000015, -000045, -000065 |
| P43 | AlwaysInstallElevated | WN11-CC-000315 |
| P44 | DEP și SEHOP | WN11-00-000145, -000150 |
| P45 | WinRM fără Basic și fără trafic necriptat | WN11-CC-000330, -000345 (și -000335, -000350, categoria II) |
| P46 | LLMNR oprit | fără regulă STIG găsită (NEVERIFICAT); recomandare de întărire |
| P47 | IIS instalat | WN11-00-000100 |

Identificatorii STIG au fost confirmați pe versiunea V2R3 (https://stigaview.com/products/win11/v2r3/onepage). Există versiuni mai noi (V2R7); înainte de un export oficial, ei se reverifică pe pachetul DISA curent.

Ce nu se poate citi din Windows: parola BIOS și locul în care se păstrează cheia de recuperare BitLocker. Acestea rămân câmpuri completate de mână de controlor.

## B. Raportul pentru evaluatori

- Export ca listă de verificare STIG (.cklb / XCCDF), alături de PDF-ul semnat. Fiecare regulă primește starea (Open / Not a Finding / Not Applicable / Not Reviewed), detaliile și severitatea. Acesta este cel mai valoros pas pentru compatibilitatea cu instrumentele evaluatorilor.
- „Nu se aplică” cu motiv scris, niciodată omis în tăcere. Exemple: Credential Guard și LAPS pe o stație fără domeniu.
- Referința la procedurile de securitate (PrOpSec) și la certificatul de acreditare al stației, cu număr și dată de expirare. Ordinul ORNISS 16/2014 aprobă INFOSEC 2 (https://legeaz.net/monitorul-oficial-262-2014/ordinul-guvernului-romaniei-orniss-16-2014), iar Ordinul ORNISS 18/2014 este ghidul PrOpSec (https://legeaz.net/monitorul-oficial-242-2014/ordinul-guvernului-romaniei-orniss-18-2014/anexa-ghid). Un model oficial de raport nu a fost găsit (NEVERIFICAT).

### Listă de verificare STIG (.cklb) — implementat

„Exportă pentru ofițer” scrie acum și un fișier `.cklb` (STIG Viewer 3, JSON) pentru STIG Windows 11 V2R3, cu 52 de reguli. Starea unei reguli nu depășește ce a verificat aplicația:

- **Evaluate una câte una** (P40–P45, P47): regula primește „Not a Finding” sau „Open” doar din valoarea exactă citită (de exemplu RestrictAnonymous = 0 dă „Open” pentru SO-000150, dar SO-000145 rămâne „Not a Finding”). O valoare necitită dă „Not Reviewed”.
- **Preluate din verificarea întreagă** (C01, P07, P13, P15, P24): același criteriu ca regula; CONFORM → „Not a Finding”, NECONFORM → „Open”, DE VERIFICAT/NEDETERMINAT → „Not Reviewed”. Excepție: la P07 (firewall) un NECONFORM rămâne „Not Reviewed”, fiindcă un firewall terț ar putea îndeplini regula.
- **Doar dovadă** (restul, inclusiv P08 și N07): întotdeauna „Not Reviewed”, cu detaliile și dovada verificării atașate, fiindcă verificarea aplicației diferă de criteriul STIG sau acoperă mai multe reguli (P08 verifică doar Defender; N07 acceptă și setarea din afara politicii). „Nu se aplică” la verificare dă „Not Applicable”; o verificare care nu a rulat dă „Not Reviewed”.

Limite: ID-urile de regulă SV nu sunt completate (nu au putut fi verificate pe sursă); V2R3 nu este versiunea curentă (există V2R7), iar data lansării nu este verificată; textul de verificare și de remediere al STIG nu este inclus; fișierul nu este semnat. Evaluatorul decide starea finală în STIG Viewer.

## C. Analiza probelor față de uneltele de referință

Există deja în cod:
- detecții pentru Kerberoasting și AS-REP;
- semnale BYOVD;
- MFT de bază, ShellBags, UserAssist, BAM, Coș de reciclare;
- PCA, MPLog;
- cache-ul de imagini RDP, istoricul PowerShell;
- accesul la LSASS (GrantedAccess).

Lipsuri, în ordinea valorii:

1. **Pachet de detecții pentru Active Directory.** Lipsesc abuzul ADCS (4886/4887 pe autoritatea de certificare, urmat de 4768) și releul NTLM (4624 tip 3 NTLM cu IP-ul care nu corespunde stației; logări anonime pe serverele CA). Surse: https://www.decryptiondigest.com/blog/how-to-detect-active-directory-certificate-services-adcs-attacks , https://nccgroup.com/au/research-blog/detecting-and-hunting-for-the-petitpotam-ntlm-relay-attack
2. **Oprirea EDR-ului prin filtre WFP.** Evenimentele 5441/5447 cu filtre noi de ieșire și pachete blocate ale proceselor de securitate. Sursa: https://detection.fyi/sigmahq/sigma/windows/builtin/security/win_security_hktl_edr_silencer
3. **Istoricul detecțiilor Defender (DetectionHistory).** Dă amenințarea, calea, ora și amprentele. Sursa: https://www.sans.org/blog/uncovering-windows-defender-real-time-protection-history-with-dhparser **Implementat:** `DefenderDetectionHistoryParser` (fișierele din `...\Scans\History\Service\DetectionHistory`, colectate de `DefenderDetectionHistoryCollector`, pe stație cu drepturi de administrator) și regula `DEF-HISTORY-ONLY` (detecție în istoric fără 1116-1119 cu același Detection ID: High cu T1070.001 doar dacă jurnalul a fost golit după detecție, System 104, sau acoperă momentul ei; Low în afara intervalului jurnalului, adică rotire sau export anterior; Medium când ora lipsește sau jurnalul nu e în caz). Verificat pe o înregistrare reală EICAR din laborator (toate câmpurile comparate cu evenimentul 1116), pe fișiere trunchiate la fiecare decalaj, lungimi uriașe, bloc de chei corupt, ore invalide, șiruri fără NUL și caractere de control. În laborator (WS01): înainte de golire, doar `DEF-DETECTION`; după `wevtutil cl` pe jurnalul Defender, `DEF-HISTORY-ONLY` High cu T1070.001, alături de `LOG-TAMPER`. Defender a rescris atunci fișierul, iar ora de după blocul de chei s-a mutat cu 13 s; cititorul trece pe `ThreatTrackingStartTime` când diferența depășește o secundă (a doua mostră reală în teste). Colecția la distanță are artefactul `defender-history` (folderul copiat de pachet cu drepturi de administrator; dacă lipsește, pasul este „absent”, nu eșuat; la import, doar fișierele cu nume GUID și conținut potrivit ajung la cititor). Verificat pe WS01: pachet rulat ca administrator, verificat, importat, `DEF-HISTORY-ONLY` produs; pentru a vedea golirea jurnalului, pachetul trebuie să includă și `evtx:System` (evenimentul 104).
4. **Unelte de administrare la distanță abuzate (RMM).** AnyDesk, ScreenConnect și altele, după instalarea serviciului, procese și jurnale. Sursa: https://redcanary.com/threat-detection-report/trends/rmm-tools **Parțial implementat (extins în regulile existente, fără regulă nouă):** `REMOTE-TOOL-PRESENT` / `REMOTE-TOOL-EXECUTED` (`Wp11Rules.Tools.cs`, `Data/remote_access_tools.json`) existau deja pentru serviciu (7045 / 4697), proces (4688 / Sysmon 1), Prefetch, BAM, UserAssist, Amcache și ShimCache. S-au adăugat: uneltele care lipseau (Remote Utilities, Ammyy Admin, DWService, BeyondTrust Remote Support, Supremo, UltraViewer, ISL Online, Quick Assist; executabilele ZA_Connect.exe și ZAService.exe pentru Zoho Assist), câmpul opțional `domains` (sufixe DNS, potrivite la graniță de etichetă) și interogările DNS (Sysmon 22, DNS-Client 3006/3008) ca dovadă de prezență, nu de execuție. PRESENT crește la Medium când unealta este serviciu dintr-un folder în care utilizatorul poate scrie (Downloads, Desktop, Documents, AppData\Roaming, Temp, Public, Windows\Temp, PerfLogs, Coșul de reciclare, rădăcina ProgramData) sau când cel puțin două unelte distincte au dovezi pe aceeași stație la cel mult 24 de ore una de alta (nu contează Quick Assist, uneltele aprobate în profil, interogările DNS singure și, într-un caz cu mai multe stații, dovezile fără nume de stație); Quick Assist rulat din locul lui (System32, WindowsApps) este Medium, fiindcă escrocheriile de tip „suport tehnic” folosesc exact acea copie; Chrome Remote Desktop rulat rămâne High, ca înainte; o aprobare în profil doar pe nume acoperă numai Program Files și Windows, orice altă copie este Medium, nu Info. Un ImagePath cu un intermediar (`cmd /c`, `rundll32`) în fața uneltei numește tot unealta. Identificarea este după nume, nu după semnătură. Verificat în laborator pe WS01 cu copii redenumite ale PING.EXE (fără descărcări): AnyDesk ca serviciu și proces din `C:\Users\Public` și RustDesk din Program Files au dat două REMOTE-TOOL-EXECUTED High, fiecare numindu-l pe celălalt; stația scrisă o dată scurt și o dată ca FQDN a fost recunoscută ca aceeași. Rest acceptat după revizie: un caz fără niciun nume de stație este tratat ca o singură stație, iar același nume NetBIOS din domenii diferite este considerat aceeași stație (efect: cel mult o creștere de la Low la Medium). **Nu este implementat:** jurnalele proprii ale uneltelor (AnyDesk `ad.trace` / `connection_trace.txt`, TeamViewer `Connections_incoming.txt`, ScreenConnect în jurnalul Application) nu sunt citite ca sesiuni, deci nu se vede cine s-a conectat; nu există dovadă de instalare dincolo de serviciu și MSI.
5. **Sesiunile RDP reconstruite.** Evenimentele 1149, 21, 22, 24 și 25, legate prin ID-ul sesiunii.
6. **Artefacte mici.** Filele nesalvate din Notepad (TabState) și MUICache.
7. **Lucrări mari (L).** Indexul Windows Search (ESE sau SQLite pe Windows 11), $I30 și $LogFile, copiile umbră VSS citite offline.

### Detecții pentru domeniu și EDR — implementat

Punctele 1 și 2 de mai sus sunt acum reguli în `Wp11Rules.AdAttacks.cs`. Fiecare cere ca auditul respectiv să fie pornit; fără el jurnalul nu conține evenimentul și regula nu vede nimic.

| Regulă | Ce vede | Ce trebuie auditat |
|---|---|---|
| `KERBEROAST-RC4` (T1558.003, High la 5 servicii, Medium la 3-4) | Același cont cu tichete RC4 (0x17 / 0x18) pentru cel puțin 3 servicii diferite în 10 minute (4769) | „Kerberos Service Ticket Operations” pe controlerul de domeniu |
| `ASREP-ROAST` (T1558.004, Medium; High cu RC4) | Cont care a primit TGT fără pre-autentificare (4768, tip 0), un constat pe cont | „Kerberos Authentication Service” pe controlerul de domeniu |
| `ADCS-SAN` (T1649, High la 4887, Medium la 4886) | Cerere sau certificat emis cu un SAN în atributele cererii (calea EDITF_ATTRIBUTESUBJECTALTNAME2, ESC6; ESC1 cu SAN doar în extensia cererii nu se vede) | Auditul autorității de certificare (oprit implicit) |
| `NTLM-RELAY-MACHINE` (T1557.001, Medium) | Cont de calculator cu 4624 tip 3 prin NTLM de la o adresă IP absentă din cele de la care s-a autentificat prin Kerberos în același jurnal (fără bază Kerberos, fără constatare) | „Logon” pe serverul ținta |
| `EDR-WFP-BLOCK` (T1562.001, High) | Filtre „Custom Outbound Filter” (5441 / 5447) sau cel puțin 5 conexiuni blocate (5157) ale unui program de securitate în 10 minute | „Filtering Platform Policy Change” pentru 5441 / 5447; „Filtering Platform Connection” pentru 5157 (volum mare) |

Limite: regulile nu văd dacă hash-urile au fost sparte, nici setările șablonului de certificate (ESC1), nici procesul care a creat filtrul WFP. Sunt listate cel mult 20 de constatări pe regulă, restul într-un rezumat Low. Încă lipsește legătura ADCS cu 4768 prin certificat.
