# Checkpoint — claude-wp11 (LogAnalyzer WP11 Tier 1: rules and sources)

- **Task:** queue item 6 (first part) of `00_GOVERNANCE/coordination/projects/LOGANALYZER_DFIR/STAGE2_PLAN.md`.
  - Covers rows 26, 36, 37, 39, 43, 47 and 83 of `docs/dfir/LESSONS_LEARNED_MAPPING.md`; its WP11 line has the summary.
  - The queue item is split: WP11-T1 (this file) → WP14a (media register, users/clearances, USB/CD/NIC/Wi-Fi/BT, air-gap
    category) → WP14b (combined sequences).
- **Branch:** `loganalyzer/wp11-t1-rules`; main (with WP15b #244, `b02c4a43`) is merged in. Ready to implement.

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
- 2026-10-09 claude-wp11: all of items 1-9 implemented on `loganalyzer/wp11-t1-rules`.
  - Code: `LogAnalyzer.Dfir.Core/Analysis/Wp11Rules*.cs` (one partial per theme, called from `Correlation.Run(events, maintenance, profile)`; the pipeline now passes the
    procedure profile), `Wp11Data.cs` + embedded `Analysis/Data/{security_agents,remote_access_tools,rule_lists}.json` (lists are data), `ApprovedSoftwareMatcher.cs`,
    `RuleContracts.cs` (15 new entries), `ParserCapabilities.cs` (additive `Channels` table on `EvtxParser`, item 8), `Collectors.cs` (+WinRM/Operational, +OpenSSH/Operational).
  - New rule ids: SMB-ADMIN-SHARE, SMB-SHARE-PERMS-CHANGED, ACCOUNT-CREATED, ACCOUNT-ADDED-PRIVILEGED-GROUP, ACCOUNT-CREATED-THEN-USED, REMOTE-RDP-INTERNAL, REMOTE-WINRM,
    REMOTE-PSEXEC, REMOTE-SSH, SECURITY-AGENT-STOPPED, VSS-SNAPSHOT-DELETED, DNS-RARE-DOMAIN, DNS-SERVER-CHANGED, REMOTE-TOOL-PRESENT, REMOTE-TOOL-EXECUTED.
  - Tests: `LogAnalyzer.Dfir.Tests/Wp11*.cs` (lab + SMB/account, remote, agent/VSS, DNS/tool, contract tests); `20_TESTS/test_loganalyzer_rule_catalog.py` now also scans `Wp11Rules*.cs`.
  - Edition.Tests 15/15 with no gate exception (no banned literal/type added). Dfir 48 failures = the known Linux-only set (identical list before/after); UI 6 = known.

## Next
- Merge origin/main, rebuild, rerun .NET suites, full Python suite once, push (see the final report of the run).

## Blockers / owner decisions (safe defaults implemented)
- `DnsTunnelingClassifier` (R7.9) is left unwired: it lives in `LogAnalyzer.Core` (another layer than `Dfir.Core`), so wiring it would add a cross-layer reference; DNS-RARE-DOMAIN uses a simple, deterministic count instead.
- `DNS-SERVER-CHANGED` reads `SystemConfig` rows with `Interface`/`NameServer` fields. No collector or parser produces them yet (NetworkProfile carries no DNS servers), so in production the rule is inert
  until a registry collector (Tcpip\Parameters\Interfaces) is added. Not claimed as working end to end.
- `DNS-Client` 3006/3008/3020 do not name the querying process: it is inferred from the event's PID and the latest earlier process start (Candidate). Sysmon 22 names it directly (Direct).
- VSS-SNAPSHOT-DELETED: the command alone is Medium (spec). volsnap 25/33 alone are Info/Low (a size limit is a benign cause) and VSS 8193/8194 are errors, used only as supporting evidence, never a finding by themselves.
  High needs impact evidence already in the case (Category Impact, T1486/T1485 or a Defender "ransom" detection within 24 h); no such Dfir rule exists yet (WP17), so today High arises only via Defender ransomware detections.
- Remote Registry has no rule id in the spec; the `winreg` pipe on IPC$ (5145) is reported inside SMB-ADMIN-SHARE (Low).
- SMB-ADMIN-SHARE cannot know which accounts/hosts are administrative; it reports remote access to C$/ADMIN$/IPC$ as Low (Info for IPC$ alone, Medium if an executable/script is touched). Machine accounts and loopback are skipped.
- Approved software (WP15a): the row's most specific constraint decides (SHA-256, else path pattern, else name); a portable copy outside the approved path is not approved. Approved = Info "aprobat în profil".
- `docs/dfir/LESSONS_LEARNED_MAPPING.md` is a dated snapshot ("doar mapare") and was not edited.
