# @casa3d/twin-core

Framework-free core of Casa3D: the Digital Twin and everything that is allowed to change it.
The Next.js application (`../../app`, imported from the faza4 archive) consumes this package; nothing here imports React, Next or a database.

Pipeline (Constitution art. 1-7):

```
Brief -> DesignProvider (rules | AI) -> raw DSL -> validateDsl -> solve (Solver + Geometry Engine)
      -> candidate Twin -> measure / evaluateBoq -> preview -> user accept (revalidated) -> Revision -> share
```

| Module | Responsibility | Invariants enforced by tests |
|---|---|---|
| `geometry.ts` | mm-rounded 2D primitives, rectilinear polygons (rectangles, L/U shapes) | rect-in-polygon rejects the notch; touching edges do not overlap |
| `twin.ts` | Digital Twin v1.0 model, `normalize`, SHA-256 `fingerprint`, `wallsFromRooms`, `linkWalls` | fingerprint stable under ordering and sub-mm noise; shared walls carry both room ids |
| `engine.ts` | `validate` (ERROR blocks, WARNING confirms), `findPosition` (deterministic 5 cm grid) | never returns a position that blocks a door; same input, same output |
| `catalog.ts` | catalog contract; `UNKNOWN` price is a value | nothing is estimated |
| `dsl.ts` | Design DSL 1.1 validator over untrusted input | coordinates rejected anywhere; only existing catalog ids, rooms, placements, refs |
| `solver.ts` | `solve`, `measure`, `solveAlternatives` (1..3) | a failed operation leaves the base twin untouched; no automatic winner |
| `approval.ts` | `preview` (non-persistent), `accept` (revalidated), `reject`, revisions | STALE when the twin changed under the proposal; tampered candidates are caught |
| `view-state.ts` | one ViewerState for plan and 3D, reducer, joystick, `renderItems` | twin change clears preview/selection; clamped camera and zoom |
| `boq-eval.ts` | quantities and RON-only cost roll-up, budget verdict | verdict is `UNKNOWN` whenever a price is unknown |
| `share.ts` | read-only share of one revision by token | later edits never leak; revoked/expired tokens resolve to null |
| `catalog-feed.ts` | supplier feed import with provenance | scraped feeds refused; price without `verifiedAt` rejected |
| `designer.ts` | provider contract + deterministic rules engine | provider output is data until `validateDsl` passes |

Commands: `npm ci --ignore-scripts`, `npm run typecheck`, `npm test`. CI: `.github/workflows/casa3d-build.yml` (ubuntu + windows).

Not in this package by owner decision (2026-10-10): the model-backed provider that calls Anthropic. The contract is `DesignProvider`; the rules engine is the fallback the app ships with.
