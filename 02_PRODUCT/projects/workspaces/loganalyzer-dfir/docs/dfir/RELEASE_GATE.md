# LogAnalyzer release gate and synthetic corpus (stage 2, WP12)

The gate is implemented as described below; items marked NOT_IMPLEMENTED are real gaps that belong to later work packages.
Source of the rules: `CONTRACT_AUDIT_STAGE1.md` section 7 (WP12) and section 8, owner decisions 7 and 8 (final).

## 1. What a release needs today

**A release is currently impossible.** In enforce mode the gate fails unless every item is `PASS` or `WAIVED`. Blocking items
without a capability behind them when this was written: the findings register (response verification got its test classes with WP9
and AI claim validation with WP4, both on 2026-10-22; audit integrity see the table). It becomes checkable only when the owner
creates the findings register. Until then
the gate reports them `NOT_IMPLEMENTED` and an enforce run fails. The gate never turns a missing capability into a pass, and no
waiver can cover a blocking item.

## 2. The gate

Script: `release-gate/Invoke-ReleaseGate.ps1` (PowerShell 7). Items and rules: `release-gate/gate-items.json`. Library: `release-gate/ReleaseGate.Lib.ps1`.
It reads the `.trx` files of a `dotnet test` run, a findings register and a waiver; it runs nothing else and changes nothing.

| Item | R22 | Policy | How it is decided | Today |
|---|---|---|---|---|
| `tests_passing` | R22.1 | blocking | No failed test in any `.trx`, at least one passed | real check; PASS when CI is green |
| `synthetic_corpus` | R21.5, decision 7 | blocking | `SyntheticCorpusTests`: 14 or more executed, none failed | real check |
| `evidence_integrity` | R22.4 | blocking | `EvidenceIntegrityTests` and `EvidenceContractTests` executed and passing | real check, unit level only (see limits) |
| `audit_integrity` | R22.7 | blocking | a test class `AuditIntegrity*Tests`, `AuditChain*Tests` or `CustodyChain*Tests` must exist and pass | `AuditChainCustodyTests` exists (Dfir.Tests); the gate judges it from the CI test results (PASS only when CI says so) |
| `ai_claim_validation` | R22.5 | blocking | a test class `AiClaimValidation*Tests` must exist and pass | real check since WP4 (2026-10-22): `AiClaimValidationTests`, `AiClaimValidationReportTests` (Dfir.Tests), `AiClaimValidationUiTests` (App.Tests); fakes only, see limits |
| `response_verification` | R22.6 | blocking | a test class `ResponseVerification*Tests` must exist and pass | real check since WP9 (2026-10-22): `ResponseVerificationTests`, `ResponseVerificationProcessTests`, `ResponseVerificationRegistryTests` (fakes only, see limits) |
| `critical_findings` | R22.8 | blocking | `release-gate/findings-register.json` must exist, be valid, and hold no `critical` + `open` entry | NOT_IMPLEMENTED (no register) |
| `real_corpus` | R22.2 | waivable | `forensic_validation.txt` says `AVAILABLE` and every corpus test ran and passed | NOT_RUN in CI |
| `differential` | R22.3 | waivable | the `ForensicLab` tests (`LADFIR_LAB=1`) executed and passed | NOT_RUN in CI |

Statuses: `PASS`, `FAIL` (evidence says no), `NOT_IMPLEMENTED` (the capability has no tests/file yet), `NOT_RUN` (waivable item that did
not run), `WAIVED` (waivable item, covered by a valid signed waiver). Ready = every item `PASS` or `WAIVED`.

How a not-yet-built capability is detected: by the existence of its test class in the results. The work package that builds it adds a
test class with the agreed name; the gate then starts judging it, with no edit to the gate. A pattern is a promise: do not create an
empty or always-green class with that name. The `minExecuted` guard in `gate-items.json` stops a check from passing after its tests are deleted.

### Limits, stated plainly

- `evidence_integrity` is a unit-level check (hashing, mutation refusal, evidence contract). No artefact yet attests the integrity
  of the shipped rule set and parsers (audit R22.4).
- `response_verification` (WP9) proves the contract with fakes, never on a real firewall or process: every controlled response
  (per-program containment and its release, station isolation / restore, IoC block, audit-policy change, policy apply / rollback,
  media sanitization) returns a `ResponseOutcome` from one verifier (`LogAnalyzer.Dfir.Core/Response/ResponseVerification.cs`) that
  re-reads the target after the action; a re-read that throws, is denied or times out is NOT_VERIFIED, a re-read showing the old state
  is FAILED; the outcome goes to the case audit (policy: its own hash-chained `audit.jsonl`; sanitization: the provenance ledger) and
  an audit write failure removes the success. `ResponseVerificationRegistryTests` scans the IL of the production assemblies and fails on
  any method reaching the host-change gate or a host-changing primitive without `[ControlledResponse]`. Not covered by that scan: the
  WPF assemblies, and a primitive injected as a delegate (the suspend / resume `Action<int>`); the real-firewall run is the existing
  `[AdminFact]` in `ContainmentTests`, skipped unless elevated.
  Process safety (review 2026-10-22): a PID is suspended only when it runs the program being contained (image path, and SHA-256
  when known) with a readable identity; it is resumed only when its current holder matches the captured PID + start time + image
  SHA-256; unknown identity or an unreadable process list -> nothing is resumed, NOT_VERIFIED with the cause.
- `ai_claim_validation` (WP4) proves the contract with fakes, never with a real model: every AI statement is split into claims and checked
  against THIS case (`LogAnalyzer.Dfir.Core/AI/AiClaimValidator.cs`, no model as judge, no network). The validator confirms VALUES, never
  MEANING. A claim is VALORI_CONFIRMATE (display "valori confirmate") only when it cites an event or a finding (not just an evidence file) and
  ONE cited record contains every value it states (values spread over several cited records are at most PARTIAL, "valorile provin din
  înregistrări diferite"; counts of the cited set are still recomputed over the whole set): timestamps to the stated precision, a time of day
  ("la ora 11", "ora 10:30", compared with the hour and minute of the record's time on the clock the record states), paths, hashes, addresses and ports, file names,
  user names (`DOMAIN\user`, `user@host`, after "utilizatorul" / "user"), host names (after "hostul" / "stația", FQDN), MITRE ids, quoted values,
  and counts (events / findings / evidence are recomputed from the case; "de N ori" / "N conexiuni" must occur in the record). It is never shown
  without the caveat "afirmație a modelului, nu constatare; s-au confirmat doar valorile citate, nu sensul". PARTIAL = citations exist but nothing
  is comparable, or the claim cites only the evidence file ("citează doar fișierul, nu evenimentul"), or it contains a negation or an overall
  verdict (nu, niciodată, fără, never, benign, curat, compromis, clean, malicious ...): "sensul nu poate fi verificat automat". NESUSTINUT = no
  citation, missing / foreign / self-referential id, a value that does not match, look-alike characters ("conține caractere care imită alte
  litere": a token mixing scripts such as Latin with Cyrillic or Greek, an invisible format character, a non-ASCII digit, a letter of a small
  fixed confusable table; the claim is NFKC-normalised first, plain Romanian diacritics are never flagged), or an instruction aimed at the assistant (when the quoted
  instruction really is in the cited evidence the reason says so and it is shown as a quote, not as a confirmed claim). NEVERIFICABIL = the
  validator could not run (cause given), the validation is stale, or `ai_validation.json` no longer matches its custody SHA-256 ("fișierul de
  validare a fost modificat"). NESUSTINUT is moved to the rejected list, struck in the screen and the PDF, never a finding. The result is
  `Analysis/ai_validation.json`, registered as a case output that depends on every evidence item, with an `ai.claim_validation` entry in the
  hash-chained audit; it is bound to the evidence set (ids, SHA-256, sizes) and shows as stale, every claim NEVERIFICABIL, once that set changes;
  `verification.json` applies the same binding and custody check to the stored state and never carries VALORI_CONFIRMATE next to a REJECTED or
  CONTRADICTED verdict. `AiClaimValidationReportTests` scans the IL of the production assemblies: every type that touches raw AI text (model
  statements, verification verdicts on them, `ai_*` files) must be in an explicit registry, a stale entry fails too, and every report / PDF /
  export writer is listed with whether it goes through `AiValidationText`; `AiClaimValidationUiTests` fails on ANY type of the App assembly (not
  only report writers) that reads the raw AI types besides the validated AI screen. Limits, said plainly: the check compares values only, not
  meaning; a sentence whose values are all in the cited record can still be false (wrong subject, wrong verb, reversed relation), which is why
  the top state is "valori confirmate", a model statement and not a finding. Extraction is best effort: a name or host in prose without a
  keyword ("utilizatorul", "hostul"), without `DOMAIN\user` / `user@host` form or quotes is not compared; negation and verdict words are matched
  by a fixed list (a paraphrase outside it is not caught); the WPF screen is covered by reflection and row logic, not rendered; the scan needs
  new report writers to be added to the list by hand (an unlisted `*ReportPdf` / `*Export` fails).
  Accepted residuals of the extraction (final WP4 review, 2026-10-22), each a value or a sense that is NOT compared, so a claim relying on it
  can still reach VALORI_CONFIRMATE on its other values: (a) a bare 1-2 digit number without a keyword (no "ori" / "conexiuni" / "evenimente"
  etc.) is not compared, only a time of day ("la ora N", "ora N:MM") is; (b) a paraphrased negation or verdict outside the fixed list
  ("blocat de antivirus", "binar legitim", "quarantined") is not recognised; (c) a user or host name in prose without a keyword, without
  `DOMAIN\user` / `user@host` form or quotes is not compared; (d) numbers written as words ("de trei ori", "three times") are not compared;
  (e) a word written entirely in Cyrillic or Greek letters, with no Latin letter and no digit in the same token, is not a look-alike by the
  token rule (it also matches no Latin value of the case).
- The gate does not read CodeQL or code-scanning alerts. `critical_findings` is the owner's register; an empty or missing register
  is not "no findings", it is `NOT_IMPLEMENTED`.
- `tests_passing` depends on the tests being meaningful. On Windows CI everything runs; the corpus and lab tests are skipped there and are
  judged by their own items, never counted as proof.
- The gate checks that the evidence exists and passed; it does not decide whether the product is safe to use.

### Modes

- **Report** (default): prints the matrix, writes `release-gate/out/gate-report.json` and `.md`, adds the table to the job summary, always exits 0.
- **Enforce**: same output; exits 1 unless the gate is ready.

### Where it runs

| Where | Mode | Trigger |
|---|---|---|
| CI, job `Build and test` of `loganalyzer-dfir-build.yml` (Windows) | Report | every pull request and push to `main` touching `loganalyzer-dfir/**` |
| same job | Enforce | a tag `loganalyzer-v<version>`, or manual dispatch with `enforce_gate: true` (optional `gate_version`) |
| Owner, Windows, real corpus | Enforce | by hand, see below |

The report is uploaded as the artifact `loganalyzer-release-gate`. The gate itself is tested by `release-gate/tests/Test-ReleaseGate.ps1`
(33 assertions: PASS only with evidence, NOT_IMPLEMENTED without it, every invalid waiver refused, a fully evidenced run does reach READY,
exit codes), which CI runs on every build.

The production consumers of what the gate checks are the CI job above and the owner's local run; both call `Invoke-ReleaseGate.ps1`.

### Owner: local run on Windows with the real corpus

```powershell
# PowerShell 7, .NET 10 SDK, from the repository root
cd 02_PRODUCT/projects/workspaces/loganalyzer-dfir
pwsh release-gate/Invoke-ReleaseGate.ps1 -Mode Enforce -Version 0.1.0-dfir `
     -RunTests -CorpusRoot D:\corpus\VendorAgentCase -RunDifferential
```

`-RunTests` runs the whole solution in Release into `release-gate/out/trx`; `-CorpusRoot` sets `LADFIR_CORPUS` and
`LADFIR_REQUIRE_CORPUS=1` (a missing corpus section then fails instead of being skipped); `-RunDifferential` sets `LADFIR_LAB=1`.
Leave a switch out and the matching item is `NOT_RUN`. Evaluating results of an earlier run: `-TestResultsDir <folder with .trx>`.

## 3. Waiver (only `real_corpus` and `differential`)

File: `release-gate/waivers/<version>.json`, one per version. Fields (all required):

```json
{
  "schema": "loganalyzer-release-waiver/1",
  "version": "0.1.0-dfir",
  "date": "2026-10-20",
  "waives": ["differential", "real_corpus"],
  "reason": "At least 20 characters of real explanation.",
  "signer": "owner",
  "signature": { "alg": "ecdsa-p256-sha256", "value": "<base64>" }
}
```

The gate validates: schema; `version` equals the version being gated; `date` is `yyyy-MM-dd` and not in the future; `reason` real; `waives`
non-empty and only waivable ids (a list containing any blocking item invalidates the whole waiver); signature algorithm; and the signature itself.

Signature: ECDSA P-256 / SHA-256, verified with .NET only (no external tool). It signs a canonical text built from the fields
(schema, version, date, signer, sorted `waives`, SHA-256 of the reason), so JSON formatting and line endings cannot invalidate it, and
changing any signed field does. The public key is `release-gate/waiver-signer-public-key.b64` (base64 SubjectPublicKeyInfo). **It does not exist
in the repository: the owner must create it. Without it every waiver fails closed** (also when the key file is missing or is not a P-256 key).
Changing that file changes who may waive; review it as such.

A waiver covers only a waivable item that did not run (`NOT_RUN`). An item that ran and failed must be fixed, not waived.

```powershell
# once: create the key pair; keep the private key OUT of the repository
pwsh release-gate/Sign-ReleaseWaiver.ps1 -NewKeyPair -KeyDir $HOME\.loganalyzer-waiver
copy $HOME\.loganalyzer-waiver\waiver-signer-public-key.b64 release-gate\      # commit this one
# per release
pwsh release-gate/Sign-ReleaseWaiver.ps1 -Version 0.1.0-dfir -Waives real_corpus,differential -Signer owner `
     -Reason "Real corpus not available on the release machine; differential run postponed" `
     -PrivateKeyPath $HOME\.loganalyzer-waiver\waiver-signer-private-key.pkcs8.b64
```

Limit: the private key is an unencrypted file; its protection is the owner's. A leaked key allows forged waivers (only of the two waivable items).

## 4. Findings register (`critical_findings`)

`release-gate/findings-register.json`, owner-maintained (nothing creates it automatically):

```json
{ "schema": "loganalyzer-findings-register/1",
  "findings": [ { "id": "F-001", "severity": "critical|high|medium|low", "status": "open|resolved|accepted", "title": "..." } ] }
```

The gate fails on any `critical` + `open` entry, and on a file that does not follow the schema.

**Draft (2026-10-22).** `release-gate/findings-register.draft.json` was prepared by an agent (`"prepared_by": "agent, unverified"`)
from `SECURITY_AUDIT.md`: every fixed finding (#1-#9, #11-#20, R3, R4, V1-V6, W1-W5; `status` `resolved`, `disposition` `fixed`, with
its fix commit) and the open owner items (#10 Authenticode, #21 legacy trees, the offline licence key, the QuestPDF Community
eligibility; `status` `open`, `disposition` `owner-decision`). `status` keeps the gate's vocabulary; `disposition` is informative. The gate
never reads the draft: it reads only the exact path above (`release-gate/tests/Test-ReleaseGate.ps1` checks this). **The owner reviews
the draft and saves it as `findings-register.json`**; only the owner creates that file, and saving it is the owner's attestation that the
entries are right (an owner item may become `accepted` once decided).

## 5. Synthetic corpus (decision 7)

Generated in code (`LogAnalyzer.Dfir.Tests/Synthetic/SyntheticCorpus.cs`), byte-identical on every run and platform: fixed timestamps,
no clock, no randomness, no host API. Nothing comes from a real case. Tests: `SyntheticCorpusTests` (`[Trait("Category","SyntheticCorpus")]`);
the manifest test pins the SHA-256 of every generated artifact, so an accidental generator change fails the build. They run in the
Windows CI job (all tests) and on Linux (`dotnet test LogAnalyzer.Dfir.Tests -p:EnableWindowsTargeting=true`).
`SyntheticCorpus.Materialize(dir)` writes the files for use with other tools.

| Artifact | Covered | How | Asserted |
|---|---|---|---|
| Prefetch SCCA v30 (uncompressed) | yes | hand-built bytes | fingerprint, version, hash, run count, 3 run times, referenced files, volume; one event per run; v23 and truncated file fail |
| Shell link `.lnk` | yes | `LnkParserTests.BuildLnk` | target, arguments, working dir, machine id, drive type; truncated and zero-filled fail |
| Registry NTUSER | yes | minimal regf writer (not DiscUtils, which needs Windows ACL APIs) | UserAssist decoded, Run/RunOnce events, user-path finding with MITRE id; damaged hive is an error |
| Registry SOFTWARE | yes | same | Run keys incl. WOW6432Node, Winlogon non-default flags, IFEO debugger finding |
| Scheduled task XML | yes | UTF-16 XML | command, process, task path, hidden, run level, registration time |
| EVTX file/chunk container | partly | header + CRC-valid chunks | chunk validity, record-id range, damaged chunk detected and dropped, the gap stays visible, source untouched |
| **EVTX records** | **no** | - | the parser reads them through the Windows EventLog API, which needs real binary XML |
| **SRUM** | **no** | - | ESE database through `esent.dll`; a synthetic ESE file needs the same Windows API |
| **MAM-compressed Prefetch** | **no** | - | decompression uses a Windows API |
| Amcache, SYSTEM hive, USB, services, browser history, Jump Lists, USN, PCAPNG | **no** | - | not generated; WP11 owns further fixtures |

This corpus catches parser regressions on every pull request. It is not a forensic validation and a green run does not replace the
real-corpus run: that corpus stays with the owner (private, not redistributable) and is covered by the `real_corpus` item.

## 6. Other WP12 pieces

- **Windows path helper** (PR212 item 4): `LogAnalyzer.Dfir.Core/FileSystem/WinPath.cs` gives Windows path semantics to paths that are
  evidence, on any host. 26 call sites that took file names/directories from evidence strings now use it (LNK, Jump List, Correlation,
  AntiForensics, AuditCoverage, scheduled task and the registry/services/browser/SRUM/EVTX/BAM/ShimCache parsers). Host file paths still use
  `System.IO.Path`. Effect on Linux: 9 fewer platform-caused failures; none added. On Windows behaviour is unchanged.
- **CaseId / operator CR/LF** (PR212 item 3): `RemoteCollection.OneLine` replaces CR, LF, NEL, LS and PS in the text written into a `#`
  comment of the generated PowerShell package; tests show a hostile CaseId cannot start a new script line.
- **ViewModel smoke tests**: `LogAnalyzer.App.Tests` references the WPF app and runs on Windows CI: construction without an `Application`,
  defaults, commands decidable without touching the machine. Not covered: XAML/binding correctness, `MainViewModel` (heavy service graph),
  `ContainmentViewModel`, `DomainInvestigationViewModel` (machine state at construction), and any visual behaviour. There is no UI automation (R21.3 stays PARTIAL).
- **CodeQL for C#**: `.github/workflows/codeql-csharp.yml` (Windows, manual build, path-filtered, plus weekly). Separate from `codeql.yml`
  so the actions/python scans and unrelated pull requests are untouched.
