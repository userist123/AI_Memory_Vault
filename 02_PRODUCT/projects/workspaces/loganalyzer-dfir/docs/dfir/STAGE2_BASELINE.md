# LogAnalyzer stage 2 - Windows CI baseline

Recorded 2026-10-08 (WP0). Source: workflow `loganalyzer-dfir-build.yml`, GitHub Actions.

## Which run

| Item | Value |
|---|---|
| Main SHA when recorded | `07ec83d979921afa262e6743373b2cd62d6fc9e6` |
| Latest `loganalyzer-dfir-build.yml` run on main | run 37746891007 (run number 19), push to `main` |
| SHA that run built | `f8dc8a31b9c0d4dba9303a9b563c5678190b5931` (merge of PR #212) |
| Run on the main SHA above | none. The workflow is path-filtered and no LogAnalyzer file changed after `f8dc8a31b`, so this run is the valid baseline for the LogAnalyzer sources. |
| Result | success; jobs "Build and test" and "Package LogAnalyzer" both green |

The stage-1 audit read the code at `0689f5d48b67`; per the audit header no LogAnalyzer file changed after 2026-10-07, so the baseline above applies to it as well.

## Test counts (Release, windows-latest)

| Project | Passed | Failed | Skipped | Total |
|---|---|---|---|---|
| `LogAnalyzer.Dfir.Tests` | 252 | 0 | 28 | 280 |
| `LogAnalyzer.UI.Tests` | 120 | 0 | 0 | 120 |

The 28 skipped Dfir tests are the ones that need the owner's real corpus (`D:\FORENSIC_CASE`), the on-demand lab (`LADFIR_LAB=1`), owner GPO samples, admin-only checks or a local Ollama model.

## Forensic validation state

The run reported `FORENSIC VALIDATION = UNAVAILABLE (0/21 corpus sections present)`; the workflow emitted a warning annotation. Every corpus section was MISSING on the runner. This means the corpus-backed parser tests were skipped. **A green CI run is therefore not forensic validation**; only the owner's local corpus run on Windows (`LADFIR_REQUIRE_CORPUS=1`, `LADFIR_LAB=1`) is. The most recent lab result recorded in `CURRENT.md` is 174 PASS, 1 PARTIAL, 0 FAIL (2026-10-06).

Stage 2 decision 7 (see `CONTRACT_AUDIT_STAGE1.md` section 8) adds a synthetic, redistributable corpus that is mandatory in CI; until that exists (WP12) this baseline stays UNAVAILABLE.

## Linux container note

On Linux (`EnableWindowsTargeting`) the same suites show platform-caused failures that Windows CI does not have (ACLs, wintrust, powershell/wevtutil, DPAPI, backslash paths). They are pre-existing and are compared against `origin/main` in every stage-2 PR rather than treated as regressions.
