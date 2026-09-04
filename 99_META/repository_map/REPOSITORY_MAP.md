# R001 Repository Map

| Source | Role | Target | Decision |
|---|---|---|---|
| `cognitive_core/` | active_component | `03_IMPLEMENTATION/packages/memory_vault/{graph,memory,learning,observability,providers,interfaces,security}/` | classify by responsibility; compatibility shim only |
| `memory_controller/` | active_component | `03_IMPLEMENTATION/packages/memory_vault/{retrieval,memory,security,lifecycle,interfaces,observability}/` | classify by responsibility; compatibility shim only |
| `vault_api.py` | active_component | `03_IMPLEMENTATION/packages/memory_vault/interfaces/api/` | explicit API boundary |
| `xau_kinetic/` | product/subproject | `03_IMPLEMENTATION/products/xau_kinetic/engine/` | active engine |
| `XAU_Kinetic.Desktop/` | product/subproject | `03_IMPLEMENTATION/products/xau_kinetic/desktop/` | companion desktop, generated bin/obj removed |
| `XAU_Kinetic_Standalone/` | product/subproject | `03_IMPLEMENTATION/products/xau_kinetic/standalone/` | active bundled distribution |
| `AI_Memory_Vault_OBSIDIAN` | configuration/pointer | `04_CONFIG/obsidian/` | preserve gitlink boundary |
| `cognitive_core/tests/` | test | `20_TESTS/unit/cognitive_core/` | no production source |
| `memory_controller/tests/` | test | `20_TESTS/unit/memory_controller/` | no production source |
| `cognitive_core/benchmarks/` | benchmark | `40_EXPERIMENTS/cognitive_core/benchmarks/` | never production import |
| notebooks | experiment | `40_EXPERIMENTS/notebooks/` | no production import |
| `06_INBOX/RAW_IMPORTS` | import brut | untracked/local-only | remove from Git index |
| migration documents | archive | `99_META/migration_logs/archive/2026-09-04/` | consolidate historical noise |
| root decorative image | artifact | `50_ARTIFACTS/design_reference/` | keep only as artifact |

Roles: `active_component`, `integration`, `experiment`, `archive`, `duplicate`.

The canonical implementation source of truth is Git code on this branch. Historical reports do not override runtime evidence.
