# Structure Migration Plan V1

## Purpose
Consolidate the repository into a functional numbered top-level spine without creating folders merely to fill numbers.

## Canonical top-level spine
- `00_GOVERNANCE` — governance, agent coordination, review and operating rules
- `01_ARCHITECTURE` — architecture, cognitive model, schemas/interfaces at design level
- `02_PRODUCT` — product specifications, projects, requirements, roadmaps
- `03_IMPLEMENTATION` — runtime production code
- `04_CONFIG` — runtime/configuration assets that are safe and stable to relocate
- `05_DATA` — curated data/state required by the product, not raw inbox material
- `06_INBOX` — local-only raw/unverified ingestion
- `07_EVALUATION` — evidence, reports, audits, experiment conclusions
- `08_OBSERVABILITY` — traces, telemetry contracts and operational observability artifacts
- `09_SECURITY` — security policy, threat models, incidents, adversarial material
- `10_DOCUMENTATION` — user/developer documentation, procedures and templates
- `20_TESTS` — all active test suites and test fixtures, consolidated from numbered test zones
- `30_SCRIPTS` — operational, migration and maintenance scripts
- `40_EXPERIMENTS` — experiments, pilots, benchmarks and notebooks when they are experimental artifacts rather than active tests
- `50_ARTIFACTS` — generated/exported artifacts that are intentionally versioned
- `60_DEPLOYMENT` — deployment packaging and environment integration
- `70_INTEGRATIONS` — external-system adapters/integration descriptors when distinct from runtime packages
- `80_ARCHIVE` — historical/retired content preserved for provenance
- `90_RELEASE` — release-specific material and release templates
- `99_META` — repository maps, migration manifests, indexes and meta-state

## Migration rules
1. Re-fetch `main` immediately before each write batch.
2. Preserve exact file contents unless a path/reference update is required by the move.
3. A directory is moved only after each child is classified.
4. Move means create target -> verify target -> delete source.
5. Never move runtime packages before import/path compatibility is checked.
6. Do not move raw `06_INBOX` content into the repository spine.
7. Do not duplicate historical evidence merely to improve appearance.
8. Do not delete content solely because it is redundant until provenance/reference checks are completed.
9. Every batch ends with a persistent state update and a commit.

## Current consolidation targets
### Absorb into canonical spine
- `00_CORE` -> split by semantics between `01_ARCHITECTURE`, `03_IMPLEMENTATION`, `10_DOCUMENTATION`
- `01_KNOWLEDGE` -> `01_ARCHITECTURE` or `07_EVALUATION` depending on evidence status
- `02_PROJECTS` -> `02_PRODUCT/projects`
- `03_PROCEDURES` -> `10_DOCUMENTATION/procedures`
- `04_MEMORY` -> `01_ARCHITECTURE/memory` and/or `03_IMPLEMENTATION/memory` by artifact type
- `05_RESOURCES` -> `10_DOCUMENTATION/resources` or `50_ARTIFACTS`
- `08_EXPORTS` -> `50_ARTIFACTS`
- `09_COORDINATION` -> `00_GOVERNANCE/coordination`
- `10_ARCHIVE` -> `80_ARCHIVE`
- generated test-numbered zones `21_*` through `29_*` -> `20_TESTS/<unit|integration|e2e|regression|adversarial|fixtures|performance|compliance|quality>` as appropriate
- `31_TOOLS` -> `30_SCRIPTS/tools` unless it is runtime code
- `32_MIGRATIONS` -> `30_SCRIPTS/migrations`
- `33_VALIDATION` and `34_VERIFICATION` -> `07_EVALUATION` or `20_TESTS` according to whether the material is evidence/reporting or executable tests
- `35_RELEASE_TOOLS` -> `90_RELEASE/tools`
- `36_DEVELOPMENT`, `37_MAINTENANCE`, `38_OPERATIONS`, `39_DIAGNOSTICS` -> `30_SCRIPTS` or `08_OBSERVABILITY` according to function
- `41_BENCHMARKS`, `42_NOTEBOOKS`, `43_PILOTS` -> `40_EXPERIMENTS`
- `90_TEMPLATES` -> `10_DOCUMENTATION/templates` or `90_RELEASE/templates`
- `99_SYSTEM` -> `00_GOVERNANCE` or `99_META` according to content

## Explicit non-goals
- No artificial 00–99 folder population.
- No semantic renaming of runtime modules during the structural pass unless required for import correctness.
- No benchmark result changes.
- No security bypasses or Defender exclusions.
