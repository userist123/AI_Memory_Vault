---
id: "572eb1de-eac8-4136-8f96-a654943bb517"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, research, matrix]
created: "2026-10-11"
updated: "2026-10-11"
provenance:
  source_type: web
  source_ref: "Web research by a Claude subagent on 2026-10-11; every source URL is cited inline in the body"
  source_date: "2026-10-11"
  redaction: not_applicable
  provenance_status: complete
confidence: medium
verification: unverified
relations:
  - type: related_to
    target_id: "3e458b8c-e0f1-4b06-b0d7-9a3ffae2ae1f"
---
# Casa3D: feature matrix of the interior-design software market (research 2026-10-11)

Scope: the tools a small interior-design studio would compare, split into **business/studio** tools and **design/visual** tools, with what each shipped in 2025-2026.
Method: web search and fetches of vendor feature pages, help centers, release notes and press (2026-10-11). Most sources are written by the vendors, so they describe what each vendor claims. Third-party aggregators are marked "(3p)".
Casa3D column: taken from `02_PRODUCT/projects/Casa3D/GAP_ANALYSIS_2026-10-11.md` (marked G). I did not re-audit the Casa3D code.

**Legend:** `Y` yes · `P` partial/limited · `N` no (only when a source says so) · `?` unknown, nothing found in this pass · `[Sn]` source id (list at the end).
`?*` = widely known to exist but not confirmed by a source in this pass. Do not rely on it until checked.

**Excluded or thin:**
- **Sourcewell:** a procurement co-op, not studio software.
- **Minimal ID:** no product pages found, so there is no column.
- **Ivy:** legacy. Houzz no longer offers new memberships or feature updates and moves users to Houzz Pro [S28]. Ivy is not scored.

---

## 1. Business / studio tools

Columns: **C3D** Casa3D · **HZ** Houzz Pro · **PG** Programa · **MD** Mydoma Studio · **DF** DesignFiles · **SD** Studio Designer · **GA** Gather · **FO** Fohlio · **DM** Design Manager · **MB** Material Bank

| Group | Feature | C3D | HZ | PG | MD | DF | SD | GA | FO | DM | MB |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Survey | LiDAR/AR room scan on phone | ? | Y [S2][S4] | ? | ? | ? | ? | ? | ? | ? | ? |
| Survey | 2D plan / image → editable 3D plan (AI) | ? | Y [S3][S7] | ? | ? | ? | ? | ? | ? | ? | ? |
| Survey | AI takeoffs (areas/lengths from plans) | ? | P [S3] (desktop, simple plans) | ? | ? | ? | ? | ? | ? | ? | ? |
| Drawing | 2D/3D floor planner | Y G | Y [S1] | N [S8] (SketchUp link only [S10]) | ? | Y [S17][S18] | ? | ? | ? | ? | ? |
| Drawing | Elevations | Y G | Y [S5] | ? | ? | P [S18] (elevation tutorials) | ? | ? | ? | ? | ? |
| Drawing | Ceiling design | Y G | Y [S4] | ? | ? | ? | ? | ? | ? | ? | ? |
| Drawing | Dimensioned trade drawings / PDF drawing set / DXF | Y G | ? | ? | ? | ? | ? | ? | ? | ? | ? |
| Kitchen | Cut list / joinery drawings | N G | ? | ? | ? | ? | ? | ? | ? | ? | ? |
| Materials | Finish schedule | Y G (per room) | ? | Y [S8] (schedules) | ? | ? | P [S21] (beta) | Y [S24] | ? | Y [S26] (specs) | ? |
| Render | 3D render | Y G (Blender) | Y [S1] | ? | ? | Y [S17] | ? | ? | ? | ? | ? |
| Render | AI render / enhance | ? | P [S3] (AI mood boards) | ? | ? | Y [S19] | Y [S21] (board→room, Aug 2026) | ? | ? | ? | ? |
| Render | Shared 3D model in client portal | P G (link) | ? | Y [S10] (SketchUp via Trimble Connect, 2026) | ? | ? | ? | ? | ? | ? | ? |
| Docs | Spec sheets / tear sheets | ? | Y [S4] | Y [S8] | ? | ? | Y [S22] (spec reports) | Y [S24] | ? | Y [S26] | P [S27] (spec downloads) |
| Docs | Revisions / issue tracking of specs | ? | ? | P [S9] (planned Q3 2026) | ? | ? | ? | Y [S24] | Y [S25] (approval snapshots) | ? | ? |
| Client | Client portal | Y G (link, no account) | Y [S1] | Y [S8] | Y [S12] | Y [S17] | Y [SD-home] | P [S23] (optional) | ? | Y [S26] | ? |
| Client | Approve/reject each item with comments | N G | Y [S1] | Y [S8] | P [S12] | ? | Y [S22] (proposal approval from PDF) | Y [S24] | P [S25] | ? | ? |
| Client | Comments on documents / 3D | P G (per room) | Y [S4][S5] | Y [S8] | ? | ? | ? | ? | ? | ? | ? |
| Client | Mood boards | ? | Y [S1] | Y [S8] | Y [S12] | Y [S17] | Y [S21] | ? | ? | ? | Y [S27] (Boards) |
| Client | Presentations module | Y G (16:9) | Y [S1] (selection boards) | Y [S9] (May–Jun 2026) | ? | ? | Y [S21] | ? | ? | ? | ? |
| Client | Intake questionnaire | N G | ? | ? | Y [S14] | Y [S17] | ? | ? | ? | ? | ? |
| Sourcing | Web clipper | N G | Y [S1] | Y [S8] | Y [S12] | Y [S17] | Y [SD-home] | Y [S24] | ? | Y [S26] | n/a |
| Sourcing | AI clipper (auto field extraction) | N | Y [S3] | Y [S8] ("Smart Fill") | ? | ? | ? | ? | ? | ? | n/a |
| Sourcing | Product library / reusable catalog | ? | Y [S1] | Y [S8] | Y [S12] | ? | Y [S21] (Catalog w/ dimension filters) | Y [S24] | Y [S25] | ? | Y [S27] |
| Sourcing | Physical sample ordering | N | ? | ? | ? | ? | ? | ? | ? | ? | Y [S27] (next-day) |
| Sourcing | AI / visual material search | N | ? | Y [S9] (Smart Search) | ? | ? | ? | ? | ? | ? | Y [S27b] |
| Money | Proposals / quotes | Y G (estimate) | Y [S1] | Y [S8] | P [S12] | P [S17] (Quotes, FS plan) | Y [SD-home] | Y [S24] | ? | ? | n/a |
| Money | Estimate with waste/labour ranges | Y G | P [S3] (AI estimates) | ? | ? | ? | ? | ? | ? | ? | n/a |
| Money | Invoices | N G | Y [S1] | Y [S8] | Y [S12] | Y [S17] (FS only) | Y [SD-home] | ? | P [S25] | ? | n/a |
| Money | Online payments | N G | Y [S1] | Y [S8] | Y [S12] (MyDoma Pay) | Y [S17] | Y [S22] (StudioPay, branded) | ? | ? | Y [S26] | n/a |
| Money | Retainers / payment schedules | N G | Y [S2] | ? | ? | Y [S17] | ? | ? | ? | ? | n/a |
| Money | Time tracking | N G | Y [S1][S2] | Y [S8] | Y [S13] | Y [S17] (FS) | Y [S21][S22] (redesigned 2026) | ? | ? | ? | n/a |
| Money | Change orders | N | Y [S5][S6] | ? | ? | ? | ? | ? | ? | ? | n/a |
| Money | Contracts + e-signature | N G | Y [S6] | P [S9] (contracts planned Q3 2026) | Y [S14] | Y [S17] (contracts) | ? | ? | ? | ? | n/a |
| Money | Local e-invoicing (RO e-Factura) | N G | N G | N G | N G | N G | N G | ? | ? | ? | n/a |
| Purchasing | Purchase orders | N G | Y [S1] | Y [S8] | Y [S12] | Y [S17] (FS) | P [S21] | Y [S24] | Y [S25] | Y [S26] | n/a |
| Purchasing | Line status ordered→delivered→installed | N G | Y [S4] (Project Tracker) | Y [S8] | ? | Y [S17] (Product Tracker) | Y [SD-blog Oct 2026] | Y [S24] | Y [S25] | ? | n/a |
| Purchasing | Budget vs quote vs actual | P G | ? | P [S9] (profitability planned) | ? | ? | Y [SD-home] (reports) | Y [S24] | Y [S25] | ? | n/a |
| Purchasing | PO ↔ supplier invoice reconciliation | N | ? | ? | ? | ? | Y [SD-home] (accounting) | ? | Y [S25] | Y [S26] | n/a |
| PM | Tasks with due dates | N G | Y [S1] | Y [S8] | Y [S12] | Y [S17] | ? | ? | ? | Y [S26] | n/a |
| PM | Gantt / timeline | N | Y [S4] | Y [S8] | ? | ? | ? | ? | ? | ? | n/a |
| PM | Daily logs / site journal | N G | Y [S2] | ? | ? | ? | ? | ? | ? | ? | n/a |
| PM | "Pulse" / overdue alerts | N | ? | Y [S8][S9] | ? | ? | ? | ? | ? | ? | n/a |
| Collab | Team permissions | ? | Y [S4] | Y [S8] | ? | Y [S17] (+users) | ? | Y [S24] | ? | ? | ? |
| Collab | Share with contractors / suppliers | ? | Y [S4] | P [S9] (builder/supplier views planned) | ? | ? | ? | Y [S24] (GCs, workrooms) | ? | ? | ? |
| Integr. | QuickBooks | N | Y [S1][S4] | Y [S8] | Y [S12] | Y [S17] (FS) | P [SD-home] (own accounting) | ? | ? | Y [S26] (own accounting) | n/a |
| Integr. | Xero | N | ? | Y [S8][S11] | N [S12] | ? | ? | ? | ? | ? | n/a |
| Integr. | Payroll | N | P [S5] (Gusto, early 2026) | ? | ? | ? | Y [SD-home] (Gusto) | ? | ? | ? | n/a |
| Integr. | Calendar / scheduling | ? | ? | ? | P [S14] (Calendly/Acuity, not Google) | ? | ? | ? | ? | ? | n/a |
| Integr. | Revit / BIM | N | ? | ? | ? | ? | ? | P [S24] (demo) | Y [S25] | ? | ? |
| Integr. | Public API / MCP for AI agents | ? | ? | ? | P [S14] (Zapier) | ? | ? | Y [S24] (API + MCP) | ? | ? | ? |
| Mobile | Native mobile app | N G (390 px issues) | Y [S2] | ? | Y [S13] | P [S17] | P [S22] (time tracking app) | ? | ? | Y [S26] (iPhone) | ? |
| Mobile | Offline | ? | Y [S2] | ? | ? | ? | ? | ? | ? | ? | ? |
| AI | AI features (note only) | (none, by policy) | Y [S3] (10 features) | Y [S9] (meeting notes, search; more planned) | ? | Y [S19] | Y [S21] | Y [S24] (agents via MCP) | ? | ? | Y [S27b] |

---

## 2. Design / visual tools (A)

Columns: **SU** SketchUp + LayOut · **CA** Chief Architect / Home Designer · **CE** Cedreo · **FY** Foyr Neo · **CO** Coohom · **HS** Homestyler · **P5** Planner 5D · **RS** RoomSketcher

| Group | Feature | C3D | SU | CA | CE | FY | CO | HS | P5 | RS |
|---|---|---|---|---|---|---|---|---|---|---|
| Survey | LiDAR / AR scan → plan | ? | P [S29] (Scan Essentials point clouds) | Y [S32] (As-Built app, X18) | ? | ? | ? | P [HS3p] (3p) | Y [S41] (Scan Room + video Home Scan) | ? |
| Survey | 2D plan import → editable (AI convert) | ? | ? | ? | P [S34] (import + trace JPG/PDF/DWG/DXF) | ? | Y [S38] (plan upload) | P [S40] (image background trace) | Y [S41] (AI plan recognition) | Y [S42] (AI Convert, Dec 2025) |
| Drawing | Walls/doors/windows 2D↔3D | Y G | Y | Y [S32] | Y [S34] | Y [S37] (3p) | Y [S38] | Y [S40] | Y [S41] | Y [S42] |
| Drawing | Elevations | Y G | ?* | Y [S32] | ? | ? | Y [S39] (auto drawings) | ? | ? | ? |
| Drawing | Ceiling plan | Y G | ? | ? | ? | ? | ? | Y [S40] (custom ceiling) | ? | ? |
| Drawing | Room area / surface lists | Y G | ? | Y [S32] (materials list m²) | Y [S34] | ? | ? | ? | ? | ? |
| Kitchen | Parametric cabinets / closets | Y G (finishes) | P [S29] (Live Components) | Y [S32] | P [S34] (planner pages) | ? | Y [S38][S39] | Y [S40] | ? | P [S42] (closet items 2026) |
| Kitchen | Cut list / BOM / CAM export | N G | ? | Y [S32] (cabinet hardware schedule) | ? | ? | Y [S39] (BOM, CAM links; enterprise) | ? | ? | ? |
| Lighting | Electrical symbols / plan | Y G | ? | Y [S32] (light sets, circuits) | Y [S34] | ? | ? | ? | ? | Y [S42] (170+ symbols, Apr 2026) |
| Lighting | Light colour temperature (K) | ? | ? | Y [S32] (X18) | ? | Y [S37] (AI lighting, 3p) | ? | ? | ? | ? |
| Lighting | Sun/daylight analysis | ? | Y [S30] (Analysis Hub, 2026.2) | Y [S32] (sun/moon) | Y [S34] (sun orientation) | ? | ? | ? | ? | ? |
| Materials | Material editor / library | Y G | Y [S29] | Y [S32] | Y [S34] | Y [S37] | Y [S38] | ? | ? | Y [S42] (Replace Materials) |
| Render | Photoreal render | Y G (Blender) | P (via plugins: D5/Enscape) | Y [S33] (ray trace, HD 2026) | Y [S34] (~5 min) | Y [S37] (4K/12K, 3p) | Y [S38] | Y [S40] | Y [S41] | Y [S42] |
| Render | AI render / style | N (policy) | Y [S31] (AI Render) | ? | Y [S35] (AI styles/night, 2026) | Y [S37] (3p) | Y [S39] (Coohom AI, Jun 2026) | Y [S40] | Y [S41] | Y [S42] (Jun + Aug 2026) |
| Render | 360 panorama | ? | ? | ? | ? | Y [S37] (3p) | Y [S39] (720 tour, Panorama Editor) | Y [S40] (4K) | ? | Y [S42] (3D Photos) |
| Render | Video walkthrough | ? | ? | ? | ? | Y [S37] (3p) | ? | Y [HS3p] (3p) | Y [S41] (MP4) | ? |
| Render | VR | ? | ? | ? | ? | ? | P [S38] (VR tours) | ? | ? | ? |
| Render | Browser 3D share link | Y G | Y [S29] (Trimble Connect share, 2026.0) | ? | ? | Y [S37] (360 links) | ? | ? | ? | Y [S42] (Live 3D, mobile 2026) |
| Docs | Sheets / title blocks | Y G (PDF dossier) | Y [S29] (LayOut) | ?* | P [S34] (to-scale plans w/ callouts) | ? | Y [S39] (DWG/PDF drawings) | ? | ? | ? |
| Docs | 2D drafting tools | ? | Y [S29] (Trim/Extend/Fillet/Chamfer) | Y [S32] | ? | ? | ? | ? | ? | ? |
| Docs | Auto schedules (doors/windows/cabinets) | ? | ? | Y [S32] | ? | ? | ? | ? | ? | ? |
| Client | Branded client presentation | Y G | P (LayOut) | ? | Y [S34] | Y [S37] | ? | ? | ? | ? |
| Client | Comments on model | P G | Y [S29] (viewers comment) | ? | ? | ? | Y [S39] (real-time feedback) | ? | ? | ? |
| Sourcing | Real-brand product catalog | N G | P (3D Warehouse) | Y [S33] (catalog downloads) | Y [S34] (8,500 items) | Y [S37] (60k, 3p) | Y [S38] (600k models) | Y [S40] (retail integration preview) | Y [S41] | Y [S42] |
| Money | Estimate / BOM from design | Y G | ? | Y [S32] (materials list) | ? | ? | Y [S39] | ? | ? | ? |
| Collab | Team accounts | ? | Y [S29] | ? | Y [S36] (Enterprise real-time collab) | ? | Y [S39] | ? | ? | Y [S42] (Team user mgmt, Mar 2026) |
| Mobile | Mobile/iPad app | P G | Y [S31] (iPad) | Y [S32] (As-Built) | ? | ? | ? | Y [HS3p] | Y [S41] | Y [S42] |
| Export | DWG/DXF export | Y G (DXF) | Y [S29] | ? | ? (import only confirmed) | ? | Y [S39] | ? | Y [S41] (PDF/DXF/DWG, 3p) | ? |
| Export | IFC / Revit | ? | Y [S29][S30] (IFC export, Revit import) | ? | ? | ? | ? | ? | ? | ? |
| AI | AI layout / design generation | N (policy) | ? | ? | ? | ? | Y [S39] (AI Layout, AI Instant) | Y [S40] (Home Copilot, text→3D) | Y [S41] (Smart Wizard) | ? |
| AI | Image → 3D object | N | ? | ? | ? | ? | ? | Y [S40] (AI Modeler) | ? | ? |

## 3. Design / visual tools (B)

Columns: **FP** Floorplanner · **LH** Live Home 3D · **VW** Vectorworks (Interiors in Architect) · **20** 2020 Design Live · **MP** magicplan · **PC** Polycam · **D5** D5 Render · **EN** Enscape · **TM** Twinmotion

| Group | Feature | FP | LH | VW | 20 | MP | PC | D5 | EN | TM |
|---|---|---|---|---|---|---|---|---|---|---|
| Survey | LiDAR / AR scan → plan | ? | P [LH-press] (AR measuring, older) | ? | ? | Y [S47] (AR scan, switches/outlets) | Y [S48] (Space Mode, Feb 2025) | n/a | n/a | n/a |
| Survey | Site photos / 360 on plan | ? | ? | ? | ? | Y [S47] (360 + photo reports, Oct 2026) | Y [S48] (Spatial Report PDF) | n/a | n/a | n/a |
| Survey | Damage / affected areas markup | ? | ? | ? | ? | Y [S47] (2025.35) | ? | n/a | n/a | n/a |
| Drawing | 2D floor plan + 3D | Y [S43] | Y [S44] | Y [S45] | Y [S46] | Y [S47] | Y [S48] | n/a | n/a | n/a |
| Kitchen | Manufacturer cabinet catalogs | ? | ? | ? | Y [S46] | ? | ? | n/a | n/a | n/a |
| Kitchen | BOM / estimates from design | ? | ? | Y [S45] (worksheets) | Y [S46] | Y [S47] (estimator) | Y [S48] (CSV per room) | n/a | n/a | n/a |
| Lighting | Lighting control in render | ? | ? | ? | ? | ? | n/a | Y [S49] | Y [S50] (light mgmt in Enscape 2026) | ? |
| Materials | AI material from photo | ? | ? | Y [S45] (Architextures textures, Update 6) | ? | ? | ? | ? | Y [S50] (2026) | ? |
| Render | Photoreal / ray trace | Y [S43] (up to 8K, credits) | Y [S44] (Cycles) | Y [S45] (Redshift) | Y [S46] | n/a | n/a | Y [S49] | Y [S50] | Y [S51] |
| Render | AI render / enhancer | ? | ? | Y [S45] (AI Visualizer) | ? | n/a | n/a | Y [S49] (15+ AI features, 3.0 Jan 2026) | Y [S50] (AI Enhancer, Veras) | ? |
| Render | 360 panorama | ? | ? | ? | Y [S46] (multi-point) | Y [S47] | n/a | Y [S49] | Y [S50] | Y [S51] |
| Render | Video | ? | ? | ? | ? | n/a | Y [S48] | Y [S49] | Y [S50] | Y [S51] |
| Render | VR | ? | P [S44] (Vision Pro app) | ? | ? | n/a | n/a | P [S49] (VR tours) | Y [EN-3p] | Y [S51] (stereo 3D panoramas) |
| Render | Web viewer / standalone | Y [S43] (browser) | ? | ? | Y [S46] (any device) | ? | Y [S48] | ? | Y [S50] (web standalone, .exe) | Y [S51] (Twinmotion Cloud) |
| Docs | Worksheets / schedules | ? | ? | Y [S45] (Worksheet Slicing) | ? | Y [S47] (stats export) | Y [S48] | n/a | n/a | n/a |
| Docs | Depth-cued elevations/sections | ? | ? | Y [S45] | ? | ? | ? | n/a | n/a | n/a |
| Docs | PDF report with photos/forms | ? | ? | ? | ? | Y [S47] | Y [S48] | n/a | n/a | n/a |
| Collab | Cloud/team | ? | ? | Y [S45] (cloud Revit/IFC import) | Y [S46] (cloud catalogs) | Y [S47] (desktop+mobile) | Y [S48] (Scene Editor, Business seats) | ? | ? | Y [S51] |
| Integr. | Xactimate / insurance | n/a | n/a | n/a | n/a | Y [S47] | Y [S48] | n/a | n/a | n/a |
| Integr. | SketchUp live link | n/a | n/a | n/a | n/a | P [S47] (DXF/IFC for SketchUp) | ? | Y [S49] (LiveSync) | Y [S50] (plugin) | Y [S51] (camera sync) |
| Mobile | Mobile app | ? | Y [S44] (iOS/visionOS) | ? | ? | Y [S47] | Y [S48] | n/a | n/a | P [S51] (VCam phone) |
| Export | DXF/DWG | ? | ? | Y [S45] | ? | Y [S47] (DXF w/o dims) | Y [S48] (DXF, Business) | n/a | n/a | n/a |
| Export | IFC | ? | ? | Y [S45] | ? | Y [S47] | N [S48] (not listed) | n/a | n/a | n/a |
| Import | Gaussian splats / point clouds | ? | ? | ? | ? | ? | Y [S48] | P [S49] (experimental splats) | ? | ? |
| AI | Image/text → 3D asset | ? | ? | ? | ? | ? | ? | Y [S49] (image→3D) | ? | ? |

---

## 4. What shipped recently (2025-2026), by vendor

- **Houzz Pro:**
  - April 2025: AutoMate AI turns a 2D plan into an editable 3D plan [S7].
  - In Houzz's AI help page (undated): a 2D product image becomes a 3D model, plus the AI Clipper [S3].
  - Listed in the 22 Jan 2026 "what's new" webinar [S5]: Contracts with e-signature (signed in about 2.55 days on average, Q2 2025 data [S6]), Change Orders for designers, Elevations and Placeholders, document commenting, and an Open Invoices report.
  - Gusto payroll was announced for early 2026 [S5].
  - Ten AI features across finance, scheduling, daily logs, takeoffs, the clipper, selection boards, mood boards, expenses and messages [S3].
- **Programa:**
  - Presentations module (May/June 2026, 80+ updates since).
  - AI Meeting Notes and Smart Search [S9].
  - SketchUp models shown in client dashboards through Trimble Connect [S10].
  - Planned for Q3 2026 [S9]:
    - Schedules 3.0 with revisions, views for builders and suppliers, and a power editor;
    - contracts, retainers and profitability;
    - procurement and delivery;
    - natural-language AI.
- **Studio Designer** (it acquired Mydoma in July 2024 [S16]):
  - July 2026 [S22]: branded checkout, proposal approval from a PDF, redesigned time tracking with a mobile app, and manufacturer specs in the Catalog.
  - August 2026 [S21]: Catalog with dimension and colour filters, Design Boards with AI room rendering (30–45 s, not CAD-accurate), Timesheets, and Finish Schedules (beta).
- **Mydoma:** the last public release notes are from version 3.23.0 (August 2024) [S15]. No 2025-2026 notes were found.
- **DesignFiles:**
  - AI render/enhance on mood boards and 3D renders, paid with credits [S19].
  - The plans page lists sub-tasks and a Kanban board as new features [S17].
- **Gather:**
  - New FF&E classification system (date not given).
  - API and MCP connector so AI agents can work with Gather [S24].
- **Design Manager:**
  - Bought DesignSpec and Lojik (Spexx and Exxpedite) in October 2024 (3p search result).
  - No 2025 notes were found.
- **SketchUp:**
  - 2026.0 (October 2025) [S29]: LayOut drafting tools and a SketchUp-style interface on Windows, live model sharing with comments, better DWG/IFC handling, and Scan Essentials mesh tools.
  - 2026.2 (May 2026) [S30]: Analysis Hub (daylight, sDA/ASE) and Advanced Shadows.
  - AI Render on iPad [S31].
- **Chief Architect X18 and Home Designer 2026:**
  - As-Built scan/draw app [S32].
  - Kelvin lighting, light sets and a cabinet hardware schedule [S32].
  - Real-time ray tracing in Home Designer 2026 (August 2025), which moved to subscription only [S33].
- **Cedreo:** AI render styles and an AI night effect (2026 blog) [S35].
- **Coohom:**
  - 5.0 for all users (January 2025) [S39].
  - Coohom AI with AI Instant and AI Layout (June 2026) [S39].
  - Panorama Editor opened to paid users [S39].
- **Homestyler** (2025): Home Copilot chat, AI Modeler (image→3D), and text→3D space (preview) [S40].
- **RoomSketcher** [S42]:
  - AI Convert and a new Web Portal (December 2025).
  - 170+ electrical symbols (April 2026).
  - Team user management and Live 3D on mobile (March 2026).
  - AI Render (June 2026), with styles and prompts added in August 2026.
- **Vectorworks 2026** (September 2025) [S45]:
  - Depth cueing, Worksheet Slicing, a unified Render Style and cloud Revit/IFC import.
  - AI Visualizer upgrades (Update 2).
  - Architextures textures (Update 6, July 2026).
- **magicplan** [S47]:
  - Affected Areas (2025), Insta360 support and mandatory custom fields.
  - New magicplan (9 October 2026): photo and 360 galleries with reports, file management with PDF markup, and multiple estimates.
- **Polycam:** Vision 25 (February 2025) added Space Mode with vectorized plans and auto-tagged objects, the Scene Editor and an AI Spatial Report [S48].
- **D5 Render 3.0** (January 2026) [S49]: AI Agent, image→3D, asset recommendation, Gaussian splat import (experimental), and D5 Lite for SketchUp.
- **Enscape** (Spring 2026) [S50]: upgraded AI Enhancer, Veras on a new engine, AI materials from photos, and light management inside Enscape.
- **Twinmotion 2025.2** (30 September 2025) [S51]: Nanite, parallax windows (fake interiors), sketch/painterly FX and exploded views.
- **Live Home 3D:**
  - Cycles ray tracing (Windows 4.9.x) [S44].
  - visionOS 26 support and 300+ interior materials [S44].

---

## 5. Features most competitors have that Casa3D lacks

This cross-checks the gap note against the matrix. The "who has it" counts only include tools where a source confirms the feature.

| # | Missing in Casa3D | Who has it (sourced) | Matches the gap note? |
|---|---|---|---|
| 1 | Web clipper (product from a shop link) | HZ, PG, MD, DF, SD, GA, DM: 7/9 business tools | Yes (priority 5) |
| 2 | Client approval per item with comments | HZ, PG, SD, GA (+P: MD, FO) | Yes (priority 4) |
| 3 | Purchase orders + line status to delivery | HZ, PG, MD, DF, GA, FO, DM (+SD tracking) | Yes (priority 2) |
| 4 | Invoices + online payments | HZ, PG, MD, DF, SD, DM | Yes (later; e-Factura) |
| 5 | Time tracking | HZ, PG, MD, DF, SD | **Not in the gap note.** Add it as low cost and on mobile. |
| 6 | Tasks with due dates / Gantt | HZ, PG, MD, DF, DM | Yes (priority 6) |
| 7 | Contracts + e-signature | HZ, MD, DF (+PG planned) | Yes (later) |
| 8 | Accounting sync (QuickBooks/Xero) | HZ, PG, MD, DF, DM/SD (own) | Partial. For RO the equivalent is SmartBill/Oblio, which the gap note lists. |
| 9 | Mood boards as a first-class object | HZ, PG, MD, DF, SD, MB | **Not explicit in the gap note.** It only mentions "Concept". Check whether Casa3D's Concept stage is a real board. |
| 10 | Native mobile app (logs, photos, approvals, offline) | HZ (offline), MD, DM, magicplan, Polycam | Yes (priority 0, phone) |
| 11 | Phone room scan (LiDAR/AR) → plan | HZ, Chief As-Built, Planner 5D, magicplan, Polycam | Mentioned only as a phone pattern. Not a priority in the gap note. |
| 12 | Import of a 2D plan (image/PDF) → editable plan | HZ, RoomSketcher, Planner 5D, Coohom, Cedreo (trace) | **Not in the gap note.** A trace-over-image tool needs no AI (Cedreo/Homestyler style). |
| 13 | 360 panorama / shareable walkthrough | Coohom, Homestyler, Foyr, 2020, D5, Enscape, Twinmotion, magicplan | **Not in the gap note.** Casa3D has a browser 3D view, which may cover part of this. |
| 14 | Change orders | HZ | Not in the gap note. Relevant during the build phase. |
| 15 | Questionnaire / intake | MD, DF | Yes (priority M/S) |
| 16 | Daily logs / site journal | HZ | Yes (priority 3, site) |
| 17 | Cut list / cabinet BOM | Coohom, Chief (hardware schedule) | Yes (later) |

Where Casa3D is already ahead, as the matrix confirms: none of the nine business tools has a sourced dimensioned trade-drawing set (survey, ceiling, floors, elevations, DXF, PDF dossier) or an estimate with waste and labour ranges. Houzz is the only business tool that sources floor plans plus elevations. Programa has no drawing at all and links SketchUp instead.

## 6. Differentiators nobody does well (opportunities)

1. **One flow from survey to trade drawings to FF&E to purchasing to handover.**
   - Business tools stop at the floor plan or mood board. Design tools stop at drawings and renders.
   - Only Houzz Pro spans both, and its drawings stop at the 3D planner plus elevations.
   - Casa3D already has the drawing half.
2. **Handover / maintenance dossier** (products, warranties, care).
   - No source showed it in any tool. The gap note reached the same conclusion.
3. **Snag (punch) list inside a design tool.**
   - Only Houzz has daily logs, and magicplan has "affected areas" markup.
   - None of the design tools has a remediation list with photos per room and a PDF report.
4. **Local e-invoicing and RO/EU tax.**
   - Every vendor is US/UK/AU-centric. Studio Designer only added Quebec tax in July 2026 [S22].
   - None mentions EU e-invoicing, which is a moat in Romania.
5. **Joinery for the small studio.**
   - Cut lists and CAM exist only in enterprise kitchen tools (Coohom BOM enterprise-only [S39], 2020).
   - A simple PAL/MDF cut list tied to the room model is an open niche.
6. **Accurate drawings and client-friendly presentation in the same link.**
   - Programa needed SketchUp + Trimble Connect to show a model next to the schedule [S10].
   - Casa3D's client link plus drawings can do this natively.
7. **Phone-first site capture tied to the design model.**
   - Scanners (Polycam, magicplan) produce plans but are not design/FF&E tools.
   - Studio tools have no scanning, except Houzz.
   - Linking site photos to rooms in the plan is rare: magicplan does it, but not for design.
8. **Spec revisions with change marks for interiors.**
   - Only Gather and Fohlio (commercial FF&E) have them. Programa plans them for Q3 2026.
   - Residential tools lack them.
9. **Open data / agent access.**
   - Only Gather advertises an API plus MCP [S24].
   - An export/API of the project model (rooms, items, quantities) would be a differentiator, even without Casa3D calling AI itself.
10. **Honest render vs. accurate drawing.**
    - AI renders are spreading: Studio Designer, DesignFiles, RoomSketcher, Cedreo, D5, Enscape.
    - Studio Designer itself says its renders are "not CAD-accurate" [S21].
    - A deterministic, dimension-true visual (Blender from the real model) is a trust differentiator.

## 7. The 15 most significant recent releases (2025-2026)

No source measured which features designers request most. The ranking below is my judgement of relevance to small studios, based on how many vendors converge on the same feature.

1. **AI 2D-plan → editable 3D plan:** Houzz AutoMate (April 2025) [S7], RoomSketcher AI Convert (December 2025) [S42], Planner 5D [S41].
2. **AI renders from boards or 3D views:** Studio Designer (August 2026) [S21], RoomSketcher AI Render (June and August 2026) [S42], DesignFiles [S19], Cedreo [S35].
3. **Built-in contracts with e-signature:** Houzz Pro [S5][S6].
4. **AI clipper (auto-extract product data):** Houzz [S3], Programa Smart Fill [S8].
5. **Presentations module:** Programa (2026) [S9], Studio Designer Design Boards [S21].
6. **Phone scan → plan:** Chief As-Built (X18) [S32], Polycam Space Mode (February 2025) [S48].
7. **Redesigned time tracking with a mobile app:** Studio Designer (July 2026) [S22].
8. **Proposal approval from the PDF on mobile, and branded checkout:** Studio Designer [S22].
9. **Change orders for designers:** Houzz [S5].
10. **3D model inside the client dashboard:** Programa × SketchUp (2026) [S10].
11. **Product catalog with dimension filters and full manufacturer specs:** Studio Designer (July and August 2026) [S21][S22].
12. **Schedules with revisions and supplier/builder views:** Programa Schedules 3.0 (planned Q3 2026) [S9].
13. **Live model sharing with comments:** SketchUp 2026.0 [S29].
14. **Site photos, 360 and reports in a survey app:** magicplan (October 2026) [S47].
15. **Image→3D object for custom pieces:** Houzz [S3], Homestyler [S40], D5 3.0 [S49].

---

## Sources

- **S1** Houzz Pro for interior designers: https://pro.houzz.com/for-pros/software-interior-designer
- **S2** Houzz Pro app help: https://pro.houzz.com/pro-help/r/how-do-i-use-the-houzz-pro-app
- **S3** Houzz Pro AI help: https://pro.houzz.com/pro-help/r/how-to-use-ai-in-houzz-pro
- **S4** Houzz Pro release (December 2023): https://pro.houzz.com/pro-learn/blog/the-houzz-pro-for-interior-designers-just-got-better
- **S5** Houzz "What's New" webinar (22 January 2026): https://www.bigmarker.com/conferences/fd1e42d80cfa/reviews/new
- **S6** Houzz Contracts: https://pro.houzz.com.au/for-pros/feature-contracts
  - also https://www.tileletter.com/houzz-pro-introduces-contracts-tool-for-faster-smarter-project-agreements-in-one-place/
  - change orders: https://pro.houzz.co.uk/pro-help/r/creating-and-sending-change-orders
- **S7** Houzz AutoMate press release (April 2025): https://www.constructiondive.com/press-release/20250415-houzz-pro-unveils-ai-powered-3d-floor-plan-features-to-enhance-the-pre-cons-1
- **S8** Programa features: https://programa.design/features
- **S9** Programa roadmap (June 2026): https://programa.design/blog/roadmap-schedules-3-0-studio-ops-and-ai
- **S10** Programa × SketchUp: https://programa.design/blog/sketchup-x-programa-integration
- **S11** Programa on Xero: https://apps.xero.com/app/programa
- **S12** Mydoma features: https://www.mydomastudio.com/features
- **S13** Mydoma time tracking and mobile: https://www.mydomastudio.com/features/time-tracking/
- **S14** Mydoma review (3p): https://softwareadvice.com/project-management/mydoma-studio-profile
  - JotForm questionnaire: https://mydomastudio.com/blog/tech-tip-tuesday-using-a-jotform-template-for-you-questionnaire/
- **S15** Mydoma NEXT 3.23.0: https://help.mydomastudio.com/en/articles/9777911-mydoma-next-updates-3-23-0
- **S16** Studio Designer acquires Mydoma: https://www.businesswire.com/news/home/20240725985972/en
  - also https://betakit.com/interior-design-project-management-platform-mydoma-acquired-by-californias-studio-designer/
- **S17** DesignFiles plans: https://s.designfiles.co/plans
- **S18** DesignFiles floor plan software: https://designfiles.co/features/floor-plan-software/
- **S19** DesignFiles AI Renders: https://designfiles.co/features/ai-render/
- **S20** DesignFiles blog (2026): https://blog.designfiles.co/ai-rendering-software/
- **S21** Studio Designer August 2026 release: https://www.studiodesigner.com/blog/studio-designer-august-2026-release/
- **S22** Studio Designer July 2026 release: https://www.studiodesigner.com/blog/studio-designer-july-2026-release/
- **SD-home** Studio Designer home page: https://www.studiodesigner.com/
- **SD-blog** Studio Designer item status post (October 2026): https://www.studiodesigner.com/blog/how-interior-design-firms-track-item-status-across-every-project-with-studio-designer/
- **S23** Gather FF&E comparison 2025: https://gatherit.co/blog/best-ffe-specification-software-comparison-2025
- **S24** Gather home page: https://gatherit.co
- **S25** Fohlio procurement: https://www.fohlio.com/procurement
- **S26** Design Manager about: https://designmanager.com/about/
  - clipper: https://www.designmanager.com/blog/did-you-know-pro-clouds-dm-product-clipper-makes-adding-items-a-snap
- **S27** Material Bank: https://www.materialbank.com/
  - how it works: https://help.materialbank.com/en/articles/14489450-how-material-bank-works
- **S27b** Material Bank AI search (partner page): https://gendo.ai/ai-material-discovery
- **S28** Ivy vs Houzz Pro: https://pro.houzz.com/for-pros/difference-between-ivy-and-houzz-pro
- **S29** SketchUp 2026.0 release notes: https://help.sketchup.com/en/release-notes/sketchup-desktop-20260
- **S30** SketchUp current release notes (2026.2): https://help.sketchup.com/en/current-release-notes
- **S31** SketchUp iPad AI Render: https://help.sketchup.com/en/sketchup-ipad/ai-render
- **S32** Chief Architect X18 what's new: https://www.chiefarchitect.com/whats-new/
- **S33** Home Designer 2026 what's new: https://www.homedesignersoftware.com/whats-new/
  - also https://hometalk.chiefarchitect.com/topic/9996-home-designer-2026-now-available/
- **S34** Cedreo interior design software: https://cedreo.com/interior-design-software/
  - draw floor plan: https://cedreo.com/floor-plans/draw-floor-plan
- **S35** Cedreo AI render styles: https://cedreo.com/fr/blog/rendu-3d-ia/
- **S36** Cedreo on GetApp (3p): https://www.getapp.ca/software/114007/cedreo
- **S37** Foyr Neo (3p only; foyr.com returned 403):
  - https://www.capterra.com/p/204757/Foyr-Neo/
  - https://techjockey.com/detail/foyr
  - https://datadrivenaec.com/tools/foyr-neo
- **S38** Coohom home page: https://www.coohom.com/
- **S39** Coohom:
  - 5.0: https://www.coohom.com/helpcenter/3FO4K4WB5FCF.md
  - KBIS: https://kbis.com/exhibitor_news/coohom-revolutionizes-kitchen-closet-design-with-cutting-edge-software-features/
  - Coohom AI (June 2026): https://art4d.com/en/2026/06/coohom-ai
  - BOM (reseller, 3p): https://platform.softwareone.com/product/coohom-kitchen-closet-design/PCP-1752-7323
- **S40** Homestyler:
  - GTC 2025: https://resources.homestyler.com/2025/03/20/homestyler-at-nvidia-gtc-2025/
  - INDEX 2025: https://resources.homestyler.com/2025/05/26/homestyler-at-index-2025
  - FAQ: https://www.homestyler.com/learn/faq?lang=en_US
- **HS3p** Homestyler review (3p): https://machow2.com/homestyler-review
- **S41** Planner 5D:
  - app: https://planner5d.com/interior-design-app
  - AI: https://planner5d.com/use/ai-interior-design
  - review (3p): https://www.architecturelab.net/software/planner-5d/
- **S42** RoomSketcher newsroom: https://www.roomsketcher.com/news/
- **S43** Floorplanner review (3p): https://www.techradar.com/reviews/floorplanner
- **S44** Live Home 3D:
  - Windows release notes: https://livehome3d.com/win/release-notes
  - Vision release notes: https://www.livehome3d.com/vision/release-notes
- **LH-press** Live Home 3D press area: https://livehome3D.com/press/
- **S45** Vectorworks 2026:
  - interiors: https://www.vectorworks.net/newsroom/interior-design-features-architect-2026
  - Update 2: https://www.vectorworks.net/newsroom/2026-update-2
  - Update 6: https://www.vectorworks.net/newsroom/2026-update-6
- **S46** 2020 Design Live:
  - features: https://2020spaces.com/2020products/2020designlive/features
  - brochure: https://www.2020spaces.com/wp-content/uploads/2021/10/Brochure_DesignLive_1920x1080_Draft008.pdf
- **S47** magicplan:
  - new magicplan (9 October 2026): https://magicplan.app/blog/the-new-magicplan
  - export formats: https://help.magicplan.app/export-formats
  - Xactimate: https://help.magicplan.app/xactimate-integration
  - 2025 notes (APKMirror, 3p): https://www.apkmirror.com/apk/sensopia-inc/magicplan/magicplan-2025-27-0-release
- **S48** Polycam:
  - Vision 25: https://www.geoweeknews.com/news/polycam-unveils-new-enterprise-grade-platform-with-collaborative-tools-for-capturing-investigating-and-designing-physical-spaces
  - exports: https://learn.poly.cam/hc/en-us/articles/27756102599572
- **S49** D5 Render 3.0:
  - https://architosh.com/2026/02/d5-version-3-released-new-ai-workflows/
  - https://www.archdaily.com/catalog/us/products/32321/real-time-rendering-software-d5-render
- **S50** Enscape:
  - 2026 updates: https://www.motionmedia.com/mm-blog/enscape-2026-workflow-updates
  - export options: https://blog.enscape3d.com/export-options-in-enscape
- **EN-3p** Enscape VR (3p): https://www.aeco.space/en/enscape/enscape-features
- **S51** Twinmotion:
  - 2025.2: https://www.twinmotion.com/news/twinmotion-2025-2-is-here
  - media export: https://dev.epicgames.com/documentation/en-us/twinmotion/creating-and-exporting-media
- **G** Casa3D gap note (local): `02_PRODUCT/projects/Casa3D/GAP_ANALYSIS_2026-10-11.md`

## Caveats

- Cells marked `?` mean nothing was found in this pass. They are not evidence of absence. This applies especially to the established CAD tools, where sheets and title blocks are standard.
- Foyr Neo is backed only by third-party sources (its site returned 403). Floorplanner and Homestyler are also mostly third-party.
- Several features were dated only by webinars or blogs, not changelogs.
- No Reddit, ASID or IIDA data on demand was found, so section 7 is a judgement call.

## Summary (≤25 lines)

1. Matrix covers 9 business/studio tools and 17 design/visual tools across about 60 features, with every Y/P sourced (S1–S51). Minimal ID was not found, Ivy is legacy, and Sourcewell is out of scope.
2. The business-tool core is now standard: clipper, product library, client portal with per-item approval, proposals, invoices, payments, POs with status tracking, time tracking, tasks, and QuickBooks/Xero.
3. Houzz Pro is the broadest. It adds daily logs, offline mobile, a phone room scan, contracts with e-signature, change orders, elevations, and ten AI features.
4. The 2025-26 frontier has three strands:
   - AI plan→3D (Houzz, RoomSketcher, Planner 5D);
   - AI renders from boards (Studio Designer, DesignFiles, RoomSketcher, Cedreo);
   - presentations and 3D in the client portal (Programa, which links SketchUp; Studio Designer).
5. Consolidation: Studio Designer owns Mydoma (2024), Houzz absorbed Ivy, and Design Manager bought DesignSpec and Lojik (2024).
6. Casa3D's sourced lead is trade drawings plus an estimate with waste and labour. No business tool shows this, and Programa has no drawing at all.
7. Gaps confirmed against the gap note:
   - clipper;
   - per-item approvals;
   - POs and status;
   - invoices and payments;
   - tasks;
   - contracts;
   - questionnaire;
   - site log;
   - mobile.
8. Gaps not in the gap note:
   - time tracking;
   - mood boards as a first-class object;
   - importing an image or PDF plan to trace;
   - 360 panorama / walkthrough links;
   - change orders.
9. Open opportunities:
   - handover dossier;
   - snag list inside a design tool;
   - RO e-Factura;
   - a simple joinery cut list;
   - drawings and presentation in one client link;
   - site photos pinned to plan rooms;
   - spec revisions;
   - an open API/MCP export of the project model.
10. AI is everywhere in competitors. Casa3D can stay deterministic and market that its visuals are dimension-true; Studio Designer itself calls its AI renders "not CAD-accurate".
11. Confidence: medium. The sources are vendor claims, `?` cells mean unknown rather than absent, and the release ranking is judgement, not request data.
