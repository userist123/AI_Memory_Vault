# AI Memory Vault — CODEX Ready
> **Cognitive Memory Architecture, Persistent Agent Continuity & Epistemic Substrate for Autonomous Multi-Agent Systems**

[![Architecture: Cognitive Memory](https://img.shields.io/badge/Architecture-Cognitive%20Memory%20v2-blue.svg)](#architecture)
[![Evaluation: Evidence-Gated](https://img.shields.io/badge/Evaluation-Evidence--Gated%20MVE-green.svg)](#evaluation--benchmarks)
[![Python: 3.10+](https://img.shields.io/badge/Python-3.10%2B-blue.svg)](#requirements)
[![Status: Active Research & Hardening](https://img.shields.io/badge/Status-Active%20Hardening-orange.svg)](#status)

---

## 1. Overview & Vision

**AI Memory Vault** is an externalized, persistent, and verifiable cognitive memory architecture designed for autonomous LLM agents (Codex, Antigravity, Perplexity, Luna). 

Rather than treating memory as an ungrounded string storage or a naive vector lookup (RAG), the Vault models experience as **conditioned state transitions anchored in immutable evidence**:

$$\text{Memory} = \langle \text{Situation}, \text{Goal}, \text{Constraint}, \text{Action}, \Delta\text{State}, \text{Outcome}, \text{Evidence}, \text{Bitemporal Bounds} \rangle$$

The system bridges passive retrieval and active agent control by compiling historical invariants, failure boundaries, and planning priors into machine-checkable execution contracts.

---

## 2. Core Architectural Pillars

```text
                                 [ RAW ENVIRONMENT & TELEMETRY ]
                                                │
                                                ▼
                                    [ IMMUTABLE EVENT LOG ]
                                (06_INBOX / Append-Only Ledger)
                                                │
                     ┌──────────────────────────┴──────────────────────────┐
                     ▼                                                     ▼
           [ COGNITIVE CORE ]                                     [ MEMORY CONTROLLER ]
    - MultiGraph Memory (4 views)                           - Context Pack Builder
    - Spreading Activation Engine                           - Progressive Disclosure
    - Sleep Consolidation & Replay                          - Bitemporal & Lifecycle Gate
    - Causal Invariant Extractor                            - Action-Applicability Contracts
                     │                                                     │
                     └──────────────────────────┬──────────────────────────┘
                                                │
                                                ▼
                                 [ MULTI-AGENT RUNTIME BUS ]
               ┌────────────────┬───────────────┴────────────────┬───────────────┐
               ▼                ▼                                ▼               ▼
           [ CODEX ]     [ ANTIGRAVITY ]                   [ PERPLEXITY ]     [ LUNA ]
            (Build)        (Observability)                   (Research)       (Audit)
```

### A. Grounded Knowledge Hierarchy
- **00_CORE**: Core operating rules, system architecture, cognitive invariants, and immutable security boundaries.
- **01_KNOWLEDGE**: Formatted knowledge documents, benchmark forensics, and empirical bottleneck findings.
- **02_PROJECTS**: Project ledgers, tracking continuity across complex engineering goals.
- **03_PROCEDURES**: Canonical, versioned Standard Operating Procedures (SOPs).
- **04_MEMORY**: Structured canonical memory units (Facts, Concepts, Lessons, Decisions).
- **06_INBOX**: Staging area for raw imports, unverified telemetry, and proposals.
- **07_EVALUATION**: Quantitative benchmarks (LoCoMo, MVE Planning, Ablation tests, Security audits).
- **09_COORDINATION**: Cross-agent protocols, persistent agent memory state, and dispatch queues.

### B. The 4 Influence Channels
Memory influences agent computation across four decoupled channels:
1. **Representation:** Reframes task contexts using minimal high-density intermediate representations (IR).
2. **Planning:** Initializes search tree branch priors ($P(a \mid s)$ and $Q$-values) in MCTS/LATS planners.
3. **Epistemics:** Evaluates evidence debt and contradiction density, triggering active verification probes.
4. **Execution:** Enforces deterministic negative action masks directly at the driver/tool-dispatch gateway.

---

## 3. Repository Structure

```text
├── 00_CORE/                # System Architecture, Invariants, Graph Models
├── 01_KNOWLEDGE/           # Empirical findings, RAG benchmarks, LoCoMo reports
├── 02_PROJECTS/            # Active project continuity files & specifications
├── 03_PROCEDURES/          # Executable workflows and migration runbooks
├── 04_MEMORY/              # Canonical memory vault (ACTIVE/VERIFIED nodes)
├── 06_INBOX/               # Raw imports, pending proposals, quarantined artifacts
├── 07_EVALUATION/          # Test harnesses, diagnostic reports, MVE suites
├── 09_COORDINATION/        # Multi-agent persistent state (Codex, Luna, Perplexity, etc.)
├── cognitive_core/         # Graph memory, Spreading Activation, Working Memory, Models
├── memory_controller/      # Context pack compiler, relevance scoring, mutation gates
├── tests/                  # Pytest validation suites and test logs
└── requirements.txt        # Runtime dependencies
```

---

## 4. Multi-Agent Governance & Separation of Duties

To prevent epistemic contamination and benchmark gaming, agent roles are strictly separated:

| Agent | Core Responsibility | Permitted Actions | Prohibited Actions |
| :--- | :--- | :--- | :--- |
| **CODEX** | Execution & Implementation | Code fixes, regression tests, MVE runners | Modifying benchmark targets to mask bugs |
| **ANTIGRAVITY** | Observability & Tracing | Pipeline traces, dashboards, telemetry logging | Modifying core security logic without task |
| **PERPLEXITY** | Research & Acceptance Criteria | Literature anchoring, adversarial review, MVE design | Declaring unverified runtime capabilities |
| **LUNA / GPT-5.6**| Adversarial Falsification | Red-team attack, held-out validation, audit gates | Writing code on the audit branch |

---

## 5. Getting Started

### Prerequisites
- Python 3.10+
- SQLite 3.35+
- (Optional) Qdrant or local embedding provider for semantic search

### Installation
```bash
# Clone the repository
git clone https://github.com/userist123/AI_Memory_Vault_CODEX_READY.git
cd AI_Memory_Vault_CODEX_READY

# Set up virtual environment
python -m venv venv
source venv/bin/activate  # On Windows: venv\Scripts\activate

# Install dependencies
pip install -r requirements-memory-v6.txt
```

### Running Tests & Diagnostics
```bash
# Execute unit & invariant test suite
pytest tests/ -v

# Run the real retrieval diagnostic pipeline (Condition A1 vs B)
python 07_EVALUATION/retrieval_diagnostic_runner.py

# Run memory ablation benchmark
python cognitive_core/memory_ablation_benchmark.py
```

---

## 6. Evaluation & Continuous Hardening

The repository tracks all claims against empirical reality in `REALITY_SCORECARD.md`:
- **Retrieval Baseline:** Verified single-signal lexical bottleneck (6.7% coverage on cross-document queries) documented in `01_KNOWLEDGE/Retrieval_Bottleneck_P0_Empirical_Findings.md`.
- **Planning Influence MVE:** Active benchmark evaluating MCTS search efficiency under memory priors vs uniform controls in `07_EVALUATION/luna/`.
- **Security & Quarantine:** Hardened air-gap isolating instruction content from memory payloads to prevent prompt injection and authority escalation.

---

## 7. License & Attribution

Internal Research & Development — Designed for Autonomous AI Agent Systems. All rights reserved.
