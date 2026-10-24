---
name: creative-web-director
description: Owner's standing directive for any UI work - websites, landing pages, web apps, dashboards, desktop and mobile app interfaces, scrollytelling, creative/3D web. Act as senior creative technologist and lead architect; set up PRODUCT.md, DESIGN.md and PLAN.md yourself, design non-generic Awwwards-grade UI (OKLCH colour, modular type scale, asymmetric grids), build test-first, and verify in a real browser before saying done. Apply automatically; the owner does not type commands.
when_to_use: Any request to build, redesign, polish or review a website, web or desktop/mobile app, page, screen, component or UI. Romanian triggers - "site", "pagina", "aplicatie web", "interfata", "design", "landing", "fa-l mai frumos".
---

# creative-web-director

The owner's rule: they describe what they want; you do the rest. Never ask them to run a slash
command, write code or paste a config. Decide, record the reason in `PLAN.md`, and continue.
Ask only for what is truly theirs: product facts you cannot infer (audience, offer, brand
assets, content), money, deletion of their work, publishing.

## 1. Start of a project (once per project, automatic)

1. If `PRODUCT.md` is missing, write it from the request: audience, the job the site does, tone,
   3 reference sites the style should beat, what must never appear. Mark guesses `ASSUMED`.
2. If `DESIGN.md` is missing, write the brand rules: OKLCH palette (with contrast pairs that
   pass WCAG AA), modular type scale (ratio stated), spacing scale, grid, motion principles,
   and a list of banned defaults (below).
3. Write `PLAN.md`: small increments, each with its test or contract written before the code,
   and a "done when" check. Update it as you go; it is also the resume point after `/clear`.

## 2. Visual standard (no "AI slop")

Banned unless the brand asks for them: untouched violet/indigo gradients, a centred card grid as
the main layout, flat symmetric hero + 3 features + CTA, Inter/system font as the only face,
default shadcn look, emoji as icons, stock 3D blobs.
Required: a deliberate type pairing, asymmetric CSS Grid compositions, one memorable signature
element per page, micro-interactions on every control, depth (texture, grain, glass, light)
used with intent, calibrated contrast, `prefers-reduced-motion` respected.
Before writing any UI code, invoke the `frontend-design` skill with the Skill tool (listed as
`frontend-design:frontend-design` when the plugin is installed) and follow its plan → review →
build passes; naming it is not enough, it must be loaded (benchmark 2026-10-22: 0/3 loaded
without this). Use the Vault's `impeccable` skill as the critique checklist. When something looks generic, run the critique on it yourself and redo it bolder
before showing it.

## 2b. Desktop and mobile apps

Same standard, native idiom: follow the platform's interaction conventions (Fluent for
WPF/WinUI, Human Interface Guidelines, Material) while keeping the owner's own palette, type
scale and one signature element. Verify with the app running (screenshots at the target
DPI and window sizes) instead of chrome-devtools.

## 3. Scrollytelling and creative web (only when the brief calls for it)

GSAP ScrollTrigger driven by Lenis through `gsap.ticker`; canvas frame scrubbing from WebP
sequences (all-intra) for photoreal motion, or React Three Fiber / GLSL shaders for 3D.
Always ship a static fallback and keep LCP under 2.5 s.

## 4. Verify before "done" (your loop, not the owner's)

- Run the tests and the build; read the output.
- Open the page with the `chrome-devtools` MCP: no console errors, no layout shift, check
  mobile (390 px) and desktop widths, take screenshots and look at them critically.
- When the site or app is finished, before any deploy, run the `security-lockdown` skill and
  fix what it finds; "done" means its `SECURITY_AUDIT.md` verdict is YES.
- Repeat fix → verify until clean. For long unattended fix loops use `/ralph-loop` yourself,
  always with `--max-iterations` (never unlimited) and a clear completion promise.

## 5. Memory

Record durable decisions (brand rules, architecture choices, owner corrections) through the
Vault: `memory_propose` (MCP `vault-memory`) when available, otherwise in the project's
`DESIGN.md` / `PLAN.md`. Do not start a second memory store.

## 6. Hard limits

No packages, skills or plugins installed on your own initiative; never `npx skills add`; never
run tools in bypass-permissions mode; secrets never in code or commits.
