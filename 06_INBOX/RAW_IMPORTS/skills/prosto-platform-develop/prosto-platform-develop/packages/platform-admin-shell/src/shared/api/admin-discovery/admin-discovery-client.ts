import type {
  AdminDiscoveryClientResultType,
  IAdminDiscoveryBffEnvelope,
  IAdminDiscoveryClientConfig,
  IAdminDiscoveryClientDiagnostics,
} from './admin-discovery-client.types.js';
import {
  AdminDiscoveryPayloadValidator,
  type IAdminDiscoveryPayload,
} from '@prosto/platform-admin-contracts';
import { AdminDiscoveryClientError } from './admin-discovery-client.error.js';

/**
 * Default discovery endpoint path matching the admin BFF adapter route.
 */
const DISCOVERY_ENDPOINT = '/admin/api/v1/discovery';

/**
 * Default request timeout in milliseconds.
 */
const DEFAULT_TIMEOUT_MS = 10_000;

/**
 * @alpha
 * Shell-side contract client for retrieving the discovery payload
 * from the admin BFF adapter.
 *
 * Responsibilities:
 * - Issue an HTTP GET to the BFF discovery endpoint.
 * - Parse the BFF response envelope (`correlationId`, `data`, `diagnostics`).
 * - Validate the inner discovery payload against admin contracts schema.
 * - Return a discriminated result type for caller-side branching.
 *
 * The client is framework-agnostic and does not depend on Vue or any
 * UI runtime. It accepts an optional custom `fetch` implementation
 * for testability and environments where global fetch is unavailable.
 *
 * Observability: every result carries the BFF `correlationId` for
 * distributed trace correlation.
 */
export class AdminDiscoveryClient {
  private readonly _baseUrl: string;
  private readonly _timeoutMs: number;
  private readonly _fetch: typeof fetch;
  private readonly _validator: AdminDiscoveryPayloadValidator;

  constructor(config: IAdminDiscoveryClientConfig) {
    this._baseUrl = config.baseUrl.replace(/\/+$/, '');
    this._timeoutMs = config.timeoutMs ?? DEFAULT_TIMEOUT_MS;
    this._fetch = this._resolveFetch(config.fetch);
    this._validator = new AdminDiscoveryPayloadValidator();
  }

  /**
   * Fetches and validates the discovery payload from the admin BFF.
   *
   * @returns A discriminated result containing the validated payload
   *   on success, or a structured failure descriptor on error.
   */
  async getDiscovery(): Promise<AdminDiscoveryClientResultType> {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), this._timeoutMs);
    let response: Response;

    try {
      response = await this._fetch(`${this._baseUrl}${DISCOVERY_ENDPOINT}`, {
        method: 'GET',
        credentials: 'same-origin',
        headers: { Accept: 'application/json' },
        signal: controller.signal,
      });
    } catch (error) {
      if (this._isAbortError(error)) {
        return {
          success: false,
          reason: 'TIMEOUT',
          message: `Discovery request timed out after ${this._timeoutMs}ms.`,
        };
      }

      return {
        success: false,
        reason: 'NETWORK_ERROR',
        message: error instanceof Error ? error.message : String(error),
      };
    } finally {
      clearTimeout(timeoutId);
    }

    if (response.status === 401) {
      return {
        success: false,
        reason: 'UNAUTHENTICATED',
        message: 'Authentication is required.',
        statusCode: 401,
      };
    }

    if (!response.ok) {
      return {
        success: false,
        reason: 'HTTP_ERROR',
        message: `Discovery request failed with HTTP ${response.status}.`,
        statusCode: response.status,
      };
    }

    let envelope: IAdminDiscoveryBffEnvelope;

    try {
      envelope = await response.json();
    } catch {
      return {
        success: false,
        reason: 'VALIDATION_FAILED',
        correlationId: undefined,
        issues: [
          {
            code: 'invalid_json',
            message: 'Response body is not valid JSON.',
            path: '$',
          },
        ],
      };
    }

    if (!this._isRecord(envelope)) {
      return {
        success: false,
        reason: 'VALIDATION_FAILED',
        correlationId: undefined,
        issues: [
          {
            code: 'invalid_envelope',
            message: 'Response envelope is not an object.',
            path: '$',
          },
        ],
      };
    }

    const correlationId =
      typeof envelope.correlationId === 'string'
        ? envelope.correlationId
        : undefined;

    if (!this._isRecord(envelope.data)) {
      return {
        success: false,
        reason: 'VALIDATION_FAILED',
        correlationId,
        issues: [
          {
            code: 'missing_data',
            message: 'Response envelope is missing the "data" field.',
            path: '$.data',
          },
        ],
      };
    }

    const validationResult = this._validator.validate(envelope.data);

    if (!validationResult.success) {
      return {
        success: false,
        reason: 'VALIDATION_FAILED',
        correlationId,
        issues: validationResult.error.issues,
      };
    }

    return {
      success: true,
      payload: validationResult.payload,
      correlationId: correlationId ?? '',
      diagnostics: this._normalizeDiagnostics(
        envelope,
        validationResult.payload,
      ),
    };
  }

  /**
   * Fetches the discovery payload and throws on any failure.
   *
   * Prefer {@link getDiscovery} for caller-side error handling.
   * This method is a convenience for bootstrapping paths where
   * a failure should halt shell startup.
   *
   * @throws {AdminDiscoveryClientError} on authentication, network, HTTP,
   *   timeout, or validation failure.
   */
  async getDiscoveryOrThrow(): Promise<IAdminDiscoveryPayload> {
    const result = await this.getDiscovery();

    if (result.success) {
      return result.payload;
    }

    if ('message' in result) {
      throw new AdminDiscoveryClientError(result.reason, result.message, {
        statusCode: result.statusCode,
      });
    } else {
      throw new AdminDiscoveryClientError(result.reason, 'Validation failed.', {
        issues: result.issues,
      });
    }
  }

  private _resolveFetch(customFetch: typeof fetch | undefined): typeof fetch {
    if (customFetch) {
      return customFetch;
    }

    if (typeof globalThis.fetch !== 'function') {
      throw new AdminDiscoveryClientError(
        'NETWORK_ERROR',
        'Fetch API is not available in the current runtime.',
      );
    }

    return globalThis.fetch.bind(globalThis);
  }

  private _isAbortError(error: unknown): boolean {
    return error instanceof DOMException && error.name === 'AbortError';
  }

  private _isRecord(
    value: unknown,
  ): value is Readonly<Record<string, unknown>> {
    return typeof value === 'object' && value !== null;
  }

  private _readNumberOrFallback(value: unknown, fallback: number): number {
    return typeof value === 'number' && Number.isFinite(value)
      ? value
      : fallback;
  }

  private _normalizeDiagnostics(
    envelope: IAdminDiscoveryBffEnvelope,
    payload: IAdminDiscoveryPayload,
  ): IAdminDiscoveryClientDiagnostics {
    const diagnostics = this._isRecord(envelope.diagnostics)
      ? envelope.diagnostics
      : undefined;

    return {
      acceptedCount: this._readNumberOrFallback(
        diagnostics?.acceptedCount,
        payload.plugins.length,
      ),
      rejectedCount: this._readNumberOrFallback(
        diagnostics?.rejectedCount,
        payload.rejected.length,
      ),
      duration: this._readNumberOrFallback(diagnostics?.duration, 0),
    };
  }
}
