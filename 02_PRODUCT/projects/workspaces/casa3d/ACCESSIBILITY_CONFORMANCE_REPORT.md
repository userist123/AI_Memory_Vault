---
type: project
category: compliance-report
status: review
priority: medium
confidence: medium
id: "3b5eea1c-0f04-4a12-8cbb-b9866a47ce83"
lifecycle: REVIEW
provenance_status: incomplete
provenance:
  source_type: agent_analysis
  source_ref: "casa3d app code and automated checks on 2026-10-23 (scripts/check-a11y.mjs axe-core 4.14.0, scripts/check-slides.mjs, tests/*.test.ts); no external audit"
  redaction: not_applicable
tags: [casa3d, accessibility, wcag, en-301-549, vpat, legea-232-2022]
verification: unverified
relations: []
---

# Casa3D: accessibility conformance report (VPAT style, WCAG 2.2 A/AA, EN 301 549)

Product: Casa mea 3D (designer application and client share link). Date: 2026-10-23.
Method: self-assessment plus automated checks in the repository. **Not an external audit**; statuses marked
"Not evaluated manually" have no manual test with assistive technology yet.

Public statement (RO/EN): page `/accesibilitate` (`app/lib/a11y-statement.ts`).

## Evidence

- `scripts/check-a11y.mjs` (axe-core 4.14.0, tags wcag2a, wcag2aa, wcag21a, wcag21aa, wcag22aa) on 18 page states:
  home, accessibility statement, editor stages Survey / Finishes / FF&E / Quote / Present / Orders, the command palette,
  seven print documents, client link on desktop (1600 px) and phone (390 px). Result on 2026-10-23: **0 violations,
  0 accepted exceptions** (`scripts/a11y-known.json` is empty). Runs in CI (`.github/workflows/casa3d-build.yml`).
- `scripts/check-slides.mjs`: no presentation content leaves its 16:9 slide, no horizontal scroll at 390 px.
- Unit tests: typed dimensions (2.5.7), dialog keyboard handling, i18n coverage.
- Measured contrast of the editor chrome (2026-10-23): minimum 4.89:1 (white on terracotta buttons).

## EN 301 549 mapping

Chapter 9 (web) and chapter 11 (software) map to the WCAG 2.2 A/AA table below (EN 301 549 v3.2.1 references
WCAG 2.1; WCAG 2.2 additions 2.4.11, 2.5.7, 2.5.8, 3.2.6, 3.3.7, 3.3.8 are reported as well). Chapter 10 (non-web
documents): the PDF produced by the browser's "Save PDF" is not tagged (see 1.3.1). Chapters 5-8 (hardware, ICT with
two-way voice, video) are not applicable.

## WCAG 2.2 level A and AA

| Criterion | Status | Remarks |
|---|---|---|
| 1.1.1 Non-text content | Partially supports | Images have text alternatives (room renders use the room name); icon buttons have accessible names (axe: 0). The WebGL 3D view has no full text equivalent; the same data (rooms, areas, materials, pieces, budget) is available as text. |
| 1.2.1-1.2.5 Time-based media | Not applicable | No audio or video content. |
| 1.3.1 Info and relationships | Partially supports | Semantic headings, lists, tables, labelled form fields (axe: 0). Browser-generated PDFs are untagged. |
| 1.3.2 Meaningful sequence | Supports | DOM order matches visual order (checked on slides at 390 px and 16:9). |
| 1.3.3 Sensory characteristics | Supports | Instructions do not rely on shape or position alone. |
| 1.3.4 Orientation | Supports | Client link works in portrait and landscape. |
| 1.3.5 Identify input purpose | Partially supports | Client contact fields are labelled; autocomplete tokens not set everywhere. |
| 1.4.1 Use of color | Supports | Status uses text and icons as well as color (e.g. "Ready" / "To do" dots have text). |
| 1.4.2 Audio control | Not applicable | No audio. |
| 1.4.3 Contrast (minimum) | Supports | axe color-contrast: 0 violations on all checked pages; measured minimum 4.89:1. |
| 1.4.4 Resize text | Not evaluated manually | Layout uses relative units and container queries; no 200 % zoom test recorded yet. |
| 1.4.5 Images of text | Supports | No images of text. |
| 1.4.10 Reflow | Supports | Client link at 390 px: no horizontal scroll (automated). Editor plan/3D canvases are exempt (two-dimensional content). |
| 1.4.11 Non-text contrast | Not evaluated manually | Focus rings and control borders designed to 3:1; not measured on every control. |
| 1.4.12 Text spacing | Not evaluated manually | |
| 1.4.13 Content on hover or focus | Supports | Tooltips are native `title`; popovers close with Escape. |
| 2.1.1 Keyboard | Partially supports | All controls and the client link are keyboard operable; every plan object is in a keyboard list ("Plan objects", first Tab stop of the plan) with generated descriptions, Enter selects and frames it; arrows move, R rotates. Drawing new geometry needs pointer or typed dimensions after a first point. |
| 2.1.2 No keyboard trap | Supports | Modal dialogs trap focus by design and close with Escape (tests/dialog.test.ts). |
| 2.1.4 Character key shortcuts | Partially supports | Single-key shortcuts (R, F, ?) only act when the plan has focus; no setting to turn them off. |
| 2.2.1 Timing adjustable | Supports | No time limits. |
| 2.2.2 Pause, stop, hide | Supports | No auto-moving content; loading shimmer stops under reduced motion. |
| 2.3.1 Three flashes | Supports | No flashing content. |
| 2.4.1 Bypass blocks | Not evaluated manually | Landmarks present (banner, navigation, complementary); no skip link. |
| 2.4.2 Page titled | Supports | Every page has a title (axe: 0). |
| 2.4.3 Focus order | Supports | Focus follows reading order; menus move focus to the current item. |
| 2.4.4 Link purpose | Supports | Link text describes the target (axe: 0). |
| 2.4.5 Multiple ways | Supports | Stage bar, compact stage selector and command palette (Ctrl+K). |
| 2.4.6 Headings and labels | Supports | |
| 2.4.7 Focus visible | Supports | Visible focus ring on all controls. |
| 2.4.11 Focus not obscured (minimum) | Not evaluated manually | Fixed bars on the client link leave space below content; not tested for every focusable element. |
| 2.5.1 Pointer gestures | Supports | Pinch and multi-point gestures have single-pointer button alternatives. |
| 2.5.2 Pointer cancellation | Supports | Actions fire on pointer up / click. |
| 2.5.3 Label in name | Supports | axe label-content-name-mismatch: 0 (fixed for stage buttons and the 3D export menu on 2026-10-23). |
| 2.5.4 Motion actuation | Not applicable | No device-motion input. |
| 2.5.7 Dragging movements | Supports | Plan: zoom/pan buttons, click-click rooms, typed dimensions; 3D: rotate/zoom buttons. |
| 2.5.8 Target size (minimum) | Supports | Targets at least 24 × 24 px; 44 × 44 px on touch devices (audited on the client link). |
| 3.1.1 Language of page | Supports | `lang` set from the reader's language. |
| 3.1.2 Language of parts | Supports | |
| 3.2.1 On focus / 3.2.2 On input | Supports | No context change on focus or input. |
| 3.2.3 Consistent navigation / 3.2.4 Consistent identification | Supports | |
| 3.2.6 Consistent help | Supports | Accessibility statement linked from the same place on the home page and the client link. |
| 3.3.1 Error identification | Partially supports | Errors announced as text (role=alert / status); not every field names the error. |
| 3.3.2 Labels or instructions | Supports | |
| 3.3.3 Error suggestion | Partially supports | Typed dimensions and name fields explain the expected format; not all forms. |
| 3.3.4 Error prevention (legal, financial) | Supports | Client approvals are confirmed in a review dialog listing the items, with the client's name, and recorded with a date; decisions can be changed. |
| 3.3.7 Redundant entry | Supports | The client's name is remembered on the link. |
| 3.3.8 Accessible authentication (minimum) | Supports | Password login supports paste and password managers; the share PIN is numeric and can be pasted. |
| 4.1.2 Name, role, value | Supports | axe: 0 (ARIA tablist / menu / dialog roles checked; level tabs fixed on 2026-10-23). |
| 4.1.3 Status messages | Supports | Save state, toasts and approval progress use live regions. |

## Known gaps and plan

1. Manual tests with NVDA / VoiceOver / TalkBack and 200 % zoom; an external audit before public sale.
2. Tagged PDF output for documents (server-side PDF generation).
