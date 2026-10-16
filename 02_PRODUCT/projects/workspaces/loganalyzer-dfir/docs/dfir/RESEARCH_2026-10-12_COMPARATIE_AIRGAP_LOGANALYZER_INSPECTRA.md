# Air-gapped station requirements: LogAnalyzer vs InspectraVeritas (2026-10-12)

Status: research, read-only. No InspectraVeritas binary or archive file was run, extracted, decompiled or
opened as code. Its column rests only on the 4 364 UI strings extracted in the earlier review
(`inspectra_strings.txt`, session scratchpad, not committed) and on
`RESEARCH_2026-10-11_COMPARATIE_INSPECTRA.md`. Nothing from that tool is to be copied.

Evidence states:
- **LogAnalyzer (LA)**: `CODE_VERIFIED` by grep/Read on this branch (`claude/loganalyzer-dfir-roles-ui-0a71fb`,
  head `9bb9ad8a4`). Code presence is not runtime proof; see `RESEARCH_2026-10-11_MEDII_DE_STOCARE.md` §4bis
  for what was actually run on a PC. HAVE / PARTIAL / MISSING.
- **InspectraVeritas (IV)**: `DOCUMENT_VERIFIED` against UI text only. DESCRIBED = the UI text describes the
  feature; PARTIAL = UI text describes part of it; NOT FOUND = no string found. A string proves the feature is
  described to the user, not that it works.

Source abbreviations for requirements:
- **CTRL** = `RESEARCH_2026-10-11_CONTROL_STATII_CLASIFICATE.md` (sections 2.A-2.J, procedural P1-P15 in §3)
- **EXF** = `RESEARCH_2026-10-11_EXFILTRARE_AIRGAP.md` (channels §1, reconstruction R1-R11 §2, functions X1-X19 §5)
- **MED** = `RESEARCH_2026-10-11_MEDII_DE_STOCARE.md` (§2-§4.6)
- **CMP** = `RESEARCH_2026-10-11_COMPARATIE_INSPECTRA.md` (earlier feature review)
- **M01-M16** = `LogAnalyzer.Dfir.Core/Flow/ManualChecks.cs`; station check ids (P/C/A/U/D/N/S) =
  `LogAnalyzer.Dfir.Windows/Audit/CheckGuide.cs`, evaluated in `Audit/ControlEvaluator.cs`

Update (branch `wk-gaps-airgap`): rows B10, B11, D4, D7, D8, D9, D11, D12, D13, D14 and D15 were re-read against the new code and tests
(`AirGapChannelTests`, `AirGapStationChecksTests` in `LogAnalyzer.App.Tests`). Their LogAnalyzer cell and the "Better" column are updated; the group
scores in section 2 and the totals quoted below (73 vs 34, 64 vs 59) were NOT recomputed and still describe the earlier state. Code presence and
synthetic tests are CODE_VERIFIED / TEST_VERIFIED only; the new collectors were run read-only on one development PC (structure checked, not the content).

## 0. Answer in short

- The owner's suspicion is right in one area: **InspectraVeritas does much more post-event forensics than the
  first table showed** (medium-to-user attribution with ambiguity, file-to-stick proof by volume serial, stick
  content found on disk by SHA-256, deleted registry keys, $MFT/timestomping, VM traces, signed reports with a
  verifier, verifiable archive, PII-masked export).
- It is **not** an air-gap compliance tool. The UI strings contain no station configuration checks (no Secure
  Boot, BitLocker, UAC, firewall, Defender, AppLocker, AutoRun, banner, removable-storage policy), no registers,
  no transfer control, no real-time monitoring, and nothing on CD/DVD or printing.
- For the main use case (periodic control of an air-gapped classified station by a non-IT controller)
  **LogAnalyzer is clearly better: 73 vs 34 on a weighted 100**.
- For investigating a suspected leak through a removable medium the two are **close (LA 64 vs IV 59)** and the
  result depends on the weights: IV is stronger on the evidence itself (who, which files, which stick, report
  integrity); LA is stronger on context (was it declared, reconciled, approved, alerted).

## 1. Requirement table

Columns: requirement | source | LA status + evidence | IV status + evidence string | better.

### A. Prevention / configuration of the station

| # | Requirement | Source | LogAnalyzer | InspectraVeritas | Better |
|---|---|---|---|---|---|
| A1 | Secure Boot on, UEFI mode | CTRL B1/B2 | HAVE: check P15 | NOT FOUND | LA |
| A2 | BitLocker on every fixed volume, PIN at boot | CTRL B4 | HAVE (updated, see note U1): P32 reads every lettered volume (`Win32_EncryptableVolume` + read-only `GetKeyProtectorType`), reports per volume, flags a system drive without TPM+PIN; P11 kept. Needs administrator, else NEDETERMINAT | NOT FOUND (only "fișier criptat EFS" as a read obstacle) | LA |
| A3 | TPM present/ready, Kernel DMA protection | CTRL B3/B5 | HAVE (U1): P33 TPM present/enabled/ready/version (`Win32_Tpm`, administrator), P34 Kernel DMA Protection + `DeviceEnumerationPolicy`, P35 VBS running state. The DMA read uses the information class msinfo32 shows; to confirm in the lab | NOT FOUND | LA |
| A4 | Screen lock at 15 min or less, password on wake | CTRL G1/G2 | HAVE (U1): P16 inactivity lock; P36 password on wake (policy AC/DC, active power plan CONSOLELOCK, current user's sign-in delay) | NOT FOUND | LA |
| A5 | Application allowlisting present, mode, blocked attempts | CTRL D1/D2 | HAVE (U1): P17 AppLocker; P37 App Control (WDAC) mode from `Win32_DeviceGuard` + active policy files; A09 counts AppLocker 8003/8004/8006/8007 and CodeIntegrity 3076/3077 in the control period, with the files | NOT FOUND | LA |
| A6 | AutoPlay/AutoRun off | CTRL D3; MED §4.5 | HAVE: P18 (`ControlEvaluator.cs:526`) | NOT FOUND | LA |
| A7 | Antivirus on, signature age, exclusions, last full scan, third-party AV | CTRL I3/I4 | HAVE (U1): P08-P10; P38 last full / quick scan age (`FullScanEndTime`, `QuickScanEndTime`); P39 every antivirus registered in `root\SecurityCenter2` with its state (productState decoding is community documentation) | NOT FOUND | LA |
| A8 | Supported Windows build, date of last update | CTRL I1/I2 | HAVE: S03 | PARTIAL: "Update-uri si hotfix-uri" (history, investigative, no compliance verdict) | LA |
| A9 | Credential protection and account hygiene (LSA PPL, Credential Guard, WDigest, Guest, built-in admin, blank/never-expiring passwords, password and lockout policy) | CTRL C1-C5 | HAVE: P01-P05, P13, P21, P22, C01-C05 | NOT FOUND | LA |
| A10 | PowerShell 2.0 removed, script block logging and transcription | CTRL D5/D6 | HAVE: P23, P24 | NOT FOUND | LA |
| A11 | Data remanence: pagefile cleared at shutdown, hibernation, crash dumps | CTRL F1-F3 | HAVE: P25 | PARTIAL: "Programe blocate si dump-uri (WER)" (dumps listed as artefacts) | LA |
| A12 | Recall/Copilot off; camera and microphone blocked by policy | CTRL F4, E4/E5 | HAVE: P19, P20 | NOT FOUND | LA |
| A13 | Logon banner stating the classification level | CTRL G3 | HAVE: P26 | NOT FOUND | LA |
| A14 | Interconnection paths off: RDP, firewall, SMBv1, shares, Remote Assistance, WinRM, portproxy, ICS | CTRL A4 | HAVE (U1): P06, P07, P12, C06; N06 shares other than C$/ADMIN$/IPC$, N07 Remote Assistance (policy + system setting), N08 WinRM service/state/listeners, N09 portproxy entries, N10 ICS enabled connections + policy. Optional features (Telnet/TFTP/SNMP/IIS) not read | NOT FOUND | LA |
| A15 | Audit policy complete; Security/System logs large enough to cover the control period | CTRL H2/H4; MED §4.5 | HAVE: A01-A04 | PARTIAL: "VERIFICARE PRELIMINARĂ A SURSEI" (log coverage period) | LA |
| A16 | File-access auditing (SACL) on classified folders | CTRL H1 | HAVE (U1): profile table „Foldere clasificate” (`ProfileTable.ClassifiedFolders`); A10 reads each folder's SACL (administrator, read-only) and names the folders without an audit rule, together with the „File System” audit subcategory | NOT FOUND | LA |
| A17 | Time source configured, drift against a reference clock | CTRL H5, P10 | HAVE: P27 + manual M10 | PARTIAL: time-change events, target time zone, DST "oră repetată" | LA |
| A18 | Station compared with the organisation's approved profile / GPO baseline | CTRL §1.1 conclusion, J2 | HAVE: GPO import/validate/approve and procedure profile (`Dfir.Core/Profile/`) | NOT FOUND | LA |
| A19 | Procedural checks answered and printed (accreditation, approved procedures, BIOS password, seals, camera covered, approved exceptions) | CTRL §3 P1-P15 | HAVE: M01-M15 (`ManualChecks.cs`) | NOT FOUND | LA |

### B. Removable media control and registers

| # | Requirement | Source | LogAnalyzer | InspectraVeritas | Better |
|---|---|---|---|---|---|
| B1 | Inventory of every device class, including absent devices, with serial, VID/PID, first install, last arrival, last removal | MED §4.1/4.2 | HAVE: `Dfir.Windows/Devices/DeviceInventory.cs` (SetupAPI all classes, DEVPKEY times, WMI, MountedDevices, 1006) | DESCRIBED: "Inventarul citeste toate categoriile de dispozitive"; 0064/0065/0066/0067 | tie |
| B2 | Weak identity flagged (Windows-generated instance id, `&0` suffix) | MED §3.2 | HAVE: DeviceRules (MED §4bis) | DESCRIBED: "serial generat de Windows" | tie |
| B3 | Approved media register with verdict (in register, not in register, expired, withdrawn/destroyed) | CTRL P3; MED §4.3 | HAVE: `Registers/MediaRegister.cs` + DeviceRules | NOT FOUND | LA |
| B4 | Register filled from the station | MED §4.6 | HAVE: `Registers/RegisterAutoFill.cs` | NOT FOUND | LA |
| B5 | Real-time alert when a medium is connected, with register verdict and sound; feed kept | MED §3, §4.3 | HAVE: `Devices/DeviceWatcher.cs`; `MediaCenterViewModel.cs:212-213` (alert + sound) | NOT FOUND ("LIVE" strings mean analysing a running system) | LA |
| B6 | Native blocking policies read: RemovableStorageDevices per class (disks, CD/DVD, WPD, Deny_All), USBSTOR Start, Device Installation Restrictions, RDVDenyWriteAccess | MED §4.5; EXF X5; CTRL E1/E2 | HAVE: `DevicePolicyCheck.cs:22-45`, P14 | PARTIAL: "Security: instalare BLOCATA de politica de sistem" (events only, not the policy) | LA |
| B7 | "Is copying to removable media recorded?" (Removable Storage and PnP audit) | EXF X1; MED §4.5 | HAVE: A08 | PARTIAL: "Cel mai probabil auditarea accesului la obiecte nu era activata" (after the fact) | LA |
| B8 | BitLocker To Go state of each medium; write blocked on unencrypted media | MED §4.5 | HAVE (U1): D02 lists each removable volume connected at control time with its BitLocker state and applies the RDVDenyWriteAccess effect (unencrypted + write allowed = NECONFORM). Media not connected at control time are not seen | NOT FOUND | LA |
| B9 | Users register compared with accounts and logons | CTRL P11; M11 | HAVE: UsersRegister used by `Analysis/Wp14Rules.cs` | PARTIAL: account reconstruction, no register | LA |
| B10 | Round-trip media: where a medium may be used, flag when also used on a connected network | EXF §3, X19 | HAVE (this station only): MEDIA-ZONE-ROUNDTRIP (`Wp14Rules.OpticalAndZones.cs`): a medium registered for a zone or level that was connected while this station was on a network (NetworkProfile window or Wi-Fi association, 10-minute window when the disconnect was not logged), or whose register zone differs from the declared station zone. Not available: evidence from the other station or network (a medium seen on a connected PC is invisible here). Tests: `AirGapChannelTests` | NOT FOUND | LA |
| B11 | Media lifecycle: validity, withdrawal, destruction record | CTRL P14; M14 | HAVE: history = `Compare` in `Wp14Rules.Media.cs` (withdrawn / destroyed / observation outside ValidFrom-ValidTo -> MEDIA-UNAUTHORIZED, High on a classified scope); live = `DeviceRules.Assess` (Withdrawn / Expired) and the `MediaCenterViewModel` alert with sound. Already in the code before this row was written; now covered by positive and negative tests in `AirGapChannelTests`. Limit: the register has no withdrawal date, so an observation before the withdrawal is also flagged and the text says so | NOT FOUND | LA |
| B12 | Every registered medium physically present; port seals intact | CTRL P3/P8; M03, M08 | PARTIAL: manual checks only (by nature) | NOT FOUND | LA |

### C. Transfer control

| # | Requirement | Source | LogAnalyzer | InspectraVeritas | Better |
|---|---|---|---|---|---|
| C1 | Transfer register: gap-free yearly number, date, direction, classification, source/destination station, person, medium from the media register | EXF §4 (register practice); CMP §4.2 | HAVE: `Registers/TransferRegister.cs` (`NextNumber`), `TransferRegisterView.xaml` (own page since 2026-10-11) | NOT FOUND (CMP §4.1: no request/approval/number strings) | LA |
| C2 | Four-eyes: witness and approver recorded | EXF §4 (two-person integrity, data transfer agent) | HAVE: `Witness`, `Approver` fields | NOT FOUND | LA |
| C3 | SHA-256 of each transferred file at registration | EXF §4 ("hash-ul listei") | HAVE: `TransferViewModel.cs:181-189` via `TransferReportPdf.HashFiles` | NOT FOUND (hashes exist only for the disk search, C7) | LA |
| C4 | Transfer form and period register as PDF with signature lines | CMP §4.2 | HAVE: `TransferReportPdf.Write` (`TransferViewModel.cs:233`) | NOT FOUND | LA |
| C5 | Every medium connection in the period has a register entry; unmatched list with pre-fill | M16; CMP §4.2 | HAVE: `Registers/TransferReconciliation.cs`, `TransferViewModel.cs:144-149` (added 2026-10-12, commit 4696c6e90) | NOT FOUND | LA |
| C6 | Only approved channels (zone to zone, medium) accepted | CTRL P2; profile `TransferChannels` | HAVE (U1): `Registers/TransferChannelCheck.cs` runs when a transfer row is saved; a route or medium outside the approved channels is stopped once with a plain message, and saving again records it with the deviation in the notes. Zones are matched by name against the source/destination fields | NOT FOUND | LA |
| C7 | Verify a declared transfer by content: medium files found on the station disk by SHA-256, with strength | CMP §3 #2; EXF R6/R7 | MISSING | DESCRIBED: "Conținutul unui USB pe un disc"; "Listing: fișierele de pe USB găsite pe disc" | IV |
| C8 | Inbound controls: scan station before first connection; offline update register | CTRL P4/P5; M04, M05 | HAVE (2026-10-16; M04 is manual by nature): D03 checks each medium connection (Partition 1006) for a Defender scan within 10 minutes and a detection within an hour on this station; register of offline updates (`Registers/UpdateRegister.cs`, edited on the control page) compared by S04 with the Windows updates installed in the period; S06 compares every Defender signature update in the period (event 2000, one per version) and the signatures in force (MSFT_MpComputerStatus AntivirusSignatureVersion) with the register's antivirus rows (a row naming a version matches only it, otherwise the same date ±1 day) and flags register rows newer than the station's signatures. Still manual: the scan on the decontamination station before first connection (M04). Limits: the Defender log rotates; event 2000 field names to confirm on a real station log; antivirus products other than Defender are not read | NOT FOUND | LA |

### D. Detection of air-gap breaches

| # | Requirement | Source | LogAnalyzer | InspectraVeritas | Better |
|---|---|---|---|---|---|
| D1 | Wi-Fi adapter present, Wi-Fi service can start | CTRL A1; EXF X4 | HAVE: N03 (`ControlEvaluator.cs:445-457`) | NOT FOUND (saved profiles only) | LA |
| D2 | Network history: NetworkList first/last connection, type, gateway MAC; saved Wi-Fi profiles | CTRL A2; EXF §1.5 | HAVE: N02, N03 (profile count) | DESCRIBED: "Retele cunoscute (NetworkList)", "Profiluri Wi-Fi (Wlansvc)", "retea ascunsa (SSID nedifuzat)", "retea mobila (WWAN)" | IV (more detail) |
| D3 | Connection during the control period (WLAN 8001/8003, NetworkProfile 10000, DHCP) | EXF §1.5 | HAVE: AIRGAP-WIFI-ASSOCIATED, AIRGAP-NETWORK-CONNECTED, AIRGAP-DHCP-LEASE (`Wp14Rules.AirGap.cs:106-111`), N01 | PARTIAL: NetworkList last-connect, SRUM hourly totals | LA |
| D4 | New network adapter: USB-Ethernet, phone tethering (RNDIS), cellular modem (WWAN) | EXF §1.5, X4 | HAVE: `AdapterKinds.Classify` names RNDIS / phone tethering, WWAN-MBIM modem, USB-Ethernet, virtual (excluded) by PnP class, hardware / compatible ids (RNDIS class E0/01/03, MBIM 02/0E, CDC ECM/NCM), service and description; AIRGAP-NIC-ADDED names the kind and raises phone / cellular to High; AIRGAP-ADAPTER-KIND reports adapters from the live inventory installed in the case period (install time from the device Properties key; unknown time says so); station check N05. Limit: the kind is a deduction from names and ids; enabling / use is not in these sources | PARTIAL: network type "Wi-Fi / cablu (Ethernet)", "retea mobila" | LA |
| D5 | Bluetooth radio, service, paired devices | CTRL A3; EXF X11 | HAVE: N04, AIRGAP-BLUETOOTH-PAIRED, `DeviceKind.Bluetooth` | PARTIAL: "BTHENUM (Bluetooth)" in inventory | LA |
| D6 | Phones and cameras through MTP/WPD | EXF §1.1 | HAVE: `DeviceKind.PortableDevice`, WPD policy class | DESCRIBED: "WPDBUSENUM (telefoane si camere prin MTP)" | tie |
| D7 | Optical: writer present, burn activity and tools, ISO mounts, CD/DVD write policy, NoCDBurning, Burn staging folder | CTRL E2/E3; EXF §1.2, X9 | HAVE: MEDIA-OPTICAL-ACTIVITY (events), AIRGAP-OPTICAL-WRITE-UNBLOCKED (writer capability from WMI Win32_CDROMDrive Capabilities, Deny_Write for CD/DVD, NoCDBurning; warns when only Explorer burning is blocked), MEDIA-OPTICAL-STAGING (files in the Windows Burn staging folder), MEDIA-OPTICAL-TOOL-LOG (ImgBurn logs, excerpt), station check P28. Writer capability from two sources (2026-10-16): WMI Capabilities and the drive's own profile list through IMAPI2 (MsftDiscMaster2 → MsftDiscRecorder2.SupportedProfiles, late-bound COM, read-only, STA thread with 15 s timeout, `Devices/ImapiRecorders.cs`); a drive is a writer when either says so, and P28 names the sources read. Limits: IMAPI2 profile reading is UNVERIFIED on a real writer (the development station has no optical drive); the ImgBurn log locations are best-effort; staging folder and logs are those of the account that runs the collector | NOT FOUND (no CD/DVD string) | LA |
| D8 | Printing recorded: PrintService log on, network printers, event 307 | CTRL H3; EXF X6 | HAVE: PrintService/Operational added to the collected channels; AIRGAP-PRINT-JOB parses event 307 (document, user, printer, port, pages) per printer in the period and names print-to-file printers; AIRGAP-PRINT-NETWORK-PRINTER; AIRGAP-PRINT-UNLOGGED (log off while printers exist); station check P29; manual M12 stays for the paper register. Limits: the log is off by default, so no events is not "nothing printed"; per-user printer connections of other profiles are not listed; the PDF a virtual printer wrote is not located | NOT FOUND | LA |
| D9 | Screenshots and clipboard history | EXF X7, X16 | HAVE: AIRGAP-SCREENSHOTS (Pictures\Screenshots files and Snipping Tool / Screen Sketch work folders in the period, as a possible second channel), AIRGAP-CLIPBOARD-TIMELINE (EnableClipboardHistory / AllowClipboardHistory, ActivitiesCache.db with EnableActivityFeed), station check P30. Limits: current user only; a redirected Pictures folder is followed by the shell folder, not by search; no content of clipboard or Timeline is read | PARTIAL: "Windows Timeline (ActivitiesCache.db)", clipboard entries, "Miniaturi extrase (thumbcache)" | tie |
| D10 | Malware traces on inserted sticks (autorun.inf, RECYCLER.BIN, hidden or blank-name folders, lone root LNK) | EXF X3, §3 | MISSING (autorun.inf only in `CodeExplainer.cs` text) | NOT FOUND (a USB listing tool exists, no flagging) | tie |
| D11 | BadUSB: new keyboard followed by a command shell | EXF X12 | HAVE: AIRGAP-BADUSB-PATTERN: an external HID / USB / Bluetooth keyboard configured (Kernel-PnP 400, DriverFrameworks 2003/2100, Security 6416; PS/2 and ACPI keyboards excluded) and cmd / PowerShell / pwsh / wscript / cscript / mshta started within 30 s (4688, Sysmon 1, Prefetch); T1200; states that order in time does not prove the device typed the command (KVM, docking, a user opening a console). Needs the Kernel-PnP/Configuration channel and a process-start source | NOT FOUND ("HID (tastatura/mouse/etc.)" listed only) | LA |
| D12 | Virtual machines, nested networks, second OS | CTRL A5; EXF X10 | HAVE: AIRGAP-VM-PRESENT (Hyper-V machines, WSL distributions from Lxss, VirtualBox / VMware / QEMU installed), AIRGAP-VM-DISK-IMAGE (.vhd .vhdx .vmdk .vdi .qcow2 .avhdx in common folders of fixed volumes; bounded: depth 4, 20 000 entries, 100 results, truncation stated), AIRGAP-MULTIBOOT (bcdedit /enum osloader with more than one OS entry), plus the existing ISO / VHD mount traces. Since 2026-10-16 the same finding also lists container engines (Docker, Podman, Rancher Desktop installed, Docker / containerd services), Windows containers in %ProgramData%\Docker\containers (name from config.v2.json, at most 50), virtual networks inside the station (Hyper-V virtual switches through Msvm_VirtualEthernetSwitch, host-side virtual adapters of Hyper-V, VirtualBox and VMware) and bridge drivers that can join a VM to the physical network (VBoxNetLwf, VBoxNetFlt, VMnetBridge). Limits: Hyper-V machines, switches and BCD need administrator; WSL is the current user only; containers inside Docker Desktop's Linux VM are not listed (only the engine); the switch type (external / internal / private) is not read | DESCRIBED: "Virtualizare - WSL, Hyper-V, Docker, VirtualBox" | tie |
| D13 | Archiver history (WinRAR/7-Zip) with targets on removable drives | EXF X8 | HAVE: MEDIA-ARCHIVER-REMOVABLE: WinRAR ArcHistory and DialogEditHistory, 7-Zip File Manager FolderHistory / CopyHistory / PanelPath with a path on a drive letter that is removable (MountedDevices pointing at USB storage, removable or optical drive now, USB history letters); no MITRE id asserted. Since 2026-10-16 each entry keeps its place in the list (WinRAR values "0" = most recent, confirmed on a real WinRAR hive; 7-Zip order assumed most recent first) and, for WinRAR, whose lists each have their own key, the key's last write time (RegQueryInfoKey): when the most recent entry of a list is on the removable drive the finding takes that time, otherwise it states that the entries were added before it. Limits: current user hive only; the registry keeps no time per entry (only the most recent one is dated); 7-Zip's FM key also holds settings, so it gives no time; a created archive cannot be told from an opened one | NOT FOUND | LA |
| D14 | Network volume per application (SRUM) | EXF X18 | HAVE: AIRGAP-UPLOAD-OUTLIER: bytes sent per application per UTC day above 100 MiB (104.9 MB) and above 5 times the application's own median day (3 days of history needed; a shorter history needs 5 times the threshold), capped at 10 detailed findings plus a summary, with benign alternatives. Thresholds are defaults in `ChannelLists` and can be changed without code through the verified offline data pack (`airgap_lists.json`, section `channels`: `srumUploadBytesPerDay`, `srumMedianFactor`, `srumMinDays`, `srumMaxFindings`); they are NOT calibrated on field data (lab work, UNVERIFIED); SRUM gives no destination | DESCRIBED: "volum mare trimis in retea de o singura aplicatie intr-o zi" | tie |
| D15 | Covert-channel hardware (camera, microphone, speakers) | EXF X17; CTRL E4/E5 | HAVE: inventory of present Camera / Image / MEDIA / AudioEndpoint devices with the enabled state (SetupAPI, milliseconds instead of the WMI class that took 40 s), AIRGAP-COVERT-HARDWARE compared with the P19 consent values (Medium when not denied, Low when denied, driver still on), station check P31, manual M09 for the physical cover. Limit: "enabled" is the driver state; BIOS disabling is invisible | NOT FOUND | LA |
| D16 | Real-time alerting while the station is used (new device, log cleared, security events) | MED §3, §4.3 | HAVE: `DeviceWatcher.cs`; `LogAnalyzer.Connectors/Network/LiveEventLogWatcherService.cs` (Security, System, Sysmon, PowerShell) | NOT FOUND | LA |
| D17 | Anti-forensics: log cleared, audit policy changed, clock changed | CTRL C6/P10; A05-A07 | HAVE: A05-A07, LOG-TAMPER, LOG-GAP | DESCRIBED: "Actiuni sensibile (politica/ora/domeniu)" | tie |
| D18 | Cleaning of USB traces (deleted registry keys, device key recreated) | CMP §1.1, §1.4 | PARTIAL (2026-10-12): deleted keys and values under Enum\USBSTOR, Enum\USB, MountedDevices, Windows Portable Devices, EMDMgmt and MountPoints2 are recovered from the free cells of offline SYSTEM, SOFTWARE and NTUSER hives with the Registry library (`DeletedRegistryTraces.cs`) and reported as MEDIA-TRACES-DELETED, with a "traces may have been cleaned" signal for a device logged in 1006 that has no key left (`Wp14Rules.Cleanup.cs`). Tested on synthetic hives only. Gaps: transaction logs are not applied, no live-hive recovery, no device-key recreation check, overwritten cells are lost | DESCRIBED: "Chei sterse recuperate din registry"; "Intrare de dispozitiv stearsa din registry" | IV |

### E. Post-event investigation of a medium or user

| # | Requirement | Source | LogAnalyzer | InspectraVeritas | Better |
|---|---|---|---|---|---|
| E1 | Full connection history per device from every source, earliest evidence shown | MED §2; EXF §2 step 1 | HAVE (2026-10-12): every source merged per medium, oldest first, earliest evidence named in a MEDIA-HISTORY finding and on the medium page (`Wp14Rules.History.cs`); archived logs (Archive-*.evtx) and Windows.old logs / SYSTEM / SOFTWARE / NTUSER.DAT collected live (`ArchivedLogsCollector`) or found in a brought folder (`HistoricalSources.Discover`), origin kept in notes and custody; setupapi.dev.log of Windows.old is imported as a file, not parsed | DESCRIBED: "Sesiuni USB istorice reconstruite"; "Prima instalare (cea mai veche dovada)" | IV |
| E2 | Files tied to a specific stick: LNK/Jump List volume serial joined with the 1006 volume serial; confidence per row | EXF R2/R3, X2 | PARTIAL (2026-10-12): the volume serial of the NTFS, exFAT, FAT32 or FAT12/16 boot record logged in 1006 (Vbr0 to Vbr3, own code `VolumeBootRecord.cs`) is stored on the medium and joined with the LNK and Jump List volume serial, so a link written while the medium was connected is "opened from this medium" (strong); the drive letter stays a hint. Tested with synthetic boot sectors and LNK files. Gaps: two levels only (strong, hint); the serial is a 32-bit value; no join by serial for 4663 or USN; boot records exist only while the 1006 log does | DESCRIBED: "Dovada de volum"; "volum neverificat, indiciu" | IV |
| E3 | Direct write proof (Security 4663 WriteData on removable) | EXF R1 | HAVE: `Write4663` (`Wp14Rules.Media.cs:147`) | DESCRIBED: 4663 references in the USB-on-disk listing | tie |
| E4 | Who used the stick: MountPoints2, logon session at connection time, explicit AMBIGUOUS | EXF R10; MED §2.2 | PARTIAL (2026-10-12): the offline NTUSER parser already kept the MountPoints2 key last-write time (own reader, kept; the Registry library is not needed for it) and `MediaUsageRules` already returns direct, inferred and ambiguous per connection; the live view now reads the key last-write time as well. Gaps: the key time is the profile's last use of the volume, not the minute of a connection; logon sessions are read only in a case analysis | DESCRIBED: "Corelare USB → utilizator"; "AMBIGUU" | IV |
| E5 | What followed the mount (executions, files in a window) | CMP §1.4 | HAVE (2026-10-12): `Wp14Rules.AfterConnect.cs` — per connection, Prefetch / 4688 / Sysmon 1 executions, LNK / Jump List / Sysmon 11 files and downloads in −10 min … +2 h, scored (medium letter, first 15 min, session user) and capped at 25; other users and system accounts excluded when the interactive session user is known; MEDIA-AFTER-CONNECT finding linked both ways to the medium's findings and shown on the medium detail | DESCRIBED: "Ce a urmat dupa montare (corelare)" | IV |
| E6 | Read the medium itself: listing, Modified-before-Created, USN on NTFS sticks | EXF R5/R6, X13 | PARTIAL (2026-10-16; CODE_VERIFIED, TEST_VERIFIED on synthetic listings and a temp folder, not RUNTIME_VERIFIED on a real stick): `RemovableMediaInspector.List` (read-only, FileShare.ReadWrite, reparse points not followed, entry and depth caps, SHA-256 up to 16 MB for the P1 rules, but the arrival analysis lists with no hashing so no file is opened for it; access errors counted) now also records last-access time; `MediaArrivalAnalysis` (`Devices/MediaArrivalAnalysis.cs`) reports files with Modified earlier than Created as "pare copiat pe mediu la <created>" (an observation, not a suspicion) and an arrival timeline by creation day; both are shown on the "Acum" tab of the Media Center next to the quick check, with the `fsutil usn readjournal X: csv` hint for NTFS sticks. Limits: no finding or rule id (display only); listing is root plus two levels and at most 3000 entries, truncation is stated; the USN journal of the medium is not read by the app (export by an administrator, import with the existing USN parser); times can be rewritten and FAT stores 2-second steps; read-only is not access-time neutral: reading shortcuts (and hashing) can update last-access times on FAT/exFAT and on NTFS when enabled, so the listing is taken before the quick check opens anything and the card states that last access may reflect the inspection itself; FAT/exFAT store the local time of the computer that wrote the file, so the card adds a time-zone caveat (hours of offset possible) for those file systems; the same copy pattern also appears when extracting an archive or with tools that keep modification times | PARTIAL: "Listare si cautare pe USB/folder" | IV |
| E7 | Folder browsing and provenance: ShellBags, Zone.Identifier, Recycle Bin | EXF R4/R7 | PARTIAL (uncertain): Zone.Identifier referenced in `Analysis/Correlation.cs`; ShellBags/Recycle Bin only in texts | DESCRIBED: "Folder vizualizat (ShellBags)"; "Coș de reciclare ($I)" | IV |
| E8 | File anti-forensics: timestomping, deleted $MFT records, $LogFile | EXF R6 (caveat) | MISSING | DESCRIBED: "Fisiere sterse din $MFT"; "Marcaje suspecte" | IV |
| E9 | Unified timeline with export | EXF §2 step 5 | HAVE: Timesketch-compatible export (commit ee9290150, 2026-10-12) | DESCRIBED: "Cronologia completa"; ±15 min; NDJSON (ECS) | tie |
| E10 | Collection from the live station (all event channels) and from offline targets (disk, image, VSS) | MED §4.6 | PARTIAL → mostly HAVE (2026-10-12): live export fixed 2026-10-11 (Overwrite flag 0x2000, commit 46a7d828d); offline copies imported; Volume Shadow Copies listed read-only through WMI Win32_ShadowCopy (administrator only, otherwise "Nedeterminat") and the chosen copy's logs, hives and Prefetch imported from \\?\GLOBALROOT\Device\HarddiskVolumeShadowCopyN with the copy in notes, custody and audit (`ShadowCopies.cs`); "Colectează tot" button (G3). Collection over an admin share is NOT done by owner decision (2026-10-16): the application opens no SMB connection; remote stations are collected only with the offline remote package the operator runs there. Row closed by that decision | DESCRIBED: "Colecteaza tot"; external disk, admin share, "Snapshot-ul ales devine ținta analizei" | IV |
| E11 | Case scope and authorising act recorded at the start | CTRL P1 | HAVE: authorising act (commit 667f76d77); scope asked at start (commit 9bb9ad8a4) | PARTIAL: "Case ID", "Acquisition ID", examiner, notes | LA |
| E12 | Per-user activity: logon types, admin use, account changes, RDP | CTRL C6; U01-U06 | HAVE: U01-U06 | DESCRIBED: session pairing, "Logon-uri nocturne (22:00-06:00)", triage score | tie |

### F. Evidence and report integrity, legal value

| # | Requirement | Source | LogAnalyzer | InspectraVeritas | Better |
|---|---|---|---|---|---|
| F1 | SHA-256 per evidence file, hash-chained custody/audit log, broken chain detected | CMP §1.11 | HAVE: `Case/CaseWorkspace.Integrity.cs`, sealed audit chain | DESCRIBED: "Lant rupt la linia" | tie |
| F2 | Evidence re-verified before the report, result printed | CMP §1.11 | HAVE (2026-10-12): `Case/PreReportCheck.cs` — the investigation and control PDFs run the case re-check (`CaseWorkspace.Recheck`) before printing, the transfer-register PDF re-verifies the declared SHA-256 on the medium; page 1 prints "N OK · M modificate · K lipsă"; a changed or missing item or a broken chain blocks "confirmat" wording (rewritten to "neconfirmat") | DESCRIBED: "Verificarea dinainte de raport: re-verific hash-urile probelor" | IV |
| F3 | Report digitally signed, with a verifier | CMP §1.10 | HAVE (2026-10-12): `Reporting/ReportSignature.cs` — ECDSA P-256 key of the operator / organisation (`LogAnalyzer.KeyGen report-keygen`, refuses to write inside a git repository), detached `<raport>.pdf.sig` with SHA-256, size, signer, key id; every generated PDF is signed when a key is configured (path and trusted public keys in %APPDATA%, set on the investigation page); "Verifică un raport…" in the app and `KeyGen verify-report` print VALID / MODIFIED / UNKNOWN KEY | DESCRIBED: "SEMNATURA VALIDA - fisierul nu a fost modificat de la semnare." | IV |
| F4 | Verifiable case archive (manifest, archive hash, manual verification commands) | CMP §1.11 | HAVE (2026-10-12): `Case/CaseArchive.cs` — ZIP of the case with manifest.json (SHA-256 and size of every file), manifest.sha256, README.txt with PowerShell Get-FileHash and certutil commands, sidecar `<zip>.sha256`; import verifies everything first and refuses a changed, missing or added file; `KeyGen verify-archive` (the older `.dfir` snapshot remains) | DESCRIBED: "Arhivă creată și verificată"; "VERIFICARE FĂRĂ INSPECTRA VERITAS" | IV |
| F5 | Limitations and what the report cannot prove, before the content | CTRL §5 | HAVE: mandatory "Limitări" (commit d1fb40978), "Concluzie pe scurt" box (commit 788a2141f) | DESCRIBED: "Fiabilitatea raportului"; "Limitele raportului, inainte de continutul lui." | tie |
| F6 | Tool identity on every page: program SHA-256, detection lists, time zone | CTRL §5 | HAVE: `Audit/ReportFooter.cs:19-36` | PARTIAL: certificate fingerprint, About with libraries | LA |
| F7 | Legal framing: authorising act, controller declaration, "not a technical-scientific or expert report", legal-counsel audience | CTRL §1.1 | HAVE: commits 667f76d77, 788a2141f | PARTIAL: "nu inlocuieste documentatia procedurala/legala de predare-primire" | LA |
| F8 | Report marked with the station's classification level | CTRL §5, G5 | HAVE: `Audit/ControlReportHeader.cs:34` ("Clasificare stație") | NOT FOUND | LA |
| F9 | Honest confidence: observed vs inferred; "no match" is not "clean"; not run = undetermined | CTRL §5 | HAVE: `ControlStatus.Nedeterminat`; finding classification/confidence; counters (commit 788a2141f) | DESCRIBED: "corelările nu ajung niciodată la CONFIRMAT" | tie |
| F10 | Analysis footprint on the target; PII-masked export for sharing | CTRL §5 (privacy) | HAVE (2026-10-12): "Amprenta analizei pe țintă" section of the investigation report (`Case/AnalysisFootprint.cs`: case folder, exported logs and saved hives counted from the evidence index, temporary folders, registry areas read only, paths generalised); `Privacy/PiiMasker.cs` masks users, hosts, IPv4, e-mail, SIDs, UNC hosts and the user segment of paths with consistent pseudonyms, used by "Raport mascat (PDF)" and "Cronologie mascată (CSV)". The footprint lists what the code is designed to write; it is not a measured file-system diff | DESCRIBED: "Amprenta analizei pe tinta"; "EXPORT CU DATE PERSONALE MASCATE" | IV |

### G. Usability for a non-IT controller

| # | Requirement | Source | LogAnalyzer | InspectraVeritas | Better |
|---|---|---|---|---|---|
| G1 | Each check and code explained in plain words, with the next step | CTRL §2 ("Ce i se spune controlorului") | HAVE: `CheckGuide.cs`, `CodeExplainer.cs`, `LogAnalyzer.Core/Services/Details/ItemMeaning.cs` | PARTIAL: artefact explanations written for an examiner | LA |
| G2 | Role-based simple screens, Home as a task list | EXF header (non-IT user) | HAVE: `LogAnalyzer.Core/Services/Edition/RoleProfile.cs` (WP18) | PARTIAL: "Meniul din stanga urmeaza pasii investigatiei" | LA |
| G3 | Few steps: one "collect everything", results reused, empty screen offers the action | CMP §5 | PARTIAL → mostly HAVE (2026-10-12): "Colectează tot" in the top bar (`Services/CollectAll.cs`) runs the complete collection and analysis for the role (CSIRT: profile Complet; control: Standard plus the station control); the result is published once and reused by Acasă, Investigație and the medium detail; "Nu există încă date — Colectează acum" with the same button on Acasă, Investigație, Control stație and the medium detail. Legacy pages under "Avansat" keep their own loading; since 2026-10-16 the window shows the same offer above them (dashboard, events, registry, alerts, timeline, risk, attack chain, threat intelligence, MITRE: `MainViewModel.LegacyDataTabs`) while neither "Colectează tot" has a result nor the page has loaded events (`NoDataPanel.PageItemCount`). Limit: checked by tests on the XAML and the view model, not seen in the running window (the development build stops at the licence screen). The hide condition uses the loaded events only, so a legacy page that holds its own non-event data (registry, alerts) may still show the offer until events are loaded | DESCRIBED: "Colecteaza tot"; "ecranul spune 'Neanalizat' si ofera 'Colecteaza acum'" | IV |
| G4 | Procedural questions on screen, printed with answers | CTRL §3 | HAVE: `ManualChecks.cs` | NOT FOUND | LA |
| G5 | Works without admin; "could not verify" instead of "compliant" | CTRL §5 | HAVE: `ControlStatus.Nedeterminat` (`ControlEvaluator.cs:448`) | DESCRIBED: "vor apărea NECITIT"; run-as-admin warning | tie |
| G6 | Plain conclusion first for the reader | CTRL §5 | HAVE: "Concluzie pe scurt" | PARTIAL: triage ordered by priority, limits first | LA |
| G7 | Built-in Romanian guide and glossary | — (UX contract) | HAVE: user guide, glossary | DESCRIBED: "Manualul e organizat pe flux"; RO/EN | tie |
| G8 | Fully offline; detection lists updated by a signed offline pack | CTRL §2.I | HAVE: signed data pack (commit a207f2907) | PARTIAL: "Editia fara AI"; IoC lists pinned by SHA-256 | LA |

## 2. Scores per group

Full = HAVE (LA) or DESCRIBED (IV). Score = full + 0.5 × partial.

| Group | Rows | LA full | LA partial | LA missing | LA score | IV full | IV partial | IV not found | IV score |
|---|---|---|---|---|---|---|---|---|---|
| A. Prevention/configuration | 19 | 12 | 5 | 2 | 14.5 (76%) | 0 | 4 | 15 | 2.0 (11%) |
| B. Media control and registers | 12 | 8 | 4 | 0 | 10.0 (83%) | 2 | 3 | 7 | 3.5 (29%) |
| C. Transfer control | 8 | 5 | 2 | 1 | 6.0 (75%) | 1 | 0 | 7 | 1.0 (13%) |
| D. Air-gap breach detection | 18 | 7 | 5 | 6 | 9.5 (53%) | 6 | 4 | 8 | 8.0 (44%) |
| E. Post-event investigation | 12 | 4 | 4 | 4 | 6.0 (50%) | 10 | 2 | 0 | 11.0 (92%) |
| F. Integrity and legal value | 10 | 6 | 2 | 2 | 7.0 (70%) | 7 | 2 | 1 | 8.0 (80%) |
| G. Non-IT usability | 8 | 7 | 1 | 0 | 7.5 (94%) | 3 | 4 | 1 | 5.0 (63%) |
| **Total** | **87** | **49** | **23** | **15** | **60.5 (70%)** | **29** | **19** | **39** | **38.5 (44%)** |

**Note U1 (2026-10-12, branch `wk-gaps-station`).** Rows A2, A3, A4, A5, A7, A14, A16, B8 and C6 moved to HAVE and
C8 stays PARTIAL after new station checks P32-P39, A09, A10, N06-N10, D02, D03, S04 (StationFacts → ControlEvaluator →
CheckGuide + CodeExplainer → report), the profile table „Foldere clasificate”, the transfer channel check and the
register of offline updates. Evidence state: `CODE_VERIFIED` and `TEST_VERIFIED` on synthetic facts
(`LogAnalyzer.App.Tests/StationConfigChecksTests.cs`: compliant, non-compliant and undetermined cases); not yet
`RUNTIME_VERIFIED` on a test station. Points to confirm in the lab: the Kernel DMA information class, the WinRM
listener registry location, the SecurityCenter2 productState decoding. The tables above keep the original scores;
recomputed with U1: A 19/0/0 = 19.0 (100%), B 9/3/0 = 10.5 (88%), C 6/1/1 = 6.5 (81%), total LA 58/16/13 = 66.0 (76%);
weighted LA 80.7 (periodic control) and 66.5 (leak investigation), IV unchanged.

Reading: rows were chosen from the air-gap requirement documents, not from either tool's feature list, so the
total reflects coverage of the requirements. IV's forensic breadth outside these requirements (browsers, chat
apps, BITS, IoC/YARA, RAM capture, Windows.old accounts) is not counted.

## 3. Weighted verdicts

Weight × group percentage, summed to 100.

| Group | Weight, periodic control | LA | IV | Weight, leak investigation | LA | IV |
|---|---|---|---|---|---|---|
| A | 25 | 19.1 | 2.6 | 5 | 3.8 | 0.5 |
| B | 20 | 16.7 | 5.8 | 10 | 8.3 | 2.9 |
| C | 15 | 11.3 | 1.9 | 15 | 11.3 | 1.9 |
| D | 20 | 10.6 | 8.9 | 15 | 7.9 | 6.7 |
| E | 5 | 2.5 | 4.6 | 35 | 17.5 | 32.1 |
| F | 5 | 3.5 | 4.0 | 15 | 10.5 | 12.0 |
| G | 10 | 9.4 | 6.3 | 5 | 4.7 | 3.1 |
| **Total** | 100 | **72.9** | **34.1** | 100 | **64.0** | **59.2** |

**Verdict 1: periodic control of an air-gapped classified station by a non-IT controller. LogAnalyzer wins clearly (73 vs 34).**
IV has no configuration checks, registers, transfer control or real-time alerting, which are the core of a
periodic control; LA covers them with plain-language guidance and manual checks. IV only helps with the breach
evidence in group D (network history detail, VM traces, deleted USB keys).

**Verdict 2: investigating a suspected leak through a removable medium. Close (LA 64 vs IV 59), with IV ahead on the evidence itself.**
IV answers "which stick, which files, who, what followed, was it cleaned" and proves its report (signature,
archive, pre-report re-hash); LA answers "was this transfer declared, approved, witnessed and reconciled".
Sensitivity: moving 10 points from C to E puts IV ahead (about 67 vs 62). Today, for a real leak case, use LA
for the register and control context and expect IV-class artefact depth to be missing from LA.

Caveat on both verdicts: IV rows are UI text, not tested behaviour; LA rows are code, and much of the device and
alert code is still unvalidated on the test station (MED §4bis: no stick without serial, MTP phone, CD, ISO,
network drive or live alert run yet).

## 4. Where InspectraVeritas is honestly ahead

1. Stick-to-user attribution with an explicit AMBIGUOUS state (E4).
2. File-to-stick proof by volume serial at three strength levels (E2), and full per-device session history from
   every source including archived logs and Windows.old (E1).
3. Medium content found on a disk by SHA-256, with Weak/Medium/Strong strength and honest wording (C7, E6). This
   is the feature that would turn LA's transfer register from paperwork into evidence.
4. "What followed the mount" correlation window (E5).
5. Deleted registry keys and device-key recreation, which reveal cleaning of USB traces (D18); $MFT deleted
   records, timestomping, $LogFile (E8); ShellBags, Recycle Bin, Mark-of-the-Web (E7).
6. Virtual machine traces (WSL, Hyper-V, Docker, VirtualBox) (D12); richer network history (hidden SSID, WWAN,
   gateway MAC) (D2); SRUM per application with large-upload flag (D14); clipboard/thumbcache (D9).
7. Report integrity outside the tool: signed report and verifier, verifiable archive with manual commands,
   forced pre-report re-hash, analysis footprint, PII-masked export (F2, F3, F4, F10).
8. Step reduction: one "collect everything" and empty screens that offer the action (G3); offline targets
   (external disk, VSS, admin share) (E10).

## 5. Top 8 items for LogAnalyzer to build (ideas only, own design)

Ranked by value for both use cases, reusing what LA already has (transfer register hashes, 1006 history,
LNK parser, live watcher, sealed chain).

1. **Transfer verification by content.** The register already stores per-file SHA-256; add a check that hashes
   a chosen station folder/disk or a presented medium and states per declared file "found identical / same
   name different content / not found / unreadable", plus copies on the station that match no declared transfer.
   Wording must say identical content proves neither direction, time nor person. (C7, E6)
2. **Stick-to-file linkage by volume serial.** Join the volume serial from LNK/Jump Lists with the VBR serial in
   1006 and with the media register number, and label each row "written (4663)", "opened from this stick",
   "same letter only (hint)". (E2, EXF X2)
3. **Who used the medium.** Per profile MountPoints2 volume GUIDs joined with MountedDevices, plus the logon
   session covering each connection; several candidates = AMBIGUOUS, shared account = "not attributable". (E4)
4. **Inserted-stick inspection.** On live insertion and on demand, list the volume root without opening
   executables and flag autorun.inf, RECYCLER/RECYCLER.BIN, hidden or blank-name folders, a lone root LNK named
   like the drive, desktop.ini in ordinary folders; raise the existing live alert. (D10, EXF X3)
5. **Report that proves itself.** Force the integrity re-check when a PDF is generated and print the result;
   sign the PDF/JSON with an organisation certificate and ship an offline verifier; export the case as an
   archive with a manifest and printed manual verification commands. (F2, F3, F4)
6. **Breach paths LA still misses.** Name RNDIS (phone tethering), WWAN and USB-Ethernet adapters in
   AIRGAP-NIC-ADDED; detect VM platforms and second OS entries; check shares, Remote Assistance, WinRM, portproxy
   and ICS. (D4, D12, A14)
7. **Print and optical completeness.** PrintService/Operational on or off, local vs network printers, event 307
   when present, KeepPrintedJobs; NoCDBurning-only warning ("a separate burn program can still write"), Burn
   staging folder contents, writer capability of the optical drive. (D7, D8, EXF X6/X9)
8. **"Around this connection" view.** For each medium session: programs run, files touched, archives created
   on the medium (WinRAR/7-Zip history), screenshots saved, in a fixed window, with other users excluded; plus a
   "traces may have been cleaned" signal when a device in 1006 has no current registry key. (E5, D13, D9, D18)

Smaller follow-ups noticed during the check: validate transfer rows against approved `TransferChannels` (C6);
BitLocker on all fixed volumes and PIN protector (A2); TPM and Kernel DMA (A3); SACL on classified folders (A16);
last full Defender scan and third-party antivirus (A7); round-trip flag in the media register (B10).

## 6. Method notes

- IV terms grepped in the strings file: Wi-Fi, Bluetooth, retea, adaptor, CD, DVD, telefon, MTP, politica,
  blocat, GPO, BitLocker, criptare, registru, transfer, monitorizare, timp real, alerta, live, raport, semnatura,
  hash, plus firewall, Defender, UAC, print, screenshot, WinRAR, 7-Zip, autorun, VirtualBox. No hit for BitLocker,
  firewall, Defender (as a check), GPO, CD/DVD, print, monitoring/alerting; "criptare" hits are Wi-Fi profile
  or encrypted-app notes; "registru" hits mean the Windows registry, not a register; "transfer" hits are
  disclaimers ("transferul nu este demonstrat").
- LA statuses come from grep/Read of source, not from documents alone. Items marked "uncertain" (E7) need a
  closer read before planning.
- No code was changed and nothing was committed.

## Real-output check (2026-10-12)

Scope: two real PDF reports generated by InspectraVeritas on a development workstation (a per-user activity
report of 591 pages and an operating-system history report of 4 683 pages), read as text, compared with one
LogAnalyzer investigation report of the same machine (9 pages). Nothing from the tool was run or copied. This
section contains counts and generic descriptions only; the detailed notes with real data stay outside the
repository. The earlier sections above were not edited; where they disagree with this section, this section wins
for the IV column.

Limits of this check: one machine, one user, no removable-medium listing, no optical drive, no second user, no
Hyper-V/VirtualBox and no log clearing happened. A feature that is not visible here is either absent or simply not
triggered; the table below separates the two (NOTEX = described in the UI, not exercised in these outputs). The LA
report used for comparison came from a run with a bug (no event logs exported), so LA's event-log coverage there is
understated; no LA row was re-scored from it.

### R1. Structure and page budget

User activity report (591 pages): summary page with a 0-100 triage score and two bullets, logon/logoff list (2
pages); device table (1 page); file-activity list from LNK/Jump Lists (112 pages, oldest first); folder views from
ShellBags (28 pages); evidence manifest with SHA-256 per item (193 pages); internal custody log with 2 607 numbered
rows (257 pages). Manifest plus custody log are about 76% of the pages. No reliability or limitations section.

System history report (4 683 pages): identity and counters, table of contents, reliability box ("7 reservations"),
reading guide, "what each source proves / does not prove" table, limits (5 pages, 0.1%); "sensitive actions"
(3 363 pages, 72%); installs/updates/accounts/services/software (about 70 pages); execution evidence (76); volume
mounts and "what followed the mount" correlation (14); activity and network (397: SRUM, BITS, networks,
downloads, notifications, thumbnails, browsers, chat traces, virtualization, LNK/Jump Lists, 49 733 deleted-record
entries); NTFS journal (57); full timeline (128); sources read (6); analysis footprint (2); manifest (564, 12%).
Roughly 3% of the pages are something a non-IT reader could use.

### R2. IV statuses by evidence

Codes: DEMO = real data shown (1.0); PART = some real data, incomplete or incidental (0.5); NOTEX = described in the
UI, not exercised or not visible in these outputs (0 in the strict score); ABS = not in the output (0); UI = a UI
property that a PDF cannot show (old credit kept). Rows not listed in a group are ABS.

| Group | DEMO | PART | NOTEX | UI | Notes |
|---|---|---|---|---|---|
| A (19) | none | A7, A8, A11, A15, A17 | none | none | A7 only via a signature-update line in the update history; A17 is the clock-change count. No configuration check of any kind is shown |
| B (12) | none | B1, B9 | B2, B6 | none | B7 downgraded to ABS: object-access auditing was evidently off, and the report did not say so. B1 is one device listed twice |
| C (8) | none | none | C7 | none | nothing about transfers or registers |
| D (18) | D2, D12, D14 | D3, D4, D5, D6, D9, D17 | D18 | none | network history and per-application upload volume are strong; the "sensitive actions" heading promises log clears and policy changes that never appear |
| E (12) | E1, E4, E5, E7, E8, E12 | E2, E9, E10, E11 | E3, E6 | none | E2: volume serial and drive type are printed on LNK rows, but no row is joined to a removable device by serial; E9: see R3 |
| F (10) | F2, F5, F9, F10 | F1, F6, F7 | F3, F4 | none | no signature or verifier text, no hash chain, no program version or hash in any footer; F8 ABS |
| G (8) | G5 | G1, G6, G8 | none | G2, G3, G7 | G4 ABS |

Scores (strict): A 2.5/19 (13%), B 1.0/12 (8%), C 0/8 (0%), D 6.0/18 (33%), E 8.0/12 (67%), F 5.5/10 (55%), G 5.0/8
(63%); total 28.0/87 (32%), down from 38.5 (44%) on UI strings. Upper bound if every NOTEX row regained its old
credit: 35.0/87 (40%). The LA column is unchanged (60.5, 70%).

Weighted verdicts with the same weights as section 3: periodic control of an air-gapped station: IV 24.0 strict /
31.1 upper bound vs LA 72.9 (verdict unchanged, wider gap). Leak investigation through a removable medium: IV 41.2
strict / 52.5 upper bound vs LA 64.0 (was 59.2 vs 64.0). The earlier statement that the two are close is not
supported by the real output; LA's number is still code-verified, not run-verified, and the only real
removable-medium evidence in IV's output is one device with no listing of its contents.

### R3. Quality findings

1. **Page 1 gives no verdict and no next step.** A score with two bullets (both about night-time activity) and a
   disclaimer; the most useful page (reliability by zone) is page 2 of the large report.
2. **"Sensitive actions" is mislabelled noise.** About 88% of the 21 070 rows are per-application resource and
   network aggregates, about 10% are deleted-record entries; none is a log clear, audit-policy change, account
   change or privileged-group change. The list is sorted oldest first, so the first page shows alarms about a
   timestamp mismatch on operating-system servicing files, which is normal for files restored by servicing. Deleted
   script files from build tools are flagged too. The same term shows 0 in one report and 21 070 in the other.
3. **Time-zone label wrong, data right.** Every header says "(UTC+02:00) Athens, Bucharest", which is the base
   offset name; the times in the data are correctly shifted to the summer offset (checked three ways: the same
   connection in LogAnalyzer's UTC, the case id against the custody log, the generation time against the export
   time). A reader who trusts the label is off by one hour. The custody log is in UTC while everything else is
   local. No repeated-hour (DST) marker appears although the data range spans three transitions.
4. **Triplicated logons inflate the triage score.** Each logon produces three rows (session manager, profile,
   terminal-services session), so 4 real logons show as 12 and 2 night-time logons as 6; the session pairing then
   reports most sessions as "without logoff". The night-time logons coincide with the operating-system upgrade
   reboots listed elsewhere in the other report; there is no baseline of the user's normal hours. File "actions"
   are three views of each LNK plus Jump List entries, so the 1 000-plus "night actions" are multiplicities.
5. **Device and attribution handling.** The same physical USB-attached disk is listed twice (disk and bridge ids,
   same serial), the serial looks like a bridge default and is not flagged weak. For the USB connection the report
   honestly says the user cannot be established. Three volume mounts with no device identity are nevertheless
   described as "the device was used by the account" with SUPPORTED confidence; two of those anchors are duplicates
   (same minute, identical counts). The correlation windows are dominated by SRUM hourly bins printed as exact
   times, and programs run from a second volume appear as raw volume-GUID paths.
6. **Self-inflicted integrity noise.** The tool repaired a database working copy, so its hash no longer matched the
   registered one: about 21 000 rows are marked "unverified" (88% of the "to verify" list), and 37 manifest items
   print "MISMATCH - altered" although the cause is a growing live log or its own repair. Honest, but alarming and
   useless to a reader.
7. **The timeline in the PDF has no investigative value.** It prints the latest 1 500 of 34 841 events; all fall in
   one six-minute window and 99% come from the NTFS journal, which on this machine covered under an hour (the
   examiner's own activity). Full data only in CSV/NDJSON.
8. **Over-collection and exposure.** The reports print text of notifications from other applications (including the
   examiner's own tools), browser downloads, chat-application traces, Wi-Fi network names and thousands of
   download marks: outside the scope of a station control. Masking exists only for the NDJSON/STIX exports. An
   antivirus detection notification appears only deep in the notification list.
9. **Language.** Diacritics are used in some sections and dropped in others (sometimes in the same line); machine
   strings leak (a doubled "state" word); "logon events 12" vs "(23)". The confidence legend and the "proves / does
   not prove" table are clear, correct Romanian.
10. **What is good.** Explicit uncertainty everywhere (confidence scale, "undetermined" when the account database was
    not read, per-source "read / partial / unavailable" table with periods and counts, "correlations never reach
    CONFIRMED"); a page describing what the analysis did to the target; a visible pre-report re-hash pass;
    network history with gateway MAC, type and first/last connection; virtualization with disk location and size;
    per-application upload outliers with benign alternatives listed (49 flags on a developer machine, so alarm
    fatigue is a risk).

Legal-value features actually seen: case and acquisition ids on every page, page numbering, SHA-256 per item with
per-item verdict, numbered custody log, honest closing notes about the write-blocker and the original disk, limits
before content (large report only). Not seen: tool version or hash, detection-list identity, signature or
verifier (a PDF-level signature cannot be judged from text: UNKNOWN), hash chaining, authorising act or scope
(the case is created automatically, the examiner is the target's own account), classification marking.

### R4. What IV's real output does that LA does not (ideas only)

Per-connection correlation anchors with an explicit attribution state; an up-front reliability box with per-zone
status and a positive coverage table (period and count per source, not only failures); a "what each source proves
and does not prove" table in the front matter; an analysis-footprint page; a visible pre-report re-verification
with per-item verdict; volume serial and drive type printed on file-access rows; richer network history; virtualization
and disk location; per-application upload outliers with alternatives; notification and thumbnail traces as sources;
SIEM/threat-intelligence exports with personal data masked.

### R5. Where LA is better (evidence)

A 9-page report with conclusion and evidence gaps before findings, and "why / alternative / missing / limit" on each
finding; clock distrust lowers the confidence of the affected findings (IV only counts clock changes); an explicit
"object-access auditing was off, here are the alternatives" gap (IV is silent); correct time-zone label with the
real offset; tool hash, detection-list identity and custody/audit anchors on the page; register-based states ("no
register defined" is UNKNOWN, not compliant). LA's station configuration checks, registers, transfer control and
live alert were not exercised in this comparison. LA weaknesses seen in the same comparison: that run's missing event
logs (fixed), two HIGH correlations on ordinary installers, a RecordID-anomaly detection that looks noisy on a fresh
install, a thin removable-medium finding (no volume serial, user or session), and no ShellBags, network history,
virtualization or per-application upload view.

### R6. Top 5 ideas for LogAnalyzer (own design)

1. **Medium-session correlation with attribution states**: for each connection, facts in a window (programs, files,
   downloads) tied to the profile that mounted the volume, with states none / one / ambiguous / not attributable;
   exclude other users' facts, de-duplicate anchors, never use hourly aggregates as moments, translate volume ids
   to drive letter and label, and join the LNK volume serial to the connection record (E2, E4, E5).
2. **Reliability and coverage on page 1**: one box per zone (read / partial / unavailable, period, counts) plus a
   "what each source proves / does not prove" table, and a positive coverage table for every log read (A15, F5, F9).
3. **Report that proves itself without bulk**: re-hash before the PDF with a printed result, a one-line manifest
   digest and count instead of hundreds of pages, a single working-copy hash policy (hash the copy before and after
   any repair and record both), footprint page, and wording "source changed while live" instead of "altered"
   (F1, F2, F10).
4. **Air-gap-relevant network and exfiltration view**: network history with type, first/last connection, gateway
   id and hotspot-like names; adapters of tethering/cellular type; per-application upload outliers with the
   benign alternatives, capped and sorted by recency (D2, D4, D14).
5. **Second-system and non-system-volume traces**: WSL/Docker/VM disks with location and size, programs run from
   volumes other than the system one, and screenshot/notification traces, each as an explicit "possible second
   channel" line (D9, D12).

Page-economy rule drawn from the real output: put the answer and the gaps in the first pages, keep lists out of
the PDF (CSV/attachment) unless filtered to the control period, and never use the same label for different
counts in two reports.
