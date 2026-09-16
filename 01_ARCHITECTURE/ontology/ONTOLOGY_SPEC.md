---
id: ontology-spec
type: ontology_definition
lifecycle: ACTIVE
provenance:
  source_type: design
  source_author: ANTIGRAVITY
confidence: 1.00
status: scaffold
tags: [ontology, cognitive-architecture]
relations: []
created: 2026-09-07
---
# Ontology Specification — Master Cognitive Architecture Scaffold

## 1. Overview & Purpose

This specification defines the formal ontology layer connecting cognitive-science theory to the codebase's physical memory mechanisms. It establishes 16 standardized ontology slots that ground theoretical concepts into verified repository implementations.

This package (`r026/ontology-scaffold`) provides the foundational schema and empty slot scaffold. Ingesting specific book concepts is deferred to subsequent packages (`r027+`).

## 2. Common Frontmatter Schema

Every ontology file in this repository MUST adhere to the following YAML frontmatter schema:

```yaml
id: <slug>
ontology_slot: <one of the 16 slot names, lowercase>
type: ontology_definition
lifecycle: ACTIVE
provenance:
  source_type: design
  source_author: <your name>
confidence: 1.00
status: scaffold
tags: [ontology, cognitive-architecture]
relations: []
created: <ISO date>
```

### Schema Rules & Semantics
- **`id`**: Unique kebab-case identifier (e.g., `slot-01-identity`).
- **`ontology_slot`**: Exactly one of the 16 canonical slot names (lowercase).
- **`type`**: Fixed to `ontology_definition`.
- **`lifecycle`**: Fixed to `ACTIVE` for slot definition scaffolds. These files define settled architectural slots, not unverified claims. Future candidate concepts ingested into individual slots will enter at `REVIEW` lifecycle per standard vault policy.
- **`provenance`**: Fixed to `source_type: design` and attributed to the creating agent/author.
- **`confidence`**: Fixed to `1.00` for definition scaffolds.
- **`status`**: Set to `scaffold` until populated by downstream book ingestion.
- **`tags`**: Canonical tags `[ontology, cognitive-architecture]`.
- **`relations`**: Synaptic graph relations (empty by default for scaffolds).

---

## 3. The 16 Canonical Ontology Slots

| Slot Name | Question it Answers | Primary Theoretical Source | Validates Module |
|---|---|---|---|
| **identity** | What is this system and what is it not? | None (design decision, not literature) | none |
| **map** | Where does each kind of information live? | None (repository layout and governance) | `00_GOVERNANCE/VAULT_STATE.md`, `README.md` |
| **ontology** | What does each memory type mean? | Tulving & Schacter, Memory Systems 1994 | none (maps to `03_IMPLEMENTATION/packages/lifecycle/validation/schema.py`) |
| **relationships** | How does information connect? | Hebb, The Organization of Behavior (1949) | `03_IMPLEMENTATION/packages/graph/synapse.py`, `03_IMPLEMENTATION/packages/graph/spreading_activation.py` |
| **state** | What is implemented/real/active right now? | None (empirical execution measurement) | `00_GOVERNANCE/VAULT_STATE.md` |
| **procedures** | How are operations executed? | Anderson, How Can the Human Mind Occur in the Physical Universe (ACT-R) | `10_DOCUMENTATION/procedures/Compiling_A_Request_Into_A_Brief.md`, `10_DOCUMENTATION/procedures/Recording_A_Solved_Problem.md` |
| **judgement** | How are decisions made? | Anderson (ACT-R conflict resolution), Minsky (Society of Mind) | `30_SCRIPTS/prompt/compile_task_prompt.py` (with `--infer`) |
| **constraints** | What is forbidden or risky? | None (incident analysis and security invariants) | `03_IMPLEMENTATION/packages/security/mutation_gate.py` |
| **provenance** | Where did a piece of information come from? | None (epistemic design) | `AGENTS.md` (section 'Source of Truth') |
| **confidence** | How certain is this information? | None (empirical calibration methodology) | none |
| **history** | What has been tried and failed? | None (empirical and procedural memory) | `01_ARCHITECTURE/memory/Lessons/` (11 verified lesson files) |
| **skills** | What capabilities exist? | Minsky, Society of Mind | `.agents/skills/` |
| **agents** | Who knows how to do each thing? | Minsky, Society of Mind | none (not yet built) |
| **routing** | When should each agent be invoked? | Newell & Laird (Soar), Minsky (Society of Mind) | none (not yet built) |
| **retrieval** | How is relevant memory found? | None (grounded in production retrieval engine) | `03_IMPLEMENTATION/packages/memory/controller.py` (line 376) |
| **consolidation** | How does experience become knowledge? | McClelland et al. (1995) CLS, Kumaran & Hassabis (2016) | `03_IMPLEMENTATION/packages/graph/plasticity.py`, `03_IMPLEMENTATION/packages/graph/synapse_store.py` |

---

## 4. Slot File Architecture

Each slot definition under `slots/` contains five standard sections:
1. **Question**: Exactly one sentence stating what architectural question the slot answers.
2. **Theoretical sources**: Foundational literature, cognitive models, or design origins.
3. **Validates module**: Specific, verified file paths in this repository where the slot's mechanics are implemented or grounded.
4. **Candidate concepts**: A markdown table with columns `concept | source_book | confidence | status | date_added`. In this scaffold, this table is strictly empty and reserved for `r027+` concept ingestion.
