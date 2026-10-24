---
id: "casa3d-research-nextgen-uiux-2026-10-18"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, research, uiux, spatial_editor, accessibility, design_system]
created: "2026-10-18"
updated: "2026-10-18"
provenance:
  source_type: web_and_repository_review
  source_ref: "Official Apple HIG, W3C WCAG, Three.js docs, Chief Architect feature catalogue, and Casa3D PR #305 source files"
  source_date: "2026-10-18"
  redaction: not_applicable
  provenance_status: complete
confidence: medium
verification: unverified
---

# Casa3D — Research: next-generation UI/UX and spatial studio direction (2026-10-18)

## Scope and evidence boundary

This research supports PR #305 on branch `prompt/casa3d-pro-editor-2026`. It extends the existing Pro Editor Master Prompt; it does not claim that the app has already been redesigned or browser-tested.

Repository files inspected on the PR branch:
- `02_PRODUCT/projects/Casa3D/GEMINI_4_ARGON_PRO_EDITOR_MASTER_PROMPT.md`
- `02_PRODUCT/projects/Casa3D/research-2026-10-11c/research_showcase_ui.md`
- `02_PRODUCT/projects/Casa3D/research-2026-10-18/research_professional_workflows.md`
- `02_PRODUCT/projects/workspaces/casa3d/app/app/globals.css`
- `02_PRODUCT/projects/workspaces/casa3d/app/app/page.tsx`
- `02_PRODUCT/projects/workspaces/casa3d/app/README.md`

The CSS token layer is already large (over 100 KB), and the master prompt already describes a studio chrome, canvas-first editor, contextual inspector, command palette, material workflows and UI quality gates. Therefore the next step should not be another generic CSS facelift. It should audit and rationalise the existing system, then implement a cohesive, model-aware spatial workspace and verify it in a running browser. The current files alone do not prove the current rendered UI quality or behavior; baseline screenshots and runtime checks are required.

## Executive recommendation

Position Casa3D as a **spatial design instrument**: professional CAD precision, high-end interior material presentation and the fast interaction model of a creative desktop application. The memorable quality should come from the persistent spatial canvas, synchronized views, selected-object context, material/lighting previews, options comparison and a polished client presentation—not from visual effects alone.

## Highest-value additions

### P0 — Spatial workspace coherence
- One canonical model selection/context shared across plan, 3D, inspector, material browser, schedules and findings.
- A stable project/floor/room navigation spine; collapsible tool dock; contextual inspector; searchable asset drawer; truthful save/sync/validation state.
- Fast 2D/3D transition and synchronized split view only if performance allows. Do not lose selected object, floor, camera context or pending edits when switching views.
- Canvas-focus mode, panel collapse/resize, fit-to-project/fit-to-room, visible units/scale and a reliable reset-to-default workspace.

### P0 — A recognisable visual system
- Architectural paper-like 2D canvas and restrained studio chrome; carefully selected accent; distinct semantic status colors; precise technical typography and tabular numerals.
- Shared tokens for color, type, spacing, border, focus, elevation, controls, motion and responsive breakpoints.
- Audit the existing `globals.css` token layer and legacy aliases before adding overrides. No uncontrolled design-system replacement or UI library installation before dependency review.
- Original Casa3D visual identity; do not clone another product or lean on generic glassmorphism, gradients, floating cards or excessive rounding.

### P0 — Evidence-based UI redesign
- Before/after screenshots of identical states and viewports; baseline visual defect inventory linked to concrete components/files.
- Compare at least two design directions before choosing one; document the decision and rejection criteria.
- Test 1440, 1280, 1024, 768 and 390 CSS-pixel widths when a browser runtime is available.
- Real flow tests for create/open, dimension editing, object selection/placement, material application, 2D/3D switching, warning navigation, undo/redo, save/reload and client presentation.
- Never report visual verification without screenshots and a runnable environment.

### P1 — Design options lab
- Persist genuine A/B/C options against a base revision.
- Synchronized plan/render views, changed objects/materials, confirmed/estimated/unknown budget deltas and explicit trade-offs.
- Apply/reject/revert with revision binding; prevent stale option overwrite.
- No fabricated “AI score”, budget savings, compliance result or data.

### P1 — Material and lighting studio
- Contextual material preview on real selected geometry, alternatives side-by-side and scope-aware application.
- Named lighting previews only if the actual renderer parameters change; artistic rendering must not be described as photometric analysis.
- Material changes should update dependent quantities and outputs where the current model supports it; show the impact before bulk application.

### P1 — Spatial issue and impact navigation
- Click a finding and focus the exact object/room, enable the correct layer and open its inspector.
- Display downstream effects only where supported by real model dependencies: dimensions/area, schedules, quantities, budget, approvals and drawing freshness.
- Each status must link to evidence and a real next action.

### P1 — Client presentation mode
- Presentation generated from current project data: room sequence, actual available renders, plan views, concept/material board, approved alternatives and revision metadata.
- Maintain the existing permission model; internal margin, private survey data and unapproved options stay private.
- Treat it as another view of the canonical project, not a second manually maintained project or static mock presentation.

## Official references and findings

1. **Apple HIG — Layout** (updated 2026-09-09): emphasizes layouts that adapt across display sizes/orientations and make content relationships understandable. This supports responsive workspace composition and a clear hierarchy rather than a fixed desktop layout squeezed onto mobile.  
   https://developer.apple.com/design/human-interface-guidelines/layout/

2. **Apple HIG — Design principles** (updated 2026-06-08): stresses purpose, user agency, recovery from mistakes, flexibility, simplicity, clear feedback and craft. This supports reversible edits, honest action states, contextual controls and restrained visual novelty.  
   https://developer.apple.com/design/human-interface-guidelines/design-principles

3. **Apple HIG — Panels**: panels should provide supplementary controls/information related to the active window or selection. Use this as a principle for the contextual inspector; do not assume Apple desktop panel behavior must be copied literally into a web app.  
   https://developer.apple.com/design/human-interface-guidelines/panels

4. **W3C WCAG 2.2**: the normative reference for web accessibility requirements. In particular, SC 2.5.7 requires an alternative to dragging movements where applicable; SC 2.5.8 establishes minimum target-size/spacing criteria with defined exceptions. Provide keyboard/numeric routes for precise spatial operations and test focus/contrast.  
   https://www.w3.org/TR/WCAG22/  
   https://www.w3.org/WAI/WCAG22/Understanding/dragging-movements  
   https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum

5. **Three.js WebGLRenderer docs**: renderer exposes capabilities and performance-relevant controls. This supports profiling the actual renderer, not adding expensive blur/lighting/shadows based on appearance alone. It does not prove the Casa3D app uses a particular renderer path; confirm imports and scene lifecycle in code before making implementation decisions.  
   https://threejs.org/docs/pages/WebGLRenderer.html

6. **Chief Architect feature catalogue**: a professional capability benchmark for synchronized 2D/3D editing, object/material workflows and model-derived schedules. Use to compare workflow depth, not to imitate its visual identity.  
   https://www.chiefarchitect.com/products/features/

## Research limitations

- The repository file review is not a substitute for running the app. The current rendered UI, real interaction latency, keyboard accessibility, viewport overflow and 3D performance remain NOT VERIFIED until a browser/runtime audit is performed.
- Existing endpoint/component names do not prove end-to-end feature completeness.
- Competitor marketing pages describe capabilities, not independent usability testing.
- “Revolutionary” is a product goal, not a measurable test by itself. Convert it into the observable acceptance gates in sections 20.7–20.8 of the Pro Editor Master Prompt.

## Implementation decision

The new requirements are appended to `GEMINI_4_ARGON_PRO_EDITOR_MASTER_PROMPT.md` as sections 20–21. Keep the implementation in PR #305 and its existing branch. Audit existing functionality before creating parallel systems, use a coherent P0 vertical slice, and do not merge without explicit authorization.


## Implementation status — actual UI pass (2026-10-18)

The first code-level visual redesign has now been committed on the same PR #305 branch:
- File: `02_PRODUCT/projects/workspaces/casa3d/app/app/globals.css`
- Commit: `48092ec8e2316dada87e779e2c5852f256d96aed`
- Scope: scoped redesign of the project dashboard and editor chrome; graphite drafting-instrument surfaces, cyan active/selection accent, a deliberately light technical canvas, clearer status controls, contextual inspector styling, and responsive dashboard cards.
- The change is CSS-only: no new product behavior or JSX restructuring is claimed in this pass.
- **Runtime render verification: NOT VERIFIED.** No before/after browser screenshots or interaction trace have been captured in this environment. The Casa3D Build & Test workflow was still pending when checked. Do not mark the redesign accepted until desktop (1280px), intermediate (1024px) and mobile (375px) renders are inspected, keyboard/focus and contrast are checked, and the create/open/edit/save/reload flow is exercised.
- Remaining design work: inspect actual screenshots, remove any conflicting legacy overrides, and make evidence-led structural changes to the editor layout where the render shows they are necessary. Do not call the current CSS pass a complete or revolutionary redesign yet.


## Follow-up implementation pass — owner rejected generic card-dashboard direction (2026-10-18)

The owner clarified that a modernized palette or a row of standard cards is not sufficient. The next pass now changes actual React structure and the shared visual rules in PR #305:

- `app/app/page.tsx`: added explicit studio/archive/create/settings regions and a brand lockup so the dashboard can use a distinct editorial composition.
- `app/components/Editor.tsx`: named the top bar, workflow navigation, tool rail, spatial canvas and contextual inspector as explicit spatial-studio regions; existing editing handlers and project model remain the source of truth.
- `app/app/globals.css`: replaced the prior generic navy/cyan styling pass with a more specific visual direction: aubergine studio chrome, mineral-paper plan/inspector surfaces, a single oxidised-vermilion brand accent, editorial serif display typography, asymmetrical project archive proportions and non-uniform project silhouettes. The 2D plan remains a calm technical surface; the 3D viewport stays dark and material-focused.
- `GEMINI_4_ARGON_PRO_EDITOR_MASTER_PROMPT.md`: added section 22 as an explicit owner correction rejecting generic SaaS cards, stock palettes and CSS-only facelifts.

### Current verification status

- Repository writes: verified by successful GitHub contents API commits on branch `prompt/casa3d-pro-editor-2026`.
- Runtime screenshots, visual regression, contrast measurement against rendered pixels, keyboard/accessibility checks and end-to-end browser interactions: **NOT VERIFIED** in this pass. Source edits alone do not establish that the design renders correctly or that all existing UI states remain usable.
- Next required gate: run the Casa3D build/test workflow, open the real app in a browser, capture matching before/after states at desktop/intermediate/mobile widths, inspect the result, then repair concrete defects. Do not call the redesign finished until that evidence exists.
