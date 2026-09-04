import { describe, expect, it } from 'vitest';
import {
  PlatformDelegatedIdentity,
  PlatformHttpRequest,
  PlatformHttpResponse,
} from '@prosto/platform-sdk';
import type { IAdminBffRouteContext } from '@/admin-bff.interfaces.js';
import type { IPlatformHttpRouteHandler } from '@prosto/platform-sdk';
import { AdminActionRouteHandler } from '@/routes/index.js';
import { AdminPermissionMappingService } from '@/permissions/index.js';

function createDelegatedIdentity(
  subjectId = 'test-user',
  roles: string[] = ['admin'],
  permissions: string[] = ['read:admin', 'write:admin'],
) {
  return new PlatformDelegatedIdentity({ subjectId, roles, permissions });
}

function createSdkRequest(overrides?: {
  method?: 'GET' | 'POST';
  path?: string;
  params?: Record<string, string>;
  correlationId?: string;
  identity?: ReturnType<typeof createDelegatedIdentity>;
}) {
  return new PlatformHttpRequest({
    method: overrides?.method ?? 'GET',
    path: overrides?.path ?? '/test',
    params: overrides?.params ?? {},
    query: {},
    headers: {},
    body: { variant: 'empty' as const },
    correlationId: overrides?.correlationId ?? 'test-cid-001',
    identity: overrides?.identity ?? createDelegatedIdentity(),
  });
}

function createMockContext(overrides?: {
  correlationId?: string;
  identity?: ReturnType<typeof createDelegatedIdentity>;
}): IAdminBffRouteContext {
  return {
    correlationId: overrides?.correlationId ?? 'test-cid-001',
    identity: overrides?.identity ?? createDelegatedIdentity(),
    signal: new AbortController().signal,
    discoveryService: {} as IAdminBffRouteContext['discoveryService'],
    permissionService: new AdminPermissionMappingService({
      policy: {
        schemaVersion: 'admin-permission-policy.v1',
        roleMappings: [
          {
            roleId: 'admin',
            permissions: ['read:admin', 'write:admin'],
          },
        ],
        actionGates: [
          {
            actionId: 'test-action',
            requiredPermissions: ['read:admin'],
            match: 'any',
            effect: 'allow',
            remediationHint: 'Permission denied',
          },
        ],
      },
    }),
    diagnosticsService: {} as IAdminBffRouteContext['diagnosticsService'],
    logger: {
      /* eslint-disable @typescript-eslint/no-empty-function */
      debug: () => {},
      info: () => {},
      warn: () => {},
      error: () => {},
      /* eslint-enable @typescript-eslint/no-empty-function */
    },
  };
}

describe('Admin BFF SDK integration bridge', () => {
  describe('handler receives SDK request and BFF context', () => {
    it('should extract route params from SDK request', async () => {
      const handler: IPlatformHttpRouteHandler<IAdminBffRouteContext> =
        new AdminActionRouteHandler();
      const request = createSdkRequest({
        method: 'POST',
        path: '/admin/api/v1/action/test-action',
        params: { actionId: 'test-action' },
      });
      const context = createMockContext();

      const response = await handler.handle(request, context);

      expect(response.status).toBe(200);
      expect(response.body.variant).toBe('json');

      const data = (response.body as { variant: 'json'; data: unknown })
        .data as {
        correlationId: string;
        data: { actionId: string; allowed: boolean };
      };
      expect(data.correlationId).toBe('test-cid-001');
      expect(data.data.actionId).toBe('test-action');
      expect(data.data.allowed).toBe(true);
    });

    it('should use correlation ID from request scope in response', async () => {
      const handler: IPlatformHttpRouteHandler<IAdminBffRouteContext> =
        new AdminActionRouteHandler();
      const request = createSdkRequest({
        method: 'POST',
        path: '/admin/api/v1/action/test-action',
        params: { actionId: 'test-action' },
        correlationId: 'my-custom-cid-999',
      });
      const context = createMockContext({ correlationId: 'my-custom-cid-999' });

      const response = await handler.handle(request, context);

      const data = (response.body as { variant: 'json'; data: unknown })
        .data as {
        correlationId: string;
      };
      expect(data.correlationId).toBe('my-custom-cid-999');
    });

    it('should access delegated identity from BFF context', async () => {
      const handler: IPlatformHttpRouteHandler<IAdminBffRouteContext> =
        new AdminActionRouteHandler();
      const identity = createDelegatedIdentity(
        'specific-user-42',
        ['viewer', 'editor'],
        ['read:admin'],
      );
      const request = createSdkRequest({
        method: 'POST',
        path: '/admin/api/v1/action/test-action',
        params: { actionId: 'test-action' },
        identity,
      });
      const context = createMockContext({ identity });

      const response = await handler.handle(request, context);

      expect(response.status).toBe(200);

      const data = (response.body as { variant: 'json'; data: unknown })
        .data as {
        data: { allowed: boolean };
      };
      expect(data.data.allowed).toBe(true);
    });

    it('should deny action when identity lacks required permissions', async () => {
      const handler: IPlatformHttpRouteHandler<IAdminBffRouteContext> =
        new AdminActionRouteHandler();
      const identity = createDelegatedIdentity('no-perm-user', ['guest'], []);
      const request = createSdkRequest({
        method: 'POST',
        path: '/admin/api/v1/action/test-action',
        params: { actionId: 'test-action' },
        identity,
      });
      const context = createMockContext({ identity });

      const response = await handler.handle(request, context);

      expect(response.status).toBe(403);

      const data = (response.body as { variant: 'json'; data: unknown })
        .data as {
        correlationId: string;
        error: { code: string; message: string; remediationHint?: string };
      };
      expect(data.correlationId).toBe('test-cid-001');
      expect(data.error.code).toBe('PERMISSION_REQUIREMENT_NOT_MET');
    });

    it('should preserve current API response structure (status, correlationId, error format)', async () => {
      const handler: IPlatformHttpRouteHandler<IAdminBffRouteContext> =
        new AdminActionRouteHandler();
      const request = createSdkRequest({
        method: 'POST',
        path: '/admin/api/v1/action/test-action',
        params: { actionId: 'test-action' },
      });
      const context = createMockContext();

      const response = await handler.handle(request, context);

      expect(response.status).toBe(200);
      expect(response.body.variant).toBe('json');

      const data = (response.body as { variant: 'json'; data: unknown })
        .data as Record<string, unknown>;
      expect(data).toHaveProperty('correlationId');
      expect(data).toHaveProperty('data');
      expect(data.data).toHaveProperty('actionId', 'test-action');
      expect(data.data).toHaveProperty('allowed', true);
    });

    it('should return 200 status with JSON body variant for successful requests', async () => {
      const handler: IPlatformHttpRouteHandler<IAdminBffRouteContext> =
        new AdminActionRouteHandler();
      const request = createSdkRequest({
        method: 'POST',
        path: '/admin/api/v1/action/test-action',
        params: { actionId: 'test-action' },
      });
      const context = createMockContext();

      const response = await handler.handle(request, context);

      expect(response).toBeInstanceOf(PlatformHttpResponse);
      expect(response.status).toBe(200);
      expect(response.body.variant).toBe('json');
      expect(response.headers).toBeDefined();
    });

    it('should return 403 status with error body when action is denied', async () => {
      const handler: IPlatformHttpRouteHandler<IAdminBffRouteContext> =
        new AdminActionRouteHandler();
      const identity = createDelegatedIdentity('no-perm-user', ['guest'], []);
      const request = createSdkRequest({
        method: 'POST',
        path: '/admin/api/v1/action/test-action',
        params: { actionId: 'test-action' },
        identity,
      });
      const context = createMockContext({ identity });

      const response = await handler.handle(request, context);

      expect(response).toBeInstanceOf(PlatformHttpResponse);
      expect(response.status).toBe(403);
      expect(response.body.variant).toBe('json');

      const data = (response.body as { variant: 'json'; data: unknown })
        .data as {
        error: { code: string };
      };
      expect(data.error.code).toBe('PERMISSION_REQUIREMENT_NOT_MET');
    });
  });

  describe('identity type safety', () => {
    it('should narrow identity to IPlatformDelegatedIdentity in BFF context', () => {
      const identity = createDelegatedIdentity(
        'user-1',
        ['admin'],
        ['read:admin'],
      );

      const context = createMockContext({ identity });

      expect(context.identity.authenticationType).toBe('delegated');
      expect(context.identity.subjectId).toBe('user-1');
      expect(context.identity.roles).toEqual(['admin']);
      expect(context.identity.permissions).toEqual(['read:admin']);
    });

    it('should reject empty subjectId in delegated identity', () => {
      expect(() => {
        new PlatformDelegatedIdentity({
          subjectId: '',
          roles: [],
          permissions: [],
        });
      }).toThrow();
    });
  });
});
