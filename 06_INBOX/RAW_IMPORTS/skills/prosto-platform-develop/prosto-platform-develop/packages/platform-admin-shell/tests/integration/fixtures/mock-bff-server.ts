import type {
  IAdminDiscoveredPluginDescriptor,
  IAdminDiscoveryPayload,
  IAdminRejectedPluginDiagnostic,
} from '@prosto/platform-admin-contracts';
import type {
  IAdminDiscoveryBffEnvelope,
  IAdminDiscoveryClientDiagnostics,
} from '@/shared/api/admin-discovery/admin-discovery-client.types.js';
import { vi } from 'vitest';

/**
 * Mock BFF response builder for integration tests.
 */
export class MockBffServer {
  private _discoveryPayload: IAdminDiscoveryPayload | null = null;
  private _diagnosticsOverride: Partial<IAdminDiscoveryClientDiagnostics> = {};
  private _httpStatus = 200;
  private _networkError: Error | null = null;
  private _timeoutMs: number | null = null;
  private _requestLog: { url: string; method: string }[] = [];

  get requestLog(): { url: string; method: string }[] {
    return this._requestLog;
  }

  withDiscoveryPayload(
    plugins: IAdminDiscoveredPluginDescriptor[],
    rejected?: IAdminRejectedPluginDiagnostic[],
  ): this {
    this._discoveryPayload = {
      schemaVersion: 'admin-discovery-payload.v1',
      generatedAt: new Date().toISOString(),
      plugins,
      rejected: rejected ?? [],
    };
    return this;
  }

  withFullPayload(payload: IAdminDiscoveryPayload): this {
    this._discoveryPayload = payload;
    return this;
  }

  withDiagnostics(
    diagnostics: Partial<IAdminDiscoveryClientDiagnostics>,
  ): this {
    this._diagnosticsOverride = diagnostics;
    return this;
  }

  withHttpStatus(status: number): this {
    this._httpStatus = status;
    return this;
  }

  withNetworkError(error: Error): this {
    this._networkError = error;
    return this;
  }

  withTimeout(): this {
    this._timeoutMs = 1;
    return this;
  }

  reset(): void {
    this._discoveryPayload = null;
    this._diagnosticsOverride = {};
    this._httpStatus = 200;
    this._networkError = null;
    this._timeoutMs = null;
    this._requestLog = [];
  }

  buildFetch(): typeof fetch {
    this._requestLog = [];

    return vi
      .fn()
      .mockImplementation(
        async (
          url: string | URL | Request,
          init?: RequestInit,
        ): Promise<Response> => {
          const urlStr = typeof url === 'string' ? url : url.toString();
          this._requestLog.push({ url: urlStr, method: init?.method ?? 'GET' });

          if (this._networkError) {
            throw this._networkError;
          }

          if (this._timeoutMs !== null) {
            throw new DOMException('The operation was aborted.', 'AbortError');
          }

          if (this._httpStatus >= 400) {
            return {
              ok: false,
              status: this._httpStatus,
              statusText: `HTTP ${this._httpStatus}`,
              json: () =>
                Promise.resolve({
                  error: {
                    code: 'HTTP_ERROR',
                    message: `HTTP ${this._httpStatus}`,
                  },
                }),
              headers: new Headers(),
            } as Response;
          }

          const diagnostics: IAdminDiscoveryClientDiagnostics = {
            acceptedCount:
              this._diagnosticsOverride.acceptedCount ??
              this._discoveryPayload?.plugins.length ??
              0,
            rejectedCount:
              this._diagnosticsOverride.rejectedCount ??
              this._discoveryPayload?.rejected.length ??
              0,
            duration: this._diagnosticsOverride.duration ?? 42,
          };

          const envelope: IAdminDiscoveryBffEnvelope = {
            correlationId: `adm-mock-${Date.now().toString(36)}`,
            data: this._discoveryPayload ?? {
              schemaVersion: 'admin-discovery-payload.v1',
              generatedAt: new Date().toISOString(),
              plugins: [],
              rejected: [],
            },
            diagnostics,
          };

          return {
            ok: true,
            status: 200,
            statusText: 'OK',
            json: () => Promise.resolve(envelope),
            headers: new Headers(),
          } as Response;
        },
      );
  }
}
