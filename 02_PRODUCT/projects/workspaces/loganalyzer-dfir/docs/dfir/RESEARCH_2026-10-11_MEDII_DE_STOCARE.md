# Cercetare 2026-10-11: medii de stocare și dispozitive conectabile (model „Endpoint Protector”, fără driver)

Stare: cercetare web, `DOCUMENT_VERIFIED` doar unde sursa e citată. Nimic de aici nu a fost rulat pe o stație de test.
Marcaje: **[NESIGUR]** = sursă secundară, contradictorie sau lipsă; **[NECITAT]** = din practica DFIR, fără sursă găsită în această sesiune, de verificat în laborator (`FORENSIC_TEST_LAB.md`).
Constrângere de proiect: LogAnalyzer e instrument de control **read-only** (plus monitorizare live opțională), **nu scriem drivere kernel**.

---

## 1. Ce oferă produsele de tip Device Control

### 1.1 Endpoint Protector (CoSoSys, acum Netwrix) — Device Control

| Funcție | Ce face | Relevant pentru noi | Necesită driver/agent |
|---|---|---|---|
| Clase de dispozitive | Listă de ~45 tipuri: USB Storage Device, Internal/External CD/DVD RW, Internal Card Reader, Card Reader (MTD / SCSI), Local/Network Printers, Windows Portable Device (MTP), Android Smartphone (MTP), iPhone/iPad/iPod, Digital Camera, Webcam, Bluetooth (Radio, Smartphone, Headphones, Keyboard, Mouse, Other), FireWire Bus, Thunderbolt, Serial Port, Parallel Port (LPT), PCMCIA, Network Share, Thin Client Storage (RDP Storage), VM USB Device, Teensy Board, USB Modem, Additional Keyboard, Chip Card, Biometric, WiFi, Infrared, Audio In/Out ([Netwrix – Global Rights](https://docs.netwrix.com/docs/endpointprotector/admin/dc_module/globalrights)) | Da: e lista de clase pe care trebuie să o **inventariem** (nu să o blocăm) | Blocarea: da (agent cu componentă kernel **[NESIGUR]**, nedocumentat public). Inventarul: nu |
| Drepturi | Allow / Deny / Read-only; „Allow if Trusted Device Level 1–4”; implicit majoritatea tipurilor blocate ([Netwrix – Global Rights](https://docs.netwrix.com/docs/endpointprotector/admin/dc_module/globalrights)) | Doar ca model de afișare („ce ar fi permis”) | Da |
| Identificare per dispozitiv | Politici pe grup/utilizator/stație/tip dispozitiv ([newswire 2009](https://www.newswire.com/news/enhanced-on-the-road-protection-with-endpoint-protectors-new-offline-58059)); în practică VID/PID/serial **[NESIGUR]** – pagina consultată nu detaliază câmpurile | Da: cheia registrului aprobat = VID+PID+serial | Nu |
| Parole temporare offline (OTP) | Acces temporar și când nu există conexiune cu serverul; legate de un dispozitiv sau de stație+utilizator; 15 min – 30 zile; nu se refolosesc; „Universal OTP” pentru rețele izolate ([Netwrix – Offline Temporary Password](https://docs.netwrix.com/docs/endpointprotector/admin/otpassword), [release 5.1](https://www.endpointprotector.com/support/endpoint-protector-release-history/endpoint-protector-product-update-version-5.1.0.0)) | Nu (presupune blocare). Doar ca idee: registrul nostru poate avea „aprobare temporară până la data X” | Da |
| File Tracing | Înregistrează fișierele copiate pe/de pe dispozitive autorizate ([datasheet CoSoSys](https://www.ossir.org/paris/supports/2011/2011-04-05/Data_Sheet_Endpoint_Protector_Appliance_CoSoSys_ENG.pdf)) | Echivalent fără driver: evenimente 4663 cu auditul Removable Storage (§2.4) | Da (la ei) |
| File Shadowing | Păstrează pe server o copie a fișierelor transferate / care încalcă politica ([datasheet CoSoSys](https://www.ossir.org/paris/supports/2011/2011-04-05/Data_Sheet_Endpoint_Protector_Appliance_CoSoSys_ENG.pdf), [Netwrix KB](https://docs.netwrix.com/docs/endpointprotector/kb/enforced-encryption-easylock/managing-enforced-encryption-and-file-tracing-settings)) | **Nu** – incompatibil cu read-only și cu informații clasificate | Da |
| Enforced Encryption (EasyLock) | Criptare impusă pe stick-uri (Trusted Device nivel 1), File Tracing și Offline File Tracing (jurnal criptat pe stick, urcat ulterior) ([Netwrix KB](https://docs.netwrix.com/docs/endpointprotector/kb/enforced-encryption-easylock/managing-enforced-encryption-and-file-tracing-settings)) | Echivalentul nostru: **citim** starea BitLocker To Go (§4.4) | Da |
| Rapoarte | Logs, File Tracing, File Shadowing, Online Computers/Users, Connected Devices, Computer History ([manual](https://endpointprotector.com/support/pdf/manual/Endpoint_Protector-User_Manual.pdf)) | Da: „Dispozitive conectate acum” + „Istoric per stație” + „Istoric per dispozitiv” | Nu |

### 1.2 Microsoft Defender for Endpoint – Device Control (Windows 10/11)

Sursa unică: [Microsoft Learn – Device control overview](https://learn.microsoft.com/en-us/defender-endpoint/device-control-overview).
- Trei straturi: (a) **Windows nativ** – Device Installation Restrictions (după Device ID, Instance ID, setup class) + BitLocker; (b) **MDE device control** – acces granular per dispozitiv/tip/operație (read/write/execute)/grup de utilizatori/locație de rețea/tip fișier; (c) **Endpoint DLP** (Purview) cu captură de dovezi.
- Acoperire MDE: „Windows Portable Devices, Removable Media, CD/DVDs and Printers”; „removable media” = dispozitiv care **creează o literă de unitate**. Un telefon poate apărea de mai multe ori în Device Manager (disc + WPD).
- Câmpuri în evenimentele de audit: `SerialNumber`, `VendorId`, `ProductId`, `DeviceInstanceId`, `ClassName`, `DeviceDescription`, verdict (allow/deny); acțiuni `PnPDeviceAllowed`, `PnPDeviceBlocked`, `RemovableStoragePolicyTriggered`. Bun model pentru schema noastră.
- Imprimante: control după VID/PID și tip (rețea, USB). Bluetooth: prin politicile `Policy CSP – Bluetooth`.
- Fără servere; necesită clientul anti-malware ≥ 4.18.2103.3; serverele nu sunt suportate.
- Blocarea/auditul MDE se face de componenta Defender (kernel) – **nu reproducem**. Ce putem refolosi: modelul de câmpuri și distincția „removable media = are literă”.

### 1.3 DeviceLock (Acronis) și Ivanti Device Control (pe scurt)

- **DeviceLock**: profil **offline** separat de cel online (implicit offline când cablul de rețea lipsește) ([offline profile](https://www.acronis.com/en/support/documentation/DeviceLock/docs/14-DeviceLock-Security-Policies-Offline-Profile.14.54.htm)); **USB Devices White List** pe dispozitive individuale ([USB white list](https://www.acronis.com/en/support/documentation/DeviceLock/docs/hs5010.htm)); **Media White List** – identifică un CD/DVD anume după semnătura datelor ([media white list](https://www.acronis.com/en/support/documentation/DeviceLock/docs/hs5012.htm)); **Temporary White List** cu cod cerut de utilizator, logat ([temporary white list](https://www.acronis.com/en/support/documentation/DeviceLock/docs/15-DeviceLock-Temporary-White-List.15.3.htm)). Shadowing și arhitectura driverului: nu au apărut în surse **[NESIGUR]**.
- **Ivanti Device Control**: permisiuni temporare cu expirare automată; offline prin cod-cheie de la agent + cod de deblocare de la administrator; **shadowing** la nivel de nume de fișier sau fișier complet; criptare forțată ca permisiune; politici aplicate și offline ([Ivanti – Major features](https://help.ivanti.com/ht/help/en_US/IES/2023/UG_DC/major-features-device-control.htm), [produs](https://www.ivanti.com/products/device-control)).

**Concluzie §1.** Tot ce înseamnă *blocare*, *shadowing*, *criptare impusă*, *OTP* cere agent cu driver de filtrare și nu intră în LogAnalyzer. Ce intră: **inventar complet pe clase**, **identificare VID/PID/serial**, **registru aprobat + comparare**, **istoric per dispozitiv/stație**, **notificare live în user mode**, **citirea politicilor Windows care fac blocarea nativ** (§4.5) și **auditul de fișiere 4663** ca „file tracing” al săracului.

---

## 2. Artefacte Windows 10/11 care înregistrează conexiuni de dispozitive

Legendă coloane: **Implicit** = activ fără configurare; **Admin** = citire necesită drepturi de administrator pe stația vie; **Retenție** = cât rămâne.

### 2.1 Registru – hive SYSTEM (folosiți `Select\Current` pentru ControlSet-ul activ pe hive offline – [Elcomsoft 2026](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/))

| Cheie | Ce dă | Implicit | Admin | Retenție |
|---|---|---|---|---|
| `HKLM\SYSTEM\CurrentControlSet\Enum\USBSTOR\Disk&Ven_x&Prod_y&Rev_z\<serial>&0` | Producător, model, revizie, serial (cu sufix `&0`), `FriendlyName` ([Elcomsoft](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/), [forensics.wiki](https://forensics.wiki/usb_history_viewing/)) | Da | Nu pentru chei; `Properties` – vezi mai jos | Până la dezinstalarea dispozitivului / curățare |
| `...\Enum\USB\VID_xxxx&PID_yyyy\<serial>` | VID, PID, serialul real din descriptorul USB, `Service` (ex. `WUDFWpdMtp` la MTP), `Capabilities` ([Elcomsoft](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/)) | Da | Nu | Idem |
| `...\Enum\<dev>\<inst>\Properties\{83da6326-97a6-4088-9453-a1923f573b29}\0064 / 0065 / 0066 / 0067` | `0x64` InstallDate, `0x65` FirstInstallDate, `0x66` **LastArrivalDate**, `0x67` **LastRemovalDate**, FILETIME UTC ([Elcomsoft](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/), [NEC](https://jpn.nec.com/cybersecurity/blog/241011/index.html)). Notă: unele surse dau 0064 = „first install” ([studocu cheat sheet](https://www.studocu.com/row/document/jamaaة-alsoys/computer-network/dfir-cheat-sheet/59050860)) **[NESIGUR – sursele diferă; Elcomsoft e cea mai recentă]**. 0066/0067 doar Win8+ | Da | Cheia `Properties` are ACL restrictiv **[NECITAT]**; pe stația vie citiți prin API `CM_Get_DevNode_PropertyW` / `SetupDiGetDevicePropertyW` cu `DEVPKEY_Device_LastArrivalDate` etc., fără acces direct la cheie **[NECITAT]** | Se suprascrie la fiecare conectare/deconectare (doar ultima valoare) |
| `...\Enum\SCSI\Disk&Ven_...` | Discuri SATA/NVMe/UAS (inclusiv SSD externe UASP care **nu** apar în USBSTOR) **[NECITAT]**; MDE menționează explicit cazul UAS ([MS Learn](https://learn.microsoft.com/en-us/defender-endpoint/device-control-overview)) | Da | Nu | Idem |
| `...\Enum\SWD\WPDBUSENUM\...` | Telefoane/camere MTP/PTP și volume WPD: `FriendlyName`, producător, Container ID; populare „nu garantată” ([Elcomsoft](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/)) | Da | Nu | Idem |
| `...\Enum\BTHENUM\...`, `...\Enum\BTHLE\...` | Dispozitive Bluetooth clasice / Low Energy enumerate **[NECITAT]** | Da | Nu | Idem |
| `HKLM\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices\<MAC>` | Dispozitive Bluetooth **împerecheate**: adresă MAC, nume (binar), valori de timp „LastSeen/LastConnected” **[NESIGUR]**; calea confirmată de [Bustr](https://github.com/rootm0s/Bustr); MS documentează doar valorile COD din `Parameters` ([MS Learn – Bluetooth registry entries](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-registry-entries)) | Da | Probabil da (cheile de legătură sunt protejate) **[NECITAT]** | Până la dezîmperechere |
| `HKLM\SYSTEM\MountedDevices` | `\DosDevices\E:` și `\??\Volume{GUID}` → date binare: pentru USB, șir cu calea `USBSTOR#...#<serial>`; pentru discuri MBR, semnătură + offset ([Magnet](https://www.magnetforensics.com/blog/artifact-profile-usb-devices/)) | Da | Nu **[NECITAT]** | Literele se reutilizează; GUID-ul de volum rămâne |
| `HKLM\SYSTEM\CurrentControlSet\Enum\PCI\...` + controler Thunderbolt | Thunderbolt/USB4 apare ca PCIe; nu există artefact dedicat documentat **[NECITAT]** | Da | Nu | Idem |
| `HKLM\SYSTEM\CurrentControlSet\Control\Storage\HotPlugSecureOpen` | Unele versiuni Win10 cer valoarea 1 ca să se logheze auditul Removable Storage ([MS Learn – Monitor removable storage](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/auditing/monitor-the-use-of-removable-storage-devices)) | – | Nu | – |

### 2.2 Registru – hive SOFTWARE și NTUSER

| Cheie | Ce dă | Implicit | Admin | Retenție |
|---|---|---|---|---|
| `HKLM\SOFTWARE\Microsoft\Windows Portable Devices\Devices\<id>` | `FriendlyName` = **eticheta volumului** ultima văzută + GUID pentru corelare cu MountPoints2 ([HackTricks mirror](https://git.koelner.dedyn.io/coelner/hacktricks/src/commit/fc77b585a92f0d02faf597ca456f1856496ad462/forensics/basic-forensics-esp/windows-forensics/interesting-windows-registry-keys.md)) **[NESIGUR – sursă secundară]** | Da | Nu | Persistent |
| `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\EMDMgmt\<prefix>__<Etichetă>_<VSN zecimal>` | Eticheta + **Volume Serial Number** (zecimal → convertiți hex pentru LNK) chiar după scoaterea dispozitivului; formatarea schimbă VSN, nu serialul fizic ([peripheral-core](https://docs.rs/crate/peripheral-core/0.8.2/source/src/emdmgmt.rs), [ForensicFocus](https://www.forensicfocus.com/forums/general/emdmgmt-query/)) | Doar dacă ReadyBoost a evaluat volumul; pe SSD sistem ReadyBoost e adesea inactiv **[NECITAT]** | Nu | Persistent |
| `HKLM\SOFTWARE\Microsoft\Windows Search\VolumeInfoCache\<Literă>` | `VolumeLabel`, `DriveType` per literă – anecdotic, slab documentat ([ForensicFocus](https://www.forensicfocus.com/forums/general/emdmgmt-query/)) **[NESIGUR]** | Da (cu Windows Search) | Nu | Doar ultima valoare per literă |
| `NTUSER.DAT\Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2\{GUID}` și `##server#share` | **Ce utilizator** a avut volumul montat (GUID volum ↔ MountedDevices); intrările `##server#share` = partajări de rețea **[NECITAT pentru ##]** ([Magnet](https://www.magnetforensics.com/blog/artifact-profile-usb-devices/)); LastWrite-ul nu e interpretabil uniform între versiuni ([ForensicFocus](https://www.forensicfocus.com/forums/general/last-written-on-mountpoints2/)) | Da | Nu (pentru propriul profil); hive-urile altor utilizatori: admin | Persistent |
| `NTUSER.DAT\Network\<Literă>` | Unități de rețea mapate persistent (`RemotePath`, `UserName`) **[NECITAT]** | Da | Idem | Cât timp maparea e persistentă |
| `NTUSER.DAT\...\Explorer\Map Network Drive MRU` | Ultimele căi mapate manual **[NECITAT]** | Da | Idem | MRU |
| `NTUSER.DAT\...\Explorer\AutoplayHandlers`, `...\Portable Devices` | Interacțiunea utilizatorului cu dispozitive portabile ([Elcomsoft](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/)) | Da | Idem | Persistent |

### 2.3 Fișiere

| Fișier | Ce dă | Implicit | Admin | Retenție |
|---|---|---|---|---|
| `%SystemRoot%\INF\setupapi.dev.log` | Fiecare instalare de dispozitiv/driver cu ID și oră; **prima** instalare a unui dispozitiv pe această instalare Windows; ore în **ora locală**, nu UTC ([MS Learn – SetupAPI text logs](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/setupapi-text-logs), [MS Learn – log entries](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/setupapi-device-installation-log-entries), [Elcomsoft](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/)) | Da | Citirea de obicei nu cere admin **[NECITAT]** | Se rotește (fișiere `.log` vechi); nu e probă absolută a primei conectări |
| LNK (`%APPDATA%\Microsoft\Windows\Recent\*.lnk`) | Calea țintă (ex. `E:\...`), timpi, **VSN** și tipul unității sursă → fișier deschis de pe stick ([Elcomsoft](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/)) | Da | Nu (propriul profil) | Până la ștergere |
| Jump Lists (`AutomaticDestinations-ms`, `CustomDestinations-ms`) | Căi + VSN, ca LNK ([Elcomsoft](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/)) | Da | Idem | Limitat per aplicație |
| ShellBags (`NTUSER.DAT`, `UsrClass.dat`) | Navigare în foldere, inclusiv căi GUID pe MTP ([Elcomsoft](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/)) | Da | Idem | Persistent |
| Amcache, BAM/DAM, PCA (Win11) | Execuție de programe, eventual de pe volum amovibil ([Elcomsoft](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/)) | Da | Admin pentru Amcache/BAM **[NECITAT]** | Variabil |

### 2.4 Jurnale de evenimente

| Canal / ID | Ce dă | Implicit | Admin | Retenție |
|---|---|---|---|---|
| `Microsoft-Windows-Partition/Diagnostic` **1006** | La montare/demontare: producător, model, **serial**, capacitate, tabela de partiții, **MBR/VBR** (`Vbr0` → **VSN**) – permite VSN pentru volume deja formatate ([Elcomsoft](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/), [HackMD](https://hackmd.io/@M4shl3/USB-Forensics), [NCS](https://blog.ncst.com/usb-forensics-recover-more-volume-serial-numbers-with-the-windows-10-partition-diagnostic-event-log1678811064), [NSFOCUS](https://blog.nsfocus.net/win10usb/)) | Da în practică pe Win10 1709+ **[NECITAT – de confirmat]**; actualizările majore pot goli canalul | Citire canal: de obicei da **[NECITAT]** | Mărimea canalului (circular) |
| `Microsoft-Windows-Kernel-PnP/Configuration` **400** (started), **410** (migrated), **430** (installed); **411** = eroare la pornire | `DeviceInstanceId` (cu serialul), `DriverName` ([usb-forensic crate](https://docs.rs/crate/usb-forensic/0.3.0/source/src/sources/kernel_pnp.rs), [NXLog](https://docs.nxlog.co/integrations/os/windows-usb-auditing.html), [MS Q&A](https://learn.microsoft.com/en-gb/answers/questions/1200807/what-does-microsoft-windows-kernel-pnp-configurati)). **420**: nicio sursă **[NESIGUR]** | Da | Idem | Circular |
| `Microsoft-Windows-DriverFrameworks-UserMode/Operational` **2003, 2010, 2100, 2101, 2102** (+ 10000–10002) | Încărcare/descărcare driver UMDF la conectare/scoatere; 2100/2102 la scoatere ([Sigma](https://detection.fyi/sigmahq/sigma/windows/builtin/driverframeworks/win_usb_device_plugged/), [TechRepublic](https://www.techrepublic.com/article/how-to-track-down-usb-flash-drive-usage-in-windows-10s-event-viewer/), [Elcomsoft](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/)) | **Nu** – dezactivat implicit ([TechRepublic](https://www.techrepublic.com/article/how-to-track-down-usb-flash-drive-usage-in-windows-10s-event-viewer/), [NXLog](https://docs.nxlog.co/integrations/os/windows-usb-auditing.html)) | Activarea: admin | Circular |
| `Microsoft-Windows-Storage-ClassPnP/Operational` **507**, 512 | 507 poate conține vendor/product și serial ([Elcomsoft](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/)) | **[NESIGUR]** | – | Circular |
| `Microsoft-Windows-WPD-MTPClassDriver/Operational` | Conectare MTP reușită: model, producător, uneori firmware ([Elcomsoft](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/)) | **[NESIGUR]** | – | Circular |
| `Microsoft-Windows-DeviceSetupManager/Admin`, System/`UserPnp` | Activitate de instalare (context MTP) ([Elcomsoft](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/)) | Da **[NECITAT]** | – | Circular |
| `Microsoft-Windows-VHDMP/Operational` **1** (montare), **2** (demontare), **12** (tip imagine + cale `VhdFile`) | Montări ISO/VHD/VHDX; ISO se montează și fără admin, VHD cere admin ([Velociraptor](https://docs.velociraptor.app/exchange/artifacts/pages/windows.detection.isomount), [PSGumshoe](https://powershellgallery.com/packages/PSGumshoe/2.0.13/Content/EventLog/Get-EventVHDImageMount.ps1), [MS Learn – Mount-DiskImage](https://learn.microsoft.com/en-us/powershell/module/storage/mount-diskimage)) | Da **[NECITAT]**; semnificația 1/2 doar din Velociraptor **[NESIGUR]** | – | Circular |
| `Microsoft-Windows-Ntfs/Operational` **142/145** | Nu am găsit nicio sursă care să descrie aceste ID-uri **[NESIGUR – de testat în laborator]** | ? | ? | ? |
| Security **6416** („A new external device was recognized”) | Fiecare recunoaștere de dispozitiv extern (nu doar prima), cu ID-uri hardware **[câmpuri NECITATE]** ([MS – Audit PNP Activity](https://technet.microsoft.com/de-de/itpro/windows/keep-secure/audit-other-system-events), [rootusers](https://www.rootusers.com/configure-the-audit-pnp-activity-policy/), STIG Win11 V-253311 [stigviewer](https://www.stigviewer.com/stigs/microsoft_windows_11_v2r8/2026-05-19/finding/V-253311)) | **Nu** – cere *Detailed Tracking > Audit PNP Activity = Success* | Da (log Security) | Mărimea log Security |
| Security **4663** (succes) / **4656** (eșec), Task Category = *Removable Storage* | Utilizator, fișier, tip acces (citire/scriere) pe medii amovibile = **file tracing nativ** ([MS Learn – Monitor removable storage](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/auditing/monitor-the-use-of-removable-storage-devices), [MS Learn – 4663](https://learn.microsoft.com/ja-jp/previous-versions/windows/it-pro/windows-10/security/threat-protection/auditing/event-4663), [Lumifi](https://lumificyber.com/blog/tracking-removable-storage-with-the-windows-security-log)) | **Nu** – cere *Object Access > Audit Removable Storage*; generează pentru toate obiectele fără SACL ([MS Learn – audit-removable-storage](https://learn.microsoft.com/fr-fr/windows/security/threat-protection/auditing/audit-removable-storage)); uneori `HotPlugSecureOpen=1` | Da | Volum mare → rotire rapidă a log-ului Security |

**Capcană de timp**: registrul și jurnalele sunt în UTC, `setupapi.dev.log` în ora locală ([Elcomsoft](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/)).

---

## 3. Detecție live în user mode (fără driver)

| Mecanism | Ce primești | Serial/VID/PID? | Observații |
|---|---|---|---|
| `WM_DEVICECHANGE` + `RegisterDeviceNotification` cu `DEV_BROADCAST_DEVICEINTERFACE` pentru `GUID_DEVINTERFACE_DISK` `{53F56307-B6BF-11D0-94F2-00A0C91EFB8B}` ([MS Learn](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/guid-devinterface-disk)), `GUID_DEVINTERFACE_VOLUME`, `GUID_DEVINTERFACE_WPD` `{6AC27878-A6FA-4155-BA85-F98F491D4F33}` ([MS Learn – WPD interface GUIDs](https://learn.microsoft.com/en-us/windows/win32/wpd_sdk/device-interface-guids), valoarea confirmată indirect de [WpdDeviceInspector](https://learn.microsoft.com/en-sg/WINDOWS-HARDWARE/DRIVERS/portable/using-the-wpddeviceinspector-tool)) | `DBT_DEVICEARRIVAL` / `DBT_DEVICEREMOVECOMPLETE` cu calea interfeței (`\\?\USBSTOR#Disk&Ven..#<serial>&0#{guid}`) | Da, din calea simbolică | Necesită fereastră (WPF: `HwndSource`) sau handle de serviciu. Volumele vin implicit ca `DEV_BROADCAST_VOLUME` cu `dbcv_unitmask` (bit 0 = A:), un mesaj poate conține mai multe litere; `DBTF_MEDIA` = schimbare de mediu (CD) ([MS Learn – DEV_BROADCAST_VOLUME](https://learn.microsoft.com/en-us/windows/win32/api/dbt/ns-dbt-dev_broadcast_volume), [Detecting media insertion](https://learn.microsoft.com/en-us/windows/win32/DevIO/detecting-media-insertion-or-removal)) |
| `CM_Register_Notification` cu `CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE` | Callback la sosire/plecare interfață; fără fereastră | Da, din `SymbolicLink` | Recomandat de Microsoft de la Win8 în sus; `RegisterDeviceNotification` doar pentru Win7 ([MS Learn](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_register_notification)). Capcană: `..._DEVICEHANDLE` e pentru notificări pe un handle deschis, nu pentru sosiri |
| WMI `Win32_VolumeChangeEvent` | `DriveName`, `EventType` (2 = arrival, 3 = removal) | Nu | Eveniment extrins, fără polling; **unitățile de rețea nu sunt suportate** ([MS Learn](https://learn.microsoft.com/en-au/windows/win32/cimwin32prov/win32-volumechangeevent)) |
| WMI `Win32_DeviceChangeEvent` | Doar `EventType` | Nu | Semnal „s-a schimbat ceva”, trebuie re-enumerat ([MS Learn](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-devicechangeevent)) |
| WMI `__InstanceCreationEvent WITHIN n WHERE TargetInstance ISA 'Win32_PnPEntity'` (sau `Win32_DiskDrive`, `Win32_Volume`) | Instanța completă (`PNPDeviceID`, `SerialNumber` la DiskDrive) | Da | **Polling** – fără provider de evenimente WMI face interogări periodice; Microsoft recomandă intervale mari ([MS Learn – WITHIN](https://learn.microsoft.com/en-us/windows/win32/wmisdk/within-clause), [blog WMI](https://learn.microsoft.com/en-us/archive/blogs/wmi/using-within-in-a-wmi-event-query)). Cost CPU la intervale de 1–2 s |

**Recomandare**: `CM_Register_Notification` (DISK, VOLUME, WPD, plus `GUID_DEVINTERFACE_USB_DEVICE` și interfețele Bluetooth **[NECITAT pentru GUID-uri]**) ca semnal, apoi **re-enumerare** prin SetupAPI/CfgMgr pentru detalii. `Win32_VolumeChangeEvent` ca rezervă simplă. Unitățile de rețea: re-citire periodică (`GetLogicalDrives` / `WNetOpenEnum`) **[NECITAT]**.

### 3.1 Lanțul volum → disc → dispozitiv USB → serial

1. Literă → volum: `\\.\E:` → `IOCTL_STORAGE_GET_DEVICE_NUMBER` dă `DeviceNumber` (= `PhysicalDriveN`) **[NECITAT, API documentat în winioctl.h]**.
2. Disc → descriptor: `IOCTL_STORAGE_QUERY_PROPERTY` (`StorageDeviceProperty`) → `STORAGE_DEVICE_DESCRIPTOR` cu `VendorIdOffset`, `ProductIdOffset`, `SerialNumberOffset`, `BusType` (USB, SATA, NVMe, SD, ...) ([MS Learn](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-storage_device_descriptor), [DDI](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddstor/ns-ntddstor-_storage_device_descriptor)). Funcționează fără admin cu acces 0 la handle **[NECITAT]**. Serialul de aici poate diferi de cel USB (unele punți îl codifică hex/inversat) **[NECITAT]**.
3. Disc → nod PnP: `SetupDiGetClassDevs(GUID_DEVINTERFACE_DISK)` + potrivire pe `DeviceNumber`, apoi `CM_Get_Parent` repetat: `USBSTOR\Disk&Ven..\<serial>&0` → `USB\VID_xxxx&PID_yyyy\<serial>` (sursa VID/PID/serial real) **[NECITAT]**.
4. Varianta WMI: `Win32_DiskDrive.PNPDeviceID` (`USBSTOR\DISK&VEN_...\<serial>&0`) → `Win32_DiskDriveToDiskPartition` → `Win32_LogicalDiskToPartition` → `Win32_LogicalDisk` (literă, `VolumeName`, `VolumeSerialNumber`) **[NECITAT]**.
5. Timpi pe stația vie: proprietățile `DEVPKEY_Device_FirstInstallDate`, `..._LastArrivalDate`, `..._LastRemovalDate` (aceleași 0x65/0x66/0x67) prin `CM_Get_DevNode_PropertyW` **[NECITAT]**.

### 3.2 Capcane

- **Sufixul `&0`** la USBSTOR: nu face parte din serialul fizic; serialul real e sub `Enum\USB\VID&PID\<serial>` ([ForensicFocus](https://www.forensicfocus.com/forums/general/usb-serials/)). Formatul instance ID e opac și OS poate adăuga `&0` din alte motive ([OSR](https://community.osr.com/discussion/comment/279905)).
- **Al doilea caracter `&`** în instance ID ⇒ ID generat de Windows, dispozitivul **nu are serial unic**; ID-ul depinde de port/hub, deci același stick pe alt port = „dispozitiv nou” ([forensics.wiki](https://forensics.wiki/usb_history_viewing/), [OSR](https://community.osr.com/discussion/comment/279905)). În registru marcați „fără serial – identificare slabă (VID+PID+VSN)”.
- **Seriale clonate**: unele modele ieftine raportează același serial la toate exemplarele ([HackMD](https://hackmd.io/@M4shl3/USB-Forensics)) **[NESIGUR]**.
- **VSN** se schimbă la formatare; serialul fizic nu ([ForensicFocus](https://www.forensicfocus.com/forums/general/emdmgmt-query/)).
- Un telefon apare de mai multe ori (WPD + disc + MTP); grupați după **Container ID** ([MS Learn](https://learn.microsoft.com/en-us/defender-endpoint/device-control-overview), [Elcomsoft](https://blog.elcomsoft.com/2026/02/usb-device-forensics-on-windows-10-and-11/)).
- SSD-urile UASP apar sub `Enum\SCSI`, nu `USBSTOR` **[NECITAT]**.

---

## 4. Recomandări pentru LogAnalyzer

### 4.1 Clase de acoperit (inventar, nu blocare)
1. Discuri interne (HDD/SSD/NVMe) – baza de comparație.
2. Stocare USB (USBSTOR + SCSI/UAS) – stick-uri, discuri externe.
3. Cititoare de carduri SD/MMC (interne și USB).
4. Unități optice CD/DVD/BD (inclusiv mediu introdus: `DBTF_MEDIA`).
5. Telefoane/camere MTP/PTP (WPD).
6. Bluetooth (împerecheate + enumerate).
7. Thunderbolt/USB4/FireWire (ca controlere/dispozitive PCIe; doar prezență).
8. Unități de rețea mapate și partajări accesate.
9. Discuri virtuale: ISO/VHD/VHDX montate (VHDMP).
10. Imprimante/scanere USB (prezență + VID/PID; vezi `PRINT_TRACKING_RESEARCH.md`).

### 4.2 Câmpuri minime per dispozitiv (registru de control)
`Clasă` · `Producător` · `Model` · `VID` · `PID` · `Serial fizic` (fără `&0`) · `Serial unic? (da/nu – regula '&')` · `Container ID` · `Bus` (USB/SATA/NVMe/SD/BT/PCIe) · `Capacitate` · `Etichetă volum` · `VSN` (hex) · `Ultima literă` · `Prima instalare` (setupapi + 0x65) · `Ultima conectare` (0x66) · `Ultima deconectare` (0x67) · `Utilizatori care l-au montat` (MountPoints2) · `BitLocker To Go` (da/nu/necunoscut) · `Stare în registrul aprobat` (aprobat / neaprobat / aprobat-expirat / necunoscut) · `Surse` (lista artefactelor care confirmă, cu sha256 al fișierului sursă). Modelul de câmpuri e aliniat cu evenimentele MDE (`SerialNumber`, `VendorId`, `ProductId`, `DeviceInstanceId`, `ClassName`) ([MS Learn](https://learn.microsoft.com/en-us/defender-endpoint/device-control-overview)).

### 4.3 Vederea „live”
Ora · Ce s-a întâmplat („Stick USB conectat”, „Telefon conectat”, „CD introdus”, „Imagine ISO montată”) · Model · Serial · Literă · Utilizator activ · **Verdict față de registru** (verde „aprobat”, roșu „NU este în registrul aprobat”, galben „fără serial unic – nu poate fi identificat sigur”) · un singur buton „Ce fac acum?”.

### 4.4 Ce să-i spunem controlorului (cuvinte simple)
- „Acest stick a fost conectat ultima dată pe 03.10.2026 la 14:22 și scos la 14:40.”
- „Stick-ul nu are număr de serie propriu; Windows nu-l poate deosebi de altul la fel. Verificați fizic eticheta.”
- „Acest dispozitiv nu figurează în registrul de medii aprobate.”
- „Copierea de fișiere pe stick-uri NU este înregistrată pe această stație (auditul nu e pornit). Nu putem spune ce s-a copiat.”
- „Stick-ul este / nu este criptat cu BitLocker.”
- Limite spuse explicit: istoricul Windows păstrează doar **ultima** conectare per dispozitiv, nu toate; jurnalele se rotesc.

### 4.5 Ce mai e de obicei în asemenea unelte și ne poate lipsi
| Verificare | Unde | Sursă |
|---|---|---|
| **Removable Storage Access** (GPO) | `HKLM\Software\Policies\Microsoft\Windows\RemovableStorageDevices` (și HKCU): `Deny_All` la rădăcină; per clasă `Deny_Read` / `Deny_Write` / `Deny_Execute` sub `{53f5630d-b6bf-11d0-94f2-00a0c91efb8b}` (Removable Disks), `{53f56308-...}` (CD/DVD), `{53f56311-...}` (Floppy), `{53f5630b-...}` (Tape), `{6AC27878-A6FA-4155-BA85-F98F491D4F33}` (WPD), `Custom\Deny_Read|Deny_Write`; `AllowRemoteDASD`; `RebootTimeinSeconds` | [MS Learn – ADMX_RemovableStorage CSP](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-removablestorage). WPD are și un al doilea GUID `{F33FDC04-D1AC-4E8E-9A30-19BBD4B108AE}` în ADMX **[NESIGUR – nu apare pe pagina citită]** |
| **Device Installation Restrictions** | `HKLM\SOFTWARE\Policies\Microsoft\Windows\DeviceInstall\Restrictions`: `DenyRemovableDevices`, `DenyUnspecified`, `DenyDeviceIDs`/`AllowDeviceIDs`, `DenyInstanceIDs`/`AllowInstanceIDs`, `DenyDeviceClasses`; listele în subchei `...\DenyDeviceIDs` etc. **[NESIGUR pentru numele subcheilor]**; dispozitivele deja instalate rămân utilizabile dacă nu e bifat „retroactiv” | [MS Learn – Policy CSP DeviceInstallation](https://learn.microsoft.com/ga-ie/windows/client-management/mdm/policy-csp-deviceinstallation), [MS Learn – device control overview](https://learn.microsoft.com/en-us/defender-endpoint/device-control-overview), [Winaero](https://winaero.com/disable-installation-removable-devices-windows-10), [Microsoft365DSC schema](https://powershellgallery.com/packages/Microsoft365DSC/1.25.312.1/Content/DSCResources/MSFT_IntuneDeviceControlPolicyWindows10/MSFT_IntuneDeviceControlPolicyWindows10.schema.mof) |
| **BitLocker To Go impus** | `HKLM\SYSTEM\CurrentControlSet\Policies\Microsoft\FVE\RDVDenyWriteAccess` (1 = scriere interzisă pe necriptate), `HKLM\SOFTWARE\Policies\Microsoft\FVE\RDVDenyCrossOrg` – sursele se contrazic asupra căii **[NESIGUR]** | [Winaero](https://winaero.com/deny-write-access-to-removable-drives-not-protected-by-bitlocker), [GeekRewind](https://geekrewind.com/deny-write-access-to-unprotected-removable-drives-in-windows-11/) |
| **Starea BitLocker a unui stick** | `Win32_EncryptableVolume` (namespace `root\cimv2\Security\MicrosoftVolumeEncryption`) – **cere admin**; `ProtectionStatus` e instantaneu, `GetProtectionStatus` live; proprietatea Shell `System.Volume.BitLockerProtection` pare să meargă fără admin **[NESIGUR]** | [MS Learn – GetProtectionStatus](https://learn.microsoft.com/en-ie/Windows/Win32/secprov/getprotectionstatus-win32-encryptablevolume), [Lansweeper](https://community.lansweeper.com/t5/general-discussions/how-to-use-lspush-to-get-bitlocker-information/td-p/76510), [AutoIt forum](https://autoitscript.com/forum/topic/207944-non-elevated-bitlocker-status) |
| **AutoRun/AutoPlay** | `HKLM|HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\NoDriveTypeAutoRun` (0xFF = toate tipurile; HKLM are prioritate), `NoDriveAutoRun`; implicit 0x95 conform documentației (o sursă comunitară spune 0x91 **[NESIGUR]**) | [MS Learn – archived](https://learn.microsoft.com/en-us/previous-versions//cc959381(v=technet.10)), [MS Learn – AutoPlay reg](https://learn.microsoft.com/da-dk/windows/win32/shell/autoplay-reg), [CERT VU#889747](https://kb.cert.org/vuls/id/889747) |
| **Auditul de copiere (file tracing nativ)** | Verificați dacă *Audit Removable Storage* și *Audit PNP Activity* sunt active (`auditpol /get /subcategory:...`); dacă nu, spuneți controlorului că 4663/6416 lipsesc | §2.4 |
| **Log DriverFrameworks-UserMode** | Activ / inactiv (recomandare către administrator, nu activăm noi) | §2.4 |
| **Mărimea și ziua cea mai veche** din `Security`, `Partition/Diagnostic`, `Kernel-PnP/Configuration` | Ca să spunem „istoric disponibil din data X” | – |
| **Registrul aprobat** cu „aprobat până la” (echivalentul OTP pentru noi) și semnătura/hash-ul fișierului registru | – | idee derivată din §1 |

### 4.6 Ordinea de implementare sugerată (doar propunere)
1. Enumerare live completă (SetupAPI/CfgMgr) pe clasele §4.1 + lanțul §3.1.
2. Istoric din registru (USBSTOR/USB/SCSI/WPD/BTHENUM + DEVPKEY timpi + MountedDevices + MountPoints2 + EMDMgmt + Portable Devices) și din `setupapi.dev.log`.
3. Jurnale: Partition/Diagnostic 1006, Kernel-PnP 400/410/430, VHDMP 1/2/12, Security 6416/4663 (dacă există).
4. Verificări de politică §4.5 cu text simplu.
5. Notificare live cu `CM_Register_Notification` + comparare cu registrul.
6. Validare pe stația de test cu un stick cu serial, unul fără serial, un telefon MTP, un CD, un ISO și o unitate de rețea, înainte de orice afirmație `RUNTIME_VERIFIED`.

---

## 4bis. Stare implementare (2026-10-11)

Implementat, cu teste (`DeviceInventoryTests`):
- **`DeviceInventory`:**
  - SetupAPI pe toate clasele, inclusiv dispozitivele care nu mai sunt conectate. Pentru fiecare: timpii DEVPKEY de primă instalare, ultima conectare și ultima deconectare, plus seria, VID/PID, producătorul și modelul;
  - WMI pentru dispozitivele prezente: capacitate, seria reală a discului, litere, etichetă, sistem de fișiere, BitLocker (doar ca administrator);
  - `MountedDevices` și `Partition/Diagnostic 1006`, din care vin istoricul fiecărei conectări, legătura volum → serie prin `RegistryId` și mediile care apar doar în jurnal;
  - unitățile de rețea mapate și cele folosite (`Network`, `MountPoints2`).
- **`DeviceRules`:** clasificarea pe cele 10 clase §4.1; seria fără `&0`; un ID generat de Windows nu e tratat ca serie; compararea cu registrul (în registru, NU în registru, retras/distrus, expirat).
- **`DeviceWatcher`:** WMI `__InstanceCreation/DeletionEvent` pe `Win32_PnPEntity` (WITHIN 2), plus `Win32_VolumeChangeEvent`. `CM_Register_Notification` rămâne o îmbunătățire posibilă.
- **`DevicePolicyCheck`:** `RemovableStorageDevices` (global și pe clase), `USBSTOR Start`, `DeviceInstall\Restrictions`, `RDVDenyWriteAccess`, `NoDriveTypeAutoRun` și auditul Removable Storage / PnP. Ce nu se poate citi apare „necunoscut”, nu „protejat”.
- **Pagina „Medii de stocare și dispozitive”**, cu patru file: Acum (conectate acum și fluxul în timp real), Toate dispozitivele (listă și toate datele în cuvinte simple), Protecțiile Windows, Registrul de medii. Alerta pentru un mediu care nu e în registru apare pe orice pagină, cu un sunet. Fluxul se salvează în `media_live.jsonl` din cazul stației. „Preia de pe stație” folosește inventarul complet.
- **Verificat pe PC-ul proprietarului, fără administrator:** discul intern NVMe (seria reală, 1 TB, C: D:), un SSD extern pe USB (serie, VID:PID 0BDA:9201, 250 GB, F:), două dispozitive Bluetooth asociate. Volumul WPD al SSD-ului nu mai apare ca telefon.

Nevalidat încă pe stația de test: un stick fără serie, un telefon MTP, un CD, un ISO montat, o unitate de rețea, alerta live la conectare (§4.6 pasul 6).

## 5. Necunoscute deschise (de închis în laborator)
- Semnificația exactă a `Ntfs/Operational` 142/145 și `Kernel-PnP` 420.
- Dacă `Partition/Diagnostic` și `VHDMP/Operational` sunt active implicit pe Windows 11 24H2/25H2 și ce drepturi cere citirea lor.
- Numerotarea 0x64 vs 0x65 (InstallDate vs FirstInstallDate) – sursele se contrazic.
- Structura valorilor din `BTHPORT\Parameters\Devices` (timpi „last connected”).
- Dacă proprietatea Shell BitLocker merge fără admin.
- Calea corectă a `RDVDenyWriteAccess` (SYSTEM vs SOFTWARE).
- Al doilea GUID WPD din ADMX.
