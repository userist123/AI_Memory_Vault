---
type: system
category: orchestration
status: active
version: 1.0.0
document_kind: runtime_registry
document_status: active
---

# Agent Capability Registry

> **Provenance.** Restored on 2026-10-23, byte for byte below this note, from
> `99_SYSTEM/Agent_Capability_Registry.md` at `dbcfbc502^`. Commit `dbcfbc502` deleted
> `99_SYSTEM/` as "redundant scaffold", but 20 of the 21 agent profiles carry no capability
> list of their own and pointed here, and no other file maps agents to skill ids
> (`01_ARCHITECTURE/knowledge/Agents_Skill_Matrix.md` lists domains, not skills).
>
> **No code reads this file.** The line below saying capability lists "are queried by the
> router" was true of the design, not of the code: `routing/agent_router.py` reads
> `04_CONFIG/agent_router.json`, which maps its own 8 router agents to generic capability
> labels, not to these 21 profiles' skill ids. This file is capability metadata for agents
> and humans selecting skills.
>
> **Missing skills resolved (2026-10-24, owner decision).** 18 of the 149 skills named here were
> absent from `.agents/skills/`. Mapped to the individual steps of #196 (`ea93ec3d2`):
> - 16 design skills were removed on purpose by `f801fa385` ("remove 38 base64 demo pages",
>   policy: their demos embed base64-encoded HTML). The owner chose not to restore them; they are
>   dropped from the lists below.
> - `ansible-automation` was removed by `82a3b44a3` as an unused connector, not for a security
>   reason; it is restored unchanged from `ea93ec3d2^`.
> - `penetration-testing-playbook` was removed on purpose by `d699e9f52` (purge of offensive
>   skills); dropped from the lists below.

This is the authoritative capability index for the 21 specialised agents.

**Runtime rule:** agent profiles are identity/persona manifests. Capability lists live here and are queried by the router. They are never injected wholesale into an agent prompt.

## Registry

### agentic_workflow_orchestrator
- global-skill-registry-router
- copilot-agentic-workflows
- mcp-server-integrations
- code-refactoring-patterns
- unit-test-generation-contract
- copilot-custom-instructions

### backend_systems_engineer
- skill-api-design-governance
- skill-backend-performance-tuning
- skill-clean-architecture-layering
- skill-cqrs-event-sourcing
- skill-distributed-rate-limiting
- skill-graphql-schema-evolution
- skill-grpc-contract-management
- skill-oauth2-jwt-security
- skill-owasp-backend-hardening
- skill-postgresql-indexing-tuning
- skill-rbac-abac-authorization
- skill-redis-caching-patterns
- skill-saga-orchestration-choreography
- skill-sqlite-wal-optimization
- skill-transactional-outbox-cdc

### compiler_and_tooling_engineer
- code-refactoring-patterns
- unit-test-generation-contract
- skill-clean-architecture-layering
- skill-cpp-drogon-coroutine-backend
- skill-rust-tokio-axum-architecture

### content_strategist
- email-design
- presentation-design
- brand-identity

### database_and_persistence_engineer
- database-migration-flyway
- database-sharding-vitess
- duckdb-analytical-queries
- clickhouse-time-series
- elasticsearch-full-text
- qdrant-vector-database
- pgvector-embeddings
- neo4j-graph-database
- debezium-cdc-pipeline

### frontend_saas_engineer
- nextjs-saas-frontend
- landing-page-design
- pricing-page
- landing-page
- react-query-tanstack
- zustand-state-management
- storybook-component-docs
- playwright-e2e-testing
- vite-bundler-optimization
- tailwind-v4-theme-engine
- tailwindcss

### game_engineer
- build-isometric-arpg
- design-action-combat
- design-game-encounters
- tune-enemy-ai
- create-game-vfx
- build-game-camera-controls
- build-game-inventory
- build-game-audio-feedback
- build-mobile-threejs-games
- build-hybrid-game-assets
- ship-web-games

### local_ai_engineer
- local-ai-integration
- ollama-local-llm-integration
- pydantic-json-mode-validation
- langchain-agentic-chains
- llamaindex-rag-pipeline
- vllm-inference-optimization
- fine-tuning-lora-peft
- guardrails-ai-safety

### memory_controller_architect
- vault-operations
- vault-security-audit
- vault-secrets-management

### polyglot_systems_architect
- skill-dotnet10-minimal-api-aot
- skill-python-fastapi-async-worker
- skill-go-worker-pool-concurrency
- skill-rust-tokio-axum-architecture
- skill-typescript-nest-bullmq-orm
- skill-cpp-drogon-coroutine-backend

### quant_developer
- python-trading-systems

### secops_auditor
- powershell-secops
- dfir-operations
- vault-security-audit
- owasp-top-10-audit
- sast-static-analysis
- dast-dynamic-testing
- container-vulnerability-scanning
- secret-leak-prevention
- zero-trust-architecture
- pki-certificate-management
- opa-rego-policy-enforcement
- casbin-authorization

### site_reliability_and_devops_architect
- docker-containerization
- kubernetes-orchestration
- terraform-infrastructure
- ansible-automation
- aws-cloud-architecture
- azure-cloud-architecture
- gcp-cloud-architecture
- helm-chart-management
- argocd-gitops
- vault-secrets-management
- nginx-ingress-tuning
- envoy-proxy-configuration
- istio-service-mesh
- open-telemetry-tracing
- datadog-observability
- elastic-stack-logging

### system_architecture_agent
- docker-containerization
- kubernetes-orchestration
- terraform-infrastructure
- aws-cloud-architecture
- azure-cloud-architecture
- gcp-cloud-architecture
- vault-secrets-management

### threat_hunting_analyst
- dfir-operations
- vault-security-audit
- secret-leak-prevention

### ui_sensei_architect
- ui-sensei
- dark-blue-contrasting-clean
- dark-glass-clean-layout
- glass-dark-ui
- tech-green-dark-mode-modern

### ui_ux_designer
- dashboard-admin-ui
- brand-identity
- email-design
- presentation-design
- data-viz-design
- design-system-foundation
- motion-design
- ui-ux-review
- design-first-ui-prompting

### web_creative_developer
- threejs
- threejs-post-processing
- cobejs
- vantajs
- matterjs
- unicorn-studio
- bright-green-tech-system-webgl
- globe-gl

### web_design_engineer_agent
- web-design-engineer
- design-system-linear
- design-system-apple
- design-system-stripe
- design-system-vercel
- design-system-supabase

### web_quality_engineer
- web-quality-audit
- core-web-vitals
- accessibility
- seo
- best-practices
- performance

### wpf_engineer
- csharp-wpf-desktop
- ui-tokens

## Runtime Query Contract

1. Select agent first.
2. Query this registry using task intent, domain, keywords and constraints.
3. Rank capabilities.
4. Select at most 2 skills per agent by default.
5. Load full SKILL.md only for selected skills.
6. Never pass this entire registry to a specialist.
7. Never pass an agent's complete capability list to a specialist.
8. If no skill clears the relevance threshold, execute without a skill rather than loading unrelated context.

## Deduplication

A capability selected by multiple agents is loaded once into shared orchestrator state. Specialists receive only the relevant instruction slice, not duplicate copies.
