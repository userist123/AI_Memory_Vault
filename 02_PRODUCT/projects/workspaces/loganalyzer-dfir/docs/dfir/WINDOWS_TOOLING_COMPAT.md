# LogAnalyzer — compatibilitatea uneltelor Windows folosite la COLECTARE și ACȚIUNE

Data: 2026-10-08. Autor: cercetare read-only (nicio modificare de cod). Codul auditat: `origin/main` la `1ad7882ae` (după PR #227 WP0 și #228 WP1), director `02_PRODUCT/projects/workspaces/loganalyzer-dfir/` (prescurtat `LD/`). Diferența 126a2bd01..1ad7882ae a fost verificată: nu atinge niciun mecanism de colectare/acțiune; numerele de linie de mai jos sunt de pe 1ad7882ae.

Cerința proprietarului (decizia 14 + regula finală): aplicația rulează la capacitate maximă pe un PC Windows gol (self-contained) și **nu folosește `wmic` nicăieri**. Înlocuitorii sunt Windows PowerShell 5.1 (inclus în Windows) sau mecanisme încorporate (API .NET în proces, COM, Win32). Pentru ediția P1 clasificată (care nu trebuie să pornească procese) se preferă API-ul în proces.

Legendă stare: REMOVED (eliminat din versiunea X) · REMOVED BY DEFAULT/FoD · DEPRECATED (încă prezent) · CHANGED BEHAVIOUR · STILL SUPPORTED · **neverificat** (nu am găsit sursă oficială Microsoft).

---

## Rezumat executiv

1. **Aplicația nu pornește `wmic.exe`, `cscript`/`wscript`/VBScript, PowerShell 2.0, `schtasks`, `sc`, `vssadmin`, `bitsadmin`, `at`, `fsutil`, `whoami`, `nltest`, `dsquery`.** Șirurile „wmic”, „vssadmin” etc. din cod sunt doar tipare de detecție (reguli) și rămân. Deci „de înlocuit” = **0 invocări wmic reale** (se confirmă prin grep pe `origin/main`, vezi Partea 2.6). WMIC e eliminat din Windows 11 24H2+ (august 2026), dar aplicația nu e afectată.
2. Aplicația folosește WMI **prin API** (`System.Management`, 9 locuri) — WMI în sine nu e afectat de dispariția WMIC (Microsoft: „WMI itself isn't affected”). Rămâne acceptabil, dar se pot oferi variante alternative (Partea 3).
3. Dependențe externe reale pornite ca procese: `powershell.exe` (script lipsă), `netsh.exe`, `wevtutil.exe`, `esentutl.exe`, `reg.exe`, `auditpol.exe`, `MpCmdRun.exe`. **Niciuna nu e eliminată pe vreo versiune Windows susținută**, dar:
   - `powershell.exe` + `Scripts\AuditCollector.ps1`: scriptul **nu există în `LD/`** (există doar în `LD/_recovered/desktop_mvp_scripts/AuditCollector.ps1`), iar codul are căutare cu cale de dezvoltator (`C:\Users\Marius\Desktop\...`). Funcția „colectare audit” e **defectă pe orice mașină**, indiferent de versiunea Windows.
   - `MpCmdRun.exe` și spațiul WMI `root\Microsoft\Windows\Defender` lipsesc dacă Defender nu e instalat/înlocuit — **MISSING pe unele configurații** (neverificat oficial).
   - `Win32_EncryptableVolume` (BitLocker WMI) lipsește când funcția BitLocker nu e instalată (ex. Server) — neverificat oficial; codul tratează excepția ca „gap”.
   - Scripturile generate pentru operator folosesc `Get-Mailbox`/`Connect-ExchangeOnline` (module Exchange, **nu sunt incluse în Windows**), vezi 2.5.
4. Prioritate WP-PKG: (P0) livrarea/înlocuirea `AuditCollector.ps1`; (P0) raportare „indisponibil pe acest OS / dezactivat” în acoperire (nu omitere tăcută); (P1) înlocuirea `netsh` cu `INetFwPolicy2` (cod existent), `wevtutil epl` cu `EventLogSession.ExportLog`, `auditpol` cu API Win32 deja existent (`AuditQuerySystemPolicy`); (P2) `reg save`/`esentutl` cu API în proces; (P3) restul. Detaliu în Partea 3.8.

---

# PARTEA 1 — Ce a eliminat/depreciat Microsoft (surse oficiale)

Pagini-cheie citite la data raportului (data actualizării paginii în paranteze):
- [Deprecated features in the Windows client](https://learn.microsoft.com/en-us/windows/whats-new/deprecated-features) (ms.date 2026-09-23)
- [Features and functionality removed in Windows client](https://learn.microsoft.com/en-us/windows/whats-new/removed-features) (ms.date 2026-08-25)
- [Resources for deprecated features](https://learn.microsoft.com/en-us/windows/whats-new/deprecated-features-resources) (2026-09-23)
- [Features Removed or No Longer Developed in Windows Server](https://learn.microsoft.com/en-us/windows-server/get-started/removed-deprecated-features-windows-server-2025) (redirecționează la pagina unificată `.../removed-deprecated-features-windows-server`, actualizată 2026-03-19; file pe taburi WS2025/2022/2019/2016)
- [WMIC removal from Windows (support.microsoft.com)](https://support.microsoft.com/en-us/topic/windows-management-instrumentation-command-line-wmic-removal-from-windows-e9e83c7f-4992-477f-ba1d-96f694b8665d)

## 1.1 Tabel principal

| Resursă | Stare | Detaliu citat/rezumat | URL |
|---|---|---|---|
| **WMIC** — Windows 10 22H2 | DEPRECATED (încă prezent) | Depreciat din Windows 10 21H1 și Windows Server 21H1 SAC; „This deprecation applies only to the WMI command-line (WMIC) utility; WMI itself is not affected.” Faptul că binarul e inclus implicit pe 22H2: pagina FoD listează FoD-ul WMIC doar „Windows 11, version 22H2 and later” → prezența pe Win10 **neverificat explicit** în sursă. | https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/wmic ; https://learn.microsoft.com/en-us/windows/whats-new/deprecated-features |
| WMIC — Windows 11 22H2/23H2 | FoD, preinstalat și activ (22H2); dezactivat implicit (23H2) | Articolul de suport: „WMIC … available in Windows 11, version 22H2 as a Feature on Demand, which was preinstalled and enabled by default”; „disabled by default in Windows 11, versions 23H2 and 24H2”. **Discrepanță între pagini:** pagina Deprecated (update ian. 2024) spune că pe 23H2 era încă preinstalat și „in the next release … disabled by default”. | support.microsoft.com (linkul de mai sus); deprecated-features |
| WMIC — Windows 11 24H2 / 25H2 / 26H1 | **REMOVED** | „WMIC is removed and is no longer available as a Feature on Demand (FoD) on Windows 11, version 24H2 and above” (update august 2026). Suport: eliminat prin actualizarea preview din august 2026 pentru 24H2 și 25H2, septembrie 2026 pentru 26H1; la upgrade la 25H2 este deja „removed, if already installed”. Pagina FoD: „Starting with Windows 11, version 24H2, WMIC is not preinstalled.” | https://learn.microsoft.com/en-us/windows/whats-new/removed-features ; https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/features-on-demand-non-language-fod |
| WMIC — Windows Server 2016/2019/2022 | DEPRECATED (încă prezent) | Articolul de suport lista „Applies to”: Server 2012/2012 R2 ESU, 2016, 2019, 2022; **nu dă dată de eliminare pentru Server**. | support.microsoft.com (link mai sus) |
| WMIC — Windows Server 2025 | REMOVED BY DEFAULT / FoD | „Beginning with Windows Server 2025, WMIC is available as a Feature on Demand (FOD). It can be added with `DISM /Add-Capability`. It will be removed from Windows in a future release.” Dacă eliminarea din august 2026 se aplică și Server 2025: **neverificat** (suportul a scos Server 2025 din „Applies to”). | https://learn.microsoft.com/en-us/windows-server/get-started/removed-deprecated-features-windows-server-2025 |
| Înlocuitor WMIC recomandat de Microsoft | — | `Get-CimInstance`, `Get-WmiObject`, `Invoke-CimMethod`; programatic: WMI COM API sau `System.Management`; „Windows is not losing any functionality.” | support.microsoft.com |
| **WMI** (infrastructura) | STILL SUPPORTED | Vezi citatul de mai sus (doar utilitarul WMIC e depreciat). Lista nu menționează clase `Win32_*` depreciate; **neverificat** pentru clase individuale (`Win32_Process`, `Win32_Service`, `Win32_UserAccount`, `Win32_Group`, `Win32_NetworkLoginProfile`). | wmic (Learn) |
| **VBScript** | DEPRECATED; FoD | „VBScript will be available as a feature on demand before being retired in future Windows releases. Initially, the VBScript FoD will be preinstalled.” Windows Server 2025: „available as an FOD and preinstalled … before its removal from the operating system in a later release.” **Datele fazelor (dezactivare implicită ~2027, eliminare) provin doar din surse secundare → neverificat**; articolul oficial al blogului Windows IT Pro nu a putut fi citit. `cscript.exe`/`wscript.exe` sunt gazdele VBScript; implicația (dependente de FoD) nu e declarată explicit în paginile citite → neverificat. | https://learn.microsoft.com/en-us/windows/whats-new/deprecated-features-resources#vbscript ; pagina Server de mai sus |
| **Windows PowerShell 2.0** — Win11 24H2 | **REMOVED** | „As of August 2025, Windows 11, version 24H2, will no longer include Windows PowerShell 2.0.” Scripturile care cer v2 pornesc motorul implicit (5.1). | https://learn.microsoft.com/en-us/windows/whats-new/removed-features ; https://support.microsoft.com/topic/fe6d1edc-2ed2-4c33-b297-afe82a64200a |
| PowerShell 2.0 — Windows Server 2025 | **REMOVED** | „Starting with the September 2025 update, Windows Server 2025 no longer includes Windows PowerShell 2.0.” | pagina Server |
| PowerShell 2.0 — Win10 22H2, Win11 22H2/23H2, Server 2016–2022 | DEPRECATED (nu apare ca eliminat) | Depreciat din Windows 10 1709; „Applications and components should be migrated to PowerShell 5.0+”. | deprecated-features |
| **Windows PowerShell 5.1** | STILL SUPPORTED, inclus în Windows | Rămâne motorul recomandat de înlocuire pentru 2.0; cmdlet-urile CimCmdlets sunt în 5.1. O declarație explicită „5.1 e inclus în fiecare versiune” nu a fost găsită în paginile citite → **neverificat ca text**; dovada indirectă: pagina ISE („supported in all supported versions of Windows PowerShell up to and including 5.1”). | https://learn.microsoft.com/en-us/powershell/scripting/windows-powershell/ise/introducing-the-windows-powershell-ise?view=powershell-5.1 |
| **PowerShell 7** | NU e încorporat | „Installs and runs side-by-side with Windows PowerShell”; trebuie instalat (MSI/ZIP/winget). În PS7 cmdlet-urile WMI v1 (`Get-WmiObject` etc.) sunt eliminate; CimCmdlets rămân. | https://learn.microsoft.com/en-us/powershell/scripting/whats-new/migrating-from-windows-powershell-51-to-powershell-7?view=powershell-7.5 ; .../differences-from-windows-powershell |
| **PowerShell ISE** | STILL SUPPORTED, fără dezvoltare | „no longer in active feature development … continues to be officially supported … no plans to remove the ISE”; este FoD (`Microsoft.Windows.PowerShell.ISE~~~~`, Windows 10 2004+), dezinstalabil de utilizator. Neutilizat de aplicație. | ISE (link mai sus) ; FoD |
| `at.exe` | **neverificat** (nu apare în listele Deprecated/Removed citite; pagina `at` nu are notă de depreciere în textul citit) | Neutilizat de aplicație. | https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/at |
| `bitsadmin` | **neverificat** (nicio notă de depreciere în paginile `bitsadmin` citite) | Neutilizat. | https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/bitsadmin |
| `netsh` (general) | STILL SUPPORTED; recomandare PowerShell | „It's recommended that you use Windows PowerShell to manage networking technologies in Windows and Windows Server rather than `netsh`.” Contextul `advfirewall` e încă listat; contextul vechi `netsh firewall` este depreciat în favoarea `netsh advfirewall` (reiese dintr-un rezultat de căutare spre documentația arhivată Win7/2008R2 → **neverificat direct**). `netsh wlan` și alte contexte: neverificat. | https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh |
| `wevtutil` | STILL SUPPORTED | Pagină curentă (2026-05-15), fără notă de depreciere; `epl` exportă un jurnal în `.evtx`, `/ow:true` suprascrie. | https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/wevtutil |
| `auditpol` | STILL SUPPORTED | `/set /subcategory:{GUID} /failure:enable` documentat; fără notă de depreciere. Schimbarea de politică cere drept Write/Full Control pe politică sau `SeSecurityPrivilege`. Relația politică legacy vs avansată: **neverificat** (pagină neciteită). | https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/auditpol-set |
| `esentutl`, `reg.exe`, `sc.exe`, `schtasks`, `vssadmin`, `fsutil`, `whoami`, `nltest`, `dsquery` | Nu apar în listele Deprecated/Removed (client 2026-09-23, 2026-08-25; Server 2026-03-19). Lipsa din listă ≠ garanție; paginile individuale nu au fost citite → **neverificat** ca „STILL SUPPORTED”. | Pagini listate sus. |
| **RSAT** (`dsquery`, modulul ActiveDirectory etc.) | FoD, nu e preinstalat | „OEMs shouldn't preinstall these Features on Demand.” Pe Windows 11 25H2 Arm64: „RSAT FODs are not supported”. Aplicația nu cere RSAT (folosește LDAP prin `System.DirectoryServices`). | https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/features-on-demand-non-language-fod |
| **INetFwPolicy2 / HNetCfg.FwPolicy2** | STILL SUPPORTED | Minim Windows Vista / Server 2008; `FirewallAPI.dll`; „All configuration changes take effect immediately”; cere serviciul Windows Firewall/ICS pornit; fără notă de depreciere. | https://learn.microsoft.com/en-us/windows/win32/api/netfw/nn-netfw-inetfwpolicy2 |
| **NTLM** | DEPRECATED; NTLMv1 REMOVED | „All versions of NTLM … are no longer under active feature development and are deprecated.” „NTLMv1 is removed starting in Windows 11, version 24H2 and Windows Server 2025.” Server 2025: NTLMv2 „will be removed from Windows Server in a future release”. Impact: colectare remotă care se bazează pe NTLM; aplicația nu are colectare remotă prin rețea (pachet rulat local, `RemoteCollection`); doar `EventLogSession` către DC (Negotiate). | deprecated-features ; removed-features ; pagina Server |
| **SMBv1** | CHANGED BEHAVIOUR | Windows Server 2019: „SMB version 1 is no longer installed by default”. Serviciul Computer Browser „first disabled by default in Windows 10 with the removal of the SMB1 service”. | pagina Server ; deprecated-features |
| **Credential Guard** | CHANGED BEHAVIOUR | Implicit activat de la Windows 11 22H2 și Server 2025 pe sisteme în domeniu, non-DC, care îndeplinesc cerințele hardware/licență. Blochează NTLMv1, delegare Kerberos necondiționată, extragere TGT; „Services or protocols that rely on Kerberos, such as file shares or remote desktop, continue to work.” | https://learn.microsoft.com/en-us/windows/security/identity-protection/credential-guard/ |
| **Timeline / Activity History** | CHANGED BEHAVIOUR | „The timeline user experience was retired in Windows 11, although it remains in Windows 10.” Sincronizarea între dispozitive pentru conturi Entra s-a oprit în ian. 2024; încărcarea pentru conturi MSA oprită din iulie 2021. **Dacă `ActivitiesCache.db` mai este creat/populat în Win11: neverificat.** | deprecated-features |
| **Steps Recorder (psr.exe)** | DEPRECATED | „no longer being updated and will be removed in a future release”. Neutilizat. | deprecated-features |
| **MSDT** | DEPRECATED | Disponibil ca FoD înainte de retragere. Neutilizat. | deprecated-features-resources |
| **Defender** (clase WMI `MSFT_MpComputerStatus`/`MSFT_MpPreference`, cmdlet-uri `Get-MpComputerStatus`/`Get-MpPreference`, `MpCmdRun.exe`) | **neverificat** (pagina oficială „Windows Defender WMIv2 Provider” e arhivată și a venit goală) | Disponibilitatea depinde de Defender instalat/activ; schimbări de cmdlet-uri: neverificat. | https://learn.microsoft.com/en-us/previous-versions/windows/desktop/defender/windows-defender-wmiv2-apis-portal |
| **Prefetch** (dezactivat pe SSD/server implicit) | **neverificat** de Microsoft | Surse secundare (forumuri, Wikipedia) susțin că aplicația-prefetch e oprită implicit pe Server și că Windows poate dezactiva Prefetch pe unele SSD-uri; nu am găsit pagină Microsoft. Tratare în aplicație: raportare ca „gol/dezactivat” (vezi 3.6). | — |
| **SRUM, Amcache, ShimCache, BAM, Jump Lists/Recent, ETW** | **neverificat** | Nu există pagini oficiale Microsoft cu garanții de format/disponibilitate; disponibilitatea variază după versiune (BAM: Windows 10 1709+, documentat în parserul aplicației, `LD/LogAnalyzer.Dfir.Windows/Parsers/ExecutionArtifactParsers.cs:21`). | — |
| **Jurnale de evenimente / audit**: politica de audit legacy vs avansată, jurnalizarea PowerShell | **neverificat** (pagini neciteite); aplicația citește politica avansată prin `AuditQuerySystemPolicy`. | — | — |

## 1.2 Suport și cerințe pentru versiuni

- **.NET 10** (runtime-ul inclus self-contained): tabelul oficial „supported OS” listează Windows client: Windows 11 26H1/25H2/24H2/23H2(E), Windows 10 21H2(E)/1809(E)/1607(E); Windows Server 2025, 23H2, 2022, 2019, 2016, 2012 R2, 2012 (2012/2012 R2 doar cu ESU); Server Core 2012–2025; Nano 2019+. Sufixul „(E)” nu e definit în pagină. https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md
- **Windows 10 22H2 (Home/Pro)**: sfârșitul suportului 14 octombrie 2025 (pagina arată 10/15/2025 06:59 UTC). Starea ESU: **neverificat**. https://learn.microsoft.com/en-us/lifecycle/products/windows-10-home-and-pro
- **WPF pe Server Core**: aplicația e WPF (`net10.0-windows`); WPF cere Desktop Experience — **neverificat în sursele citite**, rezultă din cunoștințe generale; de confirmat în laborator.

---

# PARTEA 2 — Ce folosește aplicația (origin/main `1ad7882ae`)

Aplicația livrabilă este `LD/LogAnalyzer.App` (profil `win-x64-singlefile`, `SelfContained=true`, `PublishSingleFile=true`: `LD/LogAnalyzer.App/Properties/PublishProfiles`, `LD/Directory.Build.props`). Arborele rădăcină (`LD/ViewModels`, `LD/Services`, `LD/App.xaml.cs`, `LD/LogAnalyzer.UI.csproj`) este un duplicat vechi: apelează metode inexistente (`SystemDefenseExecutionService.RemediateServices`, `EnableLsaProtection`, `RemediateVulnerableDrivers`, `ResetHardwareCoolingPolicy`, `ExecuteInstantAutoContainment` — nedefinite în `LD/LogAnalyzer.Infrastructure/Services/SystemDefenseExecutionService.cs`), deci nu se compilează cu codul curent; inventarul exclude acest arbore.

## 2.1 Procese pornite

| # | Executabil | Argumente | Tip | Fișier:linie |
|---|---|---|---|---|
| 1 | `powershell.exe` (din PATH, fără cale completă) | `-NoProfile -ExecutionPolicy Bypass -File "<Scripts\AuditCollector.ps1>" -TargetType … -OutputDirectory … -Hostname …` | COLECTARE (audit stație/server) | `LD/LogAnalyzer.Infrastructure/Services/AuditCollectionService.cs:28-33,46-52`; legat în `LD/LogAnalyzer.App/App.xaml.cs:85` |
| 2 | `netsh.exe` | `advfirewall firewall add rule name="…" dir=out action=block profile=any` (izolare) / `… remoteip=<IP/CIDR>` (blocare IoC) / `… delete rule name="…"` / `… show rule name="…"` (verificare prin cod de ieșire) | ACȚIUNE | `LD/LogAnalyzer.Infrastructure/Services/SystemDefenseExecutionService.cs:35-47,63,107,128,143`; apelanți: `LD/LogAnalyzer.App/ViewModels/MainViewModel.cs:1806,1819,1859` |
| 3 | `System32\wevtutil.exe` | `epl <canal> <dest.evtx> /ow:true` pentru 17 canale (Security, System, Application, PowerShell/Operational, Windows PowerShell, Defender/Operational, TaskScheduler/Operational, TerminalServices LSM/RCM, Sysmon/Operational, Bits-Client, WMI-Activity, Firewall, Partition/Diagnostic, NetworkProfile, WLAN-AutoConfig, DNS-Client) | COLECTARE | `LD/LogAnalyzer.Dfir.Windows/Acquisition/Collectors.cs:14-21,32,42` |
| 4 | `System32\esentutl.exe` | `/y <src> /vss /d <dest>` pentru `SRUDB.dat` și `Amcache.hve{,.LOG1,.LOG2}` | COLECTARE | `Collectors.cs:99-100,246` |
| 5 | `System32\reg.exe` | `save HKLM\SYSTEM <dest> /y` (BAM, ShimCache) | COLECTARE | `Collectors.cs:230` |
| 6 | `System32\auditpol.exe` | `/get /subcategory:{0CCE9226-…} /r` (CSV; parsare pe text localizat) și `/set … /failure:enable` | COLECTARE (verificare audit) + ACȚIUNE (activare audit eșec, doar cu acordul operatorului) | `LD/LogAnalyzer.Dfir.Windows/Containment/Detection.cs:98-113,160-170` |
| 7 | `%ProgramFiles%\Windows Defender\MpCmdRun.exe` | `-Scan -ScanType 3 -File <path> -DisableRemediation` | COLECTARE (verdict Defender); lipsa fișierului e raportată ca NotAvailable | `LD/LogAnalyzer.Dfir.Windows/Containment/ProcessScanner.cs:272-283` |
| 8 | `explorer.exe` / deschidere PDF prin `UseShellExecute` | deschidere folder/fișier | UI, nu colectare | ex. `LD/LogAnalyzer.App/ViewModels/MainViewModel.cs:1094-1099` |

Nu există `cmd.exe`, `wmic`, `cscript`, `schtasks`, `vssadmin` etc. în `ProcessStartInfo`/`Process.Start` (grep exhaustiv pe `LD/`, fără `_recovered/`).

## 2.2 Scripturi PowerShell

**Livrate/referite și dependența lor**

| Script | Există? | Cerințe | Referință |
|---|---|---|---|
| `Scripts\AuditCollector.ps1` | **NU în `LD/`** (nici ca resursă/Content în `LogAnalyzer.App.csproj`). Există copia `LD/_recovered/desktop_mvp_scripts/AuditCollector.ps1` (352 linii). Codul încearcă și `C:\Users\Marius\Desktop\LogAnalyzer.MVP\Scripts` (cale de dezvoltator). | Windows PowerShell 5.1. Cmdlet-uri folosite: `Get-NetTCPConnection`, `Get-FileHash`, `Get-DnsClientCache`, `Get-ScheduledTask`/`Get-ScheduledTaskInfo`, `Get-LocalUser`, `Get-LocalGroupMember`, `Get-NetFirewallRule`+`Get-NetFirewallPortFilter`/`AddressFilter`/`ApplicationFilter` (NetSecurity), `Get-CimInstance Win32_SystemDriver`/`Win32_NetworkConnection`, `Get-AuthenticodeSignature`, **`Get-MpComputerStatus`/`Get-MpPreference` (modulul Defender — absent dacă Defender lipsește)**, `Get-NetRoute`, `Get-SmbShare`/`Get-SmbSession`, `Get-Acl`, `qwinsta`, `wevtutil epl`. Nu cere RSAT/ActiveDirectory. | `AuditCollectionService.cs:28`; `_recovered/…/AuditCollector.ps1:42-217,236-330` |

**Scripturi generate pentru operator (nu rulează în aplicație)**

| Generator | Cerințe | Referință |
|---|---|---|
| Pachet de colectare la distanță | `#Requires -Version 5.1`; folosește `wevtutil.exe epl`, `reg.exe save HKLM\<hive>`, `Copy-Item`; SHA-256 prin .NET (nu `Get-FileHash`, comentat explicit) | `LD/LogAnalyzer.Dfir.Windows/Acquisition/RemoteCollection.cs:84-170` |
| Script izolare stație | `netsh advfirewall export`, `Set-NetFirewallProfile`, `New-NetFirewallRule` (modul NetSecurity, inclus în Windows 8/Server 2012+) | `LD/LogAnalyzer.Core/Services/IncidentResponsePlaybookService.cs:28-48` |
| Script terminare arbore proces | `Get-CimInstance Win32_Process`, `Stop-Process` | `IncidentResponsePlaybookService.cs:67-71` |
| Investigație e-mail | `Connect-ExchangeOnline`, `Get-MessageTraceV2`, `Get-InboxRule`, `Get-Mailbox`; on-prem `Get-TransportService`, `Get-MessageTrackingLog` — **module/shell Exchange, nu sunt în Windows** | `LD/LogAnalyzer.Dfir.Windows/Domain/MailInvestigation.cs:40-70` |
| Fragmente de contramăsuri | `New-NetFirewallRule`, `Stop-Process`, `Get-Process` (text afișat) | `LD/LogAnalyzer.Core/Services/Network/CyberAttackCountermeasureEngine.cs:70-336` |
| Transpiler Sigma → PS | `Get-WinEvent -FilterHashtable` | `LD/LogAnalyzer.Core/Services/SigmaTranspilerService.cs:67` |

Notă: în `StationFacts.cs:126` mesajul de „gap” sugerează „auditpol /get /category:*” operatorului (text).

## 2.3 WMI / CIM (`System.Management`, pachet `System.Management 10.0.10`)

| Namespace\Clasă | Scop | Fișier:linie |
|---|---|---|
| `root\cimv2` `Win32_Process` (Name, ExecutablePath, CommandLine, ParentProcessId, CreationDate, + `GetOwner`) | procese + lanț părinte | `LD/LogAnalyzer.Dfir.Windows/Containment/ProcessScanner.cs:101,131` |
| `Win32_Process` | snapshot stare live | `Collectors.cs:131` |
| `Win32_Service` | servicii | `Collectors.cs:145` |
| `Win32_NetworkLoginProfile` | ultimul logon | `LD/LogAnalyzer.Dfir.Windows/Audit/StationFacts.cs:106` |
| `Win32_UserAccount`, `Win32_Group`, `Win32_GroupUser` | conturi/grupuri locale | `StationFacts.cs:111,125,128` |
| `root\Microsoft\Windows\Defender` `MSFT_MpComputerStatus`/`MSFT_MpPreference` | stare Defender, excluderi | `StationFacts.cs:179,188` |
| `root\cimv2\Security\MicrosoftVolumeEncryption` `Win32_EncryptableVolume` | BitLocker | `StationFacts.cs:200` |
| `Win32_Process` | arbore procese UI | `LD/LogAnalyzer.App/ViewModels/MainViewModel.cs:2253` |
| `Win32_Processor`, `Win32_BaseBoard` | amprentă hardware pentru licență | `LD/LogAnalyzer.Core/Services/LicenseService.cs:21-22,91` |

## 2.4 COM, P/Invoke, API-uri Windows, LDAP

- **COM**: `HNetCfg.FwPolicy2` și `HNetCfg.FWRule` (`FirewallController.cs:34,123`; `StationFacts.cs:170`) — acțiune (reguli per program) și citire.
- **Jurnale**: `EventLogReader`/`EventLogQuery`/`EventLogSession` din `System.Diagnostics.Eventing.Reader` (nu clasa veche `EventLog`): `Detection.cs:127-130` (5157), `StationFacts.cs:262-270`, `UserInvestigation.cs:54-56` (la distanță pe DC), `Parsers/EvtxParser.cs:66` și `LD/LogAnalyzer.Infrastructure/Parsers/EvtxParser.cs:37` (fișiere `.evtx`), `LD/LogAnalyzer.Infrastructure/Watchers/LiveEventLogWatcherService.cs:32-78` (abonare live, inclusiv sesiune la distanță cu utilizator/parolă).
- **LDAP**: `System.DirectoryServices` (`DirectoryEntry`/`DirectorySearcher`) — `LD/LogAnalyzer.Dfir.Windows/Domain/DirectoryCollector.cs:74-129`. Fără RSAT.
- **P/Invoke**: `advapi32` `AuditQuerySystemPolicy` (`Native/PolicyNative.cs:63`); `netapi32` `NetUserModalsGet` (`PolicyNative.cs:97`); `wintrust` `WinVerifyTrust`, `CryptCATAdmin*` (`Native/TrustAndNetwork.cs:207-226`); `iphlpapi` `GetExtendedTcpTable` (`TrustAndNetwork.cs:293`); `kernel32`/`ntdll` `OpenProcess`, `NtSuspendProcess`/`NtResumeProcess` (`TrustAndNetwork.cs:321-324`); `ntdll` `RtlDecompressBufferEx` (prefetch comprimat, `Native/NtCompression.cs:10-31`); `esent.dll` `Jet*` (citire SRUM, `Native/Esent.cs:23-37`).
- **Registry**: `Microsoft.Win32.Registry` (UAC, Winlogon, SMB1, RDP, WDigest, LSA RunAsPPL, NetworkList, USBSTOR, Run/RunOnce, servicii): `StationFacts.cs:144-250`, `Collectors.cs:165-170`, `ProcessScanner.cs:355-375`, `Policy/SettingProviders.cs:30-106`.
- **Task Scheduler**: fișiere XML din `System32\Tasks` (nu `schtasks`): `Collectors.cs:~155`.
- **VSS**: doar indirect prin `esentutl /vss` (2.1 #4); nu există API VSS direct.

## 2.5 Artefacte citite

| Artefact | Sursă | Fișier |
|---|---|---|
| Event logs | export `wevtutil` (17 canale) + live (`EventLogReader`) | `Collectors.cs:14-52` |
| Prefetch | `%WINDIR%\Prefetch\*.pf` (copiere; cere admin; dacă gol: „Prefetch dezactivat sau gol”) | `Collectors.cs:58-80` |
| SRUM | `System32\sru\SRUDB.dat` prin `esentutl /vss` | `Collectors.cs:86-105` |
| Amcache | `%WINDIR%\appcompat\Programs\Amcache.hve` + LOG1/LOG2 prin `esentutl /vss` | `Collectors.cs:215-262` |
| ShimCache + BAM | din hive-ul SYSTEM salvat cu `reg save` (parser `ExecutionArtifactParsers.cs`) | `Collectors.cs:230`; `Parsers/ExecutionArtifactParsers.cs:11-26` |
| Jump Lists / Recent | doar **parsere** de fișiere importate (`Parsers`/`UserActivityParser.cs:82`, `JumpListParser.cs`); **nu există colector live** |  |
| ActivitiesCache.db (Timeline) | **nu există colector sau parser** în cod (grep gol) |  |
| Scheduled tasks, servicii, autorun, conexiuni TCP, procese cu semnătură | snapshot live (WMI + registry + IPHelper + WinVerifyTrust) | `Collectors.cs:107-210` |
| USB (USBSTOR), profiluri de rețea | registry | `StationFacts.cs:208-250` |
| Politică de audit / parole | `AuditQuerySystemPolicy`, `NetUserModalsGet` | `PolicyNative.cs` |

## 2.6 Verificare „wmic” (obligatorie)

Grep pe `origin/main`, fără `_recovered/`: șirul `wmic` apare numai în: liste de LOLBin/tipare de detecție (`LD/LogAnalyzer.Dfir.Core/Analysis/Correlation.cs:19`, `AntiForensics.cs:42`), `LD/LogAnalyzer.Core/Services/LolbasEngine.cs:29`, `RansomwareDetectionEngine.cs:25` (`"wmic shadowcopy delete"`), `SigmaCorrelationEngine.cs:57`, `AptAttributionEngine.cs:77`. **Niciuna nu execută wmic.** Concluzie: **nicio utilizare de înlocuit**; aceste tipare de detecție se păstrează (urmăresc atacatori, nu depind de prezența WMIC).

---

# PARTEA 3 — Matrice de compatibilitate și înlocuitori

Abrevieri coloane: W10 = Windows 10 22H2; W11a = 11 23H2; W11b = 11 24H2/25H2/26H1; S16/S19/S22/S25 = Server 2016/2019/2022/2025. Cod: OK, MISSING (lipsă implicit), REM (eliminat), DEP (depreciat), CHG (comportament schimbat), ? = neverificat.

## 3.1 Matrice

| Dependență (aplicație) | W10 | W11a | W11b | S16 | S19 | S22 | S25 | Observații |
|---|---|---|---|---|---|---|---|---|
| `wmic.exe` (neutilizat) | DEP | MISSING (FoD oprit implicit) | **REM** | DEP | DEP | DEP | MISSING (FoD) | Neafectează aplicația |
| `powershell.exe` 5.1 | OK | OK | OK | OK | OK | OK | OK | PS 2.0 eliminat în W11b și S25 (neutilizat) |
| `AuditCollector.ps1` | defect: lipsește din pachet (toate versiunile) | idem | idem | idem | idem | idem | idem | Nu e o problemă de OS |
| `Get-MpComputerStatus`/`Get-MpPreference` în script | OK dacă Defender prezent | idem | idem | ? (Defender opțional) | OK dacă prezent | idem | idem | Absent la AV terț/Defender neinstalat (?) |
| `netsh.exe advfirewall` | OK (DEP recomandare PS) | idem | idem | OK | OK | OK | OK | „use PowerShell rather than netsh” |
| `wevtutil.exe` | OK | OK | OK | OK | OK | OK | OK | Canalele variază (Sysmon, WMI-Activity, DNS-Client lipsesc implicit pe unele) |
| `esentutl.exe` | OK? | OK? | OK? | OK? | OK? | OK? | OK? | ? (neverificat; folosește `/vss`, cere admin) |
| `reg.exe` | OK? | idem | idem | idem | idem | idem | idem | ? |
| `auditpol.exe` | OK | OK | OK | OK | OK | OK | OK | Rezultatul text e localizat |
| `MpCmdRun.exe` | MISSING dacă Defender absent (?) | idem | idem | idem | idem | idem | idem | tratat ca NotAvailable |
| WMI `Win32_Process/Service/UserAccount/Group` | OK | OK | OK | OK | OK | OK | OK | WMI neafectat de WMIC |
| WMI `root\Microsoft\Windows\Defender` | MISSING dacă Defender absent (?) | idem | idem | idem | idem | idem | idem | prins ca `ManagementException` |
| WMI BitLocker `Win32_EncryptableVolume` | OK pe Pro/Ent (?) | idem | idem | MISSING dacă funcția nu e instalată (?) | idem | idem | idem | prins ca gap |
| COM `HNetCfg.FwPolicy2` | OK | OK | OK | OK | OK | OK | OK | cere serviciul firewall pornit |
| `EventLogReader`/`Session` | OK | OK | OK | OK | OK | OK | OK | |
| LDAP (`DirectoryServices`) | OK | OK | OK | OK | OK | OK | OK | NTLM/Credential Guard pot afecta autentificarea pe DC (CHG) |
| P/Invoke wintrust, iphlpapi, ntdll, esent, advapi32, netapi32 | OK | OK | OK | OK | OK | OK | OK | |
| Prefetch (folder `.pf`) | CHG (poate fi gol/dezactivat, ?) | idem | idem | idem (Server: adesea oprit, ? neoficial) | idem | idem | idem | raportare explicită necesară |
| SRUM / Amcache / ShimCache / BAM | OK (BAM 1709+) | OK | OK | BAM: ? | OK | OK | OK | |
| ActivitiesCache.db (Timeline) | prezent (UX Timeline există) | UX retras (CHG), fișier ? | idem | ? | ? | ? | ? | nu e colectat azi |
| Module Exchange în scripturile generate | MISSING | MISSING | MISSING | MISSING | MISSING | MISSING | MISSING | externe, de instalat de operator |

## 3.2 Versiuni Windows de declarat (propunere; decide proprietarul)

- **Țintă completă (testate):** Windows 11 23H2, 24H2, 25H2 (și 26H1 când e disponibil); Windows Server 2022 și 2025 (Desktop Experience).
- **Țintă extinsă (best-effort):** Windows Server 2016 și 2019 (Desktop Experience); Windows 10 22H2 — suport Microsoft Home/Pro încheiat la 14 oct. 2025 (ESU neverificat), de declarat „legacy”.
- **Nesusținute:** Server Core/Nano (WPF cere Desktop Experience — de confirmat), Windows 10 sub 22H2, Server 2012/2012 R2 (suport .NET doar ESU).
- Declarația „self-contained” acoperă runtime-ul .NET; **nu** acoperă componentele OS (`wevtapi`, `esent.dll`, `FirewallAPI.dll`, WMI, `powershell.exe`). Acestea sunt prezente pe toate versiunile de mai sus, cu excepțiile din 3.1.

## 3.3 Înlocuitori pentru fiecare dependență (ambele variante + recomandare)

Regulă: **API în proces** = preferat (ediția P1 clasificată nu pornește procese; fără parsare de text localizat; fără dependență de PATH); **PowerShell 5.1** = alternativă acolo unde API-ul e impracticabil sau pentru scripturile date operatorului. Coloana „WMIC” indică dacă utilizarea e de înlocuit: **nicio utilizare wmic curentă → nimic „de înlocuit” cu titlul wmic**; mecanismele de mai jos sunt cele depreciate/fragile.

| Dependență curentă | Opțiune A: Windows PowerShell 5.1 | Opțiune B: în proces (.NET/Win32/COM) | Recomandare |
|---|---|---|---|
| (referință) `wmic path win32_… get …` — **nu există în aplicație** | `Get-CimInstance Win32_X` | `ManagementObjectSearcher` (System.Management) | Dacă apare vreodată: B |
| `netsh advfirewall firewall add/delete/show rule` (SystemDefenseExecutionService) | `New-NetFirewallRule`/`Remove-NetFirewallRule`/`Get-NetFirewallRule` (modul NetSecurity, inclus din Win8/Server 2012; lipsește pe Win7) | `INetFwPolicy2` + `INetFwRule` prin COM (cod existent: `WindowsFirewallController`, `Containment/FirewallController.cs:34,123`). Regulă fără `ApplicationName`, `Direction=Out`, `Action=Block`, `Profiles=All` = aceeași izolare; verificare prin citirea `Rules.Item(name)`. | **B** — mecanismul există deja; elimină parsarea codului de ieșire netsh, merge fără proces, compatibil Vista+. Păstrează APPLY→VERIFY. |
| `powershell.exe -File AuditCollector.ps1` | Script livrat în pachet (resursă încorporată extrasă într-un director temporar), rulat cu calea completă `%WINDIR%\System32\WindowsPowerShell\v1.0\powershell.exe`, `#Requires -Version 5.1`. Atenție: dacă politica de execuție este impusă prin GPO, `-ExecutionPolicy Bypass` poate fi ignorat (**neverificat**) → rulare prin `-Command` din stdin/`-EncodedCommand` sau semnare. | Port în C# pe module: TCP (`GetExtendedTcpTable` — există), procese (`Process`/WMI), conturi locale (`NetUserEnum`/`NetLocalGroupGetMembers`), reguli firewall (`INetFwPolicy2.Rules`), rute (`GetIpForwardTable2`), sesiuni (`WTSEnumerateSessions`), partajări (`NetShareEnum`/`NetSessionEnum`), taskuri (XML din `System32\Tasks` — există), Defender (WMI `root\Microsoft\Windows\Defender` — există), drivere (`Win32_SystemDriver`), DNS cache (`DnsGetCacheDataTable` nedocumentat sau `root\StandardCimv2` `MSFT_DNSClientCache`), semnături (`WinVerifyTrust` — există). | **P0: A** (rapid, repară funcția); **P2-P3: B** modul cu modul, ca să se poată elimina procesul în P1. Orice modul indisponibil se raportează în acoperire. |
| `wevtutil epl <canal> <dest>` | `Get-WinEvent`+export nu produce `.evtx` brut; pentru `.evtx` brut rămâne `wevtutil` | `EventLogSession.ExportLog(path, PathType.LogName, query, targetFile, tolerateQueryErrors)` / API-ul nativ `EvtExportLog` (`wevtapi.dll`) — același motor, fișier `.evtx` brut. Canal inexistent: verificare prealabilă cu `EventLogSession.GetLogNames()` → „sursă absentă”, nu eșec. | **B** (de validat în laborator că produce același hash/format ca `wevtutil`). |
| `esentutl /y … /vss` (SRUM, Amcache) | `(Get-WmiObject -List Win32_ShadowCopy).Create(...)` + copiere din `\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopyN` (neverificat pe client); `vssadmin`/`diskshadow` (nu recomandat) | Creare snapshot prin WMI `Win32_ShadowCopy.Create` (System.Management, deja folosit) sau prin API VSS (`vssapi.dll`) și `File.Copy` din calea umbrei; alternativ citire „brută” NTFS. Pentru SRUM live există deja `Esent.cs`. | **B cu esentutl ca rezervă**; până la validare, păstrează `esentutl` (inclus în Windows) și raportează „esentutl indisponibil / VSS eșuat” în acoperire. `esentutl` nu apare în listele de depreciere (neverificat în rest). |
| `reg save HKLM\SYSTEM` | `reg.exe` rămâne o opțiune; `Save-…` nu există ca cmdlet | `RegSaveKeyEx` (`advapi32`) cu privilegiul `SeBackupPrivilege` | **B**; `reg.exe` rezervă. |
| `auditpol /get` (parsare CSV localizată) | `auditpol /get /r` (la fel de fragil) | `AuditQuerySystemPolicy` — **deja** în `PolicyNative.cs:63`, independent de limbă; `AuditSetSystemPolicy` pentru activarea auditului eșec | **B** — elimină detecția „Eșec/Eroare” bazată pe text român/englez (`Detection.cs:106-111`). |
| `MpCmdRun.exe -Scan` | `Start-MpScan -ScanPath` (modul Defender; același prerequisit) | Fără API public simplu; COM `IAmsiStream`/AMSI nu înlocuiește scanarea de fișier | Păstrează `MpCmdRun`; lipsa → „Defender indisponibil”. |
| WMI `Win32_Process` (3 locuri) | `Get-CimInstance Win32_Process` | `System.Management` (curent) sau `Process.GetProcesses` + `NtQueryInformationProcess` pentru părinte/linie de comandă; `CreateToolhelp32Snapshot` pentru părinte | Păstrează B (curent); dacă P1 interzice WMI: Toolhelp + `NtQueryInformationProcess`. |
| WMI `Win32_Service` | `Get-CimInstance Win32_Service` | `EnumServicesStatusEx`+`QueryServiceConfig` (`advapi32`) sau `ServiceController`; sau registry `Services` (folosit deja) | **B** (API Win32/registry), WMI ca rezervă. |
| WMI `Win32_UserAccount`/`Win32_Group`/`Win32_GroupUser`/`Win32_NetworkLoginProfile` | `Get-LocalUser`, `Get-LocalGroupMember` (modul `Microsoft.PowerShell.LocalAccounts`, 5.1) | `NetUserEnum`/`NetUserGetInfo`/`NetLocalGroupGetMembers` (`netapi32`, același DLL deja folosit) | **B**; `Win32_UserAccount` este lent pe mașini în domeniu. |
| WMI Defender `MSFT_Mp*` | `Get-MpComputerStatus`, `Get-MpPreference` | WMI (curent) | Păstrează WMI; absența spațiului → gap „Defender indisponibil/înlocuit de AV terț”. |
| WMI BitLocker `Win32_EncryptableVolume` | `Get-BitLockerVolume` (modul BitLocker, doar unde funcția e instalată) | WMI (curent) | Păstrează; gap dacă lipsește. |
| WMI hardware pentru licență (`Win32_Processor`, `Win32_BaseBoard`) | `Get-CimInstance` | WMI (curent); `GetSystemFirmwareTable` (SMBIOS) pentru serie placă | Păstrează WMI; la eșec produce `UNKNOWN_HW_ID_001` — de tratat ca eroare de licență explicită (fix separat). |
| Scripturi generate cu `netsh advfirewall export/import` | `Export-…` nu există pentru politică; `netsh advfirewall export` rămâne cel mai simplu | — | Păstrează (script pentru operator, 5.1). |
| Scripturi generate cu `Get-Mailbox`/`Get-MessageTraceV2` | Module Exchange (externe) | Microsoft Graph API (necesită rețea; incompatibil AirGapped) | Păstrează ca script pentru operator; marchează în UI „necesită modul Exchange instalat de operator, neinclus în Windows”. |
| `Get-WinEvent`/`Stop-Process`/`Get-CimInstance` în scripturi generate | 5.1 | — | OK pe toate versiunile. |

## 3.4 Reguli pentru scripturi PowerShell livrate

1. Țintă **Windows PowerShell 5.1** (`#Requires -Version 5.1`), pornit cu calea completă din `Environment.SystemDirectory\WindowsPowerShell\v1.0\powershell.exe`, nu din PATH.
2. Fără RSAT, fără `ActiveDirectory` module, fără PS 2.0, fără PS 7, fără VBScript. Pentru AD: LDAP prin `System.DirectoryServices` (cum e deja).
3. Fără `Get-FileHash` dacă `PSModulePath` poate fi contaminat de PS7 (precedent deja documentat în `RemoteCollection.cs`); hash prin .NET.
4. Orice cmdlet din module opționale (Defender, BitLocker, SmbShare, NetSecurity) să fie în `try/catch` și să producă o linie „indisponibil: <motiv>” în manifest, nu o omisiune tăcută.

## 3.5 Evenimente/surse care trebuie raportate „indisponibil pe acest OS / dezactivat”

Aplicația raportează deja parțial (Prefetch gol, `NotAvailable` când nu e admin, `MpCmdRun.exe nu există`). De acoperit complet:
- Canale absente (ex. Sysmon, WMI-Activity, DNS-Client): acum intră ca eroare `wevtutil exit N` și degradează starea la `Partial` (`Collectors.cs:44-49`); trebuie `NotAvailable: canal inexistent pe această versiune/configurație`.
- Prefetch: distinge „folder lipsă”, „dezactivat prin registry `EnablePrefetcher`”, „gol”. (Dezactivarea implicită pe SSD/Server: neverificat oficial.)
- ActivitiesCache/Timeline, Jump Lists/Recent live: nu există colector; să apară ca rând de acoperire „nu este colectat pe această versiune (Timeline retras în Windows 11)” — nu omis.
- SRUM, Amcache, BAM: stare per versiune (BAM din Windows 10 1709).
- Defender: „absent / înlocuit de AV terț / dezactivat”.
- BitLocker WMI: „funcție neinstalată”.
- Politica de audit avansată necitibilă fără admin: există deja gap (`StationFacts.cs`).

## 3.6 Efectul altor schimbări Microsoft asupra colectării

- NTLMv1 eliminat (W11 24H2, S25) și NTLM depreciat → conexiuni `EventLogSession` cu utilizator/parolă și LDAP pot cere Kerberos (nume DNS, nu IP). Credential Guard implicit (W11 22H2+, S25 în domeniu) blochează NTLMv1/delegare necondiționată; nu blochează Kerberos.
- SMBv1 oprit implicit (Server 2019+, Windows 10 1709+): irelevant pentru aplicație (nu folosește SMB).
- `wmic` eliminat: irelevant (nefolosit).

## 3.7 Reguli de implementare pentru WP-PKG (funcționalitatea nu se elimină)

- Se înlocuiește mecanismul, se păstrează funcția: izolare stație, blocare IoC, export evtx, SRUM/Amcache/BAM/ShimCache, verificare audit, colectare audit.
- Fiecare înlocuire păstrează calea veche ca rezervă documentată doar dacă e inclusă în Windows pe toate versiunile declarate; dispozitivele P1 (fără procese) folosesc exclusiv varianta B.
- Fiecare rezervă e înregistrată în raportul de acoperire cu motivul.

## 3.8 Listă prioritizată de reparații (WP-PKG)

| Prio | Reparație | Fișiere | Motiv |
|---|---|---|---|
| **P0** | Includerea `AuditCollector.ps1` în pachet (Content/EmbeddedResource) din copia `_recovered`, eliminarea căii `C:\Users\Marius\...`, pornire cu calea completă a PowerShell 5.1; mesaj clar când lipsește | `AuditCollectionService.cs:28-52`, `LogAnalyzer.App.csproj` | Funcția e defectă pe orice mașină |
| **P0** | Randare de acoperire „indisponibil pe acest OS / dezactivat” în loc de omisiune/eroare generică (canale absente, Prefetch, Timeline, Defender, BitLocker) | `Collectors.cs`, `StationFacts.cs` | Cerința proprietarului: niciodată omis tăcut |
| **P1** | `netsh` → `INetFwPolicy2` (reutilizare `WindowsFirewallController`), cu verificare prin citire; `netsh` doar ca rezervă | `SystemDefenseExecutionService.cs` | Elimină un proces extern și dependența de PATH; același efect |
| **P1** | `auditpol` → `AuditQuerySystemPolicy`/`AuditSetSystemPolicy` | `Detection.cs:98-170` | Elimină parsarea textului localizat |
| **P1** | `wevtutil epl` → `EventLogSession.ExportLog` / `EvtExportLog` | `Collectors.cs:42` | Elimină proces, tratează canalele absente |
| **P2** | `reg save` → `RegSaveKeyEx` | `Collectors.cs:230` | |
| **P2** | `esentutl /vss` → snapshot VSS în proces + copiere (rezervă `esentutl`) | `Collectors.cs:99,246` | Cea mai complexă; de validat în laborator |
| **P2** | Port modulele critice din `AuditCollector.ps1` în C# (TCP, procese, conturi, reguli firewall, taskuri, Defender) | nou | Ediția P1 fără procese |
| **P3** | WMI → API Win32 pentru servicii și conturi (rezervă WMI) | `Collectors.cs:145`, `StationFacts.cs:106-130` | Robustețe când serviciul WMI e oprit |
| **P3** | Marcaj UI pentru scripturile Exchange: „necesită modul Exchange instalat de operator” | `MailInvestigation.cs` | Nu sunt incluse în Windows |
| **P3** | Pagină de „Versiuni susținute” + test de laborator pe W10 22H2, W11 24H2/25H2, S2016/2019/2022/2025 | doc + CI | Proprietarul decide lista |

## 3.9 Ce rămâne neverificat

- Datele fazelor VBScript (2027 etc.) — doar surse secundare; articolul oficial nu a putut fi citit.
- Prefetch dezactivat pe SSD/server; existența/populația `ActivitiesCache.db` în Windows 11; formatele SRUM/Amcache/ShimCache/BAM pe versiuni.
- Stare oficială pentru `at.exe`, `bitsadmin`, `esentutl`, `reg.exe`, `sc`, `schtasks`, `vssadmin`, `fsutil`, `whoami`, `nltest`, `dsquery`, `netsh wlan`/alte contexte.
- Clase WMI individuale depreciate; schimbări ale cmdlet-urilor Defender; disponibilitatea spațiului `root\Microsoft\Windows\Defender` fără Defender.
- Dacă WMIC e eliminat și din Windows Server 2025; prezența inbox a WMIC pe Windows 10 22H2 (declarat explicit).
- Stare ESU pentru Windows 10 22H2; WPF pe Server Core.
- Execuția `-ExecutionPolicy Bypass` când politica vine din GPO; `Win32_ShadowCopy.Create` pe Windows client; `EventLogSession.ExportLog` produce fișier identic cu `wevtutil` (validare în laborator).
- Rularea pe Windows reală: nicio verificare dinamică nu a fost făcută (mediu Linux; cercetare statică).
