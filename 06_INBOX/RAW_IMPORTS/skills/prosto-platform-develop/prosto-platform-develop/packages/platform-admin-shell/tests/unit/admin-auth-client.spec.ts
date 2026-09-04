import { describe, expect, it, vi } from 'vitest';
import { AdminAuthClient } from '@/shared/api/admin-auth/index.js';
import type { AdminAuthClientError } from '@/shared/api/admin-auth/index.js';

const CSRF_COOKIE = 'prosto-admin-local-csrf-v1=csrf-value';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('AdminAuthClient', () => {
  it('should get and validate same-origin authentication status', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({
        schemaVersion: 'admin-authentication-api.v1',
        mode: 'local',
        state: 'anonymous',
      }),
    );
    const client = new AdminAuthClient({
      baseUrl: 'https://admin.example',
      fetch: fetchMock,
    });

    await expect(client.getSessionStatus()).resolves.toMatchObject({
      mode: 'local',
      state: 'anonymous',
    });
    expect(fetchMock).toHaveBeenCalledWith(
      'https://admin.example/admin/api/v1/auth/session',
      expect.objectContaining({
        method: 'GET',
        credentials: 'same-origin',
        headers: expect.objectContaining({
          Accept: 'application/json',
          Origin: 'https://admin.example',
        }),
      }),
    );
  });

  it('should acquire CSRF before local login without exposing it to callers', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(
        jsonResponse({
          schemaVersion: 'admin-authentication-api.v1',
          mode: 'local',
          state: 'anonymous',
        }),
      )
      .mockResolvedValueOnce(
        jsonResponse({
          schemaVersion: 'admin-authentication-api.v1',
          mode: 'local',
          state: 'password-change-required',
        }),
      );
    const client = new AdminAuthClient({
      baseUrl: 'https://admin.example',
      fetch: fetchMock,
      readCookie: () => CSRF_COOKIE,
    });

    await expect(client.login('admin', 'correct-password')).resolves.toEqual({
      schemaVersion: 'admin-authentication-api.v1',
      mode: 'local',
      state: 'password-change-required',
    });
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock).toHaveBeenLastCalledWith(
      'https://admin.example/admin/api/v1/auth/login',
      expect.objectContaining({
        method: 'POST',
        credentials: 'same-origin',
        headers: expect.objectContaining({
          'Content-Type': 'application/json',
          'x-prosto-csrf': 'csrf-value',
          Origin: 'https://admin.example',
        }),
        body: JSON.stringify({
          schemaVersion: 'admin-authentication-api.v1',
          username: 'admin',
          password: 'correct-password',
        }),
      }),
    );
  });

  it('should submit password changes through the typed local API', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(
        jsonResponse({
          schemaVersion: 'admin-authentication-api.v1',
          mode: 'local',
          state: 'password-change-required',
        }),
      )
      .mockResolvedValueOnce(
        jsonResponse({
          schemaVersion: 'admin-authentication-api.v1',
          mode: 'local',
          state: 'authenticated',
        }),
      );
    const client = new AdminAuthClient({
      baseUrl: 'https://admin.example',
      fetch: fetchMock,
      readCookie: () => CSRF_COOKIE,
    });

    await expect(
      client.changePassword('old-password', 'new-password'),
    ).resolves.toMatchObject({ state: 'authenticated' });
    expect(fetchMock).toHaveBeenLastCalledWith(
      'https://admin.example/admin/api/v1/auth/change-password',
      expect.objectContaining({
        body: JSON.stringify({
          schemaVersion: 'admin-authentication-api.v1',
          currentPassword: 'old-password',
          newPassword: 'new-password',
        }),
      }),
    );
  });

  it('should logout through the local API with CSRF protection', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(
        jsonResponse({
          schemaVersion: 'admin-authentication-api.v1',
          mode: 'local',
          state: 'authenticated',
        }),
      )
      .mockResolvedValueOnce(
        jsonResponse({
          schemaVersion: 'admin-authentication-api.v1',
          mode: 'local',
          state: 'anonymous',
        }),
      );
    const client = new AdminAuthClient({
      baseUrl: 'https://admin.example',
      fetch: fetchMock,
      readCookie: () => CSRF_COOKIE,
    });

    await expect(client.logout()).resolves.toMatchObject({
      mode: 'local',
      state: 'anonymous',
    });
    expect(fetchMock).toHaveBeenLastCalledWith(
      'https://admin.example/admin/api/v1/auth/logout',
      expect.objectContaining({ method: 'POST', credentials: 'same-origin' }),
    );
  });

  it('should return a generic failure for rejected credentials', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(
        jsonResponse({
          schemaVersion: 'admin-authentication-api.v1',
          mode: 'local',
          state: 'anonymous',
        }),
      )
      .mockResolvedValueOnce(
        jsonResponse(
          {
            schemaVersion: 'admin-authentication-api.v1',
            code: 'AUTHENTICATION_FAILED',
          },
          401,
        ),
      );
    const client = new AdminAuthClient({
      baseUrl: 'https://admin.example',
      fetch: fetchMock,
      readCookie: () => CSRF_COOKIE,
    });

    await expect(client.login('admin', 'incorrect-password')).rejects.toEqual(
      expect.objectContaining({
        name: 'AdminAuthClientError',
        reason: 'AUTHENTICATION_FAILED',
      } satisfies Partial<AdminAuthClientError>),
    );
  });

  it('should fail when the CSRF cookie is unavailable', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({
        schemaVersion: 'admin-authentication-api.v1',
        mode: 'local',
        state: 'anonymous',
      }),
    );
    const client = new AdminAuthClient({
      baseUrl: 'https://admin.example',
      fetch: fetchMock,
      readCookie: () => '',
    });

    await expect(client.acquireCsrf()).rejects.toMatchObject({
      reason: 'CSRF_UNAVAILABLE',
    });
  });
});
