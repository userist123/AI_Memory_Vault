# Module Config Access Policy

## Status
- Scope: Phase 06 security hardening — **Completed** (`AGENTS.md`)
- Applies to: runtime policy enforcement and module context shaping in `packages/platform-core/src/modularity/policy/` and `packages/platform-core/src/modularity/context/`
- Principle baseline: **default deny** + **scoped-by-default** access

## Purpose
This document defines the target policy model for module configuration access used by runtime lifecycle and module context assembly.

Policy objective:
- prevent unrestricted configuration reads by modules;
- provide deterministic, auditable policy decisions;
- allow controlled escalation to selected global sections only through explicit capability grants and security-class checks.

## Core Rules

### 1) Default Deny
If access cannot be positively proven by policy, it is denied.

- Unknown capability → deny
- Missing mapping capability → section → deny
- Missing section allowlist for module security class → deny

### 2) Scoped-by-Default Module Access
Each module always receives only its own namespace subtree:

- `modules.<moduleId>`

This base scope does not require any global capability grant and is the only guaranteed read surface.

### 3) Capability-Gated Global Section Access
Access to any global configuration section is allowed only when all checks pass:

1. capability is declared by the module manifest;
2. capability is recognized by runtime policy map;
3. mapped section is present in allowlist for module security class.

No direct section request bypass is allowed.

### 4) Production Strict Mode
For production, strict enforcement is mandatory.

- policy violation blocks module startup;
- no fallback to permissive behavior;
- diagnostics record structured denial without secret values.

For non-production environments, strategy may support best-effort behavior, but policy evaluation remains deterministic.

### 5) Wildcard Access is Forbidden
Wildcard capabilities or wildcard section patterns are prohibited.

Forbidden examples:
- `config.read.*`

Only explicit capability-to-section bindings are valid.

## Policy Data Model (Target)
Runtime policy model expected by evaluator:

- `productionStrictMode`: boolean (must be true for production)

## Security Class Constraints
Security classes used by policy checks:

- `trusted` – Core platform modules, full access
- `internal` – Internal team modules, standard access
- `third-party-reviewed` – External modules, reviewed and approved

Each class has independent allowlist boundaries for global sections. Lower-trust classes must not inherit broader access implicitly.

## Access Decision Table

| Case | Module namespace `modules.<moduleId>` | Global section | Decision | Reason/Error code |
|---|---|---|---|---|
| Base module read | yes | no | Allow | Scoped-by-default |
| Declared + known capability + allowlisted section | yes | yes | Allow | Explicit grant |
| Capability not declared in manifest | yes | yes | Deny | `CONFIG_ACCESS_DENIED` |
| Capability declared but unknown in policy map | yes | yes | Deny | `CONFIG_CAPABILITY_INVALID` |
| Section not allowlisted for security class | yes | yes | Deny | `CONFIG_SECTION_NOT_ALLOWLISTED` |
| Wildcard capability/section requested | any | any | Deny | `CONFIG_CAPABILITY_INVALID` |

## Deterministic Enforcement Pipeline
1. Load module manifest.
2. Resolve module security class.
3. Collect requested config capabilities.
4. Validate capability format (no wildcard).
5. Map capability to target section.
6. Validate section allowlist for security class.
7. Build configuration projection:
   - always include `modules.<moduleId>`
   - include only policy-approved global sections
8. Build module context with projected config only.
9. Emit diagnostics with structured metadata and redacted payloads.

## Diagnostics and Auditability Requirements
- Record both allow and deny outcomes in runtime diagnostics.
- Mandatory fields for denial path:
  - `moduleId`
  - `phase`
  - `errorCode`
  - `remediationHint`
- Never include plaintext secret values in diagnostics/logs.

## Refusal Cases (Minimal Set)
- Unknown capability requested.
- Wildcard capability/section pattern detected.
- Section not in allowlist for module security class.
- Production strict mode disabled or bypass attempted.

## Error Taxonomy (Implementation)

All configuration access policy errors implement the [`IRuntimeFailureDiagnostic`](packages/platform-core/src/diagnostics/interfaces/runtime-failure-diagnostic.interface.ts:7) interface with mandatory fields:
- `moduleId`: identifies the module that triggered the error
- `phase`: the lifecycle phase where the error occurred
- `errorCode`: machine-readable error classification
- `message`: human-readable description (no secrets)
- `remediationHint`: actionable guidance for resolution

### Error Codes

| Error Code | Thrown When |
|---|---|
| `CONFIG_ACCESS_DENIED` | Module attempts to access config outside permitted scope |
| `CONFIG_CAPABILITY_INVALID` | Module declares invalid or unrecognized capability |
| `CONFIG_SECTION_NOT_ALLOWLISTED` | Module's security class cannot access requested section |
| `CONFIG_WILDCARD_FORBIDDEN` | Module uses wildcard patterns for config access |

### Diagnostics Schema Validation

The [`diagnostics-reports.schema.ts`](packages/platform-core/src/diagnostics/diagnostics-reports.schema.ts:1) provides runtime validation:

1. **Structure validation**: All failed module diagnostics must include required fields
2. **Secret detection**: Validates that diagnostic payloads don't contain sensitive data patterns
3. **Error code recognition**: Identifies config access policy errors for special handling

Validation functions:
- [`assertStartupReport()`](packages/platform-core/src/diagnostics/diagnostics-reports.schema.ts:67): Validates startup report structure and checks for sensitive data
- [`validateNoSensitiveData()`](packages/platform-core/src/diagnostics/diagnostics-reports.schema.ts:33): Recursively checks payloads for sensitive field patterns
- [`isConfigAccessErrorCode()`](packages/platform-core/src/diagnostics/diagnostics-reports.schema.ts:26): Identifies config policy errors

## Secret Redaction (Implementation)

All logs and diagnostics automatically redact sensitive data to prevent secret leakage in production and staging environments.

### Redaction Layers

| Layer | Component | Responsibility | Source File |
|---|---|---|---|
| Diagnostics | [`ReportBaseBuilder`](packages/platform-core/src/diagnostics/builders/report.base-builder.ts:20) | Redacts `message` and `remediationHint` in all failure diagnostics | `packages/platform-core/src/diagnostics/builders/report.base-builder.ts` |
| Logging | [`ConsoleModuleLogger`](packages/platform-core/src/logging/module-logger/console/console-module-logger.ts:8) | Redacts messages and context objects in all log levels | `packages/platform-core/src/logging/module-logger/console/console-module-logger.ts` |
| Core Engine | [`SecretsRedactor`](packages/platform-core/src/security/redactors/secrets.redactor.ts:36) | Centralized redaction engine with configurable patterns | `packages/platform-core/src/security/redactors/secrets.redactor.ts` |

### Redacted Patterns

The [`SecretsRedactor`](packages/platform-core/src/security/redactors/secrets.redactor.ts:36) redacts the following patterns by default:

**Built-in Rules:**
- `Bearer <token>` → `Bearer [REDACTED]`
- `Authorization: basic <value>` → `Authorization: basic [REDACTED]`

**Key-Value Patterns:**
- `key=<value>` → `key=[REDACTED]`
- `token=<value>` → `token=[REDACTED]`
- `secret=<value>` → `secret=[REDACTED]`
- `password=<value>` → `password=[REDACTED]`
- `passphrase=<value>` → `passphrase=[REDACTED]`
- `connectionString=<value>` → `connectionString=[REDACTED]`
- `privateKey=<value>` → `privateKey=[REDACTED]`
- `apiKey=<value>` → `apiKey=[REDACTED]`
- `databaseUrl=<value>` → `databaseUrl=[REDACTED]`
- `jwtSecret=<value>` → `jwtSecret=[REDACTED]`
- `encryptionKey=<value>` → `encryptionKey=[REDACTED]`

### Logging Context Redaction

The [`ConsoleModuleLogger`](packages/platform-core/src/logging/module-logger/console/console-module-logger.ts:8) performs deep redaction on context objects:

1. **Key-based redaction**: Keys matching sensitive patterns are replaced with `[REDACTED]`
2. **String value redaction**: String values are processed through `SecretsRedactor`
3. **Recursive redaction**: Nested objects and arrays are recursively processed

Sensitive key patterns include: `key`, `token`, `secret`, `password`, `passphrase`, `api_key`, `private_key`, `connection_string`, `database_url`, `jwt_secret`, `encryption_key`.

### Configuration

Redaction is enabled by default in all environments. To customize:

```typescript
import { SecretsRedactor } from '@prosto/platform-core';

// Custom patterns
const redactor = new SecretsRedactor({
  enabled: true,                    // Default: true
  patterns: ['password', 'token'],  // Custom key patterns
});
```

## Evidence Linkage
- Hardening completed: `AGENTS.md`.
- Runtime enforcement integration points: `packages/platform-core/src/modularity/policy/`.
- Context shaping integration point: `packages/platform-core/src/modularity/context/factories/module-context.factory.ts`.
- Diagnostics validation: `packages/platform-core/src/diagnostics/diagnostics-reports.schema.ts`.
