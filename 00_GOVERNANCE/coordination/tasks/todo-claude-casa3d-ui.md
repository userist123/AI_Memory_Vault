# todo-claude-casa3d-ui
STATUS: DONE (uncommitted, owner asked: no commit/push)    UPDATED: 2026-10-15T21:40:00Z
TASK: Casa3D UI/UX wave from audit scratchpad/audit2/ui-report.md (37 findings). Branch codex/casa3d-memory, working tree only.
DONE: layout P0 (F-1 side clipping, F-2 tablet grid override, F-3/F-6 compact tabs Plan/3D/Panou + bottom sheet, F-26 keyboard plan),
  44px touch targets on pointer:coarse (F-28), contrast tokens (F-16, F-27), design system tokens + single .btn + 6-size type scale (F-12/15/17/18/19),
  Icon set (components/Icon.tsx), Dialog (lib/dialog.ts + components/DialogHost.tsx) replacing every confirm/prompt (F-22), save chip on all sizes (F-23),
  share page room approval + sticky bar + batch approvals API (F-32), room stills rendered at slide aspect, core/format qty/date helpers (F-20/35),
  glossary Ofertă/Deviz/Proformă, nf + LoadError (F-36), print missing-data chips + page numbers (F-34), editor leftovers (room resize moves walls
  core/room-resize.ts, localhost warning box, room-suited groups, add without room, swap size hint).
TESTS: vitest 90 files / 529 tests pass; tsc clean. Screens: scratchpad/audit2/ui-after/.
NEXT / OPEN: stage phases + done dots (F-10), command palette (F-13), skeletons (F-24), print package navigator (F-34 part), template thumbs (F-33),
  date input locale (F-18), batch approval endpoint needs independent security review before merge.
KEY FILES: app/globals.css, components/Editor.tsx, PlanView.tsx, PresentView.tsx, FeedbackBox.tsx, lib/feedback.ts, core/{room-resize,plan-keys,format}.ts
