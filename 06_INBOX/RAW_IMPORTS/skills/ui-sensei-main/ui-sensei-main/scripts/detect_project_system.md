# Script: Detect Project Design System

Used by `SKILL.md` Step 0.3 — runs only on first use in a project (no `## Global` yet in `.ui-sensei/MASTER.md`). Distinct from `scripts/detect_existing_system.md`, which extracts from an *external* public site the user references — this one looks at the current project's own code.

## What to look for

Search the project for common design-token signals, roughly in this order of confidence:

1. **CSS custom properties** — a `globals.css`, `theme.css`, `tokens.css`, or similar file with a `:root { --... }` block defining color, spacing, or typography variables. OKLCH/HSL/hex color tokens, `--font-*` variables, brand-named tokens (`--brand`, `--primary`, `--accent`) are strong signals.
2. **Tailwind theme configuration** — `tailwind.config.{js,ts}` or a `@theme` block (Tailwind v4) extending the default palette, font family, or spacing scale away from Tailwind's stock values.
3. **A dedicated design-tokens file** — `design-tokens.json`, `theme.ts`, or similar, especially in a shared `packages/ui` or `packages/design-system` directory in a monorepo.
4. **Existing component library conventions** — a `components.json` (shadcn/ui), a themed component wrapper, or consistent use of specific font/color classes across multiple existing components.

## What counts as "clear" vs. "ambiguous"

- **Clear:** a single, coherent source (e.g. one `globals.css` with a full token set, actually used across multiple existing components) — high confidence this is the intentional system, not leftover boilerplate.
- **Ambiguous:** conflicting signals (e.g. Tailwind defaults still used in some places, custom tokens in others), or a token file that looks unused/vestigial (defined but not referenced anywhere in actual component code), or multiple different token sources that don't agree with each other.

## What to extract, if clear

Pull the same shape Step 3 previews expect for Global: dominant palette values (with names/roles if the tokens have them — brand, accent, background, foreground), the typography stack (font family per role — heading, body, mono/data), and note the source file path so it's traceable later. This becomes the `## Global` section directly — not run through the curated style sources' opinions.

## What this does NOT do

- Does not touch structural/layout/motion decisions — that's still Step 3-5's job, using the six curated sources, same as any other request.
- Does not modify the detected tokens — this step only reads and preserves, never "improves" an existing system as a side effect of running.
- Does not run again once `## Global` exists — later categories in the same project just inherit it via Step 0, this script isn't re-invoked per category.