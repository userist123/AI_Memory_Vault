# todo-claude-casa3d-uiux
STATUS: IN_PROGRESS   UPDATED: 2026-10-23T01:52:00+03:00
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
  unchanged); applied at display in PresentView/share, PrintView, SiteReport, MoodBoard; Editor translates once per UI
  language on load as an undoable, saved change with a toast. BOQ paint label: colour name (colorName, tinted greys
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
NEXT: items 2, 3; independent review; PR; merge.
KEY FILES: casa3d/app/core/templates.ts, core/boq.ts, core/budget-excluded.ts, components/{Editor,PresentView,PrintView,SiteReport,MoodBoard,PlanView,Viewer3D,DialogHost}.tsx, core/{typed-dimension,feedback-log}.ts, lib/dialog.ts
