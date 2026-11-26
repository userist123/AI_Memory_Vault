# The team: who does every operation of creating something

Opus (main session) is the project lead and the integrator: owns decisions, briefs, merges and the
final check. Model choice per step always goes through `cost-router`. Every brief follows
`prompt-director` (meta-prompt + task prompt).

## Execution order (a DAG, not a line)
```
A -> B -> C   (sequential: requirements, research, architecture + contracts; C freezes them)
Wave 1 (parallel, after C):  D, E, F, G
Gate H: tests first (unit, security, contract tests) - red run shown before any code
Wave 2 (parallel slices):    I1, I2, ... (disjoint files, see "Parallel work")
Wave 3 (parallel, on the integrated candidate): J, K, L, M
then sequential: N -> O -> P -> Q -> R
```

| # | Operation | Owner (agent / skill) | Output that proves it |
|---|---|---|---|
| A | Requirements: goal, users, scope, acceptance criteria. Also: non-functional budgets (p95 latency, memory, bundle size, Core Web Vitals, a11y WCAG 2.2 AA, supported devices, runtime cost), each with how it is measured and what blocks release; privacy: data inventory + classification, DPIA decision when personal data is processed; i18n if the product is multilingual | Opus; skill `product-management:write-spec` for larger products | `docs/SPEC.md` with testable criteria and budgets |
| B | Research: prior art, stack choice, security of the stack | `agent-bridge` (Codex + Gemini), skill `surse-web`; `Explore` (Haiku) for local code | research note with sources, disagreements kept |
| C | Architecture and decisions. Also: budgets mapped to components; data minimisation, retention, deletion/export; data migration + rollback strategy (expand-and-contract, feature-flag kill switch); observability design (structured logs with correlation ids, health checks, SLOs scaled to the product: a local single-user tool needs little) | Opus; `fable-consultant` for hard trade-offs; skill `engineering:architecture` | `docs/DESIGN.md` + ADRs + frozen contracts |
| D | UI/UX, colours, accessibility (WCAG 2.2 AA). i18n if needed: string catalogs, Romanian diacritics, locale formats | skill `designer-ui` (60-30-10), `frontend-design`, `design:accessibility-review` | mockups/screens, design tokens |
| E | Tools and environment | `references/unelte.md` (study, install on the free disk, practice, card) | `INSTALLED.json` entry + tool card |
| F | Project scaffold, git, CI config | `vault-worker` (Sonnet) | empty repo builds; CI file |
| G | Threat model and probe list. Privacy: no personal data in logs or LLM prompts, data flows listed | `fable-consultant` + Opus | `SECURITY.md` / threat model |
| H | Gate: tests first (unit, security, e2e skeleton, contract tests against the frozen contracts) | `test-author` (Sonnet) | red run shown |
| I | Implementation, slice by slice. Also: data migrations with tested down-migrations; logs/health checks as designed in C; UTC storage when i18n applies | `vault-worker` (Sonnet); escalation to Opus, then `fable-executor` per cost-router 4G | green tests per slice |
| J | Code + security review (max 2 rounds; see "Review selection") | `vault-reviewer` (Opus) | findings fixed or accepted residuals |
| K | QA: e2e, visual, accessibility, performance against the budgets from A, compatibility (phone/tablet/desktop) | `check-runner` (Haiku) runs them; Playwright / chrome-devtools (Lighthouse); Opus reads the results | test reports, screenshots |
| L | Dependencies, licences, secrets scan; supply-chain evidence: SBOM, build inputs/provenance | `vault-worker` runs OSV / pip-audit / npm audit, gitleaks, licence check | clean report or recorded residual; SBOM |
| M | Documentation: README, user guide in Romanian, changelog | `vault-worker`; skill `document-uman` for owner-facing documents | docs in the repo |
| N | Build, packaging, versioning, installer/APK; artefact sha256 and recorded build inputs/provenance | `vault-worker`; skill `engineering:deploy-checklist` | reproducible artefact + sha256 + SBOM |
| O | Final gate (rules below) | Opus + `fable-consultant` full check, open questions to ChatGPT/Gemini via agent-bridge (memories ai-app-studio-final-gate, build-local-first) | delivery checklist all ticked |
| P | Release / deploy (live operation). Backup/restore rehearsal done; migration rollback path and kill switch confirmed | Opus only; push/PR/CI only at the end | CI green, release notes |
| Q | Operate: logs, backups, update plan, runbook; observability live; post-release vulnerability response (watch the SBOM for new CVEs, triage, patch plan) | skill `operations:runbook`; `vault-worker` | `docs/RUNBOOK.md` |
| R | Close: checkpoint, memory, cleanup; FinOps note: delivery token cost (from cost-router telemetry) AND runtime cost of what was built | Opus; skill `curatenie-temporare` | checkpoint file, `.curatenie.json` |

Skip a row only when it does not apply (no UI -> skip D; no personal data -> no DPIA; one language -> no
i18n) and say so in the report.

## Row O: final gate rules
- Before anything goes to ChatGPT/Gemini it is sanitised: no secrets, no personal data, no private
  paths; a generic description or PUBLIC excerpts only.
- At most 3 loops. Disagreements are settled by tests and evidence, not by model votes.
- Record what stays open (residuals) in the delivery checklist and the security document.

## Parallel work (several cheap agents)
- Split by complexity, give each slice to the cheapest capable tier: mechanical edits, renames, docs,
  config -> Haiku; spec'd logic with tests -> Sonnet `vault-worker`; ambiguity or risk decisions stay
  with Opus.
- Parallel writers only on disjoint files, each in its own git worktree + branch. A file-ownership
  table is written in the coordination checkpoint BEFORE dispatch.
- Shared files (lockfiles, migrations, route registries, barrel exports, CI config) are edited only
  by the integrator.
- At most 6 concurrent writers and 8 concurrent read-only agents (owner decision 2026-11-24, same as cost-router
  fan-out.md); size by complexity, never more writers than disjoint slices.
- The integrator is the Opus main session: merges in small batches and runs the affected tests after
  each merge. Workers commit/push their own branch only when the brief says so, then stop; they
  never poll CI and never merge. The integrator merges.

## Review selection
- Deterministic gates first (lint, types, tests, SAST, gitleaks): no LLM reviews a red build.
- Then `vault-reviewer` (Opus) by default.
- Add `fable-consultant` as a second, read-only reviewer only for auth/crypto, sandbox/trust
  boundaries, concurrency, irreversible migrations, or when Opus review rounds disagree.
- The author never reviews its own work.
- Reviewers get the acceptance criteria, the probe list, the diff and the test evidence.

Specialists for special products: `animatie-3d` (3D/video), `dfir-analyst` (forensics),
`analist-juridic` (legal texts), `ai-website-studio` MCP (sites with live preview),
AI App Studio (D:\w\ai-app-studio) for web + Android apps once it is release-clean.
