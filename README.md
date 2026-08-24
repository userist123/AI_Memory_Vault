---
category: index
status: active
version: 4.0.0
confidence: high
verification: verified
provenance_status: official
relations: []
---

# AI Memory Vault (v4.0.0)

> Codex operating contract: [[AGENTS.md]] | Invariante de Securitate: [[vault_cognitive_rules.md]]

A persistent, trust-boundary-enforced memory and cognitive architecture for AI agents, designed to serve as the canonical shared knowledge backend for one or more coding/reasoning agents (local or cloud) working on the same projects over time -- without relying on copying conversation history between them.

---

## What This Repository Actually Is

This is **not** a plain note vault. It is a working Python system with five integrated layers:

1. **`memory_controller/`** -- the canonical, security-hardened memory store (SQLite WAL mode & file-backed).
2. **`cognitive_core/`** -- the reasoning/orchestration layer built on top of it, driving the Consiliul celor 21 de Agenți Specializați.
3. **`00_CORE/` .. `99_SYSTEM/`** -- the Obsidian-compatible Markdown vault itself (rules, knowledge, projects, procedures, memory, resources).
4. **`00_CORE/GRAPH/`** -- an Obsidian graph-view navigation layer (Maps of Content) sitting on top of the vault content, for human visual navigation only.
5. **`.agents/` & `06_INBOX/RAW_IMPORTS/skills/`** -- the operational agent skill repository (251 local skills + access to 50,000+ global skills and raw ingestion pipeline).

---

## 1. Memory Layer -- `memory_controller/`

`MemoryController` is the single, canonical entry point for every memory read or write. It enforces:

- **Authorization** -- per-operation policy (`propose`, `read`, `search`, `review`, `promote`, `archive`, `update`, `supersede`, `attest`) scoped to `HUMAN` / `AI_AGENT` / `ADMIN` principals.
- **Provenance** -- every note records `source_type` (`user`, `official`, `ai`, `inference`, `execution`, `import`, ...), gated per principal so an AI agent cannot self-claim a human/official source.
- **Lifecycle** -- `RAW -> CLASSIFIED -> NORMALIZED -> REVIEW -> VERIFIED -> ACTIVE -> {SUPERSEDED, ARCHIVED}`, with injection of privileged states blocked at creation for untrusted callers.
- **Verification / Attestation** -- `verification` (`unverified`, `partially_verified`, `verified`, `inferred`) can only reach `verified` through the dedicated `attest()` method, restricted to `HUMAN`/`ADMIN`. No agent can self-escalate `unverified -> verified`.
- **Audit Chain** -- every operation (success or failure) is logged with actor, target, outcome, metadata, and SHA-256 tamper-evident cryptographic hash chaining.
- **Supersession** -- explicit, atomic, cycle-free replacement of one memory by another, with human-verified memories protected from automatic AI-driven supersession.
- **Storage backends** -- `FileStorageEngine` (canonical Markdown+YAML files, Obsidian-compatible) and `SQLiteStorageEngine` (production-grade, WAL mode, `PRAGMA busy_timeout=5000`, `BEGIN IMMEDIATE` atomic transactions).
- **P16-P18 Hardware Telemetry & Forensics** -- Physical environment identifiers (VID, PID, Serial Number, Physical Capacity, System Host ID, Timestamp, SHA-256) are strictly Read-Only and tamper-evident; users may modify logical volume labels only.

This trust boundary was hardened under `99_SYSTEM/Phase43_P0_Implementation_Contract.md` and `vault_cognitive_rules.md`.

---

## 2. Cognitive Layer -- `cognitive_core/`

`Executive` runs the main cognitive loop (**Observe -> Retrieve -> Attend -> Reason -> Plan -> Act -> Reflect -> Learn**), coordinating:

- `ActivationEngine` / `RecallEngine` -- spreading-activation retrieval with semantic + authority + temporal + version-aware scoring.
- `WorkingMemory` -- bounded, attention-weighted active context.
- `ReasoningEngine`, `Planner` -- context-aware multi-step planning and synthesis (Tree-of-Thought).
- `ReflectionPipeline`, `LearningEngine`, `Consolidator`, `Deduplicator` -- automatic lesson capture, confidence promotion, lesson consolidation, and duplicate detection, all gated through `ToolRouter` so writes stay inside the same trust boundary as direct `MemoryController` calls.

---

## 🏛️ 3. Consiliul celor 21 de Agenți Specializați (`.agents/agents/`)

`MultiAgentOrchestrator` (`cognitive_core/orchestrator.py`) coordinates specialized worker agents defined in `.agents/agents/` and `cognitive_core/agents/`, each scoped to a minimal `permitted_actions` set under the Multi-Agent Least Privilege (MNP) principle:

| # | Agent ID | Rol / Specializare | Competențe Cheie |
|---|---|---|---|
| 1 | `compiler_and_tooling_engineer` ⚙️ | Compiler & Tooling Engineer | Compilatoare, AST Parsers, TinyCC, CPython, Roslyn, LLVM, JIT Tuning, Code Refactoring |
| 2 | `site_reliability_and_devops_architect` 🚀 | SRE & Cloud Automation | Kubernetes, Helm, ArgoCD GitOps, Terraform IaC, Ansible, AWS, Azure, GCP, Istio, Prometheus |
| 3 | `polyglot_systems_architect` 🌐 | Polyglot Systems Architect | Architecture Multi-Limbaj (C#, Go, Rust, Python, TypeScript, C++20 Drogon) |
| 4 | `system_architecture_agent` 🏛️ | Enterprise Architecture Agent | Arhitectură Enterprise .NET 10, Isolation Loopback (`127.0.0.1`), Vault Secrets |
| 5 | `backend_systems_engineer` ⚡ | Backend Microservices Engineer | REST, gRPC, GraphQL, Redis Caching, CQRS, Outbox CDC, Saga Pattern, SQLite WAL |
| 6 | `secops_auditor` 🛡️ | SecOps & DevSecOps Auditor | Audit OWASP Top 10, SAST, DAST, Container Scan, Secret Leak, Zero Trust, OPA Rego, HG 585 |
| 7 | `threat_hunting_analyst` 🔍 | Threat Hunting Analyst | DFIR Operations, YARA/Sigma Offline Engine, Forensic Analysis, Containment Runbooks |
| 8 | `wpf_engineer` 🖥️ | WPF .NET 10 Engineer | C# WPF Enterprise, MVVM, Obsidian Tactical UI Tokens, Async Thread Safety |
| 9 | `web_creative_developer` 🌟 | Creative Web & 3D Developer | Three.js, Shaders, GSAP ScrollTrigger, Lenis, CobeJS, VantaJS, MatterJS, Globe GL |
| 10 | `web_design_engineer_agent` 🎨 | Web Design Engineer | Design Systems (Linear, Apple, Stripe, Vercel, Supabase), Agency Grids, Editorial Tech |
| 11 | `web_quality_engineer` ⚡ | Web Quality & Vitals Engineer | Core Web Vitals (LCP/CLS/INP), WCAG 2.2 AAA Accessibility, Web Performance, SEO |
| 12 | `ui_sensei_architect` ⛩️ | UI Sensei Visual Architect | Filosofia UI Sensei, Grid 8px, Micro-Spacing, Dark Glass, Skeuomorphic Clean |
| 13 | `frontend_saas_engineer` 🌐 | Frontend SaaS Engineer | Next.js App Router, Tailwind CSS v4, TanStack Query, Zustand, Storybook, Playwright |
| 14 | `game_engineer` 🎮 | ARPG & Web Game Engineer | Isometric ARPG Engine, Tactical Combat, Enemy AI, VFX Shaders, Audio Feedback |
| 15 | `quant_developer` 📈 | Quant Trading Developer | Python Algorithmic Trading (5 Module: data/strategy/risk/execution/journal) |
| 16 | `local_ai_engineer` 🤖 | Local AI & LLM Engineer | Ollama (`127.0.0.1:11434`), Pydantic JSON Mode, LangChain, LlamaIndex, vLLM, LoRA |
| 17 | `content_strategist` ✍️ | Content & Voice Strategist | Copywriting, Technical Briefings, Presentation Design, Voice Generation |
| 18 | `agentic_workflow_orchestrator` 🔄 | Agentic Workflow Orchestrator | Router Global 50k Skill-uri, Copilot Workflows, MCP Integrations, Ciclul OODA, Reflexion |
| 19 | `ui_ux_designer` 🎨 | UI/UX Designer & Prompting | Dashboard Admin UI, Brand Identity, Data Visualization, Motion Design, UI Prompting |
| 20 | `database_and_persistence_engineer` 💾 | DB & Persistence Engineer | Flyway, Vitess Sharding, DuckDB OLAP, ClickHouse Time-Series, pgvector, Neo4j |
| 21 | `memory_controller_architect` 🧠 | Memory Controller Architect | Arhitectura Memoriei, PRAGMA WAL, SHA-256 Audit, Invariante P0-P18 |

---

## 4. Vault Layer -- Markdown Knowledge Base & Skill Corpus

```text
00_CORE/                  Identity, Rules, Memory Protocol, Confidence Model, System Architecture
00_CORE/GRAPH/            Obsidian Maps of Content (graph-view navigation layer, see section 5)
01_KNOWLEDGE/             Durable technical knowledge, Agents Skill Matrix, Master Skills Catalog
02_PROJECTS/              Active project state and continuity handoff documents
03_PROCEDURES/            Repeatable procedures & Autonomous Program Construction Protocols
04_MEMORY/                Decisions, Errors, Experiences, Lessons, Preferences
05_RESOURCES/             Reference material
06_INBOX/RAW_IMPORTS/     Raw, unprocessed imports & 06_INBOX/RAW_IMPORTS/skills/ (RAW_EXTERNAL boundary)
90_TEMPLATES/             Canonical note templates
99_SYSTEM/                Schemas, protocols, forensic/security documentation, Graph Health Report
.agents/agents/           Profiles for the 21 Specialized Subagents
.agents/rules/            Cognitive rules & security invariants (vault_cognitive_rules.md)
.agents/skills/           Physical library of 251 operational skills stored on disk
.obsidian/                Obsidian vault configuration, including graph.json folder-color groups
```

Every canonical note carries the frontmatter schema defined in `99_SYSTEM/Canonical_Frontmatter.md`: `id`, `type`, `lifecycle`, `category`, `tags`, `created`, `updated`, `provenance`, `confidence`, `verification`, `relations`.

---

## 📥 5. Ingestia Brută nealterată pe 12 Faze (`06_INBOX/RAW_IMPORTS/skills/`)

Ingestia de cunoștințe externe respectă granița strictă de securitate **`RAW_EXTERNAL`**. Materialele brute nealterate sunt stocate izolat în `06_INBOX/RAW_IMPORTS/skills/` fără a modifica memoriile canonice.

- **`SOURCE.json`** (în fiecare pachet): Proveniență `source_repository`, `source_url`, `license`, `sha256`.
- **`_SOURCE_REGISTRY.json`**: Catalogul celor 141 de surse unice procesate.
- **`_PROGRAMMING_SOURCES.json`**: Index de referință pentru 70 de limbaje și compilatoare (CPython, Rust, Go, Roslyn, TinyCC, etc.).
- **`_BACKEND_SOURCES.json`**: Index de referință pentru 51 de arhitecturi backend (Express, .NET, Rails, Spring, Vapor, NestJS).
- **`_DISCOVERY_GRAPH.json`**: Graful DAG de descoperire recursivă (fără cicluri).
- **`_VALIDATION_REPORT.md`**: Certificatele de validare a integrității SHA-256 (10/10 checks PASSED).

---

## 6. Obsidian Graph Navigation Layer -- `00_CORE/GRAPH/`

A set of `type: moc` (Map of Content) hub notes providing human-facing visual navigation across the vault's Obsidian graph view: `00 Core Map`, `01 Cognitive System Map`, `02 Memory Knowledge Map`, `06 Obsidian Graph Map`, `07 Knowledge Domains Map`, `08 Memory Subsystems Map`, `09 Agent Evidence Map`, `10 Imports and Sources Map`, `11 Templates and System Map`, `12 Projects and Procedures Map`, `13 Root and Control Map`, plus focused per-category maps for Lessons, Decisions, Errors, Experiences, and Preferences, all linked from a central `Knowledge Graph Home`. `.obsidian/graph.json` assigns folder-based color groups for visual clarity. `99_SYSTEM/Graph Health Report.md` documents what was added/corrected and validates link integrity.

**This layer is purely for human visual navigation inside Obsidian.** It has no runtime role in `MemoryController` or `cognitive_core` -- it does not affect authorization, provenance, lifecycle, or retrieval scoring. Treat MOC notes as a separate, lightweight documentation layer, not as canonical memory content.

---

## What This Project Is Designed For, Right Now

The active direction is to make this Vault the **shared, canonical project-state backend for multiple AI coding agents** (local and cloud) working on the same codebases over time -- so that continuing work does not require manually re-pasting conversation history into a new agent session. This includes:

- Autonomous multi-agent program construction protocols (`03_PROCEDURES/Autonomous_Program_Construction_Protocol.md`).
- Multi-agent review-gate mechanisms for architecture-, security-, or contract-changing decisions.
- Zero local compute consumption with air-gapped server execution via `127.0.0.1` binding.

---

## Status Discipline

This repository distinguishes explicitly between **code correctness** (verifiable by reading a diff) and **runtime verification** (requires an actual `pytest` execution against a real checkout). Historical documents in `99_SYSTEM/` record which security findings were fixed and how they were verified -- treat any claim of "fixed" or "passed" as meaningless unless it cites the actual commit and, where relevant, the actual test output.

---

## Tests

```bash
python -m pytest -q
```
run from the repository root, covering `memory_controller/tests/` and `cognitive_core/tests/`.
