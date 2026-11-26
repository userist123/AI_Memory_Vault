# Casa3D — research for D3 (asset pipeline) and D4 (viewport), 2026-11-26

Sources: three independent web researches (Codex/ChatGPT, Gemini Argon + Hydrogen via the local agent bridge, Fable 5.2 with web tools), each told to cite opened URLs and mark the rest UNVERIFIED. Agent output was treated as untrusted; the decisive facts below were re-checked by Opus against primary sources (installed three.js r186 source, the app's own CSP, the npm registry).

## Verified facts (primary source)
| Fact | Evidence |
|---|---|
| GLTFLoader handles KHR_mesh_quantization and KHR_texture_transform in plain JS (no decoder); only Draco needs `dracoLoader`, meshopt/KTX2 need their decoders | `node_modules/three/examples/jsm/loaders/GLTFLoader.js` lines 521-531 (r186) |
| The app CSP already allows `img-src 'self' data: blob:` and `worker-src 'self' blob:`; no `wasm-unsafe-eval` | `app/lib/security-headers.ts` lines 16, 20 |
| OrbitControls has `zoomToCursor` (default false) | `OrbitControls.js` line 306 |
| TransformControls in r186 exposes `getHelper()` (the helper must be added to the scene) | `TransformControls.js` line 453 |
| `gltf-validator` npm latest = 2.0.0-dev.3.10 (2024-10-22); no newer published build | registry.npmjs.org/gltf-validator (Fable, Codex, Gemini agree) |
| Poly Haven assets are CC0, attribution not required, redistribution allowed | polyhaven.com/license (Fable, Codex) |

## Decisions
1. **Asset pipeline (D3)** — validate with `gltf-validator` 2.0.0-dev.3.10 pinned, isolated in `app/tools/assets` (does not touch the app's package.json); fail on `numErrors > 0`, list warnings. Manifest `app/data/model-manifest.v1.json` with sha256, size, triangles, bounds, SPDX `CC0-1.0`, source URL, retrieval date.
2. **Optimization** (when needed, build time only): glTF-Transform 4.5.x `prune`, `dedup`, `resize`; `weld`/`simplify` only with a visual check; **never** `draco`, `meshopt`, `etc1s`/`uastc` (browser decoders need WASM, forbidden by our CSP); `optimize` must be run with compression off. Keep PNG for normal/ORM maps (lossy formats distort them). Keep `.gltf` paths and bounding boxes (two consumers: viewer and the Blender renderer).
3. **Budgets** (Khronos 3D Commerce guidelines, cited by Codex and Gemini): ≤ 100k triangles and < 5 MB per asset, 1K–2K textures; our target for decor/furniture: ≤ 50k triangles, ≤ 1K textures. Enforce later from the manifest.
4. **Camera (D4)** — sources disagree: Codex keeps OrbitControls (`zoomToCursor`, presets), Gemini and Fable prefer `camera-controls` (fitToBox, boundaries, smooth transitions, ortho support). Decision: keep the engine's own orbit for now and add presets/pan/ortho through the pure, tested `core/view-presets.ts`; re-evaluate `camera-controls` only if the wiring slice needs animated transitions (it is a new dependency).
5. **Selection & transform** — one undo command per drag (commit on drag end); floor-constrained move (X/Z) + Y rotation with 15°/45° snap; pure helpers in `core/selection-set.ts` and `core/measure3d.ts`.
6. **Accessibility** — WCAG 2.2 SC 2.5.7 requires a single-pointer alternative to dragging (nudge buttons / numeric X-Z-rotation fields), plus keyboard moves and an aria-live status. To be part of the wiring slice.

## Deferred (blocked by open PRs 394/395/371/386 that own the engine and UI files)
Wiring of presets (keys 1–6), pan, 3D measure tool, multi-select and the gizmo into `components/viewer3d-engine.js`, `Viewer3D.tsx`, `lib/commands.ts`, locales; with a Playwright spec `e2e/viewport-views.spec.ts`.
