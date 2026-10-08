# Open PRs: make every PR functional (2026-10-07)

Baseline: `main` @ `609b01bf`, full suite 2611 passed / 13 skipped / 9 xfailed (local, 21 min).
Execution is delegated to Sonnet 5.5 agents, one PR (or stacked pair) each, pushing
ordinary commits to the PR's own head branch (no rebase, no force-push, no merge to main).

## Cross-PR constraint
- [x] `.gitleaks.toml`: everyone keeps the `[[allowlists]]` format. #209 must not
      reintroduce the legacy `[allowlist]` table (gitleaks rejects a file with both).

## Per PR
- [x] #203 Casa3D memory: fix 7 schema violations in `Casa3D.md` (UUID id, top-level
      confidence/verification, source_type, relation `target_id`, drop `review_note`);
      move ledgers from `03_IMPLEMENTATION/projects/` to `02_PRODUCT/projects/Casa3D/`.
- [x] #215 deps audit: update PR body (mongodb 4→6, next-intl 0→4, site build fix),
      reword "never runs project code" (`dotnet restore` runs MSBuild), fix the 13
      trading-journal type errors.
- [x] #214 vault:// routes: cut the ~28 s first call (CSafeLoader, parse frontmatter
      once, warm-up at server start); test the VAULT_STATE route count.
- [x] #211 + #213 routing: fix `adapter_ref` → program name (one shared resolver, test
      against the real `04_CONFIG/agent_router.json`); single output dir for
      route.json/result.json; restore CLAUDE.md sections dropped by the rewrite
      (Protected Core, Production-Consumer rule, provenance/safety) + contract test;
      enforce `min_ttl_seconds`; VAULT_STATE row "not wired"; then #213 → #211.
- [x] #209 security audit: `.gitleaks.toml` back to `[[allowlists]]`; restore
      CLAUDE.md quarantine contract (REVIEW readable, marked unverified) in
      `memory_access.py` / `pack_builder.py` + tests on non-verified fixtures; fix
      REST approve→promote; reviewer gate not self-declared; auth token in
      `vault_client.js`; skill import fails closed; honest PR body (unwired findings).
- [x] #207 owner-authority hook: truly fail-closed (try/except → deny, `|| exit 2`
      in settings), protocol stops claiming expiry/authentication it lacks, PR body.
      Gating policy (blocks Bash/Edit/Write without a broker) = owner decision.
- [x] #212 LogAnalyzer: guard `RawRegistry` subkey recursion (visited set + depth)
      + crafted-hive regression test; regex match timeouts in SigmaLite/YaraLite;
      malformed-input smoke tests for CFB/LNK/USN/JumpList/pcapng; PR body; CURRENT.md.
- [x] #206 Book-to-Memory: remove hardcoded HMAC fallback; drop `UNVERIFIED`
      lifecycle state / schema widening the policy doesn't know; ablation must not
      score "with note" higher without data; stop injecting sentences into note
      bodies (revert the 9 notes); PR body.

## Owner decisions (cannot be made by an agent)
- #207: should the hook block every mutating tool before an approval broker exists?
- #207: agents push with the owner's token, so GitHub cannot tell owner from agent.
- #206: keep as one research PR or split into docs / notes / edges / code.

## Review
(2026-10-07, after execution. Local full suite at 0 failures on every head except #215, whose 4 local failures came from node_modules/.next left in the worktree; those 4 files pass once removed, the full suite was not re-run there, CI is green.)

| PR | Head | CI | Status |
|---|---|---|---|
| #203 Casa3D | d6d8dcc2 | green | **merged by owner** |
| #215 deps audit | 612c3686 | green | ready; Polar SDK 0.22→0.49 + persisted `autopilot` plan need owner check |
| #214 vault:// routes | 71432c2b | green | ready; first call 15 s → 5.6–8.8 s (0.2 s once warm) |
| #212 LogAnalyzer | 3a8aa80a | green (Windows build 252/120) | ready; corpus run + page smoke test on Windows still owner-side |
| #209 security audit | 73528a22 | green 34/35 (CodeQL neutral) | ready; memory probe 48/48 (main 38/48, before fix 2/48) |
| #213 → #211 routing | 917ac413 | #213 green 12/12 | #211 turns green only once #213 lands in its branch (push was refused by the permission guard; owner merges #213) |
| #207 owner guardrail | 0714656c | green | functional; **merging blocks most agent tools until a broker exists** |
| #206 Book-to-Memory | 2fe1b674 | green | research-only; split recommended |

Every PR still open merges cleanly with main after #203 (trial merge + state test).

Owner decisions collected from the agents:
- #207: block before a broker exists? separate bot identity for agents; configure-script side effects.
- #206: split into docs / notes / edge verdicts / code; accept the 9 restored notes as listed islands.
- #209: keep serving never-stamped REVIEW notes flagged unverified; trim ~1k lines of PR209 docs; wire the runtime-authority layer (separate PR).
- #212: Sigma stops a rule after its first timeout; YARA 10 s limit vs large files.
- #215: test Polar checkout in a sandbox.
- Suggested merge order: #215 → #214 → #212 → #209 → #213→#211 → #207 → #206.

## Merge round (2026-10-08)
Merged to main in order, each after green CI on main + PR: #209 (`e98ab626`, plus a 401 fix for
non-ASCII auth headers), #215 (`c19fd5b6`), #214 (`0e7d7857`), #212 (`f8dc8a31`), #213 into #211, #211
(`1e2f6a0d`, plus cryptography 50.0.2 and the MCP-surface test fix that #209+#214 broke on main).
main @ `1e2f6a0d`: security/tests 204 passed; full suite 2912 passed / 13 skipped / 9 xfailed.
Waiting on owner: #207 (blocks agent tools once merged), #206 (split recommended).

## NEXT PROJECT (queued 2026-10-08) — LogAnalyzer contract audit
Start only after the Book-to-Memory blocker PRs are merged and no PR is open.
Stage 1 is an audit, **no code changes**:
- [x] Read, in order: `tasks/loganalyzer/LOGANALYZER_PROGRAM_REQUIREMENTS.md` (functional/architectural
      contract), then `tasks/loganalyzer/LOGANALYZER_PRODUCT_UX_CONTRACT.md` (how capabilities are shown).
- [x] **Do not redo existing research or audits** (owner, 2026-10-08). Start from what exists and only
      fill the gaps: `loganalyzer-dfir/docs/dfir/REALITY_AUDIT.md`, `DFIR_CURRENT_ARCHITECTURE_AUDIT.md`,
      `RESEARCH_AND_ROADMAP.md`, the other `docs/dfir/*.md` contracts, `Documentation/PHASE1-STATUS.md`,
      `00_GOVERNANCE/coordination/projects/LOGANALYZER_DFIR/CURRENT.md`, and
      `tasks/loganalyzer/PR212_RESEARCH_2026-10-07.md`. Reuse their findings; re-check a finding
      only where the code changed after it was written (#212 merged 2026-10-08).
- [x] **Freshness rule** (owner, 2026-10-08): reuse an existing audit only if it is close to current
      main. Measured on main @ a6c6d7aa (LogAnalyzer code commits made after the doc was last updated):
      | Doc | Last update | Code commits since | Use |
      |---|---|---|---|
      | `CURRENT.md` (coordination) | 2026-10-07 | 0 | reuse |
      | `docs/dfir/RESEARCH_AND_ROADMAP.md` | 2026-10-07 | 2 | reuse, re-check the 2 commits |
      | `docs/dfir/REALITY_AUDIT.md` | 2026-10-07 | 3 | reuse, re-check the 3 commits |
      | `tasks/loganalyzer/PR212_RESEARCH_2026-10-07.md` | 2026-10-07 | fixes landed after it | reuse; its 6 fix items are done |
      | `docs/dfir/DFIR_CURRENT_ARCHITECTURE_AUDIT.md` | 2026-10-04 | 54 | **stale**: re-audit the areas it covers |
      | `Documentation/PHASE1-STATUS.md` | 2026-09-16 | 56 | **stale**: historical only, do not rely on it |
      Re-measure these numbers when the audit actually starts (main will have moved).
- [x] Map the two contracts onto those findings; audit only the requirements they do not cover, and
      audit `main` (`02_PRODUCT/projects/workspaces/loganalyzer-dfir/`) for those, classifying each requirement and
      classify each as IMPLEMENTED / PARTIAL / MISSING / UNWIRED, with file:line evidence and the
      production consumer (or its absence) for each.
- [x] Rules from the owner: existing functionality is not refactored or removed just to build the UI;
      a facade without a real consumer is never presented as production-ready.
- [x] Deliver the classified list for owner review before any AI is allowed to modify code.
      → `tasks/loganalyzer/CONTRACT_AUDIT_STAGE1.md` (main @ 0689f5d4, 2026-10-08). Requirements: 194 rows,
      53 IMPLEMENTED / 94 PARTIAL / 6 UNWIRED (27 components) / 41 MISSING. UX: 35 rows, 0 / 23 / 0 / 12.
      Spot-checked by the orchestrator: static "ALL SYSTEMS NORMAL"/"SHIELD ARMED" text, `ComplianceAuditEngine`
      CONFORM lines 52/75 unconditional, `Finding.ContradictingEvidence` declared and never set.
- [x] Owner: answer the 12 questions in §6 (2026-10-08: all 12 final) → audit §8.
- [ ] Owner: explicit go for stage 2 (WP0 first, then WP1 ∥ WP12).

## STAGE 2 — LogAnalyzer (started 2026-10-08, owner "start etapa 2")
Spec: `tasks/loganalyzer/CONTRACT_AUDIT_STAGE1.md` §7 (WP order) + §8 (12 final owner decisions).
- [ ] WP0 baseline & governance (PR `loganalyzer/wp0-baseline`): CUR update, audit + contracts copied to docs/dfir, historical banners, Windows CI baseline.
- [ ] WP1 honest shell (PR `loganalyzer/wp1-honest-shell`): static safety text, CONFORM without evidence → not assessed, APT page renamed, test-alert label, unwired facades marked.
- [ ] WP12 tests & release gate (PR `loganalyzer/wp12-release-gate`): synthetic corpus in CI, gate script + CI job (report on PRs, enforce on release), waiver format, CodeQL C#, PR212 #3/#4.
- [ ] Review each PR: Windows CI green, local tests vs main, production-consumer grep; then merge in order WP0 → WP1/WP12.
- [ ] Next: WP2 (Finding contract + state vocabulary), then WP3/WP4.

## Side deliverable — why a local admin should not collect/delete audit logs (owner request 2026-10-08)
Standalone document for the organisation, NOT tied to the app. Delivered to the owner as .docx only (not in the repo).
Basis: Legea 182/2002, HG 585/2002 (INFOSEC), ORNISS; NIS2 / 2024/2690 / OUG 155/2024 as recommendations.
Scope: systems with and without classified information (air-gapped networks, standalone PCs, connected for unclassified).
- [ ] Draft (scratchpad) with verbatim citations.
- [ ] Orchestrator spot-checks every quote against the fetched source before delivery.

## App requirements — audit-data handling inside LogAnalyzer (owner, 2026-10-08)
Independent of the standalone audit-log document. Each item cites its provision once the
HG 585 accreditation analysis is verified.
- [ ] Deployment profiles selected by signed policy at install, not downgradable by the operator:
      P1 classified air-gapped (HG 585/2002 INFOSEC + Legea 182/2002 + ORNISS), P2 unclassified air-gapped
      (NIS2 / 2024/2690 / OUG 155/2024 / DNSC), P3 internet-connected (same, plus internet-facing controls).
- [ ] WP3: case field "system category" (air-gapped network / standalone PC / connected × classified / unclassified)
      driving collection, export and retention rules.
- [ ] WP3: read-only collection + SHA-256 at acquisition; the app never deletes or clears logs on the source system.
- [ ] WP3: hash-chained custody records who collected, from which system, with which removable medium; warn when the
      collector is the audited system's own administrator.
- [ ] WP3: lifecycle states (decision 11); deletion only as a manual, dual-approved, logged mark with a legal-hold check.
- [ ] WP10: exports carry the case's classification marking, a hash manifest and the medium used.
- [ ] WP1→WP4: link the existing HG 585 checks in `ComplianceAuditEngine` to the cited articles; never "CONFORM" without evidence.
- [ ] WP10: "audit/compliance" report profile showing which provision each check rests on and what is missing.

## Accreditation readiness of the app itself (owner, 2026-10-08)
The owner will accredit LogAnalyzer to run on accredited internal SIC (classified) and on unclassified
air-gapped networks / standalone PCs. The app's own behaviour and documentation must satisfy HG 585/2002
(INFOSEC) + Legea 182/2002 (and NIS2 / 2024/2690 where unclassified).
- [ ] Requirements (verbatim) + gap analysis of origin/main + WP-ACR proposal → `tasks/loganalyzer/HG585_ACCREDITATION_REQUIREMENTS.md`.
- [ ] Orchestrator verifies the quotes; owner approves WP-ACR and where it slots in the WP order.
