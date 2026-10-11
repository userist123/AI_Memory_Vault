# Active Task Ledger

This file is the execution ledger for non-trivial Claude tasks.

## Current task — Casa3D: professional studio redesign (2026-10-11)

Owner request: "make the app as if built by a professional human developer to an interior designer's spec; it should not look AI-made".
Owner decisions (AskUserQuestion): visual direction = editorial studio; scope = workflow by project stages.

Diagnosis (screenshots of home, editor, room panel, tabs):
- monospace labels/stats everywhere (developer look), long explanatory sentences in panels, generic centered landing;
- 8 flat tabs with dev jargon ("Twin", "Consilier"), no notion of design phases;
- finishes chosen from long text dropdowns (truncated product names) instead of visual swatches;
- furniture on the plan drawn as labelled rectangles with brand names, not architectural symbols;
- default browser selects, blue "SaaS" buttons, demo banner tone.

Plan (each step: tsc + vitest + build + screenshots; no change to core calculations):
- [ ] A. Design system: tokens (warm paper, ink, walnut accent, hairline rules), Newsreader (headings) + Instrument Sans (UI),
      tabular numbers instead of mono, restyled buttons/inputs/selects/tabs/cards; print pack in the same identity.
- [ ] B. Stage navigation in the editor: Releveu · Concept · Finisaje · Mobilier & lumini · Instalații · Planșe · Ofertă · Prezentare;
      each stage sets the default view (plan / 3D), the visible tools and the side panel; existing panels reused, "Twin" folded
      into Concept as "Variante", advisor checks shown where they apply.
- [ ] C. MaterialPicker: swatch tiles (colour + pattern preview, name, brand, price/unit) replacing the long selects in finishes,
      textiles, kitchen and bath.
- [ ] D. Copy pass RO/EN: trade vocabulary, short labels, explanations moved to tooltips; no sentences inside forms.
- [ ] E. Home = studio dashboard: project cards with plan thumbnails, "new project" from typologies with plan miniatures.
- [ ] F. Plan: furniture as architectural symbols (bed, sofa, table+chairs, wardrobe, kitchen run, sanitary), dimension style.
- [ ] G. Verification: full suite, build, screenshots of every stage at 1440 and 1280, independent design review, PC update.
Risks: large UI diff touching Editor.tsx/FinishesPanel.tsx; e2e selectors in tests may depend on tab labels; keep i18n keys parity.

## Current task — Claude Code model router (cost-aware model/effort routing)

Objective: give Claude Code (and its subagents) an explicit, testable policy for which
Claude model and effort level to use per task class, grounded in current pricing, plus a
report on real token consumption from local Claude Code transcripts, so the owner can see
where the money goes and how much the policy saves.

Scope (affected files):
- `04_CONFIG/claude_model_routing.json` — policy: models, prices, task classes, rules
- `03_IMPLEMENTATION/packages/routing/claude_model_router.py` — classify + route + cost
- `03_IMPLEMENTATION/packages/routing/claude_usage_report.py` — JSONL usage aggregation
- `03_IMPLEMENTATION/packages/routing/claude_model_cli.py` — `route` / `report` / `policy`
- `.claude/agents/{Explore,vault-worker,vault-reviewer}.md` — subagents pinned to cheaper models
- `00_GOVERNANCE/protocols/Claude_Model_Routing_Policy_V1.md` — the human-readable policy
- `CLAUDE.md` — short pointer section
- `20_TESTS/test_claude_model_router.py`
- `00_GOVERNANCE/coordination/tasks/todo-claude-model-router.md` — checkpoint

Dependencies: none new (stdlib only). Does not touch the protected core
(`providers/model_tier_router.py` keeps its light/standard/heavy contract; this layer is
Claude-Code-side and independent).

Steps:
- [x] Research: pricing table (claude-api skill, cached 2026-10-06), Claude Code docs on
      subagent `model`/`effort` frontmatter and model aliases, cost-optimization lever order
- [x] Inspect existing routers: `providers/model_tier_router.py` (tier->provider, council),
      `routing/agent_router.py` (task->runtime). Neither routes Claude model *within* Claude Code.
- [x] Write policy JSON
- [x] Write router module + usage report + CLI
- [x] Write subagent definitions
- [x] Write protocol doc + CLAUDE.md pointer
- [x] Tests (red -> green)
- [x] Run `report` on this session's own transcript as evidence
- [x] Full `20_TESTS` run, checkpoint, commit, push

Risks: price table drifts (kept in config with a `pricing_as_of` field); keyword classifier
is heuristic (returns `confidence: low` + policy default instead of guessing silently).

Review criteria: tests green; `route` output explains model, effort, class, rationale and
estimated cost; `report` reproduces the token totals of a fixture transcript.

## Review (2026-10-10)
- New tests: 23/23. Full suite: 3604 passed; every failure traced to the environment (mcp 2.x, missing
  dependency) or to the two new plain `.md` files, both fixed (route re-measurement + allowlist).
- Measured on this session: 91% cache-hit; same tokens on Opus would be 43% of the Fable bill.
- Follow-up (same day): the policy is now a Claude Code skill (`cost-router`) with a per-prompt hook and a
  one-shot installer (`install.py`, tested in a temp HOME: install, idempotent re-install, foreign hook kept,
  uninstall). 12 more tests. Remaining: prices to refresh with the rate card.
