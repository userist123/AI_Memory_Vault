# todo-claude-casa3d-uiux-p2
STATUS: IN_PROGRESS   UPDATED: 2026-10-23T06:20:00+03:00
TASK: Casa3D UI/UX P2 from the research report (rows 8-14; P1 merged in PR #321). Owner: "Continua recomandat".
BRANCH / PR: claude/casa3d-uiux-p2 (worktree D:/w/uiux2, base origin/main 944d1427)
P2 items (report rows 8-14):
8. Chrome recalibrated: warm desaturated neutral top bar (OKLCH), terracotta only for primary/selection/focus
9. Compact stage selector; Releveu with remembered collapsible sections and a wide document mode
10. Single 2D<->3D selection: mirrored hover, "frame in the other view", selection drawn over geometry, X-Ray in 3D
11. 3D presets as Saved Views (camera + section + display); cut-height slider (dollhouse); one Display menu
12. Health opens a problem list; click selects and frames the object; named revision checkpoints
13. Native motion: @starting-style, duration tokens, View Transitions with support detection, canvas excluded
14. Damped, interruptible camera transitions; direct cut under reduced motion
DONE:
- (12) core/frame.ts selectionBox + frameViewBox (aspect-aware, min 3 m); PlanView `frame` prop + F key (no
  selection: whole plan); Health action selects and frames. Health list and named revisions already existed.
- (13) lib/motion.ts withViewTransition (support + reduced-motion detection, error-safe); stage switch animates only
  the side panel (view-transition-name), root/canvas not animated, ::view-transition pointer-events:none; dialogs and
  toast enter with @starting-style; duration/easing tokens.
- (14) core/camera-glide.ts (frame-rate independent exp damping, shortest-angle yaw, exact arrival); engine: tour room
  switch glides, any input (keys, drag, pinch, nudge) cancels, reduced motion cuts; house orbit uses dampFactor.
- Pre-existing bug fixed: Finishes room rows broke names ("Livi/ng") - name min 7em, material wraps right.
  Browser-verified (screens v01-v04 kept locally). Suite 703/703.
- (11) core/saved-views.ts (tolerant parse, replace by name, max 12, cut clamp); engine getView/setView/setCut
  (cutFrac replaces fixed .42); Viewer3D: one "Views" menu (presets + saved views with delete + save current, stored
  per project in localStorage) and one "Display" panel (doors, dollhouse cut slider, time of day, quality, sun),
  moved under the 3D bar (it covered the nudge buttons). Suite 706/706.
NEXT: items 9, 10, 8; review; PR; merge.
KEY FILES: casa3d/app/core/{frame,camera-glide,saved-views}.ts, lib/motion.ts, components/{PlanView,Editor,KeyboardHelp,Viewer3D}.tsx,
  components/viewer3d-engine.js, app/globals.css
