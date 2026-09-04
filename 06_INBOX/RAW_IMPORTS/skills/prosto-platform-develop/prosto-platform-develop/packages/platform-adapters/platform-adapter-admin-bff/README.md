# @prosto/platform-adapter-admin-bff

Policy-aware admin BFF adapter for plugin discovery, permission mapping, compatibility filtering, diagnostics, and observability.

## Status
- Phase 08 baseline completed
- Phase 09 integration baseline completed
- Phase 11: migrated to SDK transport contracts (`@alpha`)

## Public API

### Interfaces
- `IAdminBffRouteContext` — extends `IPlatformHttpRouteContext` from `@prosto/platform-sdk`, narrows identity to `IPlatformDelegatedIdentity`
- `IAdminDiscoveryAggregationService` — discovery pipeline, accepts `IPlatformDelegatedIdentity`
- `IAdminPermissionMappingService` — action gating and permission checks, accepts `IPlatformDelegatedIdentity`
- `IAdminPluginCatalogSource` — catalog source contract for fetching manifests
- `IAdminDiscoveryResult` / `IAdminDiscoveryPayloadResult` / `IAdminDiscoveryDiagnostics`
- `IAdminPermissionFilterResult` / `IAdminActionEvaluationResult`
- `IAdminDiagnosticsRequestContext` — diagnostics request context with `IPlatformDelegatedIdentity`
- `IAdminDiagnosticsPayload` / `IAdminDiagnosticsPluginEntry` / `IAdminDiagnosticsSummary` / `IAdminDiagnosticsMetadata`
- `IPlatformAdminBffAdapterConfig`

Request, response, and route handler contracts (`IPlatformHttpRequest`, `IPlatformHttpResponse`, `IPlatformHttpRouteHandler<T>`) are imported directly from `@prosto/platform-sdk`.

### Classes
- `PlatformAdminBffAdapter`

### Services
- `AdminDiscoveryAggregationService`
- `AdminPermissionMappingService`
- `AdminDiagnosticsService`

### Constants
- `ADMIN_BFF_ROUTES`
- `ADMIN_BFF_REJECTION_REASON_CODES`
- `ADMIN_BFF_HEALTH_STATUSES`
- `AdminBffErrorCodes`
- `AdminBffPhase`

## Usage

```typescript
import {
  PlatformAdminBffAdapter,
  AdminDiscoveryAggregationService,
  AdminPermissionMappingService,
  AdminDiagnosticsService,
} from '@prosto/platform-adapter-admin-bff';
import {
  PlatformDelegatedIdentity,
  PlatformHttpRequest,
} from '@prosto/platform-sdk';

const adapter = new PlatformAdminBffAdapter(
  new AdminDiscoveryAggregationService(/* catalog, validator, compat, config */),
  new AdminPermissionMappingService(/* policy config */),
  new AdminDiagnosticsService(/* diagnostics config */),
);

// Handlers implement IPlatformHttpRouteHandler<IAdminBffRouteContext>
const handlers = adapter.getHandlers();

// Framework-agnostic dispatch
const response = await adapter.handleRequest(
  new PlatformHttpRequest({
    method: 'GET',
    path: '/admin/api/v1/discovery',
    params: {},
    query: {},
    headers: {},
    body: { variant: 'empty' },
    correlationId: 'req-001',
    identity: new PlatformDelegatedIdentity({
      subjectId: 'operator-1',
      roles: ['admin'],
      permissions: ['read:admin'],
    }),
  }),
);
// response.status, response.body.variant, response.body.data
```

## Subsystems

### Discovery
- `AdminDiscoveryAggregationService` — aggregates plugin manifests from catalog sources, validates compatibility, applies policy checks, builds discovery payload
- `IAdminPluginCatalogSource` — contract for fetching UI plugin manifests from catalog sources

### Permissions
- `AdminPermissionMappingService` — maps delegated identity roles to permissions, evaluates action gates, filters required permissions
- Uses `AdminActionGateEvaluator` from `@prosto/platform-admin-contracts`

### Diagnostics
- `AdminDiagnosticsService` — generates structured diagnostics payloads with correlation metadata for incident triage

### Policy
- `AdminPluginTrustClassFilter` — filters plugins by trust class
- `AdminPluginReviewStatusFilter` — filters plugins by review status
- `AdminPluginAllowlistEvaluator` — evaluates plugin allowlist policies

### Observability
- `IAdminBffLogger` — structured logging interface for admin BFF operations
- `ConsoleAdminBffLogger` — console implementation with secret redaction
- `AdminBffPhase` / `AdminBffLogEvents` / `AdminBffErrorCodes` — observability constants

## Commands
- `npm run --workspace @prosto/platform-adapter-admin-bff build`
- `npm run --workspace @prosto/platform-adapter-admin-bff typecheck`
- `npm run --workspace @prosto/platform-adapter-admin-bff test`

## Notes
- Framework-agnostic — no HTTP framework dependency.
- Route handlers implement `IPlatformHttpRouteHandler<IAdminBffRouteContext>` from `@prosto/platform-sdk`.
- `IAdminBffRouteContext` extends `IPlatformHttpRouteContext` and narrows identity to `IPlatformDelegatedIdentity`.
- Service contracts accept `IPlatformDelegatedIdentity` instead of the legacy `IAdminOperatorContext`.
- Request/response types (`IPlatformHttpRequest`, `IPlatformHttpResponse`) are imported directly from `@prosto/platform-sdk`.
- Identity is propagated from the transport layer via SDK contracts; the adapter does not perform authentication.
- Dependencies: `@prosto/platform-sdk`, `@prosto/platform-admin-contracts`.
- All contracts are marked `@alpha`.
