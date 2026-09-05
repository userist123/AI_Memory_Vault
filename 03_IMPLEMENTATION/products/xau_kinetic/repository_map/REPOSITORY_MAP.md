# R001 Repository Map

| Current Source | Role | Canonical Location | Decision |
|---|---|---|---|
| `03_IMPLEMENTATION/packages/graph/` | runtime component | `03_IMPLEMENTATION/packages/graph/` | canonical graph implementation |
| `03_IMPLEMENTATION/packages/memory/` | runtime component | `03_IMPLEMENTATION/packages/memory/` | canonical memory implementation |
| `03_IMPLEMENTATION/packages/learning/` | runtime component | `03_IMPLEMENTATION/packages/learning/` | canonical learning implementation |
| `03_IMPLEMENTATION/packages/retrieval/` | runtime component | `03_IMPLEMENTATION/packages/retrieval/` | canonical retrieval implementation |
| `03_IMPLEMENTATION/packages/security/` | runtime component | `03_IMPLEMENTATION/packages/security/` | canonical security implementation |
| `03_IMPLEMENTATION/packages/interfaces/` | integration boundary | `03_IMPLEMENTATION/packages/interfaces/` | canonical interfaces and CLI/API integration |
| `03_IMPLEMENTATION/packages/memory_vault/` | compatibility namespace | `03_IMPLEMENTATION/packages/memory_vault/` | stable import facade over sibling packages |
| `03_IMPLEMENTATION/packages/memory_vault/interfaces/api/vault_api.py` | API boundary | same path | canonical API location |
| `03_IMPLEMENTATION/products/xau_kinetic/engine/` | product runtime | same path | active engine |
| `03_IMPLEMENTATION/products/xau_kinetic/desktop/` | product runtime | same path | companion desktop |
| `03_IMPLEMENTATION/products/xau_kinetic/standalone/` | product distribution | same path | active bundled distribution |
| `04_CONFIG/obsidian/` | configuration/pointer | same path | preserve gitlink/config boundary |
| `20_TESTS/` | test suite | same path | tests remain outside production packages |
| `40_EXPERIMENTS/` | experiments/benchmarks | same path | never a production import source |
| `06_INBOX/` | transient import boundary | README/.gitkeep only | raw and derived imports removed from tracking |
| `99_META/` | repository metadata | same path | migration history and metadata only |

## Legacy source paths

The former root runtime directories `cognitive_core/`, `memory_controller/`, `vault_api.py`, `xau_kinetic/`, `XAU_Kinetic.Desktop/`, and `XAU_Kinetic_Standalone/` are migration inputs, not current sources of truth. Their active code was reclassified into `03_IMPLEMENTATION/`.

The canonical implementation source of truth is the current Git tree on `main`. Historical reports and migration notes describe intent and provenance but do not override current runtime evidence.
