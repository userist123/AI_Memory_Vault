import type {
  IPlatformHttpRouteRegistration,
  IPlatformRequestIdentityResolver,
  IPlatformSecretCipher,
} from '@prosto/platform-sdk';

/** @alpha */
export type PlatformOidcSessionAlgorithmType = 'RS256' | 'PS256' | 'ES256';

/** @alpha */
export interface IPlatformAuthOidcSessionModuleConfig {
  readonly issuer: string;
  readonly jwksUri: string;
  readonly authorizationEndpoint: string;
  readonly tokenEndpoint: string;
  readonly revocationEndpoint: string;
  readonly redirectUri: string;
  readonly clientId: string;
  readonly clientSecret: string;
  readonly scopes: readonly string[];
  readonly audiences: readonly string[];
  readonly resource?: string;
  readonly allowedAlgorithms?: readonly PlatformOidcSessionAlgorithmType[];
  readonly cookieVersion?: number;
  readonly cipher: IPlatformSecretCipher;
  readonly accessTokenResolver: IPlatformRequestIdentityResolver;
}

/** @alpha */
export interface IPlatformAuthOidcSessionModuleFacade {
  readonly resolver: IPlatformRequestIdentityResolver;
  readonly routes: readonly IPlatformHttpRouteRegistration[];
  readonly ready: boolean;
}
