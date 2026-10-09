# Checkpoint — claude-wp11 (LogAnalyzer WP11 Tier 1: rules and sources)

- **Task:** queue item 6 (first part) of `00_GOVERNANCE/coordination/projects/LOGANALYZER_DFIR/STAGE2_PLAN.md`.
  - Covers rows 26, 36, 37, 39, 43, 47 and 83 of `docs/dfir/LESSONS_LEARNED_MAPPING.md`; its WP11 line has the summary.
  - The queue item is split: WP11-T1 (this file) → WP14a (media register, users/clearances, USB/CD/NIC/Wi-Fi/BT, air-gap
    category) → WP14b (combined sequences).
- **Branch:** `loganalyzer/wp11-t1-rules`; main (with WP15b #244, `b02c4a43`) is merged in. Ready to implement.
  is merged. Both touch `Correlation.cs` and `RuleContracts.cs`, so merge main first.

Paths are relative to `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`.

## Already in place (reuse)
- `LogAnalyzer.Dfir.Core/Analysis/Correlation.cs`: rule sections, `Finding` contract (WP2 `FindingContract`), `RuleContracts.cs`
  catalog kept in sync with `20_TESTS/test_loganalyzer_rule_catalog.py`.
- Existing rules to reuse, not duplicate: `REMOTE-RDP-PUBLIC`, `DEF-TAMPER`, `CRED-BRUTEFORCE`, `PS-SUSPICIOUS`,
  `INCIDENT-CHAIN`; `AntiForensics.cs` (renamed utilities list includes vssadmin); the WP4 verifier (`LogAnalyzer.Verification`)
  gives every new finding a verdict automatically (declare `SemanticType` correctly).
- WP15a procedure profile: approved software list (use it for remote-tool and agent-name decisions: approved = Info, not a finding).
- Collected channels: `LogAnalyzer.Dfir.Windows/Acquisition/Collectors.cs` `EventLogCollector.Channels`.

## Spec (each item: rule id in `RuleContracts.cs`, SemanticType, Limitations, MissingEvidence, AlternativeExplanations,
## RecommendedNextSteps; never claim more than the event proves; "creat ≠ folosit")
1. **SMB / file shares (row 26):**
   - Security 5140 (share accessed), 5145 (detailed share object access) and 4670 (permissions changed) are normalized.
   - Rules:
     - `SMB-ADMIN-SHARE` (C$/ADMIN$/IPC$ from a non-admin host or account);
     - `SMB-SHARE-PERMS-CHANGED` (4670 on share or file objects).
   - Volume thresholds stay for WP17; only list counts here.
2. **Accounts created → used (row 37):**
   - Events: 4720 (created), 4722 (enabled), 4724 (password reset), 4728/4732/4756 (added to a global/local/universal group),
     4738.
   - Rules:
     - `ACCOUNT-CREATED` (Info / Low);
     - `ACCOUNT-ADDED-PRIVILEGED-GROUP` (Administrators, Domain Admins, Remote Desktop Users…; Medium);
     - `ACCOUNT-CREATED-THEN-USED` (a later 4624/4648 by the same SID/name; High only when privileged AND used remotely).
   - With no later logon, say "creat, nefolosit în dovezile colectate".
3. **Remote administration (row 39):**
   - Internal RDP: 4624 LogonType 10 and TerminalServices 21/22/25/1149, beyond `REMOTE-RDP-PUBLIC`.
   - WinRM: Microsoft-Windows-WinRM/Operational 6/91/142 and 4624 type 3 + wsmprovhost.exe.
   - PsExec: 7045 service PSEXESVC or a random-name service from ADMIN$, plus 5145 `\\*\ADMIN$\*.exe`.
   - Remote Registry / SSH (OpenSSH/Operational 4).
   - Rule ids: `REMOTE-RDP-INTERNAL`, `REMOTE-WINRM`, `REMOTE-PSEXEC`, `REMOTE-SSH`.
   - Add the WinRM and OpenSSH channels to `EventLogCollector.Channels`. An absent channel stays "indisponibil".
4. **Security agent stopped / uninstalled (row 36):**
   - Events:
     - System 7036/7040/7045 (service stopped / start type disabled / new) for known AV/EDR/Sysmon service names;
     - Sysmon 4 (state changed) and 16 (config changed);
     - MsiInstaller 1034 / 11724 (product removed) for those products.
   - Rule `SECURITY-AGENT-STOPPED`, distinct from `DEF-TAMPER`.
   - A known-agent name list in data, not code, under `Detection/` or `Analysis/`.
5. **Snapshot deletion (row 43):**
   - Sources: vssadmin/wmic shadowcopy/`Get-WmiObject Win32_ShadowCopy` delete in 4688/4104/Sysmon 1; VSS 8193/8194; volsnap 25/33.
   - Rule `VSS-SNAPSHOT-DELETED`: alone = Medium. High only with mass file modification or encryption-like rename evidence
     already in the case (reuse existing findings; do not build a new detector).
6. **DNS (row 47):**
   - Source: DNS-Client/Operational 3006/3008/3020.
   - Rules:
     - `DNS-RARE-DOMAIN`: a domain seen ≤ N times in the case, resolved by a LOLBin or user-path process; Low/Medium.
     - `DNS-SERVER-CHANGED`: NetworkProfile, or registry NameServer if collected.
   - Wire the existing unwired `DnsTunnelingClassifier` (R7.9) only if it is deterministic and tested. Otherwise leave it and say
     so under Blockers.
7. **Remote-access tools (row 83):**
   - AnyDesk, TeamViewer, VNC variants, RustDesk, ScreenConnect, Splashtop, Chrome Remote Desktop, MeshAgent.
   - Evidence: execution artifacts (Prefetch/Amcache/BAM/4688/Sysmon 1), services (7045), installs (MsiInstaller 11707), and their
     own log files if collected.
   - Rule `REMOTE-TOOL-PRESENT` / `REMOTE-TOOL-EXECUTED`. Approved in the WP15a profile = Info with "aprobat în profil".
   - The tool list is data.
8. **Parser descriptors:** for every new or extended source, extend its `ParserDescriptor` with what it can prove / cannot prove /
   correlation sources (row 2), as fields that already exist or are added additively.
9. **Tests (TDD):** for each rule, synthetic positive and negative cases; approved-in-profile cases; "created, not used"; the agent
   list is data-driven; the rule catalog test stays green; Edition green (no new process start or network in shared assemblies).

## Verification required before push
- Build 0 errors.
- Dfir 48 / UI 6 known Linux-only failures only; Edition green.
- Full Python suite once on the final head → 0 failed.
- New .md files are in `20_TESTS/fixtures/unreadable_notes_allowlist.json`.

## Done
- 2026-10-09T08:30Z claude-orchestrator: spec written; branch created.

## Next
- Implement 1-9 (main with WP15b already merged in).

## Blockers
- None.
