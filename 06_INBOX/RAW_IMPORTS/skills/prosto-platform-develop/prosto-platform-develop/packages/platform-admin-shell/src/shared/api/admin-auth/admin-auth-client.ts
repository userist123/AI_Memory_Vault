import {
  ADMIN_AUTHENTICATION_API_ROUTES,
  ADMIN_AUTHENTICATION_API_SCHEMA_VERSION,
  AdminAuthenticationContractValidator,
  type AdminAuthenticationSessionResponseType,
  type IAdminAuthenticationChangePasswordResponse,
  type IAdminAuthenticationLoginResponse,
  type IAdminAuthenticationLogoutResponse,
} from '@prosto/platform-admin-contracts';
import { AdminAuthClientError } from './admin-auth-client.error.js';

const LOCAL_CSRF_COOKIE_NAME = 'prosto-admin-local-csrf-v1';
const CSRF_HEADER_NAME = 'x-prosto-csrf';

/**
 * @alpha
 * Configuration for the same-origin admin authentication client.
 */
export interface IAdminAuthClientConfig {
  readonly baseUrl: string;
  readonly fetch?: typeof fetch;
  /** Test seam for the non-HttpOnly local CSRF cookie. */
  readonly readCookie?: () => string;
}

/**
 * @alpha
 * Same-origin client for browser authentication broker operations.
 */
export class AdminAuthClient {
  private readonly _baseUrl: string;
  private readonly _origin: string;
  private readonly _fetch: typeof fetch;
  private readonly _readCookie: () => string;
  private readonly _validator = new AdminAuthenticationContractValidator();

  constructor(config: IAdminAuthClientConfig) {
    this._baseUrl = config.baseUrl.replace(/\/+$/, '');
    this._origin = new URL(config.baseUrl).origin;
    this._fetch = config.fetch ?? globalThis.fetch.bind(globalThis);
    this._readCookie = config.readCookie ?? (() => document.cookie);
  }

  /**
   * Gets the provider-neutral authentication status and refreshes the local
   * CSRF cookie when the selected provider is local.
   */
  async getSessionStatus(): Promise<AdminAuthenticationSessionResponseType> {
    const response = await this._fetch(
      this._url(ADMIN_AUTHENTICATION_API_ROUTES.SESSION),
      {
        method: 'GET',
        credentials: 'same-origin',
        headers: this._headers(),
      },
    );

    return this._parseSuccess(response, (payload) =>
      this._validator.parseSessionResponse(payload),
    );
  }

  /**
   * Acquires the rotating CSRF value required by local JSON mutations.
   * The value remains private to this client and is never exposed to pages.
   */
  async acquireCsrf(): Promise<string> {
    await this.getSessionStatus();
    const csrfToken = this._cookie(LOCAL_CSRF_COOKIE_NAME);

    if (!csrfToken) {
      throw new AdminAuthClientError('CSRF_UNAVAILABLE');
    }

    return csrfToken;
  }

  /** Submits local credentials and returns the validated state transition. */
  async login(
    username: string,
    password: string,
  ): Promise<IAdminAuthenticationLoginResponse> {
    const csrfToken = await this.acquireCsrf();

    return this._post(
      ADMIN_AUTHENTICATION_API_ROUTES.LOGIN,
      {
        schemaVersion: ADMIN_AUTHENTICATION_API_SCHEMA_VERSION,
        username,
        password,
      },
      csrfToken,
      (payload) => this._validator.parseLoginResponse(payload),
    );
  }

  /** Completes a local password-change flow and rotates the browser session. */
  async changePassword(
    currentPassword: string,
    newPassword: string,
  ): Promise<IAdminAuthenticationChangePasswordResponse> {
    const csrfToken = await this.acquireCsrf();

    return this._post(
      ADMIN_AUTHENTICATION_API_ROUTES.CHANGE_PASSWORD,
      {
        schemaVersion: ADMIN_AUTHENTICATION_API_SCHEMA_VERSION,
        currentPassword,
        newPassword,
      },
      csrfToken,
      (payload) => this._validator.parseChangePasswordResponse(payload),
    );
  }

  /** Invalidates a local server-side session. */
  async logout(): Promise<IAdminAuthenticationLogoutResponse> {
    const csrfToken = await this.acquireCsrf();

    return this._post(
      ADMIN_AUTHENTICATION_API_ROUTES.LOGOUT,
      { schemaVersion: ADMIN_AUTHENTICATION_API_SCHEMA_VERSION },
      csrfToken,
      (payload) => this._validator.parseLogoutResponse(payload),
    );
  }

  private async _post<T>(
    path: string,
    body: Record<string, string>,
    csrfToken: string,
    parse: (payload: unknown) => T,
  ): Promise<T> {
    const response = await this._fetch(this._url(path), {
      method: 'POST',
      credentials: 'same-origin',
      headers: this._headers(csrfToken),
      body: JSON.stringify(body),
    });

    return this._parseSuccess(response, parse);
  }

  private async _parseSuccess<T>(
    response: Response,
    parse: (payload: unknown) => T,
  ): Promise<T> {
    let payload: unknown;

    try {
      payload = await response.json();
    } catch {
      throw new AdminAuthClientError('INVALID_RESPONSE');
    }

    if (!response.ok) {
      throw new AdminAuthClientError('AUTHENTICATION_FAILED');
    }

    try {
      return parse(payload);
    } catch {
      throw new AdminAuthClientError('INVALID_RESPONSE');
    }
  }

  private _headers(csrfToken?: string): HeadersInit {
    return {
      Accept: 'application/json',
      'Content-Type': 'application/json',
      ...(csrfToken === undefined
        ? {}
        : {
            [CSRF_HEADER_NAME]: csrfToken,
          }),
      Origin: this._origin,
    };
  }

  private _url(path: string): string {
    return `${this._baseUrl}${path}`;
  }

  private _cookie(name: string): string | undefined {
    const prefix = `${name}=`;

    for (const entry of this._readCookie().split(';')) {
      const value = entry.trim();

      if (value.startsWith(prefix)) {
        return decodeURIComponent(value.slice(prefix.length));
      }
    }

    return undefined;
  }
}
