# Editions and station posture

One codebase builds three executables. The posture of the station each one is made for is fixed at compile time.

| Edition | Executable | Posture | Window and report label |
|---|---|---|---|
| Standalone (air-gapped station, no network, no internet) | `LogAnalyzer.Standalone.exe` | `StationPosture.Standalone` | Ediția: Standalone |
| Network (air-gapped LAN: domain controllers, servers, domains; no internet) | `LogAnalyzer.Retea.exe` | `StationPosture.IsolatedNetwork` | Ediția: Rețea air-gap |
| Internet (everything) | `LogAnalyzer.Internet.exe` | `StationPosture.Internet` | Ediția: Internet |

## What each edition contains

- **Standalone** references only Core, Infrastructure and Dfir.Windows. It contains no network code: no sockets, HTTP, directory (LDAP) access, syslog, live event watchers, response actions or AI. `LogAnalyzer.Edition.Tests` scans the build output to keep it that way.
- **Network** adds `LogAnalyzer.Connectors` (read-only domain collection, syslog and internal SIEM forwarding, live event watchers) and `LogAnalyzer.Response` (CSIRT actions). It does not contain `LogAnalyzer.Ai` (the AI layer, which uses an HTTP client) unless the build option `-p:NetworkIncludeLocalAi=true` is used (owner approval required; loopback only). It contains no cloud client and no online downloader or updater; the edition test bans HTTP/web clients outside `LogAnalyzer.Connectors`.
- **Internet** is the complete application (`LogAnalyzer.App`).

## Posture and the signed edition policy

The compile-time posture always wins. It is chosen by the edition's composition, never by a file, an argument or a policy. A signed policy can only choose the runtime mode (`AppMode`) inside what the posture permits (`StationPostures.ClampMode`): a Standalone edition is always AirGapped; a Network edition may be narrowed to AirGapped by a policy but never widened beyond the LAN; the Internet edition follows the policy. No policy can widen the posture.

## Control check applicability

The audit result type has a fifth state, `NuSeAplica` ("Nu se aplică"). A check that does not apply to the posture is shown as such in the screen and the reports and is never counted as non-compliant (nor as compliant). The table is `ControlApplicability` in `LogAnalyzer.Dfir.Windows/Audit/ControlApplicability.cs`; a test keeps this document and the code equal and fails if the evaluator produces a check id without an entry for all three postures. There is no default.

Check families: C accounts, P policies, A audit, U users, D devices, N network, S software. "Review" means the application shows the facts and the organisation decides. "non-conform" means the finding is a violation in that posture.

Applicable checks: Standalone 80, IsolatedNetwork 80, Internet 62 (of 81).

Network checks N11 and N12 are specific to this model. N11 ("no path to the internet") is PASSIVE in every edition that runs it: it reads adapter properties, the registry (interface gateways and DNS servers including adapters that are off now, persistent routes, WinINet and WinHTTP proxy settings), the VPN/RAS phonebooks, the Plug and Play device list (cellular modems, phones used as modems), the count of saved Wi-Fi profile files and the Internet Connection Sharing state (HNetCfg COM object). It uses no WMI. It opens no socket and resolves no name, so a proxy given by name is shown as "review". On Standalone any such path is non-conform; on the Network edition internal gateways and DNS are accepted and a path leading outside the internal ranges is non-conform. Domain controller reachability and internal DNS answers are not checked, because that needs traffic.

| Check | Name | Standalone | IsolatedNetwork | Internet | Reason when not applicable |
|---|---|---|---|---|---|
| C01 | Guest account | Disabled | Disabled | Disabled |  |
| C02 | Active local administrators | Review: only approved accounts | Review: only approved accounts | Review: only approved accounts |  |
| C03 | Built-in Administrator account | Disabled or controlled | Disabled or controlled | Disabled or controlled |  |
| C04 | Active accounts without required password | None | None | None |  |
| C05 | Passwords that never expire | Review | Review | Review |  |
| C06 | Remote Desktop Users members | Review | Review | Review |  |
| P01 | Minimum password length | At least 12 | At least 12 (domain policy may supersede) | At least 12 |  |
| P02 | Account lockout | Enabled | Enabled | Enabled |  |
| P03 | Password expiry | Defined | Defined | Defined |  |
| P04 | UAC enabled | Enabled | Enabled | Enabled |  |
| P05 | Automatic logon without password | Absent | Absent | Absent |  |
| P06 | Remote Desktop connections | Disabled; non-conform if enabled | Disabled unless the administration procedure approves it; review if enabled | Review if enabled |  |
| P07 | Windows Firewall on all profiles | Active on all profiles | Active on all profiles, Domain profile included | Active on all profiles |  |
| P08 | Microsoft Defender real-time protection | Active | Active | Active |  |
| P09 | Antivirus signatures up to date | Offline update procedure followed | Updated from the internal update source | Updated within 7 days |  |
| P10 | Defender exclusions | Review | Review | Review |  |
| P11 | System disk encryption (BitLocker) | Enabled | Enabled | Enabled |  |
| P12 | SMBv1 disabled | Disabled | Disabled | Disabled |  |
| P13 | WDigest does not keep passwords in memory | Off | Off | Off |  |
| P14 | USB storage blocked by policy | Review (blocked expected) | Review (blocked expected) | n/a | Blocking removable media is a data-handling rule of controlled stations, not a hardening rule of a connected workstation. |
| P15 | Secure Boot | Enabled | Enabled | Enabled |  |
| P16 | Automatic screen lock | Configured | Configured | Configured |  |
| P17 | Application allow-list (AppLocker) | Enforced or documented alternative | Enforced or documented alternative | Enforced or documented alternative |  |
| P18 | AutoPlay / AutoRun | Disabled | Disabled | Disabled |  |
| P19 | Camera and microphone blocked by policy | Blocked | Blocked | n/a | Blocking capture devices is a rule for controlled rooms; connected office stations legitimately use them. |
| P20 | Windows Recall and Copilot off | Off | Off | Off |  |
| P21 | LSA protection | Enabled | Enabled | Enabled |  |
| P22 | Credential Guard | Running | Running | Running |  |
| P23 | PowerShell logging | Enabled | Enabled | Enabled |  |
| P24 | PowerShell 2.0 removed | Removed | Removed | Removed |  |
| P25 | Residual data on disk (paging, hibernation, dumps) | Cleared or disabled per procedure | Cleared or disabled per procedure | n/a | Residual-data handling is a classified-material rule. |
| P26 | Logon warning banner with classification level | Present | Present | n/a | The classification banner exists only where classified material is handled. |
| P27 | Station time source | Local source documented | Domain time hierarchy (internal NTP) | Review |  |
| P28 | CD/DVD burning blocked | Blocked; non-conform if a burner is writable | Blocked; review if a burner is writable | n/a | Burning media is an exfiltration channel only on controlled stations. |
| P29 | Printing is logged | Logged; non-conform if not | Logged; review if not | n/a | Print logging is a data-handling rule of controlled stations. |
| P30 | Clipboard and activity history off | Off; non-conform if on | Off; review if on | n/a | History features are a leak path only on controlled stations. |
| P31 | Camera, microphone and speakers present and active | None exposed; non-conform if exposed | None exposed; review if exposed | n/a | Capture and audio devices are a rule for controlled rooms. |
| P32 | BitLocker on all fixed volumes with boot PIN | Enabled with PIN | Enabled with PIN | Enabled |  |
| P33 | TPM present and ready | Ready | Ready | Ready |  |
| P34 | Kernel DMA Protection | On; non-conform if off | On; review if off | On; review if off |  |
| P35 | Virtualization-based security | Running | Running | Running |  |
| P36 | Password required after sleep | Required | Required | Required |  |
| P37 | Application allow-list (App Control / WDAC) | Enforced or documented alternative | Enforced or documented alternative | Enforced or documented alternative |  |
| P38 | Last full and quick antivirus scan | Recent | Recent | Recent |  |
| P39 | Antivirus registered in Security Center | Registered | Registered | Registered |  |
| A01 | Audit policy records the required activity | Required categories on | Required categories on | Required categories on |  |
| A02 | Security log size | At least 128 MB | At least 128 MB | At least 128 MB |  |
| A03 | Security log covers the audited period | Covers the period | Covers the period | Covers the period |  |
| A04 | Other logs cover the audited period | Cover the period | Cover the period | Cover the period |  |
| A05 | Logs cleared in the period | None | None | None |  |
| A06 | Audit policy changed | Review | Review | Review |  |
| A07 | System time changed manually | None or explained | None or explained | None or explained |  |
| A08 | File copies to removable media are audited | Audited | Audited | n/a | Removable-media copy auditing belongs to controlled stations. |
| A09 | Programs stopped or flagged by the allow-list | Review | Review | Review |  |
| A10 | Access auditing (SACL) on classified folders | SACL present | SACL present | n/a | Classified folders exist only in the controlled editions. |
| U01 | Changes to the Administrators group | Review | Review | Review |  |
| U02 | Accounts created, deleted, enabled, disabled or reset | Review | Review | Review |  |
| U03 | Remote (RDP) logons | Review | Review (administration over the LAN is normal) | Review |  |
| U04 | Series of wrong passwords (5+ in an hour) | None | None | None |  |
| U05 | Who worked with administrator rights | Review | Review | Review |  |
| U06 | PowerShell scripts with commonly abused techniques | Review | Review | Review |  |
| D01 | USB storage devices used on the station | Review | Review | Review |  |
| D02 | Connected removable media encrypted (BitLocker To Go) | Encrypted | Encrypted | n/a | Media encryption is a controlled-station rule. |
| D03 | Media scanned with antivirus at connection | Scanned | Scanned | n/a | Scan-at-connection is a controlled-station procedure. |
| N01 | Isolated station never connected to any network | Never connected; non-conform on any connection | n/a | n/a | Only a standalone station must never touch a network; a LAN station is connected by design. |
| N02 | Networks known to the station (history) | Review (none expected) | Review (only the domain LAN expected) | Review |  |
| N03 | Wi-Fi adapter | No active adapter; non-conform if a radio is up | Wired LAN expected; non-conform if a radio is up | n/a | A radio is a second path out of an isolated station or LAN, but normal on an internet station. |
| N04 | Bluetooth | No active radio; non-conform if the service is on | No active radio; non-conform if the service is on | n/a | Bluetooth sends files without cable out of an isolated station or LAN. |
| N05 | Phone, cellular modem or USB Ethernet adapters | None; non-conform on phone or cellular | None; non-conform on phone or cellular (internet path) | n/a | They give an isolated station or LAN a way to the internet. |
| N06 | Shared folders | None; non-conform if any | Review (file shares may be intended) | Review |  |
| N07 | Remote Assistance | Disabled; non-conform if allowed | Disabled; review if allowed | Review if allowed |  |
| N08 | Remote administration (WinRM) | Not listening; non-conform if running | Review (domain administration may use it) | Review |  |
| N09 | Port forwarding (portproxy) | None; non-conform if any | Review if any | Review if any |  |
| N10 | Internet Connection Sharing (ICS) | Off; non-conform if on | Off; non-conform if on (a bridge to another network) | Review if on |  |
| N11 | No path to the internet (passive configuration check) | No gateway, DNS, proxy, VPN, radio, cellular or ICS configuration found; non-conform on any | Only internal gateway and DNS; non-conform on proxy, VPN, radio, cellular or ICS leading outside the internal ranges | n/a | Reads local configuration only (registry, adapter properties, Plug and Play device list, local files; no WMI) and never opens a socket. An internet station is connected by design. |
| N12 | Domain membership | n/a | Member of the domain | n/a | Only a station on an isolated domain LAN is expected to be a domain member. |
| S01 | Programs installed or uninstalled | Review | Review | Review |  |
| S02 | New services and scheduled tasks | Review | Review | Review |  |
| S03 | Windows version and last update | Known and documented | Known and documented | Recent |  |
| S04 | Offline updates applied and update register | Register matches installed updates | Register matches installed updates | n/a | The offline update register exists only where updates cannot come online. |
| S05 | Station clock versus the application's time memory | Consistent | Consistent | Consistent |  |
