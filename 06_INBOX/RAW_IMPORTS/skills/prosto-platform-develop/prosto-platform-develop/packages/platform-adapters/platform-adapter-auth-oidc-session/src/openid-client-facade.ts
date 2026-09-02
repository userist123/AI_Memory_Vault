import {
  authorizationCodeGrant,
  buildAuthorizationUrl,
  calculatePKCECodeChallenge,
  ClientSecretBasic,
  Configuration,
  customFetch,
  enableNonRepudiationChecks,
  refreshTokenGrant,
  tokenRevocation,
} from 'openid-client';
import type {
  IPlatformOidcClientFacade,
  IPlatformOidcSessionRuntimeConfig,
} from '@/interfaces/index.js';

interface IOpenIdClientFacadeConfig {
  readonly config: IPlatformOidcSessionRuntimeConfig;
  readonly allowedUrls: ReadonlySet<string>;
}

export function createOpenIdClientFacade(
  config: IPlatformOidcSessionRuntimeConfig,
): IPlatformOidcClientFacade {
  const allowedUrls = new Set([
    config.jwksUri,
    config.tokenEndpoint,
    config.revocationEndpoint,
  ]);
  const client = new Configuration(
    {
      issuer: config.issuer,
      jwks_uri: config.jwksUri,
      authorization_endpoint: config.authorizationEndpoint,
      token_endpoint: config.tokenEndpoint,
      revocation_endpoint: config.revocationEndpoint,
      id_token_signing_alg_values_supported: [
        ...(config.allowedAlgorithms ?? ['RS256']),
      ],
    },
    config.clientId,
    undefined,
    ClientSecretBasic(config.clientSecret),
  );

  client.timeout = 5;
  client[customFetch] = async (url, options): Promise<Response> => {
    if (!allowedUrls.has(url)) {
      throw new Error('OIDC endpoint is not allowed.');
    }

    return fetch(url, {
      ...options,
      redirect: 'error',
    } as unknown as RequestInit);
  };

  enableNonRepudiationChecks(client);

  return new OpenIdClientFacade({ config, allowedUrls }, client);
}

class OpenIdClientFacade implements IPlatformOidcClientFacade {
  constructor(
    private readonly _input: IOpenIdClientFacadeConfig,
    private readonly _client: Configuration,
  ) {}

  async createAuthorizationUrl(input: {
    readonly state: string;
    readonly nonce: string;
    readonly pkceVerifier: string;
  }): Promise<string> {
    const codeChallenge = await calculatePKCECodeChallenge(input.pkceVerifier);
    const url = buildAuthorizationUrl(this._client, {
      response_type: 'code',
      redirect_uri: this._input.config.redirectUri,
      scope: this._input.config.scopes.join(' '),
      state: input.state,
      nonce: input.nonce,
      code_challenge: codeChallenge,
      code_challenge_method: 'S256',
      ...(this._input.config.resource !== undefined && {
        resource: this._input.config.resource,
      }),
    });

    return url.href;
  }

  async exchangeAuthorizationCode(input: {
    readonly code: string;
    readonly state: string;
    readonly nonce: string;
    readonly pkceVerifier: string;
  }): Promise<{
    readonly accessToken: string;
    readonly refreshToken: string;
    readonly expiresIn: number;
  }> {
    const callback = new URL(this._input.config.redirectUri);
    callback.searchParams.set('code', input.code);
    callback.searchParams.set('state', input.state);
    const result = await authorizationCodeGrant(this._client, callback, {
      expectedState: input.state,
      expectedNonce: input.nonce,
      pkceCodeVerifier: input.pkceVerifier,
    });

    return this._toTokenSet(result);
  }

  async refresh(refreshToken: string): Promise<{
    readonly accessToken: string;
    readonly refreshToken: string;
    readonly expiresIn: number;
  }> {
    return this._toTokenSet(
      await refreshTokenGrant(this._client, refreshToken),
    );
  }

  async revoke(refreshToken: string): Promise<void> {
    await tokenRevocation(this._client, refreshToken, {
      token_type_hint: 'refresh_token',
    });
  }

  isInvalidGrant(error: unknown): boolean {
    return (
      typeof error === 'object' &&
      error !== null &&
      'cause' in error &&
      typeof error.cause === 'object' &&
      error.cause !== null &&
      'error' in error.cause &&
      error.cause.error === 'invalid_grant'
    );
  }

  private _toTokenSet(result: {
    readonly access_token?: string;
    readonly refresh_token?: string;
    readonly expires_in?: number;
  }): {
    readonly accessToken: string;
    readonly refreshToken: string;
    readonly expiresIn: number;
  } {
    if (
      typeof result.access_token !== 'string' ||
      typeof result.refresh_token !== 'string' ||
      typeof result.expires_in !== 'number'
    ) {
      throw new Error('OIDC token response is invalid.');
    }

    return Object.freeze({
      accessToken: result.access_token,
      refreshToken: result.refresh_token,
      expiresIn: result.expires_in,
    });
  }
}
