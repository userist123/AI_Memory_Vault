# Casa3D implementation ledger — PR #203

This PR is the continuity/source-of-truth record for the Casa3D implementation work performed after the original memory-only PR was opened.

## Current implementation baseline

- Digital Twin v1.0
- shared renderer-neutral ViewerState
- read-only revision share links
- semantic Design DSL v1.1
- deterministic semantic Solver
- AI Design Approval with stale-base protection
- up to 3 AI design alternatives
- BOQ-aware alternative evaluation

## Canonical pipeline

`Brief -> AI -> Design DSL -> DSL Validator -> Semantic Solver -> Geometry Engine -> Digital Twin -> BOQ -> Preview -> User approval -> Revision`

The AI never supplies coordinates. Geometry is generated and validated by deterministic code. Preview is non-persistent. Approval revalidates the current project before creating a revision.

## v8 core file hashes

| File | SHA-256 |
|---|---|
| `core/digital-twin.ts` | `315371c8bac687db725e6bdc04f68016e6b5b2820b49432f9ee08a386e1c7a1a` |
| `core/geometry-engine.ts` | `a57fca04f279e0ea449a4ac2917d5e039413171782bab45a67049a70a425f543` |
| `core/view-state.ts` | `3d7a7e29f64294064c206b6a25fdae3d36f415df88d2f23aa9c2a7bf9930f5b7` |
| `core/design-dsl.ts` | `8335cd35774941999b41211c0da2038aa5078570c65885df84501998a6ae3308` |
| `core/design-search.ts` | `5953aacdb15327ff6403fa09ad2aa676aeaa44efd64ec77c7797772cf757f85e` |
| `core/design-approval.ts` | `46822e6cc7ffdba06eb1c8a689b3cea1ec5d7384ba48622d38d15db412074b66` |
| `core/boq-search.ts` | `73b3c5517254e72799f9bd4f7a739c04c26f7709393f6dbe06ebda63158f97b7` |
| `lib/ai-design.ts` | `0f59a93ed516b46c6fc05482d7157936926d243928f252a5fe2462351941760f` |
| `lib/schema.ts` | `4903ff683d1e687a5a8ad55843260ff6285ae289e7b2852635c9eadfff1b11bd` |
| `lib/repo.ts` | `d7cb45ea996555e456b5f408f21d27975aa7cf338afbc0122d4ed5764060eb0e` |
| `components/DesignPanel.tsx` | `3441edd71c0c5da8acbbdd86f42c8c649fda35511512d1ac560a781880297bbe` |
| `components/Editor.tsx` | `85fbfbd50e39639875960dd0b58f1ecf9c3e55e3e1fd4d482ec06e5fcbd91a01` |
| `app/api/projects/[id]/design/route.ts` | `057cd8569c31b78b693b774a99b8b7f5ec1ca05ad7ed82c8b4c63d2dd44f848f` |
| `app/api/projects/[id]/design/[pid]/route.ts` | `dc234c4c28f13efc19fcf934e62a0ed3f3f5ffcedf8a090210f9a22a4875c1fe` |
| `app/api/projects/[id]/shares/route.ts` | `b3e063f0ea98855f160bf6d1f012585c12d6941d7f4b068cf7ee5a84804eb07e` |
| `app/api/share/[token]/route.ts` | `66312a44b8974cd7c9e879843324847ced9bc29ea34c6ca0fefc4affc6072504` |

## Verification evidence

Core smoke/compile checks have been executed for each increment. The latest verified core checks include:

- `BOQ_SEARCH_COMPILE_PASS`
- `BOQ_SEARCH_SMOKE_PASS`
- `AI_SEARCH_COMPILE_PASS`
- `DESIGN_APPROVAL_SMOKE_PASS`
- `DSL_CONSTRAINT_COMPILE_PASS`
- `DSL_CONSTRAINT_SMOKE_PASS`
- `AI_DSL_CORE_COMPILE_PASS`
- `VIEWER_STATE_CORE_COMPILE_PASS`
- `DIGITAL_TWIN_SMOKE_PASS`

ZIP integrity was also checked for the generated development artifact.

## Verification limitation

Full Vitest and `next build` have not been claimed as passing because the working environment does not currently have a usable `node_modules` installation and npm registry installation attempts timed out. Isolated core compilation and deterministic smoke tests are the evidence currently available.

## Product safety gates

- AI coordinates are forbidden.
- Geometry Engine is authoritative.
- ERROR blocks.
- WARNING requires explicit user confirmation.
- Preview does not mutate the project.
- Approval revalidates against the current Digital Twin.
- Stale proposals are rejected.
- Catalog IDs are authoritative.
- Unknown commercial data remains `UNKNOWN`.
- External commercial provenance is preserved.
- External 3D assets require rights/licensing evidence.
