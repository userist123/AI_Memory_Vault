# Backend SaaS Foundation

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Build](https://github.com/fgorordodev/saas-backend-foundation/actions/workflows/dotnet.yml/badge.svg)](https://github.com/fgorordodev/saas-backend-foundation/actions/workflows/dotnet.yml)
[![Architecture](https://img.shields.io/badge/architecture-modular%20monolith-0A66C2)](#architecture)
[![Status](https://img.shields.io/badge/status-early%20development-orange)](#project-status)

An enterprise-oriented, multi-tenant SaaS backend built with C# and ASP.NET Core on .NET 10.

The project is starting as a modular monolith with Clean Architecture boundaries. Its first major capabilities will be tenant isolation, identity, secure token-based authentication, and policy-based authorization.

> [!IMPORTANT]
> This repository is in its foundation stage. The current code is the initial ASP.NET Core Web API template; the architecture and security capabilities described below are the planned baseline and are not yet production-ready.

## Project goals

- Keep business rules independent from frameworks and infrastructure.
- Organize capabilities into explicit, independently evolvable modules.
- Enforce tenant isolation at both authorization and persistence boundaries.
- Support short-lived JWT access tokens and rotated opaque refresh tokens.
- Apply permission-based authorization across platform and tenant scopes.
- Provide predictable API errors, observability, auditing, and automated tests.
- Remain simple to deploy while preserving a path to future service extraction.

## Architecture

The target design is a **modular monolith**. Each business module owns its domain model, use cases, persistence configuration, and API endpoints. Cross-module interaction will happen through explicit contracts or events instead of direct access to another module's internals.

```text
src/
├── Api/                         # Application host and composition root
├── BuildingBlocks/              # Small, reusable cross-cutting abstractions
└── Modules/
    ├── Identity/
    │   ├── Domain/
    │   ├── Application/
    │   ├── Infrastructure/
    │   └── Presentation/
    └── Tenancy/
        ├── Domain/
        ├── Application/
        ├── Infrastructure/
        └── Presentation/

tests/
├── ArchitectureTests/
├── IntegrationTests/
└── UnitTests/
```

Dependencies point inward: presentation and infrastructure depend on application and domain contracts, while the domain remains independent of ASP.NET Core, EF Core, and external services.

## Multi-tenancy

The initial persistence strategy will use a shared database with a mandatory tenant discriminator on tenant-owned records.

Planned tenant-safety rules include:

- A global user identity may belong to one or more tenants through memberships.
- Each tenant-owned record carries a non-null `TenantId`.
- Unique constraints and relevant relationships include tenant scope.
- The active tenant is established from an authenticated, validated membership.
- EF Core query filters provide default read isolation.
- Write-time guards prevent records from being assigned to another tenant.
- Cross-tenant access is covered by integration and security tests.
- Platform administration uses explicit platform endpoints and audited tenant access.

The API will not trust a caller-provided tenant identifier by itself.

## Identity and authentication

The Identity module is planned around ASP.NET Core Identity with application-owned abstractions.

- Short-lived JWT access tokens
- Cryptographically random opaque refresh tokens
- Refresh-token hashes stored instead of raw token values
- Rotation on every successful refresh
- Replay detection and token-family revocation
- Per-device or per-client sessions
- Session revocation after security-sensitive account changes
- Explicit tenant context in tenant-scoped access tokens

JWT validation will include signature, issuer, audience, and expiration checks.

## Authorization

The initial access model has three baseline roles:

| Scope | Role | Responsibility |
| --- | --- | --- |
| Platform | `SystemAdmin` | Operates and manages the SaaS platform |
| Tenant | `TenantManager` | Manages users and resources inside one tenant |
| Tenant | `TenantMember` | Reads data and performs limited tenant actions |

Endpoints will authorize against named permissions and policies. Roles act as permission bundles, allowing the permission model to evolve without coupling endpoint behavior to hard-coded role checks.

## API conventions

The planned API conventions are:

- Typed request and response contracts
- Standard HTTP status codes
- RFC-compatible `ProblemDetails` error responses
- Stable application error codes
- Validation details for invalid requests
- Trace identifiers for diagnostics
- A result abstraction inside the application layer, translated at the API boundary

## Technology

| Area | Technology |
| --- | --- |
| Runtime | .NET 10 |
| API | ASP.NET Core Web API with controllers |
| Language | C# |
| API description | OpenAPI |
| Identity | ASP.NET Core Identity (planned) |
| Persistence | Entity Framework Core (planned) |
| Database | PostgreSQL (planned) |
| Testing | Unit, integration, architecture, and security tests (planned) |

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git

### Run locally

```bash
git clone git@github.com:fgorordodev/saas-backend-foundation.git
cd saas-backend-foundation
dotnet restore
dotnet run
```

When running in the Development environment, the generated OpenAPI document is available at:

```text
/openapi/v1.json
```

The exact local host and port are displayed by `dotnet run` and configured in `Properties/launchSettings.json`.

## Project status

- [x] Create the .NET 10 Web API repository
- [x] Add initial GitHub Actions workflow
- [x] Add repository ignore rules
- [ ] Establish the modular solution structure
- [ ] Add shared API result and error handling
- [ ] Implement tenant persistence and isolation
- [ ] Implement identity and authentication
- [ ] Implement refresh-token rotation and replay detection
- [ ] Implement permission-based authorization
- [ ] Add audit logging and observability
- [ ] Add automated test suites and CI quality gates

Work is tracked through GitHub Issues and GitHub Projects.

## Commit convention

Commits follow this format:

```text
<type>(<scope>): <short description in imperative mood>
```

Example:

```text
devops(git): add repository ignore rules
```

## Security

Do not report suspected vulnerabilities in public issues. A private security reporting process will be documented before the first production release.

