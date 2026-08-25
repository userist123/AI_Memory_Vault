---
id: "c1a01101-7291-49fa-9481-22904c10d010"
type: knowledge
lifecycle: ACTIVE
category: multi-agent-orchestration
tags:
  - agent-skills
  - vault-core
  - orchestration
  - 21-agents-council
  - jarvis-command-center
created: 2026-08-24T18:00:00Z
updated: 2026-08-25T22:00:00Z
provenance:
  source_type: execution
  source_ref: "master-raw-skills-agent-integration"
confidence: high
verification: verified
---

# Matricea Totală a Consiliului extins la 21 de Agenți Specializați

Consiliul de Memorie AI are 21 de agenți specializați, conectați la skill registry și la orchestrarea din Memory V6.

| # | Nume Agent | Domeniu & Rol | Skill-uri Cheie |
|---|---|---|---|
| 1 | `compiler_and_tooling_engineer` | Compilatoare, AST Parsers & Tooling | Refactoring, testing, compiler tooling, Rust Tokio |
| 2 | `site_reliability_and_devops_architect` | SRE, Kubernetes & Cloud | Docker, Kubernetes, Terraform, Ansible, AWS/Azure/GCP, Prometheus, Grafana, OpenTelemetry |
| 3 | `polyglot_systems_architect` | C#, Go, Rust, Python, TS, C++ | .NET, FastAPI, Go worker pools, Rust Axum, NestJS, Drogon |
| 4 | `system_architecture_agent` | Enterprise Architecture & Air-Gapped | Docker, Kubernetes, Terraform, cloud architecture, secrets |
| 5 | `backend_systems_engineer` | Backend APIs, Microservices, Redis | API governance, CQRS, rate limiting, GraphQL, gRPC, OAuth2, OWASP, Postgres, RBAC/ABAC, Redis, Saga, SQLite WAL, Outbox |
| 6 | `secops_auditor` | Security, DevSecOps & Compliance | DFIR, OWASP, SAST, DAST, secret prevention, Zero Trust, PKI, OPA, Casbin |
| 7 | `threat_hunting_analyst` | Threat Hunting & Forensics | DFIR, Vault Security Audit, secret leak prevention, pentest playbook |
| 8 | `wpf_engineer` | C# WPF Desktop | WPF, desktop UI tokens |
| 9 | `web_creative_developer` | Creative Coding, 3D WebGL & Motion | Three.js, GSAP, WebGL, CobeJS, MatterJS, VFX |
| 10 | `web_design_engineer_agent` | Design Systems & Editorial Grids | Linear, Apple, Stripe, Vercel, Supabase and editorial design systems |
| 11 | `web_quality_engineer` | Performance & Quality | Core Web Vitals, WCAG, SEO, accessibility, performance |
| 12 | `ui_sensei_architect` | UI Philosophy & Visual Hierarchy | UI Sensei, clean hierarchy, spacing systems, technical UI, dark glass |
| 13 | `frontend_saas_engineer` | Frontend SaaS | Next.js, TanStack Query, Zustand, Storybook, Playwright, Vite, Tailwind |
| 14 | `game_engineer` | 3D Game Engineering | WebGL, ARPG, VFX, AI, cameras, inventory, audio |
| 15 | `quant_developer` | Algorithmic Trading | Python trading systems, risk and strategy modules |
| 16 | `local_ai_engineer` | Local LLM / RAG | Ollama, Pydantic, LangChain, LlamaIndex, vLLM, LoRA, Guardrails, embeddings |
| 17 | `content_strategist` | Copy / Voice / Brand | Email, presentation, brand identity |
| 18 | `agentic_workflow_orchestrator` | Agent Routing & Workflows | Global Skill Registry Router, MCP, agentic workflows, refactoring, testing |
| 19 | `ui_ux_designer` | UI/UX & Interaction | Dashboard UI, brand identity, data viz, motion design |
| 20 | `database_and_persistence_engineer` | Persistence / Vector / Graph | PostgreSQL, DuckDB, ClickHouse, Elasticsearch, Qdrant, pgvector, Neo4j, CDC |
| 21 | `memory_controller_architect` | Memory V6 & Vault Operations | Vault Operations, Security Audit, Secrets, lifecycle, provenance |

## JARVIS Command Center Skill

`jarvis-command-center` is an operational skill for building and maintaining the JARVIS full-screen AI Command Center.

Primary compatible agents:

- `ui_sensei_architect`
- `web_design_engineer_agent`
- `web_creative_developer`
- `web_quality_engineer`
- `frontend_saas_engineer`
- `agentic_workflow_orchestrator`

Required operating rule:

```text
JARVIS UI
  ↓
Memory API / Registry
  ↓
Canonical Vault
  ↓
Agent Router
  ↓
Operational Skills
  ↓
Verification
  ↓
Memory / Audit
```

JARVIS is a control-plane client and must never become a second canonical memory store.

## Legaturi

- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
- [[Master_Skills_Catalog_251]]
- [[Deep Visual Web Engineering Master Report]]
