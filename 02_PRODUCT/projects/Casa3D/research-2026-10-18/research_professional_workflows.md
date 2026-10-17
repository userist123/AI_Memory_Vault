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
