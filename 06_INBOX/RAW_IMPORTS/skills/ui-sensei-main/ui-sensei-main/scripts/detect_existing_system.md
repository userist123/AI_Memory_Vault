# Script: Detect Existing System (adapted from arvindrk/extract-design-system)

Used by `SKILL.md` Step 0.5 — only triggers when the user references an existing public site/product to mirror ("parecido com o Stripe", "no estilo do Linear", or a direct URL).

## What the original does

Runs a Playwright-driven scrape against a **public reference site** (not the current project's own codebase — that distinction matters, see the correction logged in `PROVENANCE.md`) to extract its actual computed design tokens: color values, spacing scale, typography, border-radius, shadow treatment. It reads what a site's CSS actually resolves to, rather than guessing from a screenshot.

## What to do here (adapted, no separate tool install)

1. Given a reference URL, use `web_fetch` to retrieve the page.
2. Extract concrete, verifiable design signals from the fetched content: dominant color values, font-family declarations, structural layout cues (grid vs. flex patterns, spacing rhythm) visible in inline styles or linked stylesheet references.
3. If the fetched page doesn't expose enough usable signal (heavily obfuscated build output, no readable CSS), say so explicitly rather than guessing a palette from vague visual memory of the named site — a wrong guess presented confidently is worse than admitting the extraction didn't yield much.
4. Package the result the same way a `references/styles/*.md` file would present itself in Step 3: 4 bullets (structure, visual treatment, one interaction note if visible, "extraído de [site]" instead of "works best when").

## Boundaries

- This never touches the current project's own codebase — that's a different, not-yet-built capability (project-level style detection), not what this script does.
- Treat extracted colors/fonts as a starting point to adapt, not something to reproduce pixel-for-pixel — the goal is "in the spirit of," not a clone that could raise its own IP questions about copying another product's specific brand identity.
- If the referenced site turns out to be a direct competitor of the user's own product, flag that plainly rather than silently proceeding — worth a one-line check-in, not a full stop.

---
**Provenance:** Logic adapted from `arvindrk/extract-design-system`, MIT licensed — no attribution required beyond this note. This is a description of adapted behavior, not a port of the original Playwright implementation; `ui-sensei` doesn't run a separate scraping tool, it uses `web_fetch` already available to the agent.