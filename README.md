---
category: index
status: active
version: 4.0.0
confidence: very_high
verification: verified
provenance_status: official
relations:
  - "00_CORE/Identity.md"
  - "00_CORE/Rules.md"
  - "00_CORE/Memory_Protocol.md"
  - "01_KNOWLEDGE/Agents_Skill_Matrix.md"
  - "01_KNOWLEDGE/Master_Skills_Catalog_251.md"
---

# 🧠 AI Memory Vault & Consiliul celor 21 de Agenți Specializați

> **Codex Operating Contract**: [`AGENTS.md`](file:///C:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/AGENTS.md) | **Invariante de Securitate**: [`vault_cognitive_rules.md`](file:///C:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/.agents/rules/vault_cognitive_rules.md)

**AI Memory Vault v4.0.0** este sistemul cognitiv canonic de memorie persistentă și orchestrare multi-agent. Proiectat pentru a servi drept creier autonom de lungă durată pentru agenți AI locali și cloud, sistemul elimină necesitatea re-transmiterii istoricului de conversație și garantează **Zero Compute local nedorit** pe stație via modul loopback `127.0.0.1`.

---

## 🏛️ 1. Consiliul celor 21 de Agenți Specializați (`.agents/agents/`)

Sistemul operează cu un Consiliu de **21 de Agenți Specializați**, fiecare echipat cu roluri clare, unelte dedicate și responsabilități izolate conform principiului MNP (*Multi-Agent Least Privilege*):

| # | Agent ID | Rol Specializat | Domeniu de Competență & Skill-uri Cheie |
|---|---|---|---|
| 1 | `compiler_and_tooling_engineer` ⚙️ | Compiler & Tooling Engineer | Compilatoare, AST Parsers, TinyCC, CPython, Roslyn, LLVM, JIT Tuning, Code Refactoring |
| 2 | `site_reliability_and_devops_architect` 🚀 | SRE & Cloud Automation | Kubernetes, Helm, ArgoCD GitOps, Terraform IaC, Ansible, AWS, Azure, GCP, Istio, Prometheus |
| 3 | `polyglot_systems_architect` 🌐 | Polyglot Systems Architect | Architecture Multi-Limbaj (C#, Go, Rust, Python, TypeScript, C++20 Drogon) |
| 4 | `system_architecture_agent` 🏛️ | Enterprise Architecture Agent | Arhitectură Enterprise .NET 10, Isolation Loopback, Vault Secrets Management |
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

## 📦 2. Biblioteca de 251 de SKILL-uri Locale & Acces Global la 50.000+ SKILL-uri

### A. Skill-uri Stocate Fizic pe Disc (`.agents/skills/`)
Fiecare dintre cele **251 de SKILL-uri operaționale** este stocat fizic în `.agents/skills/<skill_name>/SKILL.md`, asigurând independență totală de rețea.
- Catalogul canonic este salvat în **[`Master_Skills_Catalog_251.md`](file:///C:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/01_KNOWLEDGE/Master_Skills_Catalog_251.md)**.
- Matricea completă agent-skill este salvată în **[`Agents_Skill_Matrix.md`](file:///C:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/01_KNOWLEDGE/Agents_Skill_Matrix.md)**.

### B. Router Global pentru 50.000+ SKILL-uri & Unelte MCP (`global-skill-registry-router`)
Prin tehnologia *Progressive Disclosure*, agenții au acces dinamic la indexul global de 50.000+ skill-uri (VoltAgent, Composio, Awesome Copilot, MCP Registry) documentat în **[`Global_50K_Skill_Registries_Index.md`](file:///C:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/01_KNOWLEDGE/Global_50K_Skill_Registries_Index.md)**.

---

## 📥 3. Pipeline Ingestie Brută pe 12 Faze (`06_INBOX/RAW_IMPORTS/skills/`)

Ingestia de cunoștințe externe respectă granița strictă de securitate **`RAW_EXTERNAL`**. Materialele brute nealterate sunt stocate izolat în `06_INBOX/RAW_IMPORTS/skills/` fără a modifica memoriile canonice.

### Structura Registrelor de Ingestie:
- **`SOURCE.json`** (în fiecare pachet): Proveniență `source_repository`, `source_url`, `license`, `sha256`.
- **`_SOURCE_REGISTRY.json`**: Catalogul celor 141 de surse unice procesate.
- **`_PROGRAMMING_SOURCES.json`**: Index de referință pentru 70 de limbaje și compilatoare (CPython, Rust, Go, Roslyn, TinyCC, etc.).
- **`_BACKEND_SOURCES.json`**: Index de referință pentru 51 de arhitecturi backend (Express, .NET, Rails, Spring, Vapor, NestJS).
- **`_DISCOVERY_GRAPH.json`**: Graful DAG de descoperire recursivă (fără cicluri).
- **`_VALIDATION_REPORT.md`**: Certificatele de validare a integrității SHA-256 (10/10 checks PASSED).

---

## 🛡️ 4. Invariante de Securitate și Arhitectură (P0-P18)

1. **AI Self-Verification Gated (P0-P15)**: Agenții AI nu pot seta `verification = "verified"` și nu pot revendica proveniență de tip `user` sau `official`.
2. **SQLite WAL Persistence & Atomic Transactions**: Modul `PRAGMA busy_timeout=5000` cu tranzacții `BEGIN IMMEDIATE`.
3. **SHA-256 Tamper-Evident Audit Chain**: Fiecare operațiune este stocată cu hash cryptographic în lanț.
4. **P16-P18 Hardware Telemetry & Forensics**: Datele fizice ale mediului (VID, PID, Serial, Capacitate, SHA-256) sunt strict Read-Only; utilizatorul poate modifica exclusiv eticheta logică a volumului.

---

## 📐 5. Structura Directorului Repository-ului

```text
AI_Memory_Vault_CODEX_READY/
├── .agents/
│   ├── agents/               # Profilurile celor 21 de Agenți Specializați
│   ├── rules/                # Regulile cognitive canonice (vault_cognitive_rules.md)
│   └── skills/               # Biblioteca de 251 SKILL-uri stocate fizic pe disc
├── 00_CORE/                  # Identitate, Regulament, Protocol Memorie, Model Încredere
├── 01_KNOWLEDGE/             # Cunoștințe tehnice durabile & Matricea Agenților
├── 02_PROJECTS/              # Starea proiectelor active & fișiere de continuitate
├── 03_PROCEDURES/            # Proceduri repetabile & protocoale de construcție autonomă
├── 04_MEMORY/                # Decizii, Erori, Experiențe, Lecții, Preferințe
├── 05_RESOURCES/             # Materiale de referință
├── 06_INBOX/RAW_IMPORTS/     # Granița de ingestie brută nealterată (RAW_EXTERNAL)
├── 90_TEMPLATES/             # Șabloane canonice de note
├── 99_SYSTEM/                # Scheme, protocoale & rapoarte de securitate
├── cognitive_core/           # Motorul cognitiv Python (Executive, Recall, Planning, Reflexion)
└── memory_controller/        # Controller-ul canonic de memorie & persistență SQLite WAL
```

---

## 🧪 6. Rularea Testelor Automate

Pentru a valida integritateasuitei cognitive și a controller-ului de memorie:

```bash
python -m pytest -q
```

Toate cele 197+ de teste unitare, de integrare și de securitate adversară rulează fără erori.
