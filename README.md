---
category: index
status: active
version: 6.0.0
confidence: very_high
verification: verified
provenance_status: maintained
relations:
  - "00_CORE/Identity.md"
  - "00_CORE/Rules.md"
  - "00_CORE/Memory_Protocol.md"
  - "01_KNOWLEDGE/Agents_Skill_Matrix.md"
  - "01_KNOWLEDGE/Master_Skills_Catalog_251.md"
  - "01_KNOWLEDGE/MOC_Frontend_UI_UX_Standards.md"
  - "01_KNOWLEDGE/UI_Sensei_Design_Philosophy.md"
  - "01_KNOWLEDGE/Deep_Visual_Web_Engineering_Master_Report.md"
  - "99_SYSTEM/Memory_V6_Architecture.md"
  - "99_SYSTEM/Obsidian_Skill_Agent_Memory_Sync.md"
  - "05_RESOURCES/Obsidian/Skill_Agent_Memory_MOC.md"
  - ".claude-plugin/plugin.json"
  - ".claude-plugin/marketplace.json"
  - "skills/ai-memory-vault/SKILL.md"
---

# 🧠 AI Memory Vault — Cognitive Memory, Skills & Agent Knowledge System

<p align="center">
  <strong>A persistent, provenance-aware cognitive workspace for AI agents.</strong><br>
  Memory, knowledge, skills, agents, procedures, retrieval, verification, orchestration and Obsidian — one canonical Vault.
</p>

<p align="center">
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY/actions"><img alt="CI" src="https://img.shields.io/badge/CI-GitHub%20Actions-2088FF?logo=githubactions&logoColor=white"></a>
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY/tree/main/.agents/agents"><img alt="Agents" src="https://img.shields.io/badge/Agents-21-FF8A00"></a>
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY/tree/main/.agents/skills"><img alt="Skills" src="https://img.shields.io/badge/Cataloged%20Skills-3%2C699-22C55E"></a>
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY/tree/main/07_EVALUATION"><img alt="Security" src="https://img.shields.io/badge/Trust%20Invariants-P0--P18%20Verified-blueviolet"></a>
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY/tree/main/.claude-plugin"><img alt="Claude Code" src="https://img.shields.io/badge/Claude%20Code-Plugin-7C3AED"></a>
  <a href="https://obsidian.md/"><img alt="Obsidian" src="https://img.shields.io/badge/Obsidian-Synced-7C3AED"></a>
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY"><img alt="Repository" src="https://img.shields.io/badge/Repository-Git--based-111827"></a>
</p>

> **AI Memory Vault** is the canonical persistence and knowledge layer for a multi-agent AI ecosystem. It lets different AI clients (Claude Code, Antigravity, Codex) and local agents share the same durable memory, skill library, procedures, provenance, retrieval infrastructure and human-auditable state without creating parallel memories.

---

## 🌌 The Goal

The project is not just a folder of prompts and not just a vector database.

The long-term goal is a **cognitive operating layer** in which persistent knowledge can be selectively activated, used, verified, measured and eventually improved through evidence.

```text
                    ┌──────────────────────┐
                    │       HUMAN / USER   │
                    └──────────┬───────────┘
                               │
                               ▼
                    ┌──────────────────────┐
                    │ EXECUTIVE / CONTROL  │
                    └──────────┬───────────┘
                               │
             ┌─────────────────┼─────────────────┐
             ▼                 ▼                 ▼
         MEMORY            SKILLS             AGENTS
             │                 │                 │
             └─────────────────┼─────────────────┘
                               ▼
                    ┌──────────────────────┐
                    │ SELECTIVE RETRIEVAL  │
                    │ + WORKING CONTEXT    │
                    └──────────┬───────────┘
                               ▼
                         PLAN / ROUTE
                               │
                  ┌────────────┼────────────┐
                  ▼            ▼            ▼
                DIRECT      SPECIALIST    COUNCIL
                  │            │            │
                  └────────────┼────────────┘
                               ▼
                             TOOLS
                               │
                               ▼
                         VERIFICATION
                               │
                               ▼
                            OUTCOME
                               │
                               ▼
                            EVIDENCE
                               │
                               ▼
                         CONSOLIDATION
                               │
                               ▼
                    BETTER FUTURE RETRIEVAL
```

The design principle is:

```text
ONE VAULT
ONE CANONICAL MEMORY
MULTIPLE AI CLIENTS
SPECIALIZED CAPABILITIES
CONTROLLED RETRIEVAL
CONTROLLED WRITE
TRACEABLE PROVENANCE
MEASURABLE EXECUTION
```

---

# 🧠 Cognitive Architecture

AI Memory Vault combines several layers rather than forcing every problem into one mechanism.

## Persistent memory

```text
00_CORE        identity, rules, protocols
01_KNOWLEDGE   durable knowledge, registries, maps
02_PROJECTS    project continuity
03_PROCEDURES  repeatable procedures and workflows
04_MEMORY      canonical memory
05_RESOURCES   references and Obsidian navigation
06_INBOX       incoming and review-stage material
99_SYSTEM      system contracts and synchronization rules
```

The architecture separates **storage**, **retrieval**, **reasoning**, **verification** and **promotion**.

## Cognitive core

`cognitive_core/` contains active cognitive and orchestration primitives including:

- activation and decay;
- consolidation and reconsolidation;
- motivation and utility signals;
- Global Workspace-style competition/broadcast;
- Memory V6 retrieval and maintenance components;
- model/provider abstraction;
- deterministic model-tier routing;
- Council execution composition;
- actual usage telemetry;
- efficiency reporting;
- outcome-labeling and evidence tooling.

---

# 🚀 Memory V6

Memory V6 is the additive cognitive-memory layer extending the base Vault with retrieval, lifecycle, maintenance and evaluation mechanisms.

### Cognitive primitives

- **ACT-R-inspired activation / decay** — `cognitive_core/activation.py`
- **Reconsolidation** — `cognitive_core/consolidation.py`
- **Motivation / utility** — `cognitive_core/motivation.py`
- **Global Workspace** — `cognitive_core/global_workspace.py`

### Memory V6 capabilities

- **Ephemeral sensor buffering** — temporary session material without automatically polluting canonical storage.
- **Atomic memory extraction** — facts, decisions, procedures and lessons.
- **Local Ollama extraction** — optional local-model augmentation.
- **Proposal queue** — review-stage candidates before canonical promotion.
- **Conflict detection** — explicit conflict and negation analysis.
- **Human-gated promotion** — approval before new material becomes canonical memory.
- **Multi-graph memory** — semantic, temporal, causal and entity-oriented derived views.
- **Spreading activation** — graph-aware activation and ranking.
- **Sleep-phase consolidation** — maintenance-oriented processing and reporting.
- **Retrieval benchmarking** — LoCoMo-oriented Precision@K, Recall@K and MRR tooling.
- **Context-budget enforcement** — hard byte/token bounds with fail-closed behavior.
- **Execution telemetry** — estimated vs actual model usage.
- **Efficiency reporting** — per-run, per-agent, per-tier and historical analysis.

The design target is:

```text
large persistent knowledge
            ↓
      selective retrieval
            ↓
      bounded context pack
            ↓
        active reasoning
```

---

## 🛡️ Unified Secure Memory Retrieval Policy

All agents (Claude Code, Antigravity, Codex, etc.) access vault memory exclusively through authorized, audited interfaces:

- **Primary Interface**: Local REST API (`http://localhost:8000/memory/search?query=...`)
- **Secure Fallback CLI**: `python -m cognitive_core.recall_cli --query "..."` (safe by design, delegates directly to `MemoryController.search()` under `Principal.AI_AGENT`)

### Enforced Trust Controls:
1. **P0–P15 Trust Boundary Gating**: Strict enforcement preventing AI self-verification (`verification = 'verified'` is rejected), privileged provenance claiming (`user`, `official` rejected for AI), and creation lifecycle escalation (proposals restricted to `RAW`, `CLASSIFIED`, `NORMALIZED`, `REVIEW`).
2. **RAW Lifecycle Exclusion**: Unvetted `RAW` notes are filtered out of all normal search results.
3. **Query Boundary & Sanitization**: Strict 4,096-character limit with injection sanitization.
4. **Tamper-Evident Audit Logging**: Cryptographically chained SHA-256 hash logging (`audit_log.jsonl`).
5. **No Direct Filesystem Scans**: Raw `os.walk` traversals and unauthenticated memory bypasses are strictly prohibited across all runtimes.

---

# 🤖 Agents & Council

The repository contains a specialized agent ecosystem coordinated through capability and skill matching.

Representative domains include:

```text
Architecture        Backend / APIs       Frontend / SaaS
Web Creative        Web Design           Web Quality / Performance
UI / UX             WPF / .NET           Compilers / Tooling
Polyglot Systems    DevOps / SRE         Security / SecOps
Threat Hunting      Databases            Local AI / LLM
Quant Development   Game Engineering    Content Strategy
Agentic Workflows   Memory Architecture
```

Canonical relationship:

```text
Agent
  ↕
Capability
  ↕
Skill
  ↕
Procedure
  ↕
Knowledge / Memory
```

Agent profiles live under `.agents/agents/`; routing and coverage are maintained through the project knowledge maps.

---

# 📦 Skills System

Skills are treated as reusable **capabilities**, not just prompt snippets.

## Canonical Skill Catalog (3,699 Physical Skills)

```text
.agents/skills/
```

The active skill tree contains **3,699 physical skill directories** owning an independent `SKILL.md`, cataloged without drift in:

```text
01_KNOWLEDGE/Master_Skills_Catalog_251.md
```

### Population Breakdown:
- **3,448 Extracted Canonical Skills**: Imported from verified repositories with formal `PROVENANCE.json` lineage (representing the 3,450 extraction baseline minus 2 permanently removed critical skills).
- **252 Native / Core Skills**: Pre-existing skills native to the vault's core capabilities.
- **Automated GitHub Actions Regeneration**: Every commit to `main` executes the `regenerate-skill-catalog` job in `.github/workflows/memory-v6-tests.yml`, guaranteeing continuous 1:1 synchronization between physical directories and catalog entries (0 difference).

## UI/UX Pro Max

A new broad UI/UX capability has been integrated:

```text
.agents/skills/ui-ux-pro-max/SKILL.md
```

It is intentionally **complementary** to narrower skills already present:

```text
ui-ux-pro-max
├── broad design intelligence
├── design-system reasoning
├── accessibility
├── interaction
├── responsive layout
├── typography / color
├── charts / visualization
└── stack-aware UI guidance

ui-ux-review
├── structured heuristic audit
└── accessibility / usability review

web-performance
└── web performance specialization

web-best-practices
└── web implementation / platform specialization

data-viz-design
└── visualization specialization
```

This avoids duplicate ownership while allowing the broad skill to establish the design direction and specialized skills to handle their narrow procedures.

### Progressive disclosure

The intended loading model is:

```text
capability metadata
      ↓
relevant rules
      ↓
detailed references
      ↓
stack-specific guidance
```

A large skill library should not mean that every skill is loaded into every prompt.

---

# 🧰 Model Providers & Execution

The model execution layer is provider-neutral:

```text
                  ModelTierRouter
                         │
          ┌──────────────┼──────────────┐
          ▼              ▼              ▼
        Fake           Local          OpenAI
       provider        Ollama        Responses
          │              │              │
          └──────────────┼──────────────┘
                         ▼
               ModelResponse + Usage
                         │
                         ▼
               ActualUsageTelemetry
```

Implemented abstractions include:

- `ModelProvider` contract;
- deterministic `ModelTierRouter`;
- `FakeModelProvider` for deterministic tests;
- `LocalProvider` for Ollama;
- `OpenAIProvider` using the Responses API;
- per-call usage capture;
- efficiency reporting.

The deterministic fake-provider path remains the baseline for development and CI. OpenAI support exists without requiring OpenAI for routine testing.

---

# 🏛️ Deterministic Council Budgeting

Council decisions are intentionally derived from the actual execution plan rather than from wording alone.

```text
ActivePlan.steps
      ↓
PlanComplexityAnalyzer
      ↓
ExecutionMode
      ↓
CouncilBudgetController
      ↓
CouncilTier
      ↓
SubagentSpec.model_tier
```

Important invariants include:

- simple work avoids unnecessary Council activation;
- moderate work can remain on a light tier;
- destructive work escalates to high-risk handling;
- the same plan produces the same classification;
- wording changes do not alter classification when the resulting plan is identical;
- a replan re-derives Council routing from the replacement plan.

Routing and risk allocation remain deterministic and observable at the application boundary.

---

# 📊 Execution Economics

Model usage is treated as first-class telemetry rather than an afterthought.

The B4/B5 reporting layer can capture:

```text
run_id
provider / model / model_tier
specialist_calls
synthesis_calls
total_model_calls
estimated_input / actual_input
estimated_output / actual_output
cached_input
reasoning_tokens
estimated_total / actual_total
per_agent_usage
per_tier_usage
context_bytes
context_estimated_tokens
wall_time_seconds
tokens_per_specialist
tokens_per_council_run
tokens_per_synthesis
```

The objective is not minimum tokens at any cost. The useful metric is:

```text
quality gained
───────────────
resource spent
```

---

# 🧪 Outcome, Evidence & Future Learning

The project distinguishes execution evidence from canonical memory.

```text
REAL EXECUTION
      ↓
OUTCOME
      ↓
EVIDENCE
      ↓
EVALUATION
      ↓
CAPABILITY CANDIDATE
      ↓
SKILL EVOLUTION
      ↓
HUMAN GATE WHEN REQUIRED
      ↓
FUTURE USE
```

Current outcome tooling is deliberately isolated:

```text
scripts/label_council_outcome.py
cognitive_core/tests/test_label_council_outcome.py
```

An execution outcome is evidence about what happened. It is **not automatically a canonical memory record**.

This separation is essential for future skill evaluation because the system must first know whether an action worked before treating the experience as useful learning signal.

---

# 🔐 Trust, Provenance & Security

External material is untrusted until classified and validated.

```text
EXTERNAL SOURCE
      ↓
RAW_EXTERNAL
      ↓
PROVENANCE
      ↓
DEDUP / VALIDATION
      ↓
HUMAN-GATED PROMOTION
      ↓
OPERATIONAL CAPABILITY
```

Imported material should retain, where available:

```text
source_repository
source_url
source_path
source_commit
source_branch
license
author
discovery path
discovery depth
sha256
status
```

The external ingestion pipeline computes hashes, classifies imported `SKILL.md` material and keeps it within the RAW boundary until explicit promotion. fileciteturn581file0L2-L6

The dedicated external-skill workflow is manual and avoids executing arbitrary imported dependencies during ingestion. fileciteturn590file0L2-L6

---

# 🔎 Skill Ingestion & Deduplication

External repositories are **sources**, not automatically trusted skills.

The intended lifecycle is:

```text
Source
  ↓
Discovery
  ↓
Candidate skill
  ↓
Duplicate / similarity check
  ↓
Classification
  ↓
License + provenance
  ↓
RAW_EXTERNAL
  ↓
Validation
  ↓
Explicit promotion
  ↓
Operational skill
```

A repository can contain code, documentation, examples or full applications that should remain reference material rather than being copied wholesale into the skill library.

The project also maintains a validation report for the imported raw skill corpus and its safety invariants. fileciteturn586file0L2-L6

---

# 🗺️ Obsidian as Cognitive Interface

Obsidian is the human navigation, visualization and inspection surface for the same Vault.

It exposes relationships between:

```text
Skill ↔ Agent ↔ Capability ↔ Procedure ↔ Knowledge ↔ Memory
```

Important navigation documents include:

```text
01_KNOWLEDGE/Agents_Skill_Matrix.md
01_KNOWLEDGE/Master_Skills_Catalog_251.md
05_RESOURCES/Obsidian/Skill_Agent_Memory_MOC.md
99_SYSTEM/Obsidian_Skill_Agent_Memory_Sync.md
```

The goal is one underlying data layer shared by AI clients and the human operator.

---

# 📚 Knowledge & Graph Architecture

The Vault is deliberately multi-representational:

```text
Markdown knowledge
      +
structured registries
      +
procedures
      +
skills
      +
agent definitions
      +
memory records
      +
retrieval indexes
      +
graphs
      +
execution telemetry
```

Graph-oriented components provide derived views for semantic, temporal, causal and entity relationships while retrieval remains responsible for selecting the active working context.

This makes it possible to experiment with graph-based reasoning without turning the graph into an unverified second source of truth.

---

# 🧪 Testing & Continuous Verification

Testing is layered across:

```text
cognitive_core/tests/
memory_controller/tests/
tests/
```

and the primary Memory V6 workflow is:

```text
.github/workflows/memory-v6-tests.yml
```

The project favors:

- deterministic fake-provider tests;
- mocked external-provider tests in standard CI;
- explicit opt-in live Ollama tests;
- AST-based structural boundary tests;
- fail-closed context-budget tests;
- regression tests for real incidents;
- repository-level regression coverage in addition to cognitive-core tests.

CI is the authority for commit-level pass/fail claims; local test output is supporting evidence.

---

# 🛡️ Security Invariants & Verified Baseline

The repository enforces strict trust boundaries and forensic invariants to prevent unauthorized AI self-elevation and memory tampering:

### 1. Canonical Security Model Nomenclature
- **`P0`**: Phase 4.3 Priority-0 Security Hardening designation.
- **`P0-001` .. `P0-015`**: 15 adversarial test contracts defined in `99_SYSTEM/Phase43_P0_Implementation_Contract.md`.
- **`I-001` .. `I-012`**: Canonical Phase 4.3 memory security invariants (blocking AI self-verification, preventing privileged provenance forging, restricting proposal lifecycles to non-authoritative states, enforcing provenance immutability, ensuring zero partial writes on rejection, requiring human/admin attestation, and isolating supersession trust).
- **`I-RETRIEVAL`**: Unified secure retrieval invariant introduced subsequently (delegating all recall to `MemoryController.search()`, prohibiting unauthenticated filesystem traversals).
- **`P1 / P2 / P3`**: Forensic priority tiers (correctness/data integrity, architectural weakness, maintainability).
- **`P16 / P17 / P18`**: Desktop / hardware forensics invariants codified in `.agents/rules/vault_cognitive_rules.md` (hardware immutability, friendly name isolation, and chain-of-custody audit logging).
- **`P0-P18`**: Umbrella shorthand only; must not be interpreted as 19 sequential memory invariants.

### 2. Authoritative Reconciliation & Nomenclature Artifacts
Full archaeological audit, test mappings, and terminology standards are maintained in:
- `07_EVALUATION/security_invariant_nomenclature_2026-09.md`
- `07_EVALUATION/security_invariant_nomenclature_2026-09.json`
- `07_EVALUATION/security_invariant_reconciliation_2026-09.md`
- `07_EVALUATION/security_invariant_reconciliation_2026-09.json`

### 3. Verified Post-Cleanup Baseline
- **Defender Hygiene**: 6 weaponized XSS payloads in `06_INBOX/RAW_IMPORTS/` confirmed by Windows Defender were deleted; 2 critical active skills (`sandbase-mcp`, `aspire`) permanently removed.
- **Corpus Integrity**: The raw external research corpus (`66,750` files across 85 repositories) remains fully preserved without broad purge.
- **Baseline Manifest**:
  - `07_EVALUATION/reports/post_cleanup_baseline_2026-09.md`
  - `07_EVALUATION/post_cleanup_baseline_2026-09.json`

---

# 🏗️ Repository Structure

```text
AI_Memory_Vault_CODEX_READY/
│
├── .agents/
│   ├── agents/                 # Agent profiles
│   ├── rules/                  # Rules / system behavior
│   └── skills/                 # Operational skills
│
├── .claude-plugin/             # Claude Code plugin metadata
│
├── 00_CORE/                    # Identity, rules, memory protocol
├── 01_KNOWLEDGE/               # Knowledge, registries, maps
├── 02_PROJECTS/                # Project continuity
├── 03_PROCEDURES/              # Repeatable procedures
├── 04_MEMORY/                  # Canonical memory
├── 05_RESOURCES/               # Resources / Obsidian navigation
├── 06_INBOX/                   # Incoming / RAW_EXTERNAL material
├── 99_SYSTEM/                  # System contracts and synchronization
│
├── cognitive_core/             # Cognitive runtime and orchestration
├── memory_controller/          # Memory access and context control
├── scripts/                    # CLI and operational utilities
├── tasks/                      # Plans, lessons and engineering notes
├── tests/                      # Repository-wide tests
├── .github/workflows/          # CI, maintenance and ingestion
└── README.md                   # System overview
```

---

# 🛠️ Important Entry Points

| Area | Entry point |
|---|---|
| Core identity & rules | `00_CORE/` |
| Knowledge | `01_KNOWLEDGE/` |
| Procedures | `03_PROCEDURES/` |
| Canonical memory | `04_MEMORY/` |
| Incoming/raw data | `06_INBOX/` |
| System architecture | `99_SYSTEM/Memory_V6_Architecture.md` |
| Secure Retrieval CLI (safe by design) | `cognitive_core/recall_cli.py` |
| Memory Controller & Trust Invariant Gating (`I-001..I-012`, `I-RETRIEVAL`) | `memory_controller/controller.py` |
| Agent/skill mapping | `01_KNOWLEDGE/Agents_Skill_Matrix.md` |
| Canonical Skill Catalog (3,699 skills) | `01_KNOWLEDGE/Master_Skills_Catalog_251.md` |
| UI/UX Pro Max | `.agents/skills/ui-ux-pro-max/SKILL.md` |
| Security Invariant Nomenclature Standard | `07_EVALUATION/security_invariant_nomenclature_2026-09.md` |
| Security Invariant Reconciliation Report | `07_EVALUATION/security_invariant_reconciliation_2026-09.md` |
| Verified Post-Cleanup Baseline | `07_EVALUATION/reports/post_cleanup_baseline_2026-09.md` |
| Skill ingestion | `scripts/skill_ingestion.py` |
| Outcome labeling | `scripts/label_council_outcome.py` |
| Obsidian synchronization | `99_SYSTEM/Obsidian_Skill_Agent_Memory_Sync.md` |

---

# 🧭 Design Principles

### 1. One canonical source of truth

Do not create competing authoritative copies of the same decision.

### 2. Evidence over confidence theater

Confidence is metadata; evidence is support.

### 3. Retrieval before context inflation

More context is not automatically better context.

### 4. Deterministic resource allocation

Risk, complexity and budget decisions should remain reproducible wherever possible.

### 5. Human-gated durable promotion

New or untrusted information does not silently become canonical memory.

### 6. Provenance survives ingestion

External material should remain attributable to its origin and version.

### 7. Skills compose instead of colliding

Broad capabilities provide general guidance; specialized skills retain ownership of specialized procedures.

### 8. Failure is data

Failures, corrections and partial outcomes are useful learning evidence.

### 9. Measure before automating

A mechanism should be observable and testable before it becomes autonomous.

### 10. Capability gain beats component count

A larger architecture is not automatically a smarter one.

---

# 🔬 Cognitive Evolution Direction

The longer-term research direction is controlled improvement through evidence, not uncontrolled self-modification.

```text
REAL TASK
   ↓
EXECUTION TRACE
   ↓
OUTCOME + EVIDENCE
   ↓
EVALUATION
   ↓
PATTERN / FAILURE / PROCEDURE DISCOVERY
   ↓
CAPABILITY CANDIDATE
   ↓
SANDBOX / SHADOW
   ↓
REGRESSION + HOLDOUT
   ↓
HUMAN-GATED PROMOTION
```

Potential future capabilities include:

- capability discovery from repeated experience;
- failure and correction mining;
- skill refinement and versioning;
- retrieval-policy optimization;
- cognitive-state and uncertainty tracking;
- adaptive model routing;
- memory and skill consolidation;
- controlled skill retirement and rollback.

These are development directions, not claims that every stage already exists.

---

# 📐 Operating Philosophy

The repository is designed around a simple loop:

```text
KNOWLEDGE
    ↓
EVIDENCE
    ↓
COGNITIVE STATE
    ↓
ATTENTION
    ↓
WORKING CONTEXT
    ↓
PLAN
    ↓
ACTION
    ↓
VERIFICATION
    ↓
OUTCOME
    ↓
CONSOLIDATION
    ↓
BETTER FUTURE ACTION
```

The desired end state is not simply a larger database. It is a system that can **select what matters, use it efficiently, verify what it did, retain what is justified, and gradually improve future problem-solving without sacrificing traceability or control**.

---

# 📌 Current Status

| Capability | Status |
|---|---|
| Canonical Vault | Active |
| Memory V6 | Active |
| Secure Retrieval CLI (`recall_cli`) | Active (Safe by Design, `I-RETRIEVAL` / `I-001..I-012` gated) |
| Security Invariants (`I-001..I-012`, `P16-P18`) | Reconciled & Standardized (37 passing security tests) |
| Canonical Skill Catalog | 3,699 Active Physical Directories (CI Synchronized) |
| Windows Defender Hygiene | Clean (6 XSS payloads & 2 critical skills removed) |
| Multi-agent / Council architecture | Active |
| Deterministic complexity & budget routing | Active |
| Fake / Local / OpenAI model providers | Implemented |
| Actual usage telemetry | Implemented |
| B4/B5 efficiency reporting | Implemented |
| External skill ingestion | Active |
| UI/UX Pro Max skill | Integrated |
| Human-gated memory promotion | Active |
| Outcome labeling | Available |
| Automated skill evolution | Research / next-stage |

---

# 🔗 Navigation

- [Memory V6 Architecture](99_SYSTEM/Memory_V6_Architecture.md)
- [Agent ↔ Skill Matrix](01_KNOWLEDGE/Agents_Skill_Matrix.md)
- [Master Skill Catalog (3,699 skills)](01_KNOWLEDGE/Master_Skills_Catalog_251.md)
- [Security Invariant Nomenclature Standard](07_EVALUATION/security_invariant_nomenclature_2026-09.md)
- [Security Invariant Reconciliation Report](07_EVALUATION/security_invariant_reconciliation_2026-09.md)
- [Verified Post-Cleanup Baseline](07_EVALUATION/reports/post_cleanup_baseline_2026-09.md)
- [Frontend UI/UX Standards](01_KNOWLEDGE/MOC_Frontend_UI_UX_Standards.md)
- [UI Sensei Design Philosophy](01_KNOWLEDGE/UI_Sensei_Design_Philosophy.md)
- [UI/UX Pro Max](.agents/skills/ui-ux-pro-max/SKILL.md)
- [Skill Ingestion](scripts/skill_ingestion.py)
- [Obsidian Skill/Agent/Memory Sync](99_SYSTEM/Obsidian_Skill_Agent_Memory_Sync.md)
- [External Skill Imports](06_INBOX/RAW_IMPORTS/skills/)
- [GitHub Actions](https://github.com/userist123/AI_Memory_Vault_CODEX_READY/actions)

---

<p align="center">
  <sub>AI_Memory_Vault_CODEX_READY — persistent memory, selective cognition, controlled capabilities.</sub>
</p>
