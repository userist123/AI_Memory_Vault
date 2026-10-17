---
id: "casa3d-research-professional-workflows-2026-10-18"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, research, interior_design, procurement, interoperability, accessibility]
created: "2026-10-18"
updated: "2026-10-18"
provenance:
  source_type: web
  source_ref: "Official Autodesk, buildingSMART, CIE, W3C and Houzz Pro documentation; compared against Casa3D master prompt and existing gap analysis"
  source_date: "2026-10-18"
  redaction: not_applicable
  provenance_status: complete
confidence: medium
verification: unverified
relations:
  - type: related_to
    target_id: "7f4183b0-9c2a-4e11-8b40-15678a90c1d2"
---

# Casa3D — Research: additional professional workflows worth adding (2026-10-18)

## Executive conclusion

The current Pro Editor Master Prompt is already strong on free-form geometry, editable whole-home concepts, parametric components, preliminary MEP layers, drawing sets, surface setting-out, quantities, revision integrity, security and acceptance tests. Repeating these would inflate the prompt without increasing delivery quality.

The highest-value remaining opportunity is to connect the editor to the **full interior-design project lifecycle** and to strengthen data traceability and interoperability. The existing repository has API routes for choices, orders, site snags, handover and commercial data. Route existence alone does not prove that the complete UI → persistence → authorization → export workflow is usable, so the next agent must audit the existing implementation and extend it rather than creating duplicate modules.

## Existing context that should not be duplicated

- The current master prompt already asks for blank/imported projects, confirmed scale, whole-home styles and room overrides, parametric objects, joinery/cut lists, preliminary electrical/plumbing/HVAC layers, drawing sheets, tile setting-out, quantity calculations, coordination checks and an end-to-end save/reload/export path.
- The current gap analysis already lists client intake, item-level approvals, procurement status, site photos/snags, tasks, payment schedules, joinery cut lists and handover/warranty records as candidate gaps.
- The branch contains route modules for choices, orders, site snags, handover and commercial data. Treat these as evidence of an implementation surface, not evidence that the feature is complete. Inspect the actual domain modules, UI, data model, permissions and tests before classifying a capability.

## Recommended additions, ordered by value

### P0 — Project brief, measured-data provenance and design decisions

Create a structured client/design brief connected to the model, not a free-text note detached from the project:

- client goals, room-by-room needs, preferred/forbidden materials, furniture to retain, storage requirements, household/use constraints, target budget, must-haves and trade-offs;
- distinguish **measured**, **client-supplied**, **imported/detected**, **designer-estimated** and **unknown** dimensions;
- for survey facts, retain source, author, date, unit, confidence/tolerance and confirmation state where useful;
- maintain a decision log linking a design decision to the relevant requirement, affected rooms/objects, selected option, approver, timestamp and revision;
- show unresolved brief conflicts and missing survey data before they silently become model assumptions.

Reason: a flexible editor is useful only if each unique project can preserve its own requirements and uncertainty. The existing scale-confirmation rule is good but does not by itself provide provenance for all dimensions and client decisions.

### P0 — Close the loop between design selections, approval and procurement

Audit the current choice/order/commercial implementation. Where missing, provide:

- product/finish selection boards grouped by room, category or project phase;
- comparable alternatives with verified dimensions, finish, supplier, price/currency timestamp, lead-time/availability provenance and known/unknown fields;
- client approval or rejection at item level, with comments and exact revision binding;
- changing an approved product, size, finish or price creates a visible delta and, where material, requires reapproval;
- a procurement state machine per line item (planned → approved → ordered → partially delivered → delivered → installed/returned/cancelled), with supplier reference, order date, ETA, delivery evidence and issue state;
- supplier-grouped purchase-order/export views, substitutions and estimated-vs-actual costs;
- clear separation of internal cost/margin from client-visible data.

Do not create a second catalog, approval model or procurement database. Reuse existing stable item IDs, revision semantics, authorization rules and APIs.

### P1 — Site visits and spatial punch-list workflow

Audit existing site/snags code and complete the missing end-to-end path if needed:

- place an issue on a specific floor/room/plan location or model object;
- attach multiple photos, notes and timestamps; preserve original evidence metadata;
- classify by trade/finish/severity, assign owner/responsible party and due date, track open → in progress → ready for verification → resolved/reopened;
- retain before/after evidence and a revision/audit trail;
- filter by floor, room, trade, owner and status;
- export a readable PDF field report with project metadata, plan thumbnails, issue references and photos;
- support unreliable mobile connectivity with a safe queued-upload/retry approach only if the current architecture can do so without duplicate records or false “uploaded” states.

A route or database record without a usable phone capture flow, location context, report and tests is only partial.

### P1 — Handover, warranties and maintenance record

Audit the current handover endpoint and domain model. Where incomplete, generate a handover pack from the same canonical project data:

- final issued plans and revision identifiers;
- approved finishes, FF&E/product schedules and installed/substituted products;
- manuals, care instructions, warranty documents/periods and installation dates where supplied;
- outstanding snags, client sign-off/acceptance state and explicit exclusions;
- asset location by level/room, with serial number only when actually provided;
- a maintenance/cleaning schedule when product instructions provide it.

Never fabricate warranty terms or infer installation as completed from an order status.

### P1 — Locale and commercial assumptions as project configuration

Make units, locale, currency, date format, tax/VAT assumptions, price basis, labour rates and waste assumptions explicit project settings with provenance and effective dates. Romanian defaults may be offered when the project locale is Romania, but must not be hard-coded into the domain model or applied silently to projects in other jurisdictions. Keep estimates, supplier quotes, actual invoices and actual paid amounts distinct.

### P2 — Lighting/daylight analysis, not just visual lighting

Add an optional analysis capability only if it can be supported by a real, testable calculation or a clearly labelled qualitative preview:

- capture north/orientation, geographic location when supplied, date/time or annual weather/climate basis, window dimensions/glazing, shading and room geometry;
- distinguish artistic render lighting from daylight/illuminance analysis;
- expose assumptions, data source, method/version, limitations and result status;
- if the simulation engine or required input is absent, show “not evaluated” rather than fabricated lux, daylight autonomy, glare or energy results;
- keep any formal performance or compliance conclusion outside scope unless the methodology, jurisdiction and verification are explicitly implemented.

### P2 — Ergonomics and accessibility checks as configurable rules

Provide a rule-pack architecture for clearances, circulation, door/opening widths, approach space, turning space, reach/access zones and furniture use. Rules must declare jurisdiction/source/version, unit, applicability, severity and whether they are mandatory or advisory. Allow user-specific requirements to override generic defaults without erasing the baseline. Do not label the design accessible or code-compliant merely because a few generic checks pass.

### P2 — Open interoperability with explicit fidelity reporting

Plan a staged open-format strategy rather than claiming universal BIM support:

- retain proven DXF/PDF/CSV/JSON paths and document their exact supported subsets;
- evaluate IFC 4.3 export/import only when the canonical model can map supported entities and properties consistently;
- if IFC is added, state the exact IFC version and Model View Definition/subset; preserve stable IDs, units, levels, materials and supported property sets;
- emit a conversion report for omitted/approximated/unsupported entities and do not silently flatten parametric objects into misleading data;
- consider BCF-style issue exchange and IDS-based model-information validation as later interoperability slices, not as prerequisites for the core editor;
- add fixture-based import/export round-trip tests and malformed-file/security tests.

IFC is a vendor-neutral exchange standard, but implementations are subset/version specific. “Supports IFC” is not a meaningful acceptance claim without a declared subset and demonstrated round-trip behavior.

### P2 — Workshop-ready joinery output

Extend the existing custom joinery/cut-list idea only after the parametric cabinet model is stable:

- panel IDs, dimensions, thickness, material/decor, grain direction, edge-band sides, quantity, hardware and machining notes;
- dimensioned front/side/elevation views and assembly/part labels;
- cutting-list export and optional sheet-nesting/CNC output only if real geometry and manufacturing rules support it;
- distinguish a conceptual parts list from an optimized manufacturing cut plan.

## Suggested implementation order

1. **Audit, do not assume:** map existing brief, choices, orders, site/snags, commercial and handover code to UI, schema, permissions, tests and actual runtime evidence.
2. **Close existing lifecycle gaps:** brief + decision log; item approval and substitution; procurement state and client privacy.
3. **Make field work useful:** spatial photo punch list, report export, then handover/warranty pack.
4. **Add trust and exchange quality:** dimension provenance, configurable locale/commercial assumptions, explicit export fidelity.
5. **Defer high-cost analysis:** daylight simulation, formal accessibility/code packs, full IFC/BIM and CNC nesting until a validated engine and acceptance fixtures exist.

## Required acceptance scenarios for this research addendum

- Create two unrelated projects with different client requirements, units and locales; verify no shared template assumptions leak between them.
- Mark one dimension as measured and another as estimated/unknown; verify provenance survives save/reload and appears in the appropriate survey/drawing report.
- Approve one product, replace it with a different size/price, and verify approval, cost delta and revision semantics.
- Track an order through partial delivery, substitution and installation without confusing these states or exposing private margins.
- Capture a site issue against a room/object, attach photos, assign it, resolve/reopen it and export a PDF report.
- Produce a handover pack that includes only verified product/warranty/installation data and lists outstanding items honestly.
- Verify daylight/accessibility checks return “not evaluated” when inputs/rules are missing.
- Round-trip any newly supported open format and inspect an explicit unsupported-data report.

## Research sources (accessed 2026-10-18)

1. **Autodesk Revit — Schedules:** schedules, quantities, material takeoffs and panel schedules as model-derived views. https://help.autodesk.com/cloudhelp/2026/ENU/Revit-DocumentPresent/files/GUID-F50D6FF4-859E-43A2-A2F6-81C84A1BA0EB.htm
2. **Houzz Pro — Features:** product sourcing, selection boards, item-level client approval, procurement tracking, budgets, schedules, tasks, daily logs and client/team collaboration. Vendor feature descriptions are workflow references, not independent proof of quality. https://pro.houzz.com/for-pros/houzz-pro-features
3. **Houzz Pro — Project Management:** schedules, tasks, daily logs, purchasing and budget management. https://pro.houzz.com/for-pros/feature-project-management
4. **buildingSMART — IFC 4.3 scope:** open exchange coverage across building lifecycle, architecture, services, procurement and project management. https://standards.buildingsmart.org/IFC/RELEASE/IFC4_3/HTML/content/scope.htm
5. **buildingSMART — IFC standards and versions:** the current official IFC 4.3 release and the importance of version-specific implementation claims. https://www.buildingsmart.org/standards/bsi-standards/industry-foundation-classes/
6. **buildingSMART — Information Delivery Specification (IDS):** machine-readable information requirements and validation. https://technical.buildingsmart.org/Projects/Information-Delivery-Specification-Ids/
7. **Autodesk — Model Coordination:** cross-discipline clash detection and issue review as a coordination workflow reference. https://help.autodesk.com/cloudhelp/ENG/Coord-About/files/About_Model_Coord.html
8. **CIE — Daylight:** calculation/checking of interior daylight levels and the need for a defined method. https://cie.co.at/publications/daylight
9. **CIE — Daylight and lighting energy performance:** inputs and methodological boundaries for daylight-related energy analysis. https://www.cie.co.at/publications/light-and-lighting-energy-performance-lighting-buildings-calculation-impact-daylight
10. **W3C — WCAG 2.2:** target size and alternatives to dragging for accessible interaction. https://www.w3.org/TR/WCAG22/

## Confidence and limitations

This is a desk-research synthesis, not a fresh runtime audit of every Casa3D screen. Vendor pages are promotional. The repository's existing gap analysis is useful but is dated 2026-10-11 and itself marks some claims unverified. Before implementation, inspect current code, tests and live UI and update each recommendation to REAL/PARTIAL/MISSING/UNKNOWN with evidence.

## Follow-up research: cross-cutting reliability and trust controls (2026-10-18)

The previous lifecycle research covered missing professional workflows. A further review of official security, accessibility and EU data-protection guidance identifies cross-cutting acceptance criteria that should be explicit before treating Casa3D as a professional client-project tool. These are not automatically missing features; they must be checked against the current branch and marked REAL/PARTIAL/MISSING/UNKNOWN with evidence.

### P0 — Secure import and remote asset sourcing

PDF/raster plans, CAD, GLB/glTF, images and project archives are untrusted input. The OWASP File Upload guidance calls for allowlisted extensions, content-based validation, size limits, generated filenames, authorization and safe storage. OWASP SSRF guidance is relevant to the existing product-URL/remote-asset workflow. Therefore the acceptance contract should cover:
- format allowlists and actual content/signature/parser validation;
- byte, decompressed-size, image-dimension, entity-count and processing-resource limits;
- safe archive extraction, generated internal names and path-traversal prevention;
- server-side authorization for upload/read/write/download;
- URL fetch controls for redirects, DNS/IP resolution, private/internal network targets, protocols and response size;
- explicit import reports for rejected, approximated and unsupported entities;
- malformed/oversized file fixtures and proof that rejection does not mutate a valid project.

Sources:
- OWASP File Upload Cheat Sheet: https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html
- OWASP SSRF Prevention Cheat Sheet: https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html

### P0 — Recoverability and truthful persistence state

The app README and code inventory indicate autosave, revisions, save-conflict handling and a demonstration-mode persistence warning, but those do not by themselves prove full recovery. Test the whole lifecycle: interrupted save, stale write, idempotent retry, corrupted backup, restore into a new project and backward-compatible migration. A backup restore must preserve model relationships, units, provenance, material assignments, quantities and revision history. If infrastructure cannot support cloud backup/offline sync, do not simulate success; document the limitation and provide only a verified export/restore path that the architecture can support.

This is an engineering reliability recommendation grounded in the value and complexity of client project data; it should be treated as a verification gap until tests demonstrate it.

### P1 — Precision-editor accessibility

WCAG 2.2 AA includes a minimum pointer target size criterion and a requirement that dragging functionality have a single-pointer alternative unless dragging is essential. Casa3D already has numeric inspectors and keyboard support in parts of the editor; the gap is whether *all core editing paths* have usable alternatives and work on small screens. Test select → exact dimension edit → move/rotate by keyboard or controls → save, with visible focus, non-colour-only errors, no horizontal overflow and a truthful fallback when WebGL is unavailable. Larger touch targets around 44 CSS px are a useful mobile design target, distinct from WCAG 2.2 AA's 24 CSS px minimum criterion.

Source:
- W3C WCAG 2.2: https://www.w3.org/TR/WCAG22/
- W3C explanation of new WCAG 2.2 criteria: https://www.w3.org/WAI/standards-guidelines/wcag/new-in-22/

### P1 — Client data privacy and lifecycle controls

The European Commission's summary of GDPR principles highlights purpose limitation, data minimisation, storage limitation, accuracy, integrity/confidentiality and accountability. For Casa3D this translates into testable product controls: project-scoped server authorization, revocable revision-bound share links, client-visible projections that exclude internal margin/notes, safe deletion/trash/restore behavior, bounded audit logs, and documented retention/export/deletion behavior for client details, site photos and imported plans. Do not claim legal compliance from a checklist; report the controls implemented and unresolved policy/legal decisions.

Source:
- European Commission — Principles of personal data processing under the GDPR: https://commission.europa.eu/law/law-topic/data-protection/information-business-and-organisations/principles-gdpr_en

### P1 — Performance evidence and graceful degradation

The current prompt requires a real editor and a 2D/3D workflow, but it does not set an evidence-based scaling gate. Build representative project fixtures and record a baseline for editor load, edit latency, save/reload, scene rebuild/resource disposal and behavior when WebGL or AI is unavailable. Define thresholds only after measuring the actual target environment; do not invent performance claims. Manual 2D editing and data persistence should remain usable when optional 3D rendering or AI is unavailable.

### Priority recommendation

1. Verify and harden import/upload/remote fetch paths first, because untrusted client files and product URLs cross a security boundary.
2. Prove recovery and non-destructive conflict behavior for project data.
3. Close core accessibility gaps in precision editing and mobile workflows.
4. Verify project-scoped privacy and client-share projections.
5. Add benchmark-driven performance gates without arbitrary thresholds.

These additions complement, rather than repeat, the existing sections on whole-home design, procurement, site work, handover, daylight, accessibility rule packs and interoperability.



---

## UI/UX flagship redesign and differentiation research (2026-10-18)

### Question and scope

What additional product and interface work would make Casa3D materially more compelling for professional interior designers, beyond the existing Pro Editor Master Prompt and lifecycle addendum? The user explicitly identifies the current UI as visually poor and requests a genuinely next-generation redesign. This is desk research plus source inspection of PR #305; it is not a claim that the app has been visually tested in a running browser.

### Source-level observations from PR #305 branch

- The workspace is a Next.js app at `02_PRODUCT/projects/workspaces/casa3d/app/`, with a central `app/app/globals.css` design-token layer and many domain-specific components.
- The homepage (`app/app/page.tsx`) is a client-rendered project studio that fetches project lists and plan data and exposes filters/sorting, trash/restore and project creation. These are real workflow surfaces that a redesign must preserve.
- The editor (`app/components/Editor.tsx`) composes 2D plan, 3D viewer, design/style, catalog, technical, finishes, budget, survey, orders, commercial and site workflows. A redesign must reduce cognitive load without deleting these capabilities or creating parallel state.
- The existing CSS already declares semantic tokens for a warm-paper background, ink text, borders, accent, semantic status colors and control sizes. The next step should rationalize and extend this system, not layer an unrelated theme on top.
- `02_PRODUCT/projects/Casa3D/NEXTGEN_AUDIT.md` documents a contextual inspector, keyboard camera controls, validation-to-object focus and a command palette. These should be checked in the live rendered UI; source presence alone does not prove visual quality or end-to-end usability.
- No current screenshot or browser-runtime evidence was captured during this research. Therefore the user's visual complaint is an owner requirement, while the precise visual defects remain to be established by baseline captures and interaction testing.

### Confirmed external design principles

1. Apple's HIG layout guidance updated 2026-09-09 recommends a clear visual hierarchy, deliberate alignment and grouping, progressive disclosure and adaptive layouts. This supports a canvas-first editor with a compact tool rail and contextual inspector instead of displaying every control at once. Source: https://developer.apple.com/design/human-interface-guidelines/layout/
2. Apple's design principles updated 2026-06-08 emphasize purpose, user agency, flexibility, simplicity, craft, recovery from mistakes, transparency and clear feedback. This supports preview-before-apply, reversible transactions, honest save states and a polished but unobtrusive interface. Source: https://developer.apple.com/design/human-interface-guidelines/design-principles
3. Apple's panel guidance recommends inspectors that reflect the active selection and warns against large or distracting floating HUD panels. Source: https://developer.apple.com/design/human-interface-guidelines/panels
4. W3C WCAG 2.2 includes minimum target-size guidance and requires alternatives to dragging for applicable operations. This matters directly to a floor-plan editor where object movement, resizing and placement must not depend exclusively on drag-and-drop. Source: https://www.w3.org/TR/WCAG22/
5. Autodesk's 2026 Forma recap describes design review with models, drawings, comments, markups and analysis in a shared workspace, plus continuity between feedback and project data. This supports version-bound review and model-linked decisions as workflow ideas; it is vendor-published product information, not independent usability evidence. Source: https://blogs.autodesk.com/forma/2026/10/14/what-autodesk-forma-announced-au-2026/
6. Chief Architect's feature catalogue documents synchronized 2D/3D editing, scoped object/material painting, model-derived schedules, customizable toolbars/themes and archiving. This supports evaluating synchronized editing, contextual controls and model-derived outputs. Source: https://www.chiefarchitect.com/products/features/

### What is genuinely additive, not already covered

The existing master prompt already covers free-form geometry, whole-home style, components, conceptual technical layers, drawing sets, setting-out, coordination checks, and later lifecycle additions for brief/procurement/site/handover/security. The UI and product differentiators should therefore focus on closing the interaction gap between these capabilities:

- **Flagship visual system:** a documented art direction, canonical tokens, high-quality typography, clear canvas hierarchy, polished icon/control language, intentional motion, and responsive variants, validated with before/after screenshots.
- **Professional editor shell:** command bar, compact tool rail, collapsible resource drawer, selection-aware inspector, status/history strip, split 2D/3D view where useful, and command palette. This must be adapted to actual code rather than imposed as an inflexible template.
- **Design-options lab:** genuine revision-bound A/B comparison with synchronized views, changed objects, known/estimated/unknown cost delta and explicit trade-offs.
- **Spatial requirement traceability:** client brief items linked to room/object/material/product, approval and downstream deliverables.
- **Change-impact map:** identify drawings, quantities, budgets, approvals and procurement records affected by a model change.
- **Client presentation/review:** attractive, accessible and revision-bound client portal, with object-level comments and explicit privacy boundaries.
- **Evidence-based project health:** computed actionable blockers instead of fabricated AI scores or decorative KPIs.
- **Truthful interaction states:** save/sync/import/render/export and AI-provider failures must be recoverable and visibly distinct.
- **Visual QA as a release gate:** capture desktop/laptop/tablet/mobile states and test actual interactions, keyboard, reduced motion, focus, contrast and overflow.

### Recommended execution order

1. Capture and inspect the current UI; record actual defects rather than guessing from source alone.
2. Define and implement one coherent design system; redesign the project dashboard and core editor shell while preserving all domain behavior.
3. Verify key existing flows after redesign: new/open project, 2D/3D synchronization, selection/inspector, exact numeric edits, material application, undo/redo, save/reload, client review and export.
4. Add design-options comparison and model-change impact reporting as a vertical slice, reusing existing revisions/proposals/BOQ/approval models.
5. Add project-to-client decision traceability and a real next-action queue, reusing existing project brief, orders, choices, site issues and handover data.
6. Defer any expensive AI, computer-vision, daylight, AR or full-BIM feature until its inputs, algorithm, data model and acceptance tests are defined. Never add dead UI just to imply capability.

### Acceptance evidence required

- Actual before/after screenshots with viewport dimensions and exact route/state.
- Tested at 1440, 1280, 1024, 768 and 390 CSS-pixel widths, with no page-level horizontal overflow.
- Interaction evidence for core editing, contextual inspector, design preview/apply/reject, save/reload, revision compare, client review and export.
- Keyboard-only operation, visible focus, reduced-motion behavior, contrast/target-size checks, and non-drag alternatives for applicable spatial actions.
- Typecheck, relevant test suite and production build with real exit codes.
- Every feature labelled REAL / PARTIAL / UNKNOWN / MISSING using source plus runtime/test evidence. A polished screenshot alone cannot prove functionality.

### Research limitations

This was bounded desk research and source inspection of the PR branch. Vendor feature pages are marketing material; Apple and W3C sources are design/accessibility guidance, not proof of app quality. No browser screenshot or local runtime test was performed in this research step. The precise current visual defects and the actual usability of each component remain to be verified during implementation.
