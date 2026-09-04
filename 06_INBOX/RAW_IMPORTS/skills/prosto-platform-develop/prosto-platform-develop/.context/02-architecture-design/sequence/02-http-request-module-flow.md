# SEQ-02 HTTP Request Through Module

Date: 2026-03-24  
Scope: Request path with optional auth and module service resolution

## Sequence Diagram

```mermaid
sequenceDiagram
  autonumber
  participant Client as Client Application
  participant HTTP as HTTP Adapter
  participant Auth as Auth Module (optional)
  participant Core as Core Service Registry
  participant Feature as Feature Module
  participant Bus as Hook/Event Bus
  participant Ext as External API

  Client->>HTTP: HTTP request
  HTTP->>HTTP: validate input schema

  alt auth capability enabled
    HTTP->>Auth: authenticate(requestContext)
    Auth-->>HTTP: authResult
    alt auth failed
      HTTP-->>Client: 401/403 error response
    else auth success
      HTTP->>Core: resolve(featureServiceToken)
      Core-->>HTTP: featureService
      HTTP->>Feature: executeUseCase(requestPayload)
      Feature->>Bus: publish(domain.event)
      opt integration required
        Feature->>Ext: call integration API
        Ext-->>Feature: integration response
      end
      Feature-->>HTTP: domain response
      HTTP-->>Client: success response
    end
  else auth capability disabled
    HTTP->>Core: resolve(featureServiceToken)
    Core-->>HTTP: featureService
    HTTP->>Feature: executeUseCase(requestPayload)
    Feature-->>HTTP: domain response
    HTTP-->>Client: success response
  end
```

## Notes
- Adapter owns transport-specific concerns (routing, status codes, middleware).
- Module business logic remains transport-agnostic.
- Event publication supports decoupled reactions in other modules.

Related:
- [C4-02 Container View](../c4/02-container-view.md)
- [DFD-02 Runtime L1](../dfd/02-runtime-l1.md)
- [ADR-0001 Micro-Core Boundary](../adr/ADR-0001-micro-core-kernel-boundary.md)

