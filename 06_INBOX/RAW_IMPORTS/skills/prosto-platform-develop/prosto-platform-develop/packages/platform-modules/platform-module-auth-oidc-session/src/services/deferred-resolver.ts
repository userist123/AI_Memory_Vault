import {
  type IPlatformIdentityResolutionRequest,
  type IPlatformRequestIdentityResolver,
  PlatformHttpError,
  type PlatformRequestIdentityType,
} from '@prosto/platform-sdk';

export class DeferredResolver implements IPlatformRequestIdentityResolver {
  constructor(
    private readonly _getResolver: () =>
      | IPlatformRequestIdentityResolver
      | undefined,
  ) {}

  get ready(): boolean {
    return this._getResolver() !== undefined;
  }

  async resolve(
    request: IPlatformIdentityResolutionRequest,
  ): Promise<PlatformRequestIdentityType> {
    const resolver = this._getResolver();

    if (resolver === undefined) {
      throw new PlatformHttpError(
        'IDENTITY_RESOLUTION_UNAVAILABLE',
        'OIDC session module is not ready.',
      );
    }

    return resolver.resolve(request);
  }
}
