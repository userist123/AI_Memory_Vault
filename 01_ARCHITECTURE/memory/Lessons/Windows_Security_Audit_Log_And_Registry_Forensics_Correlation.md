---
id: "cfca79df-d8f8-4720-bd8f-7f7ea7116b47"
type: lesson
lifecycle: REVIEW
category: Security Forensics
tags: [windows, eventlogs, registry, dfir, correlation, security-audit]
created: 2026-08-11
updated: 2026-08-11
provenance:
  source_type: developer_action
  source_ref: "https://github.com/userist123/LogAnalyzer.UI"
confidence: very_high
verification: verified
relations: []
---

# Windows Security Audit Log and Registry Forensics Correlation Rules

This note documents the core rules used to correlate offline security artifacts (Event Logs and Registry hives) for digital forensics and incident response (DFIR) compliance audits.

## 1. Event Log Audit Rules (EVTX)

| Event ID | Title | Severity | MITRE ATT&CK | Compliance Tag | Description |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **4625** | Autentificări Eșuate (Brute Force) | High | T1110 | ISO 27001 - A.12.4.1 | Detects multiple password validation failures. |
| **1102 / 104** | Security Log Cleared | Critical | T1070.001 | HG 585/2002 | Indicates manual trace clearing for defense evasion. |
| **4720** | Local User Created | High | T1136.001 | ISO 27001 - A.9.2.1 | Alerts on persistence through new user creations. |
| **4732 / 4728** | Member Added to Privileged Group | High | T1098 | CIS Benchmark | Identifies local/domain privilege escalation attempts. |
| **7045 / 4697** | System Service Installed | Medium | T1543.003 | NIST SP 800-53 - SI-4 | Triggers on new system driver or service installation. |
| **4688** | Suspicious PowerShell Execution | Critical | T1059.001 | NIST SP 800-53 - CM-7 | Flags execution policies bypass (`-enc`, `-bypass`, `downloadstring`). |
| **4688** | Shadow Copies Deleted | Critical | T1490 | ISO 27001 - A.12.3.1 | Flags backup removal behavior common in Ransomware (`vssadmin delete shadows`). |

## 2. Registry Forensics Rules (OFFLINE HIVES)

| Key Path Pattern | Value Name | Target Data | Title | Severity | MITRE ATT&CK | Compliance Tag |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `..\Run` or `..\RunOnce` | Any | `*powershell*`, `*cmd*`, `*wscript*`, `*temp\*` | Suspicious Autostart Executable | High | T1547.001 | ISO 27001 - A.12.5.1 |
| `..\Control\SecurityProviders\WDigest` | `UseLogonCredential` | `1` | Cleartext Credential Caching Enabled | High | T1003.001 | NIST SP 800-53 - IA-2 |
| `..\Policies\Microsoft\Windows Defender` | `DisableAntiSpyware` | `1` | Windows Defender Antivirus Disabled | Critical | T1562.001 | NIST SP 800-53 - SI-3 |
| `..\Policies\System` | `EnableLUA` | `0` | User Account Control (UAC) Disabled | High | T1548.002 | CIS Benchmark |
| `..\Terminal Server\WinStations\RDP-Tcp` | `UserAuthentication` | `0` | RDP Network Level Authentication (NLA) Disabled | Medium | T1133 | ISO 27001 - A.13.1.1 |

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[08 Memory Subsystems Map]]
- [[Knowledge Graph Home]]
