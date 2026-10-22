# Security red-team results

Scope: local only, no network, no paid runs. Suite: `20_TESTS/redteam/` (`redteam_support.py`, `test_redteam_memory.py`, `test_redteam_runtime.py`, `test_redteam_residuals.py`).
Class numbering RT01..RT16 follows the test names (RT17 = false success). The audit-B table is the origin; severity is the auditor's (SOURCE_VERIFIED).
"Observed" = result in the final gate run (TEST_VERIFIED) unless stated. xfail = documented residual, strict=False.

| RT | Attack class | Threat model | Expected | Test id | Observed | Sev | Fix / residual |
|---|---|---|---|---|---|---|---|
| 01 | Unauthorized global install / self-update | session start or hook rewrites `~/.claude` | inert under `COST_ROUTER_NO_SELF_INSTALL=1` / `COST_ROUTER_OFF`; no fetch, no write | `test_rt01_self_update_hook_never_fetches_or_writes_under_the_switch`, `test_rt01_installer_hook_modes_are_inert_under_no_self_install` | pass | HIGH (HIGH-1) | Fixed (hook_self_update.py:90-92, install.py:471-473). Opt-in default NOT implemented: residual, see HIGH-2 |
| 02 | Secret exposure (HMAC key) | key readable by others / inherited by child processes | owner-only ACL, widened ACL refused, icacls children get no key | `test_rt02_secret_file_is_private_and_a_widened_acl_is_refused`, `test_rt02_helper_processes_never_inherit_the_hmac_secret` | pass | MED (MED-3) | Partial: `vault_runtime._child_env` (:79). Key still in `os.environ` for in-process consumers (residual) |
| 03 | Summarised-document injection (local_llm) | hostile text survives summarisation | flagged | `test_rt03_summarised_document_injection_is_flagged` | xfail (residual) | MED | Not fixed: `local_llm.py` returns summary verbatim |
| 04 | Tool output injection/exfiltration as data | tool result with instructions | not passed as trusted data | `test_rt04_tool_output_with_injection_or_exfiltration_is_not_passed_as_data` | pass | MED | Guarded |
| 05 | Retrieved-memory injection | planted note text | `review_required` flag, lifecycle unchanged, no reorder, scanner failure => flagged | `test_rt05_*` (3 tests) | pass | MED (MED-5) | Fixed for memory_get/search; `vault_read` raw text still label-only (residual) |
| 06 | Memory poisoning by repetition | 50 identical proposals | one candidate | `test_rt06_*` (3 tests) | pass | MED (MED-4) | Fixed (uuid5 dedup). No rate cap / near-duplicate detection (residual) |
| 07 | Authority claim | note or proposal claims owner / slot | cannot attest, promote or claim owner provenance | `test_rt07_*` (2 tests) | pass | HIGH if broken | Guarded |
| 08 | Path traversal | `..`, absolute, state ids | refused; state ids cannot escape state dir | `test_rt08_*` (2 tests) | pass | HIGH if broken | Guarded |
| 09 | Malformed payloads | garbage hook JSON | no crash, no escalation granted | `test_rt09_*` | pass | LOW | Guarded |
| 10 | Replay / duplicate events | replayed prompt or PostToolUse | counted once | `test_rt10_*` (2 tests) | pass | LOW | Guarded |
| 11 | Concurrency | parallel resume records | none lost | `test_rt11_concurrent_resume_records_lose_nothing` | pass | LOW (LOW-6) | Fixed (O_EXCL lock). Antigravity copy of route_state unfixed |
| 12 | Review bypass | tool_input forgery; worker type laundering a review brief | mandatory review never below opus unless owner `#escalate` | `test_rt12_*` (2 tests) + `escalation_matrix` | pass | HIGH (audit C) | Fixed (hook_agent_model.py:292-353) |
| 13 | Unknown / unrecognised model | unknown session or explicit model | cheapest tier / floor applied (fail closed) | `test_rt13_*` (2 tests) | pass | LOW | Fixed |
| 14 | Resume-guard evasion | no transcript | second resume denied | `test_rt14_a_second_resume_is_denied_with_or_without_a_transcript` | pass | LOW | Fixed |
| 15 | Telemetry forgery | forged sidecar line | rejected | `test_rt15_forged_telemetry_line_is_rejected` | xfail (residual) | LOW-MED | Not fixed: unauthenticated user-writable sidecar |
| 16 | Evaluator-instruction in benchmark answer | answer text instructs the scorer | not scored correct | `test_rt16_evaluator_instruction_in_the_answer_is_not_scored_correct` | xfail (residual) | LOW | Not fixed: ProbeTask scores any non-empty answer |
| 17 | False success | failing pytest in harness | not recorded as correct | `test_rt17_a_failing_pytest_in_the_benchmark_harness_is_not_recorded_as_correct` | pass | MED | Guarded |

## HIGH-2 proposal (not implemented; owner release-process decision)

> Update 2026-10-22 (phase 9): pin + SHA-256 manifest implemented on branch `claude/cost-router-pinned-self-update` (`lib/release_pin.py`; fetch by commit SHA, bytes verified before the trusted installed installer runs, downgrade refused). Signing is still open. See `00_GOVERNANCE/coordination/tasks/todo-claude-cost-router-pinned-update.md`.
Finding (SOURCE_VERIFIED, audit B): `hook_self_update.py` clones the unpinned, unsigned `main` branch and runs its installer on a throttle (about hourly), so anyone able to push to `main` (or to redirect the URL) executes code on every opted-in machine.
Proposal:
1. Pin updates to a signed release tag, or to a signed `MANIFEST.sha256` published with the release.
2. Verify the signature and every file hash BEFORE running the fetched installer; refuse anything not in the manifest.
3. No environment override of the update URL or of the verification key.
4. Key rotation is owner-only through `install.py --trust-key <key>` (explicit, interactive, logged).
5. On any mismatch keep the currently installed copy and report the reason.
6. Default OFF until the owner opts in (separate decision).
Status: PROPOSAL only, UNVERIFIED as a design; no code written.

## Unguarded areas (no test, no fix)
Summarised-document injection (RT03), telemetry tampering (RT15), evaluator instructions in benchmark data (RT16), `vault_read` raw text without scanner flag.

## W7 independent-review findings
- HIGH-A (fixed, TEST_VERIFIED): an unclassified brief with an explicit model above the session tier was passed through silently. Now capped to the session tier with a visible note (R3), unknown session fails closed (R5), `#escalate` allows with a note; an unclassified brief without explicit model is capped when the agent default (frontmatter model or `CLAUDE_CODE_SUBAGENT_MODEL`) is dearer than the session. Tests `test_w7_high_a_*`.
- MED-C (fixed, TEST_VERIFIED): a model named only in a settings file is a guess; escalation treats `settings`/`none` as unknown (fail closed), `ANTHROPIC_MODEL` stays trusted. Tests `test_w7_med_c_*`.
- MED-B (ACCEPTED RESIDUAL, not fixed): hook state lives in user-writable files. A model with arbitrary write access can equally remove hooks from `settings.json` or set `COST_ROUTER_OFF`; hooks are policy aids, not a boundary against an agent with unrestricted file write. Mitigation is tool permissions (deny Write/Bash outside the project). Owner decision.
- B-MED-4 status corrected: partial (exact-repeat dedup only, no rate cap; the dedup reply hardcodes REVIEW/unverified).
