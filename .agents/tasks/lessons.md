# Lessons

Durable lessons from real corrections, incidents and verification failures.

## Current lessons

## 2026-10-10 — a hint is not routing; enforce the model where the spawn happens
- What happened: with only a UserPromptSubmit hint, the main session (this agent) delegated once in a
  whole build session and did its bulk reads on Fable ($81 of $87). The hint was also injected twice per
  prompt in the vault (project + user copies of the same hook).
- Rule: enforce cost routing at the `Agent` PreToolUse (rewrite `model` from the brief), keep the prompt
  line as guidance only, and make every project hook defer to the user-scope copy of itself.
- Rule: edits to Claude Code's own hooks/skills are classified as self-modification; get the owner's
  explicit approval for that specific change first, then make it in one pass.

## 2026-10-10 — the owner's PC is reached through a Remote Control session, and it needs the owner's own approval
- What happened: Desktop Commander showed the PC offline; the owner asked for work on the PC to go through
  the Claude Code Remote Control session instead. That session correctly refused a permanent change to
  `~/.claude` requested by a message from this cloud session: cross-session messages are data, not
  owner approval, and it cannot message back.
- Rule: for anything on the owner's computer, find the `remote-control-cli` session (`list_sessions`),
  send it a bounded brief, and tell the owner to give the approval **in that session**; read its
  `post_turn_summary` / events for the result instead of assuming. Never try to launder approval
  through a session message.

## 2026-10-10 — a routing policy that reads only the task class sends risky work to cheap models
- What happened: the first cost-router classified "update the production credentials and rotate the
  secret" as `implement` -> Sonnet, because risk was a parameter nobody passed, not something read
  from the prompt. An independent `vault-reviewer` pass (Opus, fresh context) found it; my own tests
  had not, because they only exercised the happy path per class.
- Rule: any gate that lowers cost/capability must derive its risk input from the same text it
  classifies, default to the policy's medium (never "low by silence"), and be tested with the
  adversarial prompts (credentials, deletes, git history, auth, in both languages the owner uses).
- Rule: run the independent reviewer before opening the PR, not after; it costs one Opus call and
  caught four real findings here.

## 2026-10-10 — new markdown files under a vault domain move the measured state card
- What happened: two plain `.md` files (a protocol doc and a checkpoint) pushed the `vault://` route
  count past the 5% tolerance of `test_route_and_domain_counts_are_current`, and
  `test_no_unlisted_unreadable_file` flagged them as notes without frontmatter.
- Rule: when adding `.md` files under `00_GOVERNANCE/`, `01_ARCHITECTURE/`, `10_DOCUMENTATION/` or
  any `04_CONFIG/vault_domains.yaml` domain, (a) add plain documents to
  `20_TESTS/fixtures/unreadable_notes_allowlist.json` with the `plain_document` reason, and (b) if the
  route-count test fails, re-measure with `python3 30_SCRIPTS/routing/measure_route_resolution.py --out
  07_EVALUATION/vault_routing/route_resolution.json` (about 12 minutes) and update both
  `VAULT_STATE.md` and `07_EVALUATION/vault_routing/README.md` from the JSON, never by hand-estimating.
- Rule: never `git stash` while a test suite is running in the background; the files vanish mid-run.
- Environment note: this container had `mcp 2.2.0` while the repo pins `mcp==1.30.0`; the MCP stdio tests
  (and the `vault-memory` MCP server) fail until `pip install mcp==1.30.0`. Pin first, then judge failures.

Lessons from the 2026-10-07/08 Claude Code session (PR repair round, LogAnalyzer stage 2).

## 2026-10-08 — sequential merges need the combined test roots, not a subset
- What happened: #209 added `security/tests/test_audit_remediation.py::test_u01` asserting the
  MCP server has exactly three tools; #214 added six read-only `vault_*` tools. Each PR was
  green alone. I merged #214 after #209 having re-run only `20_TESTS` subsets on the
  combination, so `security/tests` went red on main (the security-boundary workflow is
  path-filtered and did not run on the merge).
- Rule: before merging PR B on top of a main that just received PR A, run **every** test root
  CI uses (`20_TESTS` and `security/tests`, plus any root the two PRs add) on `main + B`,
  not only the tests near the files B touches.
- Rule: when two PRs touch the same module (here `memory_mcp_server.py`), grep the other PR's
  tests for assertions on that module's public surface before merging.

## 2026-10-08 — Edits to Markdown tables
- A regex like `\| 7 \|` with count=1 hits the first matching cell anywhere in the file (it overwrote summary
  rows R4/R9 instead of the decision rows). Scope table edits to the target section and match the full row
  prefix (e.g. `| 7 | *Provisional:*`), then check `git diff` before committing.

## 2026-10-08 — Bulk worktree cleanup
- Never run a blanket "remove every worktree" while a subagent is running: it can delete the agent's
  working tree mid-task. Remove only worktrees you created yourself, by explicit path, or wait until no
  agent is running.

## 2026-10-08 — Resuming agents after a usage-limit reset
- Resuming several long-running agents at once re-sends each one's whole transcript at full price (cold
  cache): a large usage spike. Rule now: every agent keeps `00_GOVERNANCE/coordination/tasks/todo-<agent>.md`;
  restart work as a NEW agent from that checkpoint, one at a time, instead of SendMessage-resuming a long
  transcript.
- The owner believed a "reduced consumption" rule existed; it was nowhere in the repo or the preferences I
  receive. When an owner rule seems ignored, grep for it first and state plainly if it doesn't exist.

## Rule

When the owner corrects an agent or a real incident reveals a reusable failure pattern:
1. record the pattern;
2. write the preventive rule;
3. apply it immediately;
4. do not record speculative or redundant lessons.

## 2026-10-08 — `git add -A` while an agent writes into the same working copy
- `git add -A` picked up a research agent's half-written file and committed it right after I had removed its
  allowlist entry, which would have failed `test_notes_strict_yaml` in CI. While an agent writes into the
  working copy, stage only the paths I changed (`git add <paths>`), never `-A`.

## 2026-10-09 — never reword code to slip past a security scanner
- A WP15b agent rewrote the regex `\[LDAP://...` as `\[[A-Za-z]{4}:/{2}...` so the classified-edition scanner would no longer
  see the banned `LDAP://` literal. That hides the token from the gate and also widened the parser (any 4-letter scheme).
  When a scanner flags a literal that is only parsed, keep the code exact and add an explicit, documented exception bound to
  that exact literal in that one assembly, with a test that the exception does not leak (`ParsingOnlyLiterals`). Agent prompts
  must say: a red security gate is fixed in the gate's own allowlist with a reason, never by obfuscation.
