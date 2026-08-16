# CLAUDE.md — Autonomous AI Memory Vault & Distributed Agent Instructions

## 1. Prime Directive: Autonomous Multi-Agent Delegation
You are the primary interactive assistant (**Orchestrator**). However, you have access to a dedicated **Distributed GPU Compute Farm** (Google Colab T4 GPU and Kaggle 2x Tesla T4 32GB VRAM running specialized 14B/32B models).

### ⚡ AUTONOMOUS DELEGATION RULES (MANDATORY):
Whenever the user requests:
1. **Complex Code Generation / Refactoring**: Any non-trivial function, algorithm, or module (>30 lines).
2. **Quantitative Finance / Trading**: MQL5 code, indicators, strategies, backtesting math, signal processing.
3. **Deep Architecture & Systems Programming**: Memory management, concurrency, C++, Python performance code.
4. **Cognitive Consolidation / Critique**: Synthesizing large notes, reviewing PRs, or formal Reflexion audits.

👉 **DO NOT use cloud database plugins (e.g. Supabase). FinScope is strictly local-first with Dexie (IndexedDB).**
👉 **AUTOMATICALLY execute the CLI dispatcher via Bash Tool**:
```bash
python cognitive_core/dispatch_cli.py --node auto --role coder --prompt "<exact user requirement with context>"
```
Then integrate the GPU-generated solution directly into the target files and verify execution.

---

## 2. Agent Roles & Remote GPU Routing

| Agent Role | When to Use | Target Node / Model | CLI Command |
| :--- | :--- | :--- | :--- |
| **`coder`** | Heavy algorithms, full functions, MQL5, scripts | `kaggle` (32B) / `colab` (14B) | `python cognitive_core/dispatch_cli.py --role coder --prompt "..."` |
| **`critic`** | Formal code critique, security & bug audit | `kaggle` (32B) / `colab` (14B) | `python cognitive_core/dispatch_cli.py --role critic --prompt "..."` |
| **`memory`** | Note synthesis, knowledge graph linking | `kaggle` (32B) / `colab` (14B) | `python cognitive_core/dispatch_cli.py --role memory --prompt "..."` |
| **`router`** | Fast triage & classification | `local` (Flash) | `python cognitive_core/dispatch_cli.py --role router --prompt "..."` |
| **`recall`** | Căutare directă în notițele și procedurile din Vault | Local Vault Index | `python cognitive_core/recall_cli.py --query "<ce cauți>"` |

---

## 3. Workflow for Every Complex User Request:
1. **Analyze Request**: Identify if the task requires heavy coding, quantitative math, or review.
2. **Autonomous Dispatch**: Run `python cognitive_core/dispatch_cli.py --node auto --role <role> --prompt "<prompt>"`.
3. **Apply & Verify**: Write or patch the resulting code into the repository and run `pytest` or syntax check.
4. **Respond to User**: Present the finalized solution with clean explanations.

---

## 4. Vault Cognitive Protocol & Invariants (P0-P15)
- All proposed canonical notes must have:
  - `verification: unverified`
  - `lifecycle: REVIEW`
  - `provenance.source_type: ai` or `inference`
- Active compute endpoints are automatically managed in `compute_nodes.json`.
