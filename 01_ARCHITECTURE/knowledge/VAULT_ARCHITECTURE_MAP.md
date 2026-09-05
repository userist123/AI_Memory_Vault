---
id: "vault-architecture-map-0001"
type: knowledge
lifecycle: ACTIVE
category: meta
tags: [architecture, dataflow, lifecycle, governance]
created: 2026-09-02T00:39:00Z
updated: 2026-09-02T00:39:00Z
provenance:
  source_type: execution
  source_ref: "01_ARCHITECTURE/knowledge/VAULT_AVCHITECTURE_MAP.md"
confidence: high
verification: verified
enriched_by: ai
---

# Vault Architecture & Information Dataflow Map

## 1. Information Flow Pipeline

The I Memory Vault strictly separates research, evidence, evaluation, canonical knowledge, dynamic memory, and runtime telemetry:

```text
[09_RESEARCH / External Web / Raw Imports]
                 ┤ (Ingestion & Sanitization)
           [06_INBOX / RAW_IMPORTS]
                 ┤ (Analysis & Experimentation)
           [evaluation/ Labs & Benchmarks]
                 ┤ (Attestation & Verification)
           [01_KNOWLEDGE / 00_CORE]
                ┤ (Active Retrieval & Graph Indexing)
           [04_MEMORY / Dynamic Store]
                ┤ (Query & Relevance Scoring)
           [ContextPackBuilder.build()]
                ┤ (Physical Envelope Packaging)
           [LLM Model Prompt]
                ┤ (Passive Non-Blocking Emission)
           [telemetry/observed_memory_traces.jsonl]
                ┤ (Reconciliation & Audit)
           [evaluation/memory_usage_audit/]
```

---

## 2. Semantic Responsibility Boundaries

### Layer 1: Governance & Core Invariants (`00_GOVERNANCE/`)
* **Role**: Foundational identity, security trust boundaries P0-P18, and confidence models.
* **Mutability**: Strict human/admin attestation required.

### Layer 2: Canonical Knowledge (`01_ARCHITECTURE/knowledge/`)
* **Role**: Verified domain architectures, research findings, and protocols.
* **Mutability**: AI proposed into REVIEW; human promotes to ACTIVE.

### Layer 3: System Blueprints & Projects (`02_PRODUCT/projects/`)
* **Role**: Engineering project architectures (LogAnalyzer, Registru, XAU Kinetic).

### Layer 4: Standard Procedures (`10_DOCUMENTATION/procedures/`)
* **Role**: Execution runbooks, PowerShell scripts, and coding workflows.

### Layer 5: Dunamic Episodic Memory (`01_ARCHITECTURE/memory/`)
* **Role**: Structured decisions, errors, experiences, preferences, and lessons.

### Layer 6: Isolated Empirical Labs (`evaluation/`)
* **Role**: Self-contained laboratories for benchmarking retrieval, packing, and temporal memory.
* **Rule**: Zero production modifications allowed inside evaluation experiments.

### Layer 7: Machine Telemetry (`telemetry/`)
* **Role**: Append-only, tamper-evident logs of physical runtime context (`observed_memory_traces.jsonl`).
* **Rule**: Data minimization (zero prompt strings or note body leakage).

### Layer 8: Archive (`10_ARCHIVE/`)
* **Role**: Preserved superseded legacy duplicates with complete Git history and provenance.
