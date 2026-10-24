# Cercetare 2026-10-11: ce verifică un controlor pe o stație Windows 10/11 izolată care tratează informații clasificate

Stare: cercetare web, `DOCUMENT_VERIFIED` doar unde sursa e citată. Nimic de aici nu a fost rulat pe o stație de test.
Marcaje: **[NESIGUR]** = sursă secundară, contradictorie sau incompletă; **[NECITAT]** = din practica Windows/DFIR, fără sursă găsită în această sesiune, de verificat în laborator (`FORENSIC_TEST_LAB.md`).
Citări: `[Sn]` trimite la lista de surse din §6 (fiecare cu URL). Identificatorii STIG sunt din *Microsoft Windows 11 STIG* (V2R5–V2R8); numerele CIS nu sunt reproduse decât unde au fost văzute într-o sursă (CIS Benchmark integral cere cont pe CIS WorkBench).
Constrângeri: depozit public, fără identificatori de organizație, doar surse publice; LogAnalyzer rămâne **read-only**. Documente înrudite (nu se repetă aici): `HG585_ACCREDITATION_REQUIREMENTS.md` (cerințe HG 585 citate verbatim), `RESEARCH_2026-10-11_MEDII_DE_STOCARE.md` (USB/medii, registre), `PRINT_TRACKING_RESEARCH.md` (imprimare).

---

## 1. Cadrul: ce cere fiecare sursă și ce se poate verifica pe stație

### 1.1 Cadrul național (România)

| Sursă | Ce stabilește | Ce e verificabil pe stație |
|---|---|---|
| Legea 182/2002 [S1] | Două clase: secrete de stat (niveluri *secret*, *strict secret*, *strict secret de importanță deosebită*) și secrete de serviciu [S1][S3] | Nimic tehnic direct; dă nivelul maxim pe care stația trebuie să-l poată trata (din certificatul de acreditare). |
| HG 585/2002 – Standarde naționale [S2] | Capitolul INFOSEC; acreditarea SIC de către ORNISS; corespondența SDS→NATO TOP SECRET, SS→NATO SECRET, S→NATO CONFIDENTIAL, SSv→NATO RESTRICTED [S2][S3] | Cerințele de audit, control acces, cod malițios, medii, marcare sunt detaliate în `HG585_ACCREDITATION_REQUIREMENTS.md`; din ele derivă controalele din §2. |
| HG 781/2002 – secrete de serviciu | Trimite în mare parte la regulile HG 585/2002 [S7] **[NESIGUR]** (sursă secundară) | Același set de controale, la nivel de rigoare mai mic, stabilit prin DCS. |
| Ordin ORNISS 16/2014 – INFOSEC 2 (directiva principală) [S6] | Protecția confidențialității, integrității, disponibilității (și după caz autenticitate, nerepudiere) pentru SIC; evaluare de risc ca parte a dezvoltării [S6] | Indirect: controlorul compară stația cu DCS și PrOpSec aprobate. |
| Ordin ORNISS 17/2014 – DS-1 (ghid DCS) [S5] | Structura Documentației cu Cerințele de Securitate | Documentul de referință pentru profilul de procedură al aplicației (ce trebuie să fie activ). |
| Ordin ORNISS 18/2014 – DS-2 (ghid PrOpSec) [S4] | PrOpSec = descrierea precisă a implementării cerințelor din DCS, a procedurilor și responsabilităților; forma finală aprobată de AAS [S4] | Sursa „adevărului” pentru orar, software aprobat, medii, utilizatori; LogAnalyzer îl reprezintă ca „profil de procedură”. |

Concluzie practică: legislația română publică **nu** dă o listă de setări Windows; listele tehnice vin din DCS/PrOpSec aprobate de AAS [S4][S5]. De aceea aplicația trebuie să compare stația cu **profilul organizației** și să folosească STIG/CIS/Microsoft doar ca valori implicite și ca justificare. Ghidurile tehnice ORNISS de detaliu (dacă există public) nu au fost găsite **[NESIGUR]**.

### 1.2 UE și NATO

| Sursă | Ce cere (rezumat) | Verificabil pe stație |
|---|---|---|
| Decizia 2013/488/UE art. 10 [S9] | Toate CIS care tratează EUCI se acreditează; declarația de acreditare fixează nivelul maxim; TEMPEST de la CONFIDENTIEL UE în sus; produse criptografice aprobate [S9] | Nivelul maxim din declarație → afișat în banner/raport. |
| Decizia 2013/488/UE Anexa IV [S8] | Minimalitate și cel mai mic privilegiu (pct. 18–19), apărare în adâncime (16), securitate pe tot ciclul de viață, evaluări periodice (8–12), medii de stocare distruse după proceduri aprobate (37–38), orice sistem interconectat e „neîncrezut”, fără interconectare cu rețele publice fără BPS aprobat (33–36), SecOPs aprobate de SAA (48(d)) [S8] | Cel mai mic privilegiu (conturi, drepturi), servicii minime, lipsă interconectare (adaptoare, istoric rețele), medii. |
| NATO C-M(2002)49-REV1 [S10] + AC/35-D/2004-REV4 (Primary Directive on CIS Security) [S11] + AC/35-D/2005 (INFOSEC Management Directive) [S12] | Versiunile publice declasificate; directivele sunt obligatorii pentru sistemele care stochează/procesează informații NATO [S10][S11][S12]. Conținutul tehnic detaliat (directivele de suport) nu e public **[NESIGUR]** | Aceleași principii: acreditare, SecOPs, cel mai mic privilegiu, audit, control medii. |

### 1.3 Standarde tehnice de configurare (sursa valorilor implicite)

| Sursă | Ce acoperă | Utilitate pentru LogAnalyzer |
|---|---|---|
| DISA STIG Windows 11 [S13][S21] | ~250 de reguli cu ID `WN11-*`, verificare prin registru/comandă, severitate CAT I–III | Cea mai bună sursă publică de „cum citesc” (Check Text) și „ce valoare”. Lista completă V2R5 citită în [S13]. STIG Viewer / stigviewer.com oferă fișele individuale [S20]. |
| CIS Microsoft Windows 11 Enterprise Benchmark v4.0.0 | Setări L1/L2/BitLocker/NG | Textul integral necesită înregistrare; câteva elemente văzute prin Tenable [S22][S23][S24]. CIS-CAT Lite (gratuit) e limitat la un subset de benchmark-uri și a avut o eroare de autodetecție pe Windows 11 [S53]. |
| Microsoft Security Baselines + Security Compliance Toolkit [S25] | Baseline-uri GPO; **Policy Analyzer** compară seturi de GPO și instantanee locale, exportă în Excel; **LGPO.exe** exportă politica locală ca backup GPO, util pe stații standalone [S25] | LogAnalyzer importă deja backup-uri GPO; poate accepta direct baseline-ul Microsoft ca profil de comparație și exportul LGPO al stației. |
| NIST SP 800-53 r5 [S44] / SP 800-171 r3 [S45] | Controale: AC-11 / 03.01.10 blocare dispozitiv; CM-7(5) / 03.04.08 software permis prin excepție; MP-7 / 03.08.07 utilizarea mediilor; SC-41 dezactivarea porturilor și dispozitivelor I/O (USB, Thunderbolt, FireWire, unități optice); SC-15 camere/microfoane fără activare la distanță [S44][S45] | Etichete de trasabilitate în fișele de constatare. 800-171 r3 folosește parametri definiți de organizație (ex. 15 min e exemplu, nu valoare fixă) [S45] **[NESIGUR]** (rezumate secundare). |
| ACSC ISM (sept. 2026) + Essential Eight [S46][S47] | ISM-1713 registru de medii amovibile „dezvoltat, implementat, menținut și verificat periodic”, aplicabil la toate nivelurile inclusiv S/TS; ISM-1359 politică de utilizare a mediilor; ISM-1549 politică de management al mediilor [S46]. E8: control aplicații, patch-uri, restrângere privilegii, întărire aplicații (PowerShell 2.0 dezactivat, constrained language), backup [S47] **[NESIGUR]** (nivelurile de maturitate din rezumate secundare) | Verificarea periodică a registrului de medii; maturitate control aplicații. |
| BSI IT-Grundschutz SYS.2.2.3 „Clients unter Windows” | Cerințe A1…A2x (ex. A5 antivirus, A22 restricționarea PowerShell/WPS) [S48] **[NESIGUR]** (doar surse secundare, textul oficial nu a fost recuperat) | Confirmare independentă pentru PowerShell și antivirus. |
| ANSSI | VBS/Credential Guard pe Windows 10 [S49]; jurnalizare Windows (sincronizare timp, alegerea jurnalelor, Sysmon cere competență și MCO) [S50]; profil „sas et station blanche” (stație de decontaminare a mediilor) [S51]; telemetrie [S52] | Fundament pentru Sysmon, sincronizarea timpului și stația de decontaminare (sheep-dip). |
| SCAP/OVAL | OpenSCAP: suportul Windows declarat „void” din 1 feb. 2022 în README [S54]; CIS-CAT Lite e alternativa gratuită practică [S53] | Nu merită integrat OpenSCAP pe Windows; eventual import de rezultate XCCDF/ARF ca dovadă externă (P3). |

---

## 2. Controale verificabile care NU sunt încă în lista LogAnalyzer

Coloana „Admin”: **Nu** = citibil ca utilizator standard (HKLM e în general citibil); **Da** = cere elevare (Security log, `auditpol`, `secedit`, TPM, BitLocker, SACL). Formularea pentru controlor e scurtă și fără jargon, în stilul `LOGANALYZER_PRODUCT_UX_CONTRACT.md`.

### 2.A Izolarea stației (dovezi de încălcare a „air gap”)

| # | Control | Cum se citește (read-only) | Ce i se spune controlorului | Referințe | Admin |
|---|---|---|---|---|---|
| A1 | Adaptor fără fir prezent/activ | `Get-NetAdapter -Physical` (câmpurile `MediaType`/`PhysicalMediaType` = Native 802.11), serviciul `WlanSvc` (stare/pornire) **[NECITAT]** | „Stația are placă Wi-Fi. Pe o stație izolată nu trebuie să existe sau trebuie scoasă/dezactivată din BIOS.” | 2013/488 Anexa IV pct. 33–36 [S8]; SC-41 [S44] | Nu |
| A2 | Profiluri Wi-Fi salvate și istoricul rețelelor | `netsh wlan show profiles` **[NECITAT]**; `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles\*` (`ProfileName`, `DateCreated`, `DateLastConnected`, tip LAN/WLAN/mobil) și `...\NetworkList\Signatures\Unmanaged` (MAC-ul gateway-ului) [S42] | „Stația s-a conectat cândva la rețeaua «X» (prima dată … ultima dată …). O stație izolată nu ar trebui să aibă nicio rețea în istoric în afară de cea aprobată.” | [S8] pct. 35; [S42] | Nu |
| A3 | Bluetooth | `Get-PnpDevice -Class Bluetooth` (prezență/stare), serviciul `bthserv` **[NECITAT]** | „Stația are Bluetooth pornit. Trebuie oprit dacă organizația nu l-a aprobat în scris.” | WN11-00-000210 / -000220 / -000230 [S16][S13] | Nu |
| A4 | Alte căi de interconectare | Partajări `Win32_Share` non-implicite (WN11-00-000060); Remote Assistance (WN11-CC-000155); WinRM; `netsh interface portproxy show all` (WN11-00-000395); Internet Connection Sharing (WN11-CC-000044); funcții Telnet/TFTP/SNMP/IIS (WN11-00-000100…-000120) [S13] | „Stația are o funcție care permite acces din afară (de ex. «Asistență la distanță»). Pe o stație izolată trebuie dezactivată.” | [S13] | Nu (funcțiile opționale: Da) |
| A5 | Mașini virtuale / alt sistem de operare | Funcția Hyper-V și VM-uri (WN11-00-000080); intrări suplimentare în `bcdedit /enum` (WN11-00-000055) [S13] | „Pe stație există un al doilea sistem de operare sau o mașină virtuală. Acestea pot ocoli toate controalele; trebuie să fie aprobate.” | [S13] | `bcdedit`: Da |

### 2.B Firmware, pornire, criptare

| # | Control | Cum se citește | Ce i se spune controlorului | Referințe | Admin |
|---|---|---|---|---|---|
| B1 | Secure Boot activ | `HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State\UEFISecureBootEnabled` (1/0) sau `Confirm-SecureBootUEFI` [S29] | „Pornirea securizată e oprită: cineva poate porni stația de pe un stick cu alt sistem.” | WN11-00-000020 [S13] | Registru: Nu; cmdlet: Da [S29] |
| B2 | Mod UEFI (nu Legacy BIOS) | `msinfo32` „BIOS Mode”; `$env:firmware_type` **[NECITAT]** | „Stația pornește în mod vechi (Legacy), fără protecțiile moderne.” | WN11-00-000015 [S13] | Nu |
| B3 | TPM prezent și pregătit | `Get-Tpm` / `tpm.msc` [S20] | „Cipul de securitate (TPM) lipsește sau nu e pregătit; criptarea discului e mai slabă.” | WN11-00-000010 [S20] | Da |
| B4 | BitLocker cu PIN la pornire, pe toate discurile fixe | `manage-bde -protectors -get C:` (protector `TPM And PIN`), `manage-bde -status` pentru toate volumele **[NECITAT]** (comanda); cerința: WN11-00-000030/-000031/-000032 (PIN ≥ 6 cifre) [S13] | „Discul de sistem e criptat, dar pornește fără PIN: un disc furat cu tot calculatorul poate fi citit.” / „Discul D: nu e criptat.” | [S13] | Da |
| B5 | Protecție DMA în kernel | `msinfo32` „Kernel DMA Protection”; politica `HKLM\Software\Policies\Microsoft\Windows\Kernel DMA Protection\DeviceEnumerationPolicy` = 0 (Block All) [S28] | „Porturile Thunderbolt nu sunt protejate împotriva citirii memoriei.” Nu acoperă FireWire/PCMCIA/ExpressCard [S28] | WN11-EP-000310 [S13]; CIS (BitLocker) [S24] | Nu |
| B6 | Parolă BIOS/UEFI setată | Doar prin WMI de producător: Dell `root\dcim\sysman` `DCIM_BIOSPassword` (cere Dell Command \| Monitor), HP `root\hp\instrumentedbios` `HP_BIOSSetting` „Setup Password” `IsSet`, Lenovo `root\wmi` `Lenovo_BiosPasswordSettings.PasswordState` [S43] **[NESIGUR]** (necesită testare pe modele) | „Nu am putut confirma parola de BIOS. Verificați fizic: intrați în BIOS și cereți parola.” (dacă nu se poate citi → întrebare procedurală §3) | SC-41 (indirect) [S44] | Da (probabil) **[NESIGUR]** |
| B7 | Ordinea de pornire | Nu se poate citi generic și fiabil din Windows **[NECITAT]** | Doar procedural (§3). | — | — |

### 2.C Protecția credențialelor și întărirea sistemului

| # | Control | Cum se citește | Ce i se spune controlorului | Referințe | Admin |
|---|---|---|---|---|---|
| C1 | VBS, Credential Guard, HVCI | `Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard` → `SecurityServicesRunning` conține 1 (Credential Guard) și 2 (HVCI) [S27][S14] | „Protecția parolelor din memorie (Credential Guard) nu rulează.” Pe stații fără domeniu cerința STIG e formulată pentru domain-joined [S14] | WN11-CC-000070/-000075 (CAT I)/-000080 [S13][S14]; ANSSI VBS [S49] | Da [S27] |
| C2 | Protecție LSA (LSASS ca proces protejat) | `HKLM\SYSTEM\CurrentControlSet\Control\Lsa\RunAsPPL` (1 = cu blocare UEFI; 2 = fără, Win11 22H2+) [S26]; audit: CodeIntegrity/Operational 3065/3066 [S26] **[NESIGUR]** (sursă veche) | „Procesul care ține parolele nu e protejat; un program rău poate citi parolele.” | CIS „Configures LSASS to run as a protected process = Enabled with UEFI Lock” [S22] | Nu |
| C3 | Parola administratorului local – vechime | `Get-LocalUser` → `PasswordLastSet` pentru conturile admin active **[NECITAT]** (cmdlet); cerința 60 zile WN11-SO-000280 [S13] | „Parola administratorului local nu a fost schimbată de N zile (regula: 60).” | [S13] | Nu |
| C4 | LAPS | Politică sub `HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\LAPS` (`BackupDirectory`), jurnal `Microsoft-Windows-LAPS/Operational` [S30] **[NESIGUR]** (calea confirmată doar secundar) | Pe stație izolată fără AD/Entra, LAPS nu are unde salva parola → „Nu se aplică; verificați C3 și registrul de parole sigilate.” | [S30] | Nu |
| C5 | Credențiale în cache / LM / NTLM | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\CachedLogonsCount`; `HKLM\SYSTEM\CurrentControlSet\Control\Lsa\NoLMHash`, `LmCompatibilityLevel` **[NECITAT]** (căile); cerințele WN11-SO-000085 / -000195 / -000205 [S13] | „Stația păstrează hash-uri vechi de parolă (LM) care se sparg ușor.” | [S13] | Nu |
| C6 | Atribuirea drepturilor de utilizator | `secedit /export /areas USER_RIGHTS` **[NECITAT]**; în special „Change the system time” (WN11-UR-000035), „Manage auditing and security log” (-000130), „Debug programs” (-000065), „Load and unload device drivers” (-000120), „Back up/Restore files” (-000030/-000160), „Take ownership” (-000165) [S13] | „Utilizatorul X poate schimba ora sau șterge jurnalul de securitate. Doar administratorii trebuie să poată.” | [S13]; 2013/488 Anexa IV pct. 19 [S8] | Da |
| C7 | SID-uri orfane în drepturi | Același export, SID-uri nerezolvabile (WN11-00-000190) [S13] | „Un drept e acordat unui cont șters.” | [S13] | Da |

### 2.D Controlul execuției

| # | Control | Cum se citește | Ce i se spune controlorului | Referințe | Admin |
|---|---|---|---|---|---|
| D1 | Listă albă de aplicații (AppLocker / App Control) – prezență și mod | AppLocker: `Get-AppLockerPolicy -Effective -XML` [S18], serviciul `AppIDSvc` **[NECITAT]**; App Control (WDAC): politici active în `C:\Windows\System32\CodeIntegrity\CiPolicies\Active\` **[NECITAT]** | „Pe stație poate rula orice program. Regula cere ca doar programele aprobate să poată porni.” / „Lista albă e doar în mod «audit», nu blochează.” | WN11-00-000035 [S18]; NIST CM-7(5) / 800-171 03.04.08 [S45]; E8 [S47] | Nu (AppLocker) **[NESIGUR]** |
| D2 | Încercări blocate / care ar fi fost blocate | AppLocker `EXE and DLL` 8003 (audit) / 8004 (blocat), `MSI and Script` 8006/8007 [S31]; CodeIntegrity 3076 (audit) / 3077 (blocat), 3089 detalii semnătură [S32] | „Cineva a încercat să pornească programul X, care nu e aprobat (de N ori).” | [S31][S32] | Nu **[NESIGUR]** (ACL jurnal) |
| D3 | AutoPlay / AutoRun dezactivat | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\NoDriveTypeAutoRun` (255) și `NoAutorun` (1) **[NECITAT]** (valorile); cerințele WN11-CC-000180/-000185/-000190 [S13] | „La introducerea unui stick, stația poate porni automat conținutul lui.” | [S13] | Nu |
| D4 | „Always install elevated” oprit | `HKLM\SOFTWARE\Policies\Microsoft\Windows\Installer\AlwaysInstallElevated` **[NECITAT]**; WN11-CC-000315 [S13] | „Orice utilizator poate instala programe cu drepturi de administrator.” | [S13] | Nu |
| D5 | PowerShell 2.0 dezactivat | `Get-WindowsOptionalFeature -Online -FeatureName MicrosoftWindowsPowerShellV2Root` **[NECITAT]**; WN11-00-000155 [S13] | „Versiunea veche de PowerShell (fără jurnalizare) e încă instalată.” | [S13]; E8 [S47] | Da |
| D6 | Jurnalizare PowerShell | `HKLM\Software\Policies\Microsoft\Windows\PowerShell\ScriptBlockLogging\EnableScriptBlockLogging` = 1 (eveniment 4104 în `Microsoft-Windows-PowerShell/Operational`); pentru PowerShell 7: `...\PowerShellCore\ScriptBlockLogging` [S33]; Transcription: WN11-CC-000327 [S13] | „Comenzile PowerShell nu sunt înregistrate; dacă cineva rulează un script, nu rămâne urmă.” | WN11-CC-000326/-000327 [S13]; [S33] | Nu |
| D7 | Mod de limbaj PowerShell | `$ExecutionContext.SessionState.LanguageMode` (`ConstrainedLanguage` sub WDAC) [S34]; `__PSLockdownPolicy` e nedocumentat și nu e barieră [S34] | Informativ: „PowerShell rulează în mod restrâns” / „complet”. | E8 (întărire aplicații) [S47] **[NESIGUR]** | Nu |
| D8 | Linia de comandă în 4688 | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\Audit\ProcessCreationIncludeCmdLine_Enabled` **[NECITAT]**; WN11-CC-000066 [S13] | „Jurnalul arată ce programe au pornit, dar nu și cu ce parametri.” | [S13] | Nu |
| D9 | Sysmon prezent și activ | Serviciu `Sysmon`/`Sysmon64` + driver `SysmonDrv`, jurnal `Microsoft-Windows-Sysmon/Operational` [S35]; hash-ul configurației **[NECITAT]** | Informativ / dacă PrOpSec îl cere: „Monitorul suplimentar (Sysmon) lipsește sau e oprit.” ANSSI avertizează că cere competență și întreținere [S50] | [S35][S50] | Nu (serviciu); jurnal: **[NESIGUR]** |

### 2.E Periferice, medii, senzori (completări la ce există)

| # | Control | Cum se citește | Ce i se spune controlorului | Referințe | Admin |
|---|---|---|---|---|---|
| E1 | Restricții de instalare dispozitive | `HKLM\SOFTWARE\Policies\Microsoft\Windows\DeviceInstall\Restrictions` (`DenyRemovableDevices`, `DenyDeviceIDs`, `DenyDeviceIDsRetroactive`); blocările apar ca 125 în `DeviceSetupManager/Admin` [S40] | „Stația nu împiedică instalarea dispozitivelor noi; politica de stocare blochează doar citirea/scrierea, nu și instalarea.” [S40] | SC-41 [S44] | Nu |
| E2 | Clase de stocare dincolo de USB | `HKLM\SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\Deny_All`; CD/DVD `{53f56308-b6bf-11d0-94f2-00a0c91efb8b}` (`Deny_Read`, `Deny_Write`, `Deny_Execute`); discuri amovibile `{53f5630d-...}` [S41]; WPD/telefon, bandă, floppy **[NECITAT]** (GUID-uri) | „Scrierea pe CD/DVD e permisă. Arzătorul de CD e o cale de scoatere a datelor.” | SC-41 (unități optice) [S44]; ISM-1359 [S46] | Nu |
| E3 | Inscriptor optic prezent | `Win32_CDROMDrive` (capabilități) **[NECITAT]** | „Stația are inscriptor CD/DVD. Trebuie să fie în DCS dacă e aprobat.” | SC-41 [S44] | Nu |
| E4 | Cameră | Cameră prezentă (`Get-PnpDevice -Class Camera`) **[NECITAT]**; `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam` = `Deny` [S15]; cameră pe ecranul de blocare WN11-CC-000005 [S13] | „Stația are cameră activă. Trebuie acoperită, deconectată sau dezactivată în BIOS.” | WN11-CC-000007 [S15]; SC-15 [S44] | Nu |
| E5 | Microfon | `...\ConsentStore\microphone` **[NESIGUR]** (prin analogie cu E4); dispozitive audio de intrare **[NECITAT]** | „Stația are microfon activ.” | SC-15 [S44] | Nu |
| E6 | Activare vocală pe ecran blocat | WN11-CC-000365 [S13] | (informativ) | [S13] | Nu |

### 2.F Remanența datelor clasificate pe disc

| # | Control | Cum se citește | Ce i se spune controlorului | Referințe | Admin |
|---|---|---|---|---|---|
| F1 | Ștergerea fișierului de paginare la oprire | `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\ClearPageFileAtShutdown` = 1 [S39] | „Fragmente din documente pot rămâne în fișierul de paginare după oprire.” (Oprirea durează mai mult [S39]) | Politica „Shutdown: Clear virtual memory pagefile” [S39]; nr. CIS **[NESIGUR]** | Nu |
| F2 | Hibernare | `powercfg /a`; existența `C:\hiberfil.sys` **[NECITAT]** | „Hibernarea scrie pe disc tot conținutul memoriei, inclusiv documente deschise.” | — **[NECITAT]** | Nu |
| F3 | Fișiere de descărcare a memoriei (crash dump) | `HKLM\SYSTEM\CurrentControlSet\Control\CrashControl\CrashDumpEnabled`; existența `C:\Windows\MEMORY.DMP`, `Minidump\*` **[NECITAT]** | „După o cădere, stația păstrează pe disc o copie a memoriei (MEMORY.DMP, X GB, data …).” | — **[NECITAT]** | Nu (existență fișier) |
| F4 | Windows Recall / Copilot | `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI\AllowRecallEnablement` (0 = indisponibil), `DisableAIDataAnalysis` (1 = fără instantanee) [S37]; Copilot WN11-00-000125 [S13] | „Funcția Recall face poze periodice ale ecranului; pe o stație clasificată înseamnă copii neînregistrate ale documentelor.” | [S37][S13] | Nu |
| F5 | Telemetrie | `AllowTelemetry` – WN11-CC-000205 [S13]; ANSSI: niciun nivel nu blochează total trimiterea [S52] | Informativ pe stație izolată. | [S13][S52] | Nu |

### 2.G Sesiune, ecran, marcare

| # | Control | Cum se citește | Ce i se spune controlorului | Referințe | Admin |
|---|---|---|---|---|---|
| G1 | Blocare la inactivitate ≤ 15 min | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\InactivityTimeoutSecs` ≤ 900 (0 = dezactivat) [S19]; screensaver cu parolă `ScreenSaverIsSecure` – locația diferă între surse [S19] **[NESIGUR]** | „Dacă utilizatorul pleacă de la stație, ecranul rămâne deschis mai mult de 15 minute.” | WN11-SO-000070 [S19]; AC-11 / 800-171 03.01.10 [S45] | Nu |
| G2 | Parolă la revenirea din somn | WN11-CC-000145/-000150 [S13] | „La trezirea din somn nu se cere parolă.” | [S13] | Nu |
| G3 | Mesaj legal la logare, cu nivelul de clasificare | `LegalNoticeCaption` / `LegalNoticeText` (WN11-SO-000075/-000080) [S13]; locația uzuală `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System` **[NECITAT]** | „La pornire nu apare avertismentul că stația tratează informații clasificate de nivel maxim X.” Aplicația poate compara textul cu nivelul din profil. | [S13]; nivel maxim din acreditare [S9] | Nu |
| G4 | Notificări pe ecranul de blocare | WN11-UC-000015 [S13] | „Pe ecranul blocat pot apărea fragmente de mesaje.” | [S13] | Nu |
| G5 | Marcarea clasificării | Banner/fundal impus prin politică, etichete de volum, nume stație (deja parsate) **[NECITAT]** | Doar parțial verificabil; restul procedural (§3). | HG 585 marcare (vezi `HG585_ACCREDITATION_REQUIREMENTS.md` §1.10) | Nu |

### 2.H Jurnale și audit (completări)

| # | Control | Cum se citește | Ce i se spune controlorului | Referințe | Admin |
|---|---|---|---|---|---|
| H1 | Audit de fișiere pe directoarele clasificate | `auditpol /get /subcategory:"File System"` (WN11-AU-000581/-000582) [S13] **și** SACL pe directoarele din profil (`Get-Acl -Audit`) **[NECITAT]**; evenimente 4663/4656/4660 **[NECITAT]** | „Auditul de fișiere e pornit, dar pe directorul «Clasificat» nu e setat ce să se înregistreze: nimeni nu va ști cine a deschis documentele.” | [S13]; HG 585 audit (`HG585_ACCREDITATION_REQUIREMENTS.md` §1.7) | Da |
| H2 | Dimensiune, retenție și permisiuni pentru Application/System | WN11-AU-000500/-000510 (≥ 32768 KB), ACL WN11-AU-000515/-000520/-000525 [S13]; mod de retenție `wevtutil gl <log>` (`retention`, `autoBackup`) **[NECITAT]** | „Jurnalul de sistem se suprascrie înainte de următorul control (acoperă doar N zile, controlul e la M zile).” | [S13] | Parțial |
| H3 | Jurnal de imprimare activ | `Microsoft-Windows-PrintService/Operational` activat (dezactivat implicit), eveniment 307; numele documentului apare doar cu politica „Allow job name in event logs” [S38] | „Imprimarea nu e înregistrată.” (detalii în `PRINT_TRACKING_RESEARCH.md`) | [S38] | Nu (stare); Da (`wevtutil sl`, doar recomandare) |
| H4 | Subcategorii STIG lipsă din lista curentă | De comparat lista existentă cu: PNP Activity (AU-000045), Removable Storage S/F (AU-000085/-000090), Other Object Access (AU-000083/-000084), Handle Manipulation (AU-000583/-000584), Registry (AU-000586/-000589), MPSSVC Rule-Level (AU-000575/-000580), Detailed File Share (AU-000570) [S13] | „Lipsește înregistrarea pentru …” | [S13] | Da |
| H5 | Sursa de timp | `w32tm /query /configuration` și `/status` [S17]; pe stație izolată fără NTP: tipul (`NoSync`/`NTP` spre o sursă internă aprobată) și abaterea față de ceasul de referință al controlorului **[NECITAT]**; ANSSI: sincronizarea timpului e prima recomandare de jurnalizare [S50] | „Ora stației diferă cu N minute de ora corectă: orele din jurnale nu sunt de încredere.” | WN11-00-000260 (CAT III, formulat pentru domeniu) [S17] | `/configuration`: Da [S17] |

### 2.I Actualizări și antivirus în regim offline

| # | Control | Cum se citește | Ce i se spune controlorului | Referințe | Admin |
|---|---|---|---|---|---|
| I1 | Nivel de servicing suportat | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion` (`DisplayVersion`, `CurrentBuild`, `UBR`) comparat cu un tabel offline de versiuni suportate livrat cu aplicația **[NECITAT]** | „Versiunea de Windows nu mai primește actualizări de securitate.” | WN11-00-000040 [S13] | Nu |
| I2 | Data ultimei actualizări instalate | `Win32_QuickFixEngineering.InstalledOn` / `Get-HotFix` **[NECITAT]**; comparată cu registrul de actualizări din PrOpSec | „Ultima actualizare de securitate e din …; procedura cere lunar.” | WN11-00-000025 (adaptat offline) [S13] | Nu |
| I3 | Defender: platformă, motor, ultima scanare completă, detecții | `Get-MpComputerStatus` (`AMProductVersion`, `AMEngineVersion`, `FullScanEndTime`, `AntivirusSignatureLastUpdated`) **[NECITAT]**; sursele și ordinea de actualizare (fallback order, partajare UNC) [S36] | „Antivirusul nu a mai făcut o scanare completă din …” / „Semnăturile au fost actualizate manual pe …, dintr-un pachet mpam-fe.exe.” Pachetul offline se rulează direct [S36] | WN11-00-000045 [S13]; SYS.2.2.3.A5 [S48] **[NESIGUR]** | Nu |
| I4 | Antivirus terț | `root\SecurityCenter2` `AntiVirusProduct` **[NECITAT]** | „Stația folosește alt antivirus decât Defender: X, stare Y.” | WN11-00-000045 [S13] | Nu |

### 2.J Integritate configurație

| # | Control | Cum se citește | Ce i se spune controlorului | Referințe | Admin |
|---|---|---|---|---|---|
| J1 | Certificate rădăcină neașteptate | `Cert:\LocalMachine\Root` comparat cu un instantaneu de referință **[NECITAT]**; fișiere de certificat software rămase (WN11-00-000130) [S13] | „Pe stație a apărut un certificat de încredere nou (emitent X, data Y).” | [S13] | Nu |
| J2 | Diferențe față de instantaneul anterior al politicii locale | Export LGPO / comparare stil Policy Analyzer [S25] | „Față de controlul trecut s-au schimbat N setări de securitate (lista).” | [S25] | Da (LGPO) |

---

## 3. Verificări procedurale (aplicația doar le sprijină)

Fiecare întrebare are o dovadă care poate fi atașată la caz (fotografie, număr de înregistrare, document scanat) și, unde există, o corelare automată cu datele stației.

| # | Întrebare / document | Cu ce se compară în aplicație | Sursă |
|---|---|---|---|
| P1 | Certificatul de acreditare al SIC: valabil? nivel maxim? stația e în perimetrul acreditat? | Nume stație, nivel din banner (G3), etichete de volum | [S4][S9] |
| P2 | DCS și PrOpSec aprobate (versiunea în vigoare) | Profilul de procedură: orar, software aprobat, medii, utilizatori, directoare clasificate | [S4][S5] |
| P3 | Registrul mediilor de stocare: verificat periodic, fiecare mediu cu proprietar identificabil | Dispozitivele USB/CD văzute pe stație vs. registru (există deja) | ISM-1713 [S46]; 800-171 03.08.07 [S45] |
| P4 | Fluxul de introducere a datelor: stație de decontaminare (sheep-dip / „station blanche”), diodă de date, medii scriere-unică | Pentru fiecare mediu introdus: există înregistrare de scanare înainte de prima conectare la stație? | ANSSI [S51]; ITSB-112 [S55] |
| P5 | Registrul actualizărilor offline (data, sursa, hash-ul pachetului, cine a adus mediul) | I2, I3: data ultimei actualizări/semnături vs. registru | [S36]; WN11-00-000025 [S13] |
| P6 | Registrul exporturilor de jurnale (cine, când, pe ce mediu, unde se păstrează, cât timp) | Ștergeri de jurnal (1102, există), retenție (H2) | HG 585 audit (`HG585_ACCREDITATION_REQUIREMENTS.md` §1.7) |
| P7 | Parola BIOS/UEFI: setată, păstrată sigilat, cine o cunoaște; ordinea de pornire doar de pe disc intern | B6, B1, B7 | SC-41 [S44] **[NESIGUR]** (legătură indirectă) |
| P8 | Sigilii fizice pe carcasă și porturi; porturi dezactivate fizic | Dispozitive PnP noi față de controlul anterior | SC-41: dezactivarea fizică e mai puternică decât cea logică [S44] |
| P9 | Camera/microfonul: acoperite, deconectate, dezactivate în BIOS | E4, E5 | WN11-CC-000007 [S15] |
| P10 | Ceasul: există un ceas de referință? Cine poate schimba ora? Abaterea măsurată la control | H5, C6 („Change the system time”), evenimente 4616 (există) | [S17][S50] |
| P11 | Lista utilizatorilor autorizați (certificat de securitate/autorizație, need-to-know) | Registrul de utilizatori (există) vs. conturi locale și logări | [S8] pct. 19 |
| P12 | Registrul de imprimare și marcarea documentelor tipărite | H3 (evenimente 307) vs. registru | `PRINT_TRACKING_RESEARCH.md` |
| P13 | Registrul incidentelor de securitate și raportarea lor | Constatări „critice” ale aplicației fără incident corespondent | HG 585 incidente (`HG585_ACCREDITATION_REQUIREMENTS.md` §1.14) |
| P14 | Distrugerea/declasarea mediilor și a discurilor la scoaterea din uz | Medii din registru care nu mai apar și nu au proces-verbal | 2013/488 Anexa IV pct. 37–38 [S8] |
| P15 | Excepții aprobate (Bluetooth, inscriptor, VM etc.) | Fiecare constatare din §2 poate fi marcată „excepție aprobată prin documentul nr. …” | WN11-00-000210 (aprobare documentată) [S16] |

---

## 4. Recomandări prioritizate

Efort: **S** = o zi sau mai puțin, o singură sursă de citire; **M** = câteva zile, mai multe surse sau UI nou; **L** = săptămână+, sau testare pe hardware.

### P1 – impact mare asupra izolării și scurgerii de date

| Rang | Recomandare | Controale | Efort |
|---|---|---|---|
| 1 | Dovezi de încălcare a izolării: adaptoare Wi-Fi, profiluri WLAN, istoric `NetworkList` cu date | A1, A2 | S |
| 2 | Bluetooth (prezență, stare, serviciu) | A3 | S |
| 3 | Secure Boot + mod UEFI | B1, B2 | S |
| 4 | BitLocker extins: PIN la pornire, toate volumele fixe | B4 | M |
| 5 | Blocare la inactivitate ≤ 15 min + parolă la trezire | G1, G2 | S |
| 6 | Listă albă de aplicații: prezență, mod (audit/blocare), evenimente 8003/8004/3076/3077 | D1, D2 | M |
| 7 | AutoPlay/AutoRun | D3 | S |
| 8 | Nivel Windows suportat + data ultimei actualizări (tabel offline de versiuni) | I1, I2 | S |
| 9 | Defender: ultima scanare completă, versiuni, istoric detecții | I3, I4 | S |
| 10 | Audit de fișiere cu SACL pe directoarele clasificate din profil | H1 | M |
| 11 | Mesaj legal cu nivelul de clasificare, comparat cu profilul | G3 | S |
| 12 | Cameră și microfon | E4, E5 | S |
| 13 | Restricții de instalare dispozitive + clase de stocare non-USB (CD/DVD, WPD) | E1, E2, E3 | S |
| 14 | Recall/Copilot | F4 | S |

### P2 – întărire și calitatea dovezilor

| Rang | Recomandare | Controale | Efort |
|---|---|---|---|
| 15 | Credential Guard / VBS / HVCI + protecție LSA | C1, C2 | S |
| 16 | PowerShell: v2 eliminat, script block logging, transcriere, mod de limbaj | D5, D6, D7 | S |
| 17 | Remanență: pagefile, hibernare, crash dump | F1, F2, F3 | S |
| 18 | Jurnale: retenție/ACL Application/System, jurnal de imprimare, subcategoriile STIG lipsă | H2, H3, H4 | S |
| 19 | Sursa de timp și abaterea măsurată (cu câmp „ora de referință” introdus de controlor) | H5, P10 | S |
| 20 | Drepturi de utilizator critice + SID-uri orfane | C6, C7 | M |
| 21 | Alte căi de interconectare (partajări, Remote Assistance, WinRM, portproxy, ICS, funcții) | A4 | S |
| 22 | VM-uri și sisteme de operare alternative | A5 | S |
| 23 | Vechimea parolei adminului local; cache de credențiale, LM, NTLM | C3, C5 | S |
| 24 | Sysmon prezent/activ (doar dacă PrOpSec îl cere) | D9 | S |
| 25 | TPM și protecție DMA | B3, B5 | S |
| 26 | Importul baseline-ului Microsoft (SCT) ca profil de comparație și al exportului LGPO | J2 | M |
| 27 | Liste de verificare procedurale P1–P15 legate de constatări, cu „excepție aprobată” | §3 | M |

### P3 – util, dar cu cost sau incertitudine mare

| Rang | Recomandare | Controale | Efort |
|---|---|---|---|
| 28 | Parola BIOS prin WMI de producător (Dell/HP/Lenovo), cu „nu se poate determina” onest | B6 | L |
| 29 | Comparare a depozitului de certificate rădăcină cu instantaneul anterior | J1 | M |
| 30 | Import de rezultate CIS-CAT / XCCDF ca dovadă externă (nu integrare OpenSCAP) | — [S53][S54] | M |
| 31 | Etichete NIST/STIG/ISM pe fiecare fișă de constatare (trasabilitate pentru auditori) | toate | M |

---

## 5. Riscuri și necunoscute

- **Valorile implicite nu sunt cerințele organizației.** STIG/CIS sunt americane și formulate pentru domeniu (de ex. Credential Guard, TPM, sursa de timp „DoD”) [S14][S17][S20]; valoarea care contează e cea din DCS/PrOpSec [S4]. Aplicația trebuie să afișeze „sursa regulii” și să permită suprascrierea din profil.
- **Ghidurile tehnice INFOSEC ORNISS de detaliu nu sunt publice** (sau nu au fost găsite) **[NESIGUR]**; nici directivele NATO de suport [S10][S11].
- **Multe verificări cer administrator** (TPM, BitLocker, `auditpol`, `secedit`, SACL, Security log). Dacă aplicația rulează neelevată, rezultatul trebuie să fie „nu s-a putut verifica”, nu „conform”.
- **Parola BIOS și ordinea de pornire** nu se pot citi generic; WMI de producător depinde de model și uneori de software suplimentar (Dell Command | Monitor) [S43].
- **Căi de registru [NECITAT]** (AutoPlay, hibernare, crash dump, banner, LM/NTLM, microfon) trebuie confirmate în laborator pe Windows 10 22H2 și Windows 11 23H2/24H2/25H2 înainte de a deveni reguli.
- **BSI SYS.2.2.3** și nivelurile Essential Eight au fost văzute doar prin surse secundare [S47][S48].
- **Evenimentele LSA 3065/3066** provin din documentație veche (Windows 8.1) [S26].
- **OpenSCAP pe Windows** e declarat nesuportat din 2022 [S54]; CIS-CAT Lite are subset limitat [S53].
- **Confidențialitate:** listarea istoricului rețelelor și a numelor de documente tipărite poate expune date personale/clasificate în raport; raportul trebuie marcat la nivelul stației (vezi `HG585_ACCREDITATION_REQUIREMENTS.md` §1.10).

---

## 6. Surse

- [S1] Legea 182/2002 (text SRI): https://www.sri.ro/assets/files/legislatie/2024/Lege_182.2002.pdf
- [S2] HG 585/2002 (text SIE): https://www.sie.ro/pdf/legislatie/585.pdf
- [S3] SRI, „Protecția informațiilor clasificate – întrebări și răspunsuri” (2020): https://sri.ro/assets/files/informatii-clasificate/protectia-informatiilor-clasificate-intrebari-si-raspunsuri-2020.pdf ; încadrarea pe niveluri: https://www.sri.ro/assets/files/informatii-clasificate/INCADRAREA_IN_NOI_CLASE_SI_NIVELURI_DE_SECRETIZARE.pdf
- [S4] Ordin ORNISS 18/2014 – Ghid DS-2 PrOpSec (MO 242/2014): https://legeaz.net/monitorul-oficial-242-2014/ordinul-guvernului-romaniei-orniss-18-2014
- [S5] Ordin ORNISS 17/2014 – Ghid DS-1 DCS (MO 225/2014): https://legeaz.net/monitorul-oficial-225-2014/ordinul-guvernului-romaniei-orniss-17-2014
- [S6] Ordin ORNISS 16/2014 – INFOSEC 2, directiva principală: https://legeaz.net/monitorul-oficial-262-2014/ordinul-guvernului-romaniei-orniss-16-2014/anexa-directiva-principala
- [S7] HG 781/2002 – referire la HG 585 (sursă secundară, rezumat de doctorat): https://www.univnt.ro/wp-content/uploads/doctorat/rezumate_doctorat/Pintea_Cristian.pdf ; SRI ghid corespondență 2024: https://sri.ro/fisiere/ghid/Ghid_practic_corespondenta_oficiala_2024.pdf
- [S8] Decizia 2013/488/UE, Anexa IV: https://www.legislation.gov.uk/eudn/2013/488/annex/IV
- [S9] Decizia 2013/488/UE, art. 10: https://www.legislation.gov.uk/eudn/2013/488/article/10/data.html
- [S10] NATO C-M(2002)49-REV1 (copie publică): https://www.uvns.hr/UserDocsImages/Nato%20-%20dokumenti/1%20C-M(2002)49-REV1%20-%20Security%20Within%20the%20NATO.pdf
- [S11] NATO AC/35-D/2004-REV4, Primary Directive on CIS Security (copie publică): https://www.nbu.cz/download/pravni-predpisy---nato/AC_35-D_2004-REV4_ENG_NHQD339286.pdf
- [S12] NATO AC/35-D/2005-REV2, INFOSEC Management Directive for CIS (copie publică): https://bip.abw.gov.pl/download/1/1213/AC35D2005REV2.pdf
- [S13] DISA Windows 11 STIG V2R5 – lista regulilor: https://stigaview.com/products/win11/v2r5
- [S14] WN11-CC-000075 Credential Guard: https://stigaview.com/products/win11/v2r8/WN11-CC-000075
- [S15] WN11-CC-000007 cameră: https://stigaview.com/products/win11/v2r5/WN11-CC-000007
- [S16] WN11-00-000210 Bluetooth: https://stigaview.com/products/win11/v2r5/WN11-00-000210
- [S17] WN11-00-000260 sursa de timp: https://stigaview.com/products/win11/v2r5/WN11-00-000260
- [S18] WN11-00-000035 listă albă: https://stigaview.com/products/win11/v2r5/WN11-00-000035
- [S19] WN11-SO-000070 inactivitate: https://stigaview.com/products/win11/v2r8/WN11-SO-000070 ; ScreenSaverIsSecure (TechNet arhivă): https://technet.microsoft.com/en-us/library/cc959646.aspx
- [S20] STIG Viewer V-253255 (TPM, WN11-00-000010): https://www.stigviewer.com/stigs/microsoft_windows_11/2026-05-19/finding/V-253255
- [S21] Tenable – DISA Windows 11 STIG v2r8 audit: https://www.tenable.com/audits/DISA_STIG_Microsoft_Windows_11_v2r8
- [S22] CIS – LSASS protected process „Enabled with UEFI Lock” (Tenable): https://www.tenable.com/audits/items/CIS_Microsoft_Windows_Server_2025_Stand-alone_v1.0.0_NG_MS.audit:e61b22b787c6be88c74e9bc3b6a4a898
- [S23] CIS Intune Windows 11 v4.0.0 – VBS (Tenable): https://www.tenable.com/audits/items/CIS_Microsoft_Intune_for_Windows_11_v4.0.0_L1.audit:7c677c1e53b9f7fca1c1d74c3cb60a94
- [S24] CIS Windows 11 Enterprise v4.0.0 BitLocker – DMA (Tenable): https://www.tenable.com/audits/items/CIS_Microsoft_Windows_11_Enterprise_v4.0.0_BitLocker.audit:0628612d3eb979e12bc9907f56719d9f
- [S25] Microsoft Security Compliance Toolkit (Policy Analyzer, LGPO): https://learn.microsoft.com/en-us/windows/security/operating-system-security/device-management/windows-security-configuration-framework/security-compliance-toolkit-10 ; descărcare: https://www.microsoft.com/download/details.aspx?id=55319
- [S26] Microsoft – Configuring Additional LSA Protection: https://technet.microsoft.com/library/dn408187.aspx ; https://petri.com/lsa-protected-mode-troubleshooting-tips-server-2012-r2-windows-8-1/
- [S27] Microsoft Q&A – Win32_DeviceGuard: https://learn.microsoft.com/en-us/answers/questions/951827/use-get-ciminstance-to-get-win32-deviceguard
- [S28] Microsoft – Kernel DMA Protection: https://learn.microsoft.com/en-us/windows/security/hardware-security/kernel-dma-protection-for-thunderbolt ; OEM: https://learn.microsoft.com/nl-nl/windows-hardware/design/device-experiences/oem-kernel-dma-protection ; STIG W10 V-220902: https://www.stigviewer.com/stigs/microsoft_windows_10/2025-02-25/finding/V-220902
- [S29] Confirm-SecureBootUEFI: https://technet.microsoft.com/en-us/library/jj603041(v=wps.620).aspx ; cheia State: https://ninjaone.com/blog/confirm-if-secure-boot-is-enabled-or-disabled
- [S30] Windows LAPS policy settings: https://learn.microsoft.com/en-us/windows-server/identity/laps/laps-management-policy-settings ; NCSU: https://activedirectory.ncsu.edu/services/base-infrastructure/default-security-policies/laps-local-administrator-password-solution
- [S31] AppLocker – Event Viewer: https://learn.microsoft.com/windows/security/application-security/application-control/app-control-for-business/applocker/using-event-viewer-with-applocker
- [S32] App Control – event IDs: https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/operations/event-id-explanations
- [S33] PowerShell about_Logging_Windows: https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_logging_windows
- [S34] PowerShell about_Language_Modes: https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_language_modes ; __PSLockdownPolicy (arhivă): https://learn.microsoft.com/ja-jp/archive/blogs/kfalde/pslockdownpolicy-and-powershell-constrained-language-mode
- [S35] Sysmon: https://learn.microsoft.com/en-us/sysinternals/downloads/sysmon
- [S36] Defender – actualizări manuale: https://www.microsoft.com/en-us/wdsi/defenderupdates ; surse/fallback: https://learn.microsoft.com/en-gb/defender-endpoint/manage-protection-updates-microsoft-defender-antivirus
- [S37] Policy CSP WindowsAI (Recall): https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai
- [S38] PrintService 307: https://eventlogxp.com/blog/?p=394 ; https://www.papercut.com/kb/Main/LogPrintJobsInEventViewer/
- [S39] Shutdown: Clear virtual memory pagefile: https://technet.microsoft.com/en-us/itpro/windows/keep-secure/shutdown-clear-virtual-memory-pagefile ; cheia: https://www.techrepublic.com/article/how-to-delete-windows-11-page-file/
- [S40] Device installation restrictions: https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-deviceinstallation ; https://woshub.com/device-installation-forbidden-policy/
- [S41] Policy CSP ADMX RemovableStorage: https://github.com/MicrosoftDocs/windows-itpro-docs/blob/public/windows/client-management/mdm/policy-csp-admx-removablestorage.md
- [S42] NetworkList (Plaso): https://plaso.readthedocs.io/en/latest/_modules/plaso/parsers/winreg_plugins/networks.html ; Seatbelt NetworkProfiles: https://docs.specterops.io/ghostpack-docs/Seatbelt-mdx/commands/networkprofiles
- [S43] Parolă BIOS: Dell https://www.dell.com/support/kbdoc/el-gr/000146401/check-if-a-bios-password-is-set ; Lenovo https://download.lenovo.com/pccbbs/thinkvantage_en/cdrt_resources/2020_supplement.pdf ; HP https://forum.bigfix.com/t/wmi-relevance-for-hp-bios-password-existence/47671
- [S44] NIST SP 800-53 r5 (catalog oficial): https://csrc.nist.gov/pubs/sp/800/53/r5/upd1/final ; SC-41: https://opensecurityarchitecture.org/controls/sc-41 ; SC-15: https://www.upguard.com/compliance/nist-sp-800-53/sc/sc-15
- [S45] NIST SP 800-171 r3: https://csrc.nist.gov/pubs/sp/800/171/r3/final ; rezumate: https://compliance.theartofservice.com/controls/nist-sp-800-171-rev-3/03-01-10 , https://compliance.theartofservice.com/controls/nist-sp-800-171-rev-3/03-04-08 , https://compliance.theartofservice.com/controls/nist-sp-800-171-rev-3/03-08-07
- [S46] ACSC ISM – Guidelines for media (sept. 2026): https://www.cyber.gov.au/sites/default/files/2026-08/14.%20ISM%20-%20Guidelines%20for%20media%20%28September%202026%29.pdf ; ISM: https://www.cyber.gov.au/node/514
- [S47] Essential Eight: https://blueprint.asd.gov.au/security-and-governance/essential-eight/ ; rezumat maturitate: https://opensecurityarchitecture.org/frameworks/asd-essential-eight
- [S48] BSI SYS.2.2.3 (citări secundare): https://www.leitstelle-lausitz.de/wp-content/uploads/2021/12/A08_Checkliste_System.pdf ; BSI Umsetzungshinweise: https://bsi.bund.de/DE/Themen/Unternehmen-und-Organisationen/Standards-und-Zertifizierung/IT-Grundschutz/IT-Grundschutz-Kompendium/Umsetzungshinweise/Umsetzungshinweise.html
- [S49] ANSSI – Windows 10, securitate bazată pe virtualizare: https://cyber.gouv.fr/sites/default/files/2017/11/np_securisation_windows10_securite_reposant_sur_la_virtualisation_v1.pdf
- [S50] ANSSI-PB-090 – jurnalizarea Windows în AD: https://cyber.gouv.fr/sites/default/files/2022/01/anssi-guide-recommandations_securite_journalisation_systemes_microsoft_windows_environnement_active_directory.pdf
- [S51] ANSSI – Profil „sas et station blanche”: https://cyber.gouv.fr/sites/default/files/2020/08/anssi-profil_de_fonctionnalites_et_de_securite-sas_et_station_blanche_reseaux_non_classifies-v1.0.pdf
- [S52] ANSSI – Windows 10 colectare de date: https://cyber.gouv.fr/sites/default/files/2017/01/np_securisation_windows10_collecte_de_donnees_v1.2.pdf
- [S53] CIS-CAT Lite: https://ciscat-assessor.docs.cisecurity.org/en/latest/Change%20Log/ ; eroare Windows 11: https://cisecurity.atlassian.net/wiki/x/OIBfu
- [S54] OpenSCAP: https://github.com/OpenSCAP/openscap/ ; https://www.open-scap.org/tools/openscap-base/
- [S55] CCCS ITSB-112 (medii amovibile, Protected C/clasificat): https://www.cyber.gc.ca/en/guidance/security-considerations-use-removable-media-devices-protected-c-and-classified-information
