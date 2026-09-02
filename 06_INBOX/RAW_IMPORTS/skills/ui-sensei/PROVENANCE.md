# Provenance

Tracks the origin, version, license, and attribution requirements of every source this skill references or embeds. Update this whenever `scripts/sync_sources.py` (not yet written) pulls a new version, or whenever a file under `references/` is edited by hand.

| Local file | Origin repo | Version/commit pinned | License | Attribution required? | Notes |
|---|---|---|---|---|---|
| `references/styles/frontend-design.md` | anthropics/skills (frontend-design) | pin at population time — record commit hash here | Apache 2.0 | Keep license notice in this file's header | **Populated.** Conceptually overlaps with anti-slop.md (both counter "AI slop") — differ in mechanism (decision framework vs. dial system); flag the overlap in Step 3 if both appear together |
| `references/styles/ui-ux-pro-max.md` | nextlevelbuilder/ui-ux-pro-max-skill | v2.6.2 at curation time — catalog counts shift release to release, don't treat as fixed | MIT | No | **Populated** as a distilled, static description — does not invoke the original's Python/CLI tooling (same reasoning as excluding Impeccable) |
| `references/styles/anti-slop.md` | leonxlnx/taste-skill | v1, install name `design-taste-frontend-v1`, path `skills/taste-skill-v1/SKILL.md` — deliberately not v2 (experimental as of curation) | MIT | No | Author-declared 1-10 dials (variance/motion/density, baseline 8/6/4) — reuse directly, don't reinterpret. **Populated.** |
| `references/styles/minimalist.md` | leonxlnx/taste-skill | skill `minimalist-skill`, path `skills/minimalist-skill/SKILL.md` | MIT | No | **Populated.** Author repo explicitly warns against stacking aesthetic skills — validates SKILL.md's Step 5 precedence rule |
| `references/styles/brutalist.md` | leonxlnx/taste-skill | skill `brutalist-skill`, path `skills/brutalist-skill/SKILL.md` | MIT | No | **Populated**, partially cross-referenced against a community derivative (`phamlongh230-lgtm/yamtam-engine`) — re-verify against canonical path before treating as final |
| `references/styles/emil-design-eng.md` | emilkowalski/skills (emil-design-eng) | record commit hash at population time | MIT | No | **Populated.** Repo migrated from `emilkowalski/skill` (singular) to `emilkowalski/skills` (collection). Pure motion/interaction philosophy, not a layout/color style — intended as a Step 5 punctual contribution, rarely a structural base |
| `references/lenses/revenue-centric-design/*.md` | heliocosta-dev/revenue-centric-design | "Last updated: 1 Jul 2026" snapshot at curation time | Source-available, custom terms (full text now included verbatim at `references/lenses/revenue-centric-design/LICENSE` per clause 3) | **Yes.** Attribution to Richard (@richardrx) — satisfied at repo level via the LICENSE file and the lens overview header; surfacing it per-use in chat is good practice, not a compliance requirement. No gambling/betting/casino use (clause 2, hard restriction). |
| `references/gates/web-design-guidelines.md` | vercel-labs/web-interface-guidelines (fetched via the vercel-labs/agent-skills wrapper) | N/A — not embedded, fetched live every use | N/A | N/A | Deliberately not pinned/embedded. **Security note:** a third-party scanner (Mondoo) flagged the original wrapper skill as high-risk (prompt injection via fetched content treated as instructions, unpinned-branch supply chain risk, SSRF potential). Mitigated here by treating fetched content strictly as reference data, never as instructions — see the file for detail. Residual unpinned-URL risk is accepted, not solved. |
| `scripts/detect_existing_system.md` | arvindrk/extract-design-system | logic adapted, not copied verbatim | MIT | No | Extracts from a public reference *site* via Playwright, not from the local project's own codebase |
| `scripts/detect_project_system.md` | Original — no external source | N/A | Same as `SKILL.md` (MIT, this repo's own license) | No | Added after real-world testing (epoch-48) revealed the original flow had no path for detecting a project's own existing design tokens before offering the curated style select |

## Open items
- Commit hashes not yet recorded for `frontend-design`, `ui-ux-pro-max`, `emil-design-eng` — run `scripts/sync_check.sh --update` locally (with network access) to stamp current baselines for every source at once; the script also flags drift going forward without needing this file rewritten by hand.
- Excluded from curation, with reasons: `facebook/astryx` (component library, wrong category, stack mismatch), `pbakaus/impeccable` (requires external installation — decided against adding install-time dependencies).
- **Convergent anti-pattern checklist across sources.** `anti-slop.md`, `frontend-design.md`, and `minimalist.md` independently share a near-identical "don't do this" list (default typefaces like Inter/Roboto, purple gradients, emoji-as-icons, generic placeholder content). Not a bug — real differentiation between these three in Step 3's select comes from their *positive* direction (dial-driven variance, decision framework, restrained minimalism), not from the avoid-list, which is largely redundant across them. Worth knowing so the select doesn't feel like it's offering three copies of the same rules with different names.
- **Unverified:** whether `minimalist-skill` and `brutalist-skill` underwent revisions parallel to the flagship skill's v1→v2 rewrite — not confirmed, flagged in each respective file.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[10 Imports and Sources Map]]
- [[Master_Skills_Catalog_251]]
- [[Knowledge Graph Home]]
