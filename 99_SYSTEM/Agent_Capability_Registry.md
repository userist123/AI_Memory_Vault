---
type: system
category: orchestration
status: active
version: 1.0.0
document_kind: runtime_registry
document_status: active
---

# Agent Capability Registry

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
- penetration-testing-playbook
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
- penetration-testing-playbook

### ui_sensei_architect
- ui-sensei
- clean-minimal-beige-light-mode
- dark-blue-contrasting-clean
- dark-glass-clean-layout
- glass-dark-mode-clock
- glass-dark-ui
- high-contrast-skeuomorphic-clean
- light-mode-paper-technical
- nested-container-clean-agency
- orange-clean-paper-saas
- tech-green-dark-mode-modern
- technical-wireframe-info-layout

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
- gsap
- gsap-scrolltrigger-storytelling
- cobejs
- vantajs
- matterjs
- unicorn-studio
- background-grid-webgl
- bright-green-tech-system-webgl
- globe-gl
- corner-lasers
- dither-laser-dark-mode

### web_design_engineer_agent
- web-design-engineer
- design-system-linear
- design-system-apple
- design-system-stripe
- design-system-vercel
- design-system-supabase
- agency-grid-layout-minimal
- editorial-tech
- framed-grid-layout
- split-layout-technical

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
