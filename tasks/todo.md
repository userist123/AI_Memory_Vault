# Open PRs: make every PR functional (2026-10-07)

Baseline: `main` @ `609b01bf`, full suite 2611 passed / 13 skipped / 9 xfailed (local, 21 min).
Execution is delegated to Sonnet 5.5 agents, one PR (or stacked pair) each, pushing
ordinary commits to the PR's own head branch (no rebase, no force-push, no merge to main).

## Cross-PR constraint
- [ ] `.gitleaks.toml`: everyone keeps the `[[allowlists]]` format. #209 must not
      reintroduce the legacy `[allowlist]` table (gitleaks rejects a file with both).

## Per PR
- [ ] #203 Casa3D memory: fix 7 schema violations in `Casa3D.md` (UUID id, top-level
      confidence/verification, source_type, relation `target_id`, drop `review_note`);
      move ledgers from `03_IMPLEMENTATION/projects/` to `02_PRODUCT/projects/Casa3D/`.
- [ ] #215 deps audit: update PR body (mongodb 4→6, next-intl 0→4, site build fix),
      reword "never runs project code" (`dotnet restore` runs MSBuild), fix the 13
      trading-journal type errors.
- [ ] #214 vault:// routes: cut the ~28 s first call (CSafeLoader, parse frontmatter
      once, warm-up at server start); test the VAULT_STATE route count.
- [ ] #211 + #213 routing: fix `adapter_ref` → program name (one shared resolver, test
      against the real `04_CONFIG/agent_router.json`); single output dir for
      route.json/result.json; restore CLAUDE.md sections dropped by the rewrite
      (Protected Core, Production-Consumer rule, provenance/safety) + contract test;
      enforce `min_ttl_seconds`; VAULT_STATE row "not wired"; then #213 → #211.
- [ ] #209 security audit: `.gitleaks.toml` back to `[[allowlists]]`; restore
      CLAUDE.md quarantine contract (REVIEW readable, marked unverified) in
      `memory_access.py` / `pack_builder.py` + tests on non-verified fixtures; fix
      REST approve→promote; reviewer gate not self-declared; auth token in
      `vault_client.js`; skill import fails closed; honest PR body (unwired findings).
- [ ] #207 owner-authority hook: truly fail-closed (try/except → deny, `|| exit 2`
      in settings), protocol stops claiming expiry/authentication it lacks, PR body.
      Gating policy (blocks Bash/Edit/Write without a broker) = owner decision.
- [ ] #212 LogAnalyzer: guard `RawRegistry` subkey recursion (visited set + depth)
      + crafted-hive regression test; regex match timeouts in SigmaLite/YaraLite;
      malformed-input smoke tests for CFB/LNK/USN/JumpList/pcapng; PR body; CURRENT.md.
- [ ] #206 Book-to-Memory: remove hardcoded HMAC fallback; drop `UNVERIFIED`
      lifecycle state / schema widening the policy doesn't know; ablation must not
      score "with note" higher without data; stop injecting sentences into note
      bodies (revert the 9 notes); PR body.

## Owner decisions (cannot be made by an agent)
- #207: should the hook block every mutating tool before an approval broker exists?
- #207: agents push with the owner's token, so GitHub cannot tell owner from agent.
- #206: keep as one research PR or split into docs / notes / edges / code.

## Review
(filled in after execution)
