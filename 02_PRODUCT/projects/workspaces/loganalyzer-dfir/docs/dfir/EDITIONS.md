# LogAnalyzer editions (WP-ED, owner decision 13)

Two applications share the core libraries. Existing functionality is not removed: it sits behind an edition boundary,
and the unclassified edition keeps all of it.

| | P1 classified | P2 / P3 unclassified |
|---|---|---|
| Project / executable | `LogAnalyzer.App.Classified` / `LogAnalyzer.Classified.exe` | `LogAnalyzer.App` / `LogAnalyzer.exe` |
| Network code (HTTP clients, syslog receiver, SIEM, online threat intel, Microsoft 365, LDAP, domain-controller logs, remote/live event-log sessions, connectivity probe) | **not compiled in** | `LogAnalyzer.Connectors` |
| Any AI (local loopback model included) | **not compiled in** (option below) | `LogAnalyzer.Ai` |
| Host-modifying actions (per-program containment, firewall isolation / IoC blocking, `auditpol /set`, registry writes of the policy executor, process suspension, audit collection script) | **not compiled in** | `LogAnalyzer.Response` |
| Updates | none exist in either edition today | none |
| Mode | always air-gapped (nothing to switch) | air-gapped (P2) or connected (P3), from a **signed policy** |

"Absent" means absent from the build output: the P1 project references neither `LogAnalyzer.Connectors`, `LogAnalyzer.Response`
nor `LogAnalyzer.Ai`. In P1 the screens of those features show "nu este disponibil în ediția clasificată" and the services are
replaced by stand-ins that only refuse (`LogAnalyzer.Core/Services/Edition/UnavailableServices.cs`).

## How the code is organised
- Shared: `LogAnalyzer.Core`, `.Infrastructure`, `.Dfir.Core`, `.Dfir.Windows` (parsers, analysis, evidence, timeline, case, read-only
  collectors, report generation). Contracts for the optional capabilities live in `LogAnalyzer.Core` (`IHostDefense`,
  `ILiveEventSource`, `IConnectivityWatcher`, `IAuditCollector`, `ISyslogReceiver`, `IEditionProfile`) and in the UI
  (`IFeatureViewFactory`).
- UI: `LogAnalyzer.App` owns all UI sources. `LogAnalyzer.App.Classified` compiles the same files (linked, not copied) except
  `LogAnalyzer.App/Edition/Unclassified/**` (Containment, Domain investigation and AI screens, and the unclassified composition root).
  Each executable has its own `EditionComposition` (DI registration + mode decision).
- Screens that exist only in the unclassified edition are hosted by `FeaturePlaceholder`, which shows the unavailable notice when the
  edition supplies no view model.

## Signed policy (P2/P3)
- File: `%ProgramData%\LogAnalyzer\LogAnalyzer.policy` (JSON). Fields: `schema`, `mode` (`airgapped` | `connected`), `version`
  (monotonic), `notBefore`, `notAfter` (optional), `audience` (`*` or a machine name), `signer`, `signature`.
- Signature: ECDSA P-256 / SHA-256, IEEE P1363, public key as base64 SPKI embedded in the build
  (`LogAnalyzer.App/Edition/Unclassified/edition-policy-public-key.b64`). Same scheme as `release-gate/ReleaseGate.Lib.ps1`;
  the signed text is built from the fields, not from the JSON bytes.
- **Fail closed:** missing file, missing embedded key, bad JSON, wrong signature, not yet valid, expired, other station, or a
  `version` lower than the highest accepted (`policy.highwater`, written when an administrator runs the app) => **air-gapped**.
- `--mode=` and `LogAnalyzer.mode` are ignored (the decision text records that the request was ignored); Windows reporting an Internet
  connection no longer selects the network mode either. Going from P2 to P3 needs a new policy with a higher `version`, signed by the
  holder of the private key.
- The SHA-256 of the accepted policy is part of the mode reason shown in the UI badge tooltip and in `startup_debug.log`.
- Tool: `release-gate/Sign-EditionPolicy.ps1` creates the key pair and signs policies. **No key is committed**: until the owner
  generates one and commits the public key, every installation stays air-gapped.
- Limit: a malicious local administrator can replace the executable or the embedded key. The protection is against the operator and
  configuration mistakes, not against a hostile administrator.

## Verification
`LogAnalyzer.Edition.Tests` reads the metadata of every `LogAnalyzer*` assembly in the P1 build output and fails if it finds an excluded
assembly, a reference to a network or directory assembly, a banned type (sockets, HTTP, DNS, remote event-log session, LDAP, service
control), registry-write calls, banned P/Invoke (Winsock, WinHTTP, `NtSuspendProcess`, `AuditSetSystemPolicy`, service/driver control),
command or rule literals (`netsh.exe`, `HNetCfg.FWRule`, `/failure:enable`, `-ExecutionPolicy Bypass`) or a type of an excluded feature.
The same scan on the unclassified output must find those items (positive control). CI runs it in `build-test` on Windows.
Read-only local queries (NIC list, TCP table, firewall state, local event logs) are not egress and are allowed in P1.

## Options and open questions
- Local loopback AI in P1: **excluded by default**; `-p:ClassifiedIncludeLocalAi=true` adds `LogAnalyzer.Ai` and its screen. It
  requires the owner's approval and is never built by CI.
- Not yet split: the audit collection script (`PowerShellAuditCollector`) is excluded from P1 until a C# port exists (WP-PKG);
  media sanitization and exports are unchanged in both editions (owner decision pending for P1).
- Unwired legacy code (decision 4) is untouched.
