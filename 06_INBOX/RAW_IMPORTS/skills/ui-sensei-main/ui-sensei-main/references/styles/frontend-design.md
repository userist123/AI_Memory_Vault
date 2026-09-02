<!-- dials: variance=6 motion=5 density=5 (estimated — not author-declared, see PROVENANCE.md) -->

# Style: Frontend Design Baseline (adapted from anthropics/skills — frontend-design)

Anthropic's own baseline design philosophy: not a single fixed aesthetic like the other sources, but a **decision framework** that forces an explicit aesthetic commitment before any code gets written, specifically to counter the "AI slop" default (the same handful of safe patterns every model reaches for when given a vague brief).

## The process this source enforces

Before generating anything, it makes the agent commit to:
- **Purpose** — what problem the interface solves, who uses it.
- **Tone** — pick one deliberate extreme rather than a safe middle: brutally minimal, maximalist chaos, retro-futuristic, organic/natural, luxury/refined, playful/toy-like, editorial/magazine, brutalist/raw, art deco/geometric, soft/pastel, industrial/utilitarian, and more — used as inspiration to design something true to a chosen direction, not a menu to combine.
- **A real differentiation point** the choices can be justified by, treated like a design studio pitch a client already rejected the templated version of.

This means frontend-design overlaps conceptually with `anti-slop.md` — both exist specifically to counter generic AI output. The difference is mechanism: this source is a structured decision process with named negative examples, while `anti-slop.md` is a tunable dial system. In practice their outputs can converge; when both appear in the same Step 3 preview, call out that overlap rather than presenting them as fully independent options.

## Named anti-patterns (the "AI slop" checklist)

- Default typefaces — Inter, Roboto, Arial, system fonts. Also explicitly warns against over-correcting into a *new* cliché (Space Grotesk specifically named as the new "indie default" to avoid converging on).
- Purple-to-pink/purple-on-white gradients on hero sections.
- Cookie-cutter layouts — three feature cards in a row, soft shadow, `rounded-2xl` corners as an unexamined default.
- Emojis used as icons.
- Predictable, unjustified visual choices in general — anything that reads as "the average of every modern website," not a specific decision for this brief.

## Positive direction

- Commit to a cohesive palette via CSS variables, favoring one or two dominant colors with sharp accents — bold, not hedged safe grays and subtle blues.
- Use asymmetry, overlap, or diagonal flow to break out of standard grid defaults where the tone calls for it.
- Motion should be intentional and purposeful, not decorative — staggered reveals on page load, micro-interactions in the 150-300ms range.
- Production-grade, working code as the deliverable, not just visual direction — includes stack-specific guidance (HTML/Tailwind, React, Next.js, shadcn/ui).

## When this style works best

General-purpose baseline for most component/page requests — it's the safest "good taste" default when no other source's specific philosophy (minimalist, brutalist, anti-slop's dial system, emil-design-eng's interaction focus) is a clearly better fit for the brief. Reasonable to treat as the fallback candidate when a project's request doesn't strongly signal a more specific direction.

---
**Provenance:** Adapted and distilled from `anthropics/skills`, skill `frontend-design`. Apache 2.0 licensed — keep this attribution note when redistributing. Distilled paraphrase, not a reproduction of the original SKILL.md text; re-sync against the pinned commit via `PROVENANCE.md` if the original meaningfully updates (this source is actively maintained and has had recent substantive revisions).