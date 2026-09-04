import type { IAdminDiscoveryPayload } from '@prosto/platform-admin-contracts';
import type { IAdminDiscoveryBffEnvelope } from '@/shared/api/admin-discovery/index.js';
import {
  AdminDiscoveryClient,
  AdminDiscoveryClientError,
} from '@/shared/api/admin-discovery/index.js';
import { describe, expect, it, vi } from 'vitest';

function createValidPayload(): IAdminDiscoveryPayload {
  return {
    schemaVersion: 'admin-discovery-payload.v1',
    generatedAt: new Date().toISOString(),
    plugins: [],
    rejected: [],
  };
}

function createBffEnvelope(
  payload: IAdminDiscoveryPayload,
  overrides?: { correlationId?: string; diagnostics?: unknown },
): IAdminDiscoveryBffEnvelope {
  return {
    correlationId: overrides?.correlationId ?? 'adm-test-abc',
    data: payload,
    diagnostics: {
      acceptedCount: 0,
      rejectedCount: 0,
      duration: 42,
      ...(overrides?.diagnostics as object | undefined),
    },
  } as IAdminDiscoveryBffEnvelope;
}

function createFetchMock(response: {
  ok: boolean;
  status?: number;
  json: () => Promise<unknown>;
}): typeof fetch {
  return vi.fn().mockResolvedValue(response);
}

function createRejectingFetchMock(error: Error): typeof fetch {
  return vi.fn().mockRejectedValue(error);
}

describe('AdminDiscoveryClient', () => {
  describe('getDiscovery', () => {
    it('should return success with valid discovery payload', async () => {
      const payload = createValidPayload();
      const envelope = createBffEnvelope(payload);
      const fetchMock = createFetchMock({
        ok: true,
        json: () => Promise.resolve(envelope),
      });

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(true);

      if (result.success) {
        expect(result.payload.schemaVersion).toBe('admin-discovery-payload.v1');
        expect(result.correlationId).toBe('adm-test-abc');
        expect(result.diagnostics.duration).toBe(42);
      }
    });

    it('should call the correct BFF discovery endpoint', async () => {
      const payload = createValidPayload();
      const envelope = createBffEnvelope(payload);
      const fetchMock = createFetchMock({
        ok: true,
        json: () => Promise.resolve(envelope),
      });

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      await client.getDiscovery();

      expect(fetchMock).toHaveBeenCalledWith(
        'http://localhost:3001/admin/api/v1/discovery',
        expect.objectContaining({ method: 'GET' }),
      );
    });

    it('should strip trailing slashes from baseUrl', async () => {
      const payload = createValidPayload();
      const envelope = createBffEnvelope(payload);
      const fetchMock = createFetchMock({
        ok: true,
        json: () => Promise.resolve(envelope),
      });

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001///',
        fetch: fetchMock,
      });

      await client.getDiscovery();

      expect(fetchMock).toHaveBeenCalledWith(
        'http://localhost:3001/admin/api/v1/discovery',
        expect.anything(),
      );
    });

    it('should return TIMEOUT on abort', async () => {
      const abortError = new DOMException(
        'The operation was aborted.',
        'AbortError',
      );
      const fetchMock = createRejectingFetchMock(abortError);

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
        timeoutMs: 1,
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(false);

      if (!result.success) {
        expect(result.reason).toBe('TIMEOUT');
      }
    });

    it('should return NETWORK_ERROR on fetch failure', async () => {
      const fetchMock = createRejectingFetchMock(
        new Error('Connection refused'),
      );

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(false);

      if (!result.success) {
        expect(result.reason).toBe('NETWORK_ERROR');
      }

      if ('message' in result) {
        expect(result.message).toBe('Connection refused');
      }
    });

    it('should return HTTP_ERROR on non-ok response', async () => {
      const fetchMock = createFetchMock({
        ok: false,
        status: 500,
        json: () => Promise.resolve({}),
      });

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(false);

      if ('statusCode' in result) {
        expect(result.reason).toBe('HTTP_ERROR');
        expect(result.statusCode).toBe(500);
      }
    });

    it('should return UNAUTHENTICATED on HTTP 401', async () => {
      const fetchMock = createFetchMock({
        ok: false,
        status: 401,
        json: () => Promise.resolve({}),
      });
      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      const result = await client.getDiscovery();

      expect(result).toEqual({
        success: false,
        reason: 'UNAUTHENTICATED',
        message: 'Authentication is required.',
        statusCode: 401,
      });
    });

    it('should return VALIDATION_FAILED on invalid JSON body', async () => {
      const fetchMock = createFetchMock({
        ok: true,
        json: () => Promise.reject(new Error('Unexpected token')),
      });

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(false);

      if ('issues' in result) {
        expect(result.reason).toBe('VALIDATION_FAILED');
        expect(result.issues).toBeDefined();
        expect(result.issues.length).toBeGreaterThan(0);
      }
    });

    it('should return VALIDATION_FAILED when data field is missing', async () => {
      const fetchMock = createFetchMock({
        ok: true,
        json: () => Promise.resolve({ correlationId: 'abc' }),
      });

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(false);

      if (!result.success && 'correlationId' in result) {
        expect(result.reason).toBe('VALIDATION_FAILED');
        expect(result.correlationId).toBe('abc');
      }
    });

    it('should return VALIDATION_FAILED on invalid envelope shape', async () => {
      const fetchMock = createFetchMock({
        ok: true,
        json: () => Promise.resolve('not-an-object'),
      });

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(false);

      if (!result.success) {
        expect(result.reason).toBe('VALIDATION_FAILED');
      }
    });

    it('should return VALIDATION_FAILED with issues on invalid payload', async () => {
      const invalidPayload = {
        schemaVersion: 'wrong-version',
        generatedAt: 'not-a-date',
        plugins: 'not-an-array',
        rejected: [],
      };

      const fetchMock = createFetchMock({
        ok: true,
        json: () => Promise.resolve(createBffEnvelope(invalidPayload as never)),
      });

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(false);

      if ('issues' in result) {
        expect(result.reason).toBe('VALIDATION_FAILED');
        expect(result.issues.length).toBeGreaterThan(0);
      }
    });

    it('should pass correlationId through on validation failure', async () => {
      const fetchMock = createFetchMock({
        ok: true,
        json: () =>
          Promise.resolve({
            correlationId: 'adm-trace-123',
            data: { invalid: true },
            diagnostics: { acceptedCount: 0, rejectedCount: 0, duration: 5 },
          }),
      });

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(false);

      if ('correlationId' in result) {
        expect(result.correlationId).toBe('adm-trace-123');
      }
    });

    it('should include Accept header', async () => {
      const payload = createValidPayload();
      const envelope = createBffEnvelope(payload);
      const fetchMock = createFetchMock({
        ok: true,
        json: () => Promise.resolve(envelope),
      });

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      await client.getDiscovery();

      expect(fetchMock).toHaveBeenCalledWith(
        expect.anything(),
        expect.objectContaining({
          headers: { Accept: 'application/json' },
          credentials: 'same-origin',
        }),
      );
    });

    it('should use fallback diagnostics when envelope diagnostics are missing', async () => {
      const payload = createValidPayload();
      const envelope = {
        correlationId: 'adm-xyz',
        data: payload,
      };

      const fetchMock = createFetchMock({
        ok: true,
        json: () => Promise.resolve(envelope),
      });

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(true);

      if (result.success) {
        expect(result.diagnostics.acceptedCount).toBe(0);
        expect(result.diagnostics.rejectedCount).toBe(0);
        expect(result.diagnostics.duration).toBe(0);
      }
    });

    it('should normalize invalid diagnostics fields with safe fallbacks', async () => {
      const payload = createValidPayload();
      const envelope = createBffEnvelope(payload, {
        diagnostics: {
          acceptedCount: Number.NaN,
          rejectedCount: 'invalid',
          duration: Number.POSITIVE_INFINITY,
        },
      });
      const fetchMock = createFetchMock({
        ok: true,
        json: () => Promise.resolve(envelope),
      });

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      const result = await client.getDiscovery();

      expect(result.success).toBe(true);

      if (result.success) {
        expect(result.diagnostics).toEqual({
          acceptedCount: payload.plugins.length,
          rejectedCount: payload.rejected.length,
          duration: 0,
        });
      }
    });

    it('should clear request timeout after a successful response', async () => {
      const clearTimeoutSpy = vi.spyOn(globalThis, 'clearTimeout');
      const payload = createValidPayload();
      const envelope = createBffEnvelope(payload);
      const fetchMock = createFetchMock({
        ok: true,
        json: () => Promise.resolve(envelope),
      });

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      await client.getDiscovery();

      expect(clearTimeoutSpy).toHaveBeenCalledTimes(1);
      clearTimeoutSpy.mockRestore();
    });
  });

  describe('getDiscoveryOrThrow', () => {
    it('should return payload on success', async () => {
      const payload = createValidPayload();
      const envelope = createBffEnvelope(payload);
      const fetchMock = createFetchMock({
        ok: true,
        json: () => Promise.resolve(envelope),
      });

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      const result = await client.getDiscoveryOrThrow();

      expect(result.schemaVersion).toBe('admin-discovery-payload.v1');
    });

    it('should throw AdminDiscoveryClientError on HTTP error', async () => {
      const fetchMock = createFetchMock({
        ok: false,
        status: 403,
        json: () => Promise.resolve({}),
      });

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      await expect(client.getDiscoveryOrThrow()).rejects.toThrow(
        AdminDiscoveryClientError,
      );

      try {
        await client.getDiscoveryOrThrow();
      } catch (error) {
        expect(error).toBeInstanceOf(AdminDiscoveryClientError);

        if (error instanceof AdminDiscoveryClientError) {
          expect(error.reason).toBe('HTTP_ERROR');
          expect(error.statusCode).toBe(403);
        }
      }
    });

    it('should throw UNAUTHENTICATED on HTTP 401', async () => {
      const fetchMock = createFetchMock({
        ok: false,
        status: 401,
        json: () => Promise.resolve({}),
      });
      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      await expect(client.getDiscoveryOrThrow()).rejects.toMatchObject({
        reason: 'UNAUTHENTICATED',
        statusCode: 401,
      });
    });

    it('should throw AdminDiscoveryClientError on network error', async () => {
      const fetchMock = createRejectingFetchMock(new Error('ECONNREFUSED'));

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      await expect(client.getDiscoveryOrThrow()).rejects.toThrow(
        AdminDiscoveryClientError,
      );
    });

    it('should throw AdminDiscoveryClientError on validation failure', async () => {
      const fetchMock = createFetchMock({
        ok: true,
        json: () =>
          Promise.resolve({
            correlationId: 'adm-fail',
            data: { schemaVersion: 'wrong' },
            diagnostics: { acceptedCount: 0, rejectedCount: 0, duration: 0 },
          }),
      });

      const client = new AdminDiscoveryClient({
        baseUrl: 'http://localhost:3001',
        fetch: fetchMock,
      });

      await expect(client.getDiscoveryOrThrow()).rejects.toThrow(
        AdminDiscoveryClientError,
      );

      try {
        await client.getDiscoveryOrThrow();
      } catch (error) {
        if (error instanceof AdminDiscoveryClientError) {
          expect(error.reason).toBe('VALIDATION_FAILED');
          expect(error.issues).toBeDefined();
          expect(error.issues.length).toBeGreaterThan(0);
        }
      }
    });
  });
});
