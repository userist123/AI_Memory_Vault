# todo-claude-casa3d-uiux
STATUS: IN_PROGRESS   UPDATED: 2026-10-23T03:40:00+03:00
TASK: Casa3D UI/UX P1 from the research report (owner request 2026-10-22; report kept locally, not in repo)
BRANCH / PR: claude/casa3d-uiux-p1 (worktree D:/w/uiux, base origin/main 6eb24f87)
P1 items (report table rows 1-7):
1. Clean data on link and documents: room names in the reader's language, colour names instead of hex
2. Responsive slides + automatic overflow tests (390 px and 16:9)
3. Client link: static render as LCP, three.js on demand
4. "Approve all" confirmation listing items, exceptions, typed name, dated log
5. Ctrl+K complete (tools, 3D presets, stages, objects, settings) with shortcuts shown
6. Typed dimensions while drawing walls/rooms (chip near cursor)
7. Accessibility foundation (WCAG 2.2 2.5.7 click alternatives, target sizes, reduced motion)
DONE:
- (1) localizeRoomNames any-language -> reader language + localizeSnapshotRoomNames (floor + levels, identity when
  unchanged); applied at display only in PresentView/share, PrintView, SiteReport, MoodBoard (the Editor auto-translate was
  removed after review round 1: it rewrote the draft, causing 409s on other devices and spurious revisions). BOQ paint label: colour name (colorName, tinted greys
  "gri verzui") + exact hex kept in BoqItem.color; wall sides nord/sud/est/vest instead of N/S/E/W.
  tests/clean-data.test.ts (3), finishes.test.ts updated. Suite 684/684.
- (4) askReview dialog kind: checklist grouped by room, select/deselect all, typed name (min 2), only checked keys
  approved; counter fixed per room+key; core/feedback-log.ts groups decisions (author, verdict, minute) shown as a
  dated log on the link. tests/feedback-log.test.ts.
- (6) core/typed-dimension.ts (int = cm, decimal = m, suffixes, imperial); PlanView chip near cursor, Enter builds the
  exact wall/room; rooms by click-click as well as drag. tests/typed-dimension.test.ts (5).
- (5) Ctrl+K: tools of the current stage, objects (rooms, furniture by name -> select), health, language, units,
  documents (offer, contract, proforma, handover, site report).
- (7) WCAG 2.5.7: plan zoom/pan/fit buttons and 3D nudge buttons (engine nudge()); global prefers-reduced-motion;
  checkboxes 20 px with 24 px labels; 44 px targets on the client link under pointer:coarse / <=700 px.
  Browser-verified (screens u01-u09 kept locally). Suite 692/692, tsc clean.
- (2) scripts/check-slides.mjs (no deps: headless Chrome over CDP): stress project with long names -> revision -> share;
  audits every slide type at 390 px and 16:9 (1024/1280/1920) for content leaving the slide or clipped text; exit 1 on
  failure. Found and fixed: long project name pushed the cover row past the slide (footer cut) -> fixed row, smaller
  title steps (>40, >70 chars), 4-line clamp with title attr; floating deck bar covered cover data -> in flow above slides.
- (3) Share LCP: PresentView initialises from server data (slides in initial HTML, not after hydration); revision_renders
  table + lib/share-renders.ts (WebP/JPEG magic-byte check, <=1.2 MB, keys = overview + revision rooms, owner-only write,
  active-share-only read); lib/share-images.ts renders missing images once in the designer's browser (800 + 1600 px) after
  "client link" / share and in the background for active links; link uses <img srcset>, cover fetchpriority=high,
  three.js only for missing images. Slow 4G + 4x CPU at 390 px: LCP 3741 ms -> 806 ms, no three.js download, CLS 0.
  tests/share-renders.test.ts (4); share-gate-routes inventory updated. Suite 696/696.
REVIEW round 1 (vault-reviewer): NOT READY, 0 blockers, 3 MEDIUM fixed: PlanView room draft cleared on tool change;
  Editor auto-translate removed; share link language from Accept-Language on the server (nested PrefsProvider
  initialLang) + localized snapshot derived with useMemo. LOW fixed: typed room min 0.6 m, name prefill, Enter on
  checkbox no longer submits, unique group keys, render state sends the snapshot only when images are missing,
  renders deleted on revoke when no other active link shows the revision, explicit WebGL context loss. Suite 697/697.
NEXT: review round 2; PR; CI; merge.
KEY FILES: casa3d/app/core/templates.ts, core/boq.ts, core/budget-excluded.ts, components/{Editor,PresentView,PrintView,SiteReport,MoodBoard,PlanView,Viewer3D,DialogHost}.tsx, core/{typed-dimension,feedback-log}.ts, lib/{dialog,share-renders,share-images}.ts, scripts/check-slides.mjs
