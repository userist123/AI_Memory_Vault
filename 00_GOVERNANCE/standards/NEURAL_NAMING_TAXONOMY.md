---
id: standard-neural-naming-taxonomy
type: standard
lifecycle: REVIEW
verification: unverified
confidence: medium
provenance:
  source_type: inference
  source_ref: r006 graph measurement + package inventory
relations: []
---

# Neural naming taxonomy

The canonical vocabulary for this vault. One name per role, every role mapped
to code that exists.

## Why this is a standard and not a rename

A brain is not a set of modules with cognitive names. This vault already
contains `attention.py`, `executive.py`, `global_workspace.py` and
`reasoning.py` — and every one of them has **zero production consumers**.
Renaming decoratively would add vocabulary without adding function, which is
the failure this standard exists to prevent.

So: the taxonomy fixes **ambiguity**, not aesthetics. It applies to new files
and to names that are genuinely ambiguous today (449 files named `readme`
collapse into a single node in the graph). It does **not** authorize renaming
working code.

## The map

Afferent (input) → central processing → efferent (output).

| Neural term | Role | Where it lives now |
|---|---|---|
| **Afferent / sensory** — receives | untrusted input arrives, is classified and quarantined | `06_INBOX/`, `interfaces/financial_ingestion.py` |
| **Brainstem** — involuntary gate | authorization; nothing passes without it, and it is never bypassed | `security/authorizer.py`, `security/authority.py` |
| **Hippocampus** — forms memory | writes, lifecycle transitions, consolidation | `memory/controller.py`, `lifecycle/policy.py`, `learning/consolidation.py` |
| **Synapse** — connects memory | one edge between two notes, with weight, origin, provenance | `graph/synapse_store.py`, `graph/synapse.py` |
| **Axon propagation** — spreads signal | activation travelling along synapses | `graph/spreading_activation.py`, `graph/activation.py` |
| **Cortex** — associates and retrieves | candidate generation, ranking, context assembly | `retrieval/` |
| **Working memory** — holds the current task | bounded, short-lived, never auto-promoted | `memory/working_memory.py` |
| **Attention** — gates what gets through | selection under budget | `memory/attention.py` |
| **Global workspace** — broadcasts | what becomes available to the whole system | `memory/global_workspace.py` |
| **Cerebrum (creier mare)** — deliberates | slow, explicit reasoning over retrieved memory | `memory/reasoning.py`, `memory/executive.py` |
| **Cerebellum (creier mic)** — executes learned procedure | a validated skill run without deliberation | `.agents/skills/`, skill executor |
| **Efferent / motor** — acts | the action leaves the system | `interfaces/api_server.py`, `interfaces/dispatch_cli.py` |
| **Sleep consolidation** — reorganizes offline | deduplication, strengthening, forgetting | `learning/sleep_consolidation.py`, `learning/deduplication.py` |
| **Plasticity** — learns from outcome | reinforce what was used, decay what was not | `SynapseStore.reinforce()`, `.decay_unused()`, `.prune()` |

Two entries are aspirational and must be labelled as such wherever they are
referenced: everything from **Attention** through **Cerebrum** currently has no
production consumer, and **Plasticity** is implemented but never called.

## Naming rules

1. **A file is named after what it is, never after its container role.**
   `readme`, `index`, `notes`, `setup`, `overview`, `guide`, `summary`,
   `config`, `doc` are forbidden as full names inside vault content roots.
   `01_ARCHITECTURE/knowledge/retrieval/README.md` becomes
   `01_ARCHITECTURE/knowledge/retrieval/Retrieval Architecture.md`.
2. **The file name is the graph identity.** Obsidian resolves `[[X]]` by file
   name, so two files sharing a name are one node. Names must be unique across
   the vault, not just within a directory.
3. **A skill is named by capability, not by verb.** `telegram-publish`, not
   `send`. The name must survive being read out of context in an agent roster.
4. **A prompt is named `<skill>.prompt.md`**, an executor `<skill>.runner.py`,
   a reader `<skill>.reader.py`. The stem ties them together; the suffix says
   the role.
5. **Neural terms are reserved.** Do not name a module `cortex`, `synapse`,
   `hippocampus` or similar unless it fills that row of the map above. A
   metaphor used loosely destroys the vocabulary for everyone.
6. **Aspirational components carry `EXPERIMENTAL` in their front matter** until
   a production call path exists. The Global Production-Consumer Rule in
   `CLAUDE.md` decides this, not intent.

## What must NOT be renamed

- **`SKILL.md` (3877 files).** It is the ingestion contract in `CLAUDE.md`;
  `skill_ingestion.py` matches it by name.
- **Python packages** (`retrieval`, `graph`, `memory`, `lifecycle`, `security`,
  `learning`, `observability`, `interfaces`, `providers`). They are already
  accurate, they are listed in `pyproject.toml`, and the `cognitive_core` and
  `memory_controller` shims resolve against them. Renaming breaks imports and
  1174 tests to gain nothing.
- **The repository root `README.md`.** It stays `README.md`.
- **Numeric root directories.** They are enforced by
  `validate_repository_layout.py`.

## Scope of the first rename pass

Only the ~81 generically-named files inside the four roots `VaultIndex`
indexes (`01_ARCHITECTURE`, `02_PRODUCT`, `10_DOCUMENTATION`,
`00_GOVERNANCE`). Each renamed file leaves a `README.md` stub pointing at its
new name, so GitHub directory rendering is preserved.

`.agents/` (117 files) is explicitly out of scope until it is verified what
`skill_ingestion.py` reads by fixed name.
