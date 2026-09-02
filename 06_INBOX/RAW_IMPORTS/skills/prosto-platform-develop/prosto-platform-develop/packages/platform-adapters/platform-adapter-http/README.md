# @prosto/platform-adapter-http

**Status:** `@alpha`
**Runtime:** Node.js 22.12 or later

Fastify-backed HTTP transport for framework-neutral SDK HTTP route
registrations. It owns HTTP parsing, route dispatch, transport security headers,
CORS, response serialization, request cancellation, and safe error envelopes.

The public API exposes `PlatformHttpServer` and adapter-neutral configuration,
lifecycle, error, and observability types. Fastify and Node request/response
types do not cross the package boundary. Route contracts are imported directly
from `@prosto/platform-sdk`.

The root export intentionally does not expose Fastify mappers or adapter runtime
helpers. They are implementation details and cannot be used as a second public
transport API.

## Lifecycle and routes

Create the server in an application composition root, register all SDK route
registrations, then start it. Routes cannot be registered after startup begins.
`stop()` performs graceful shutdown and is safe to call concurrently.

```ts
import { PlatformHttpServer } from '@prosto/platform-adapter-http';
import type { IPlatformHttpRouteRegistration } from '@prosto/platform-sdk';

const registrations: readonly IPlatformHttpRouteRegistration[] = [/* routes */];
const server = new PlatformHttpServer({
  host: '127.0.0.1',
  port: 3000,
  cors: {
    allowedOrigins: ['https://admin.example.test'],
    allowedMethods: ['GET', 'POST'],
  },
});

server.registerRoutes(registrations);
await server.start();

// A process-level composition root should await this on SIGTERM/SIGINT.
await server.stop();
```

## Admin BFF composition root

The HTTP adapter does not import the Admin BFF adapter. A composition-root
application creates both adapters, provides the BFF-specific context factory,
and converts handlers to SDK registrations before starting the server.

```ts
import { PlatformAdminBffAdapter, type IAdminBffRouteContext } from '@prosto/platform-adapter-admin-bff';
import { PlatformHttpServer } from '@prosto/platform-adapter-http';
import {
  isPlatformDelegatedIdentity,
  PlatformHttpError,
  PlatformHttpRouteRegistration,
  type IPlatformHttpRouteContextFactory,
  type IPlatformHttpRouteContextFactoryInput,
} from '@prosto/platform-sdk';

class AdminContextFactory
  implements IPlatformHttpRouteContextFactory<IAdminBffRouteContext>
{
  async create(input: IPlatformHttpRouteContextFactoryInput): Promise<IAdminBffRouteContext> {
    if (!isPlatformDelegatedIdentity(input.baseContext.identity)) {
      throw new PlatformHttpError('HTTP_UNAUTHENTICATED', 'Admin access requires a delegated identity.');
    }

    return {
      ...input.baseContext,
      identity: input.baseContext.identity,
      discoveryService,
      permissionService,
      diagnosticsService,
      logger: adminBffLogger,
    };
  }
}

const adminBff = new PlatformAdminBffAdapter(
  discoveryService,
  permissionService,
  diagnosticsService,
  { logger: adminBffLogger },
);
const server = new PlatformHttpServer({ host: '127.0.0.1', port: 3000 });
const contextFactory = new AdminContextFactory();

server.registerRoutes(
  adminBff.getHandlers().map(
    (handler) => new PlatformHttpRouteRegistration(handler, contextFactory),
  ),
);
await server.start();
```

`AdminContextFactory` rejects anonymous identities before a BFF handler runs.
The executable `examples/admin-bff-http-host` contains the complete composition
root, including platform health/readiness registrations and `SIGINT`/`SIGTERM`
shutdown handling.

The adapter maps a Fastify request to immutable SDK request/context input. A
configured identity resolver runs once per request. Without one, the handler
receives an explicit anonymous identity. Authentication and authorization policy
remain route responsibilities. JWT/session validation, cookie parsing, and
cookie mutation are intentionally out of scope until a dedicated authentication
adapter exists. An identity-provider outage returns a safe `503`.

Handler responses support JSON, finite `Uint8Array` binary data, and Web
`ReadableStream<Uint8Array>` bodies. Explicitly registered `HEAD` routes retain
status and content headers but do not send a body. Request disconnects, timeout,
and forced graceful shutdown abort the request signal and cancel active streams.

All transport errors use the correlated envelope
`{ correlationId, error: { code, message } }`. Raw paths, stack traces, causes,
and request payloads are not exposed in client responses.

## Security boundary

Helmet security headers are enabled by the adapter. Custom handler headers cannot
override content metadata, the correlation ID, cookies, Helmet headers, or
adapter-managed `Access-Control-*` headers. CORS is disabled by default; when
enabled it uses exact origin and method allowlists only. Buffered JSON, UTF-8
text, and octet-stream requests are constrained by `bodyLimitBytes`; multipart,
request streams, rate limiting, and TLS termination are not provided.

## Trusted proxy boundary

TLS is terminated by the ingress or reverse proxy; this adapter does not
accept TLS certificates or private keys. `trustedProxyAddresses` is optional
and must contain only the explicit IP addresses or CIDR ranges of that trusted
ingress/reverse-proxy network. When it is omitted (or empty), forwarded headers
are not trusted.

Only configure `trustedProxyAddresses` for addresses controlled by the ingress.
Trusting arbitrary sources allows clients to spoof forwarded protocol and client
address headers. TLS certificates and private keys are intentionally outside this
adapter's scope.

## Composition boundaries

This package must not import platform core, persistence adapters, feature
modules, or Admin BFF code. A composition-root application supplies SDK route
registrations and any route context factories. This keeps HTTP transport policy
separate from domain and application policy.

## Package commands

```bash
npm run --workspace @prosto/platform-adapter-http typecheck
npm run --workspace @prosto/platform-adapter-http test
npm run --workspace @prosto/platform-adapter-http build
```
