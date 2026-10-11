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
| A2 | BitLocker on every fixed volume, PIN at boot | CTRL B4 | PARTIAL: P11 reads only the system drive (`StationFacts.cs:201`); no PIN, no other volumes | NOT FOUND (only "fișier criptat EFS" as a read obstacle) | LA |
| A3 | TPM present/ready, Kernel DMA protection | CTRL B3/B5 | MISSING: no TPM or DMA code | NOT FOUND | tie |
| A4 | Screen lock at 15 min or less, password on wake | CTRL G1/G2 | PARTIAL: P16 inactivity lock; wake password not found | NOT FOUND | LA |
| A5 | Application allowlisting present, mode, blocked attempts | CTRL D1/D2 | PARTIAL: P17 AppLocker present and mode; App Control and 8003/8004/3076/3077 not verified | NOT FOUND | LA |
| A6 | AutoPlay/AutoRun off | CTRL D3; MED §4.5 | HAVE: P18 (`ControlEvaluator.cs:526`) | NOT FOUND | LA |
| A7 | Antivirus on, signature age, exclusions, last full scan, third-party AV | CTRL I3/I4 | PARTIAL: P08-P10; no last-full-scan or SecurityCenter2 read | NOT FOUND | LA |
| A8 | Supported Windows build, date of last update | CTRL I1/I2 | HAVE: S03 | PARTIAL: "Update-uri si hotfix-uri" (history, investigative, no compliance verdict) | LA |
| A9 | Credential protection and account hygiene (LSA PPL, Credential Guard, WDigest, Guest, built-in admin, blank/never-expiring passwords, password and lockout policy) | CTRL C1-C5 | HAVE: P01-P05, P13, P21, P22, C01-C05 | NOT FOUND | LA |
| A10 | PowerShell 2.0 removed, script block logging and transcription | CTRL D5/D6 | HAVE: P23, P24 | NOT FOUND | LA |
| A11 | Data remanence: pagefile cleared at shutdown, hibernation, crash dumps | CTRL F1-F3 | HAVE: P25 | PARTIAL: "Programe blocate si dump-uri (WER)" (dumps listed as artefacts) | LA |
| A12 | Recall/Copilot off; camera and microphone blocked by policy | CTRL F4, E4/E5 | HAVE: P19, P20 | NOT FOUND | LA |
| A13 | Logon banner stating the classification level | CTRL G3 | HAVE: P26 | NOT FOUND | LA |
| A14 | Interconnection paths off: RDP, firewall, SMBv1, shares, Remote Assistance, WinRM, portproxy, ICS | CTRL A4 | PARTIAL: P06, P07, P12, C06; shares/Remote Assistance/WinRM/portproxy/ICS not found | NOT FOUND | LA |
| A15 | Audit policy complete; Security/System logs large enough to cover the control period | CTRL H2/H4; MED §4.5 | HAVE: A01-A04 | PARTIAL: "VERIFICARE PRELIMINARĂ A SURSEI" (log coverage period) | LA |
| A16 | File-access auditing (SACL) on classified folders | CTRL H1 | MISSING: SACL appears only in explanation texts and capability lists | NOT FOUND | tie |
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
| B8 | BitLocker To Go state of each medium; write blocked on unencrypted media | MED §4.5 | PARTIAL: `DeviceInventory.cs:288` (admin only) + RDVDenyWriteAccess | NOT FOUND | LA |
| B9 | Users register compared with accounts and logons | CTRL P11; M11 | HAVE: UsersRegister used by `Analysis/Wp14Rules.cs` | PARTIAL: account reconstruction, no register | LA |
| B10 | Round-trip media: where a medium may be used, flag when also used on a connected network | EXF §3, X19 | PARTIAL: MediaRegister has a `Zone` column; no round-trip flag | NOT FOUND | LA |
| B11 | Media lifecycle: validity, withdrawal, destruction record | CTRL P14; M14 | PARTIAL: `ValidFrom/ValidTo/Status` columns + manual M14 | NOT FOUND | LA |
| B12 | Every registered medium physically present; port seals intact | CTRL P3/P8; M03, M08 | PARTIAL: manual checks only (by nature) | NOT FOUND | LA |

### C. Transfer control

| # | Requirement | Source | LogAnalyzer | InspectraVeritas | Better |
|---|---|---|---|---|---|
| C1 | Transfer register: gap-free yearly number, date, direction, classification, source/destination station, person, medium from the media register | EXF §4 (register practice); CMP §4.2 | HAVE: `Registers/TransferRegister.cs` (`NextNumber`), `TransferRegisterView.xaml` (own page since 2026-10-11) | NOT FOUND (CMP §4.1: no request/approval/number strings) | LA |
| C2 | Four-eyes: witness and approver recorded | EXF §4 (two-person integrity, data transfer agent) | HAVE: `Witness`, `Approver` fields | NOT FOUND | LA |
| C3 | SHA-256 of each transferred file at registration | EXF §4 ("hash-ul listei") | HAVE: `TransferViewModel.cs:181-189` via `TransferReportPdf.HashFiles` | NOT FOUND (hashes exist only for the disk search, C7) | LA |
| C4 | Transfer form and period register as PDF with signature lines | CMP §4.2 | HAVE: `TransferReportPdf.Write` (`TransferViewModel.cs:233`) | NOT FOUND | LA |
| C5 | Every medium connection in the period has a register entry; unmatched list with pre-fill | M16; CMP §4.2 | HAVE: `Registers/TransferReconciliation.cs`, `TransferViewModel.cs:144-149` (added 2026-10-12, commit 4696c6e90) | NOT FOUND | LA |
| C6 | Only approved channels (zone to zone, medium) accepted | CTRL P2; profile `TransferChannels` | PARTIAL: channels in `ProcedureProfile.cs:36`, used by AIRGAP rules (`Wp14Rules.AirGap.cs:22-28`), not checked when a transfer row is saved | NOT FOUND | LA |
| C7 | Verify a declared transfer by content: medium files found on the station disk by SHA-256, with strength | CMP §3 #2; EXF R6/R7 | MISSING | DESCRIBED: "Conținutul unui USB pe un disc"; "Listing: fișierele de pe USB găsite pe disc" | IV |
| C8 | Inbound controls: scan station before first connection; offline update register | CTRL P4/P5; M04, M05 | PARTIAL: manual M04/M05, plus P09 and S03 data | NOT FOUND | LA |

### D. Detection of air-gap breaches

| # | Requirement | Source | LogAnalyzer | InspectraVeritas | Better |
|---|---|---|---|---|---|
| D1 | Wi-Fi adapter present, Wi-Fi service can start | CTRL A1; EXF X4 | HAVE: N03 (`ControlEvaluator.cs:445-457`) | NOT FOUND (saved profiles only) | LA |
| D2 | Network history: NetworkList first/last connection, type, gateway MAC; saved Wi-Fi profiles | CTRL A2; EXF §1.5 | HAVE: N02, N03 (profile count) | DESCRIBED: "Retele cunoscute (NetworkList)", "Profiluri Wi-Fi (Wlansvc)", "retea ascunsa (SSID nedifuzat)", "retea mobila (WWAN)" | IV (more detail) |
| D3 | Connection during the control period (WLAN 8001/8003, NetworkProfile 10000, DHCP) | EXF §1.5 | HAVE: AIRGAP-WIFI-ASSOCIATED, AIRGAP-NETWORK-CONNECTED, AIRGAP-DHCP-LEASE (`Wp14Rules.AirGap.cs:106-111`), N01 | PARTIAL: NetworkList last-connect, SRUM hourly totals | LA |
| D4 | New network adapter: USB-Ethernet, phone tethering (RNDIS), cellular modem (WWAN) | EXF §1.5, X4 | PARTIAL: AIRGAP-NIC-ADDED on any Net-class device (`Wp14Rules.AirGap.cs:214`); no RNDIS/WWAN naming | PARTIAL: network type "Wi-Fi / cablu (Ethernet)", "retea mobila" | tie |
| D5 | Bluetooth radio, service, paired devices | CTRL A3; EXF X11 | HAVE: N04, AIRGAP-BLUETOOTH-PAIRED, `DeviceKind.Bluetooth` | PARTIAL: "BTHENUM (Bluetooth)" in inventory | LA |
| D6 | Phones and cameras through MTP/WPD | EXF §1.1 | HAVE: `DeviceKind.PortableDevice`, WPD policy class | DESCRIBED: "WPDBUSENUM (telefoane si camere prin MTP)" | tie |
| D7 | Optical: writer present, burn activity and tools, ISO mounts, CD/DVD write policy, NoCDBurning, Burn staging folder | CTRL E2/E3; EXF §1.2, X9 | PARTIAL: MEDIA-OPTICAL-ACTIVITY (`Wp14Rules.Optical.cs`), burn tool list (`airgap_lists.json`), CD/DVD Deny_Write; no NoCDBurning, Burn folder or ImgBurn log | NOT FOUND (no CD/DVD string) | LA |
| D8 | Printing recorded: PrintService log on, network printers, event 307 | CTRL H3; EXF X6 | MISSING: manual M12 only | NOT FOUND | tie |
| D9 | Screenshots and clipboard history | EXF X7, X16 | MISSING | PARTIAL: "Windows Timeline (ActivitiesCache.db)", clipboard entries, "Miniaturi extrase (thumbcache)" | IV |
| D10 | Malware traces on inserted sticks (autorun.inf, RECYCLER.BIN, hidden or blank-name folders, lone root LNK) | EXF X3, §3 | MISSING (autorun.inf only in `CodeExplainer.cs` text) | NOT FOUND (a USB listing tool exists, no flagging) | tie |
| D11 | BadUSB: new keyboard followed by a command shell | EXF X12 | MISSING | NOT FOUND ("HID (tastatura/mouse/etc.)" listed only) | tie |
| D12 | Virtual machines, nested networks, second OS | CTRL A5; EXF X10 | PARTIAL: ISO/VHD mounts via VHDMP (optical rule); virtual adapters only excluded | DESCRIBED: "Virtualizare - WSL, Hyper-V, Docker, VirtualBox" | IV |
| D13 | Archiver history (WinRAR/7-Zip) with targets on removable drives | EXF X8 | MISSING | NOT FOUND | tie |
| D14 | Network volume per application (SRUM) | EXF X18 | PARTIAL: SRUM network | DESCRIBED: "volum mare trimis in retea de o singura aplicatie intr-o zi" | IV |
| D15 | Covert-channel hardware (camera, microphone, speakers) | EXF X17; CTRL E4/E5 | PARTIAL: P19 policy, M09 manual, inventory of all classes | NOT FOUND | LA |
| D16 | Real-time alerting while the station is used (new device, log cleared, security events) | MED §3, §4.3 | HAVE: `DeviceWatcher.cs`; `LogAnalyzer.Connectors/Network/LiveEventLogWatcherService.cs` (Security, System, Sysmon, PowerShell) | NOT FOUND | LA |
| D17 | Anti-forensics: log cleared, audit policy changed, clock changed | CTRL C6/P10; A05-A07 | HAVE: A05-A07, LOG-TAMPER, LOG-GAP | DESCRIBED: "Actiuni sensibile (politica/ora/domeniu)" | tie |
| D18 | Cleaning of USB traces (deleted registry keys, device key recreated) | CMP §1.1, §1.4 | MISSING | DESCRIBED: "Chei sterse recuperate din registry"; "Intrare de dispozitiv stearsa din registry" | IV |

### E. Post-event investigation of a medium or user

| # | Requirement | Source | LogAnalyzer | InspectraVeritas | Better |
|---|---|---|---|---|---|
| E1 | Full connection history per device from every source, earliest evidence shown | MED §2; EXF §2 step 1 | PARTIAL: DEVPKEY, MountedDevices, 1006 history, setupapi (`DeviceInventory.cs`, `UsbDevicesParser.cs`); no archived logs or Windows.old | DESCRIBED: "Sesiuni USB istorice reconstruite"; "Prima instalare (cea mai veche dovada)" | IV |
| E2 | Files tied to a specific stick: LNK/Jump List volume serial joined with the 1006 volume serial; confidence per row | EXF R2/R3, X2 | PARTIAL: MEDIA-FILE-ACTIVITY by drive letter (`Wp14Rules.Media.cs:159-173`); `LnkParser.cs` reads the serial but nothing joins it to 1006 | DESCRIBED: "Dovada de volum"; "volum neverificat, indiciu" | IV |
| E3 | Direct write proof (Security 4663 WriteData on removable) | EXF R1 | HAVE: `Write4663` (`Wp14Rules.Media.cs:147`) | DESCRIBED: 4663 references in the USB-on-disk listing | tie |
| E4 | Who used the stick: MountPoints2, logon session at connection time, explicit AMBIGUOUS | EXF R10; MED §2.2 | MISSING: MountPoints2 read only for network shares (`DeviceInventory.cs:300-323`) | DESCRIBED: "Corelare USB → utilizator"; "AMBIGUU" | IV |
| E5 | What followed the mount (executions, files in a window) | CMP §1.4 | MISSING | DESCRIBED: "Ce a urmat dupa montare (corelare)" | IV |
| E6 | Read the medium itself: listing, Modified-before-Created, USN on NTFS sticks | EXF R5/R6, X13 | MISSING | PARTIAL: "Listare si cautare pe USB/folder" | IV |
| E7 | Folder browsing and provenance: ShellBags, Zone.Identifier, Recycle Bin | EXF R4/R7 | PARTIAL (uncertain): Zone.Identifier referenced in `Analysis/Correlation.cs`; ShellBags/Recycle Bin only in texts | DESCRIBED: "Folder vizualizat (ShellBags)"; "Coș de reciclare ($I)" | IV |
| E8 | File anti-forensics: timestomping, deleted $MFT records, $LogFile | EXF R6 (caveat) | MISSING | DESCRIBED: "Fisiere sterse din $MFT"; "Marcaje suspecte" | IV |
| E9 | Unified timeline with export | EXF §2 step 5 | HAVE: Timesketch-compatible export (commit ee9290150, 2026-10-12) | DESCRIBED: "Cronologia completa"; ±15 min; NDJSON (ECS) | tie |
| E10 | Collection from the live station (all event channels) and from offline targets (disk, image, VSS) | MED §4.6 | PARTIAL: live export fixed 2026-10-11 (Overwrite flag 0x2000, commit 46a7d828d); offline copies imported | DESCRIBED: "Colecteaza tot"; external disk, admin share, "Snapshot-ul ales devine ținta analizei" | IV |
| E11 | Case scope and authorising act recorded at the start | CTRL P1 | HAVE: authorising act (commit 667f76d77); scope asked at start (commit 9bb9ad8a4) | PARTIAL: "Case ID", "Acquisition ID", examiner, notes | LA |
| E12 | Per-user activity: logon types, admin use, account changes, RDP | CTRL C6; U01-U06 | HAVE: U01-U06 | DESCRIBED: session pairing, "Logon-uri nocturne (22:00-06:00)", triage score | tie |

### F. Evidence and report integrity, legal value

| # | Requirement | Source | LogAnalyzer | InspectraVeritas | Better |
|---|---|---|---|---|---|
| F1 | SHA-256 per evidence file, hash-chained custody/audit log, broken chain detected | CMP §1.11 | HAVE: `Case/CaseWorkspace.Integrity.cs`, sealed audit chain | DESCRIBED: "Lant rupt la linia" | tie |
| F2 | Evidence re-verified before the report, result printed | CMP §1.11 | PARTIAL: `Case/IntegrityRecheck.cs` runs after import (`InvestigationViewModel.cs:351`), line printed; not forced at PDF time | DESCRIBED: "Verificarea dinainte de raport: re-verific hash-urile probelor" | IV |
| F3 | Report digitally signed, with a verifier | CMP §1.10 | MISSING: only the data pack and the auth card are signed | DESCRIBED: "SEMNATURA VALIDA - fisierul nu a fost modificat de la semnare." | IV |
| F4 | Verifiable case archive (manifest, archive hash, manual verification commands) | CMP §1.11 | PARTIAL: `.dfir` snapshot with SHA-256 (`CaseSnapshotService.cs:37`) | DESCRIBED: "Arhivă creată și verificată"; "VERIFICARE FĂRĂ INSPECTRA VERITAS" | IV |
| F5 | Limitations and what the report cannot prove, before the content | CTRL §5 | HAVE: mandatory "Limitări" (commit d1fb40978), "Concluzie pe scurt" box (commit 788a2141f) | DESCRIBED: "Fiabilitatea raportului"; "Limitele raportului, inainte de continutul lui." | tie |
| F6 | Tool identity on every page: program SHA-256, detection lists, time zone | CTRL §5 | HAVE: `Audit/ReportFooter.cs:19-36` | PARTIAL: certificate fingerprint, About with libraries | LA |
| F7 | Legal framing: authorising act, controller declaration, "not a technical-scientific or expert report", legal-counsel audience | CTRL §1.1 | HAVE: commits 667f76d77, 788a2141f | PARTIAL: "nu inlocuieste documentatia procedurala/legala de predare-primire" | LA |
| F8 | Report marked with the station's classification level | CTRL §5, G5 | HAVE: `Audit/ControlReportHeader.cs:34` ("Clasificare stație") | NOT FOUND | LA |
| F9 | Honest confidence: observed vs inferred; "no match" is not "clean"; not run = undetermined | CTRL §5 | HAVE: `ControlStatus.Nedeterminat`; finding classification/confidence; counters (commit 788a2141f) | DESCRIBED: "corelările nu ajung niciodată la CONFIRMAT" | tie |
| F10 | Analysis footprint on the target; PII-masked export for sharing | CTRL §5 (privacy) | MISSING | DESCRIBED: "Amprenta analizei pe tinta"; "EXPORT CU DATE PERSONALE MASCATE" | IV |

### G. Usability for a non-IT controller

| # | Requirement | Source | LogAnalyzer | InspectraVeritas | Better |
|---|---|---|---|---|---|
| G1 | Each check and code explained in plain words, with the next step | CTRL §2 ("Ce i se spune controlorului") | HAVE: `CheckGuide.cs`, `CodeExplainer.cs`, `LogAnalyzer.Core/Services/Details/ItemMeaning.cs` | PARTIAL: artefact explanations written for an examiner | LA |
| G2 | Role-based simple screens, Home as a task list | EXF header (non-IT user) | HAVE: `LogAnalyzer.Core/Services/Edition/RoleProfile.cs` (WP18) | PARTIAL: "Meniul din stanga urmeaza pasii investigatiei" | LA |
| G3 | Few steps: one "collect everything", results reused, empty screen offers the action | CMP §5 | PARTIAL | DESCRIBED: "Colecteaza tot"; "ecranul spune 'Neanalizat' si ofera 'Colecteaza acum'" | IV |
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
