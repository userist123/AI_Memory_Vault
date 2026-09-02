# C4-01 System Context

Date: 2026-03-24  
Scope: L1 context view

## Purpose
Define how `prosto-platform` interacts with primary actors and external systems.

## Context Diagram

```mermaid
flowchart LR
  Operator["Platform Operator"]
  Dev["Module Developer"]
  Client["Client Application"]
  Admin["Admin Shell"]
  Runtime["Prosto Platform Runtime"]
  AdminBFF["Admin BFF Adapter"]
  UIRegistry["UI Plugin Registry"]
  Registry["Package Registry (npm/GH Packages)"]
  Catalog["Module Catalog + Compatibility Matrix"]
  Secrets["Secrets/Config Store"]
  Obs["Observability Backend (logs/metrics/traces)"]
  ThirdParty["External APIs / Services"]

  Operator -->|"configure allowlist, startup policy"| Runtime
  Operator -->|"configure ui allowlist and trust class"| UIRegistry
  Operator -->|"reads startup report + health"| Runtime
  Dev -->|"publish module package + manifest"| Registry
  Dev -->|"publish ui plugin manifest"| UIRegistry
  Dev -->|"update support metadata"| Catalog
  Runtime -->|"resolve/pull approved artifacts"| Registry
  Runtime -->|"validate module against policy"| Catalog
  Runtime -->|"load runtime secrets/config"| Secrets
  Client -->|"HTTP/API requests via adapter"| Runtime
  Runtime -->|"domain responses/events"| Client
  Runtime -->|"structured telemetry"| Obs
  Runtime -->|"integration calls via modules/adapters"| ThirdParty
  Admin -->|"admin API calls"| AdminBFF
  AdminBFF -->|"aggregated policy aware admin operations"| Runtime
  AdminBFF -->|"ui plugin discovery and compatibility checks"| UIRegistry
  AdminBFF -->|"admin telemetry"| Obs
```

## External Actors And Responsibilities

| Actor/System | Responsibility |
|---|---|
| Platform Operator | Configures runtime policy, module allowlist, admin UI plugin allowlist, and deployment settings |
| Module Developer | Builds and publishes compatible runtime modules and optional UI plugin manifests |
| Client Application | Sends API requests and consumes responses |
| Admin Shell | Renders operator UI and loads approved UI plugins via discovery contract |
| Admin BFF Adapter | Exposes policy-aware admin APIs and UI plugin discovery payloads |
| UI Plugin Registry | Stores UI plugin manifests, compatibility metadata, trust class, and review status |
| Package Registry | Hosts module artifacts for distribution |
| Module Catalog | Stores compatibility, support, and security review status |
| Secrets/Config Store | Provides sensitive runtime configuration |
| Observability Backend | Receives logs, metrics, traces |
| External APIs | Integrated systems consumed by modules/adapters |

## Key Context-Level Policies
- Runtime does not load arbitrary modules outside explicit allowlist.
- UI plugins are discovered through explicit allowlist and integrity/compatibility checks.
- Module artifacts and UI plugin manifests must include compatibility metadata.
- Kernel remains framework-neutral; transport and admin API aggregation are adapter-owned.
- Admin shell runtime stays outside `platform-core` process boundary.

## Linked Views
- Containers: [C4-02 Container View](./02-container-view.md)
- Loading data flow: [DFD-03 Module Loading L2](../dfd/03-module-loading-l2.md)
- Governance decisions: [ADR-0003](../adr/ADR-0003-module-loading-security-allowlist-integrity.md), [ADR-0006](../adr/ADR-0006-external-module-repository-and-distribution-model.md)
