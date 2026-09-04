import {
  type IPlatformHttpResponse,
  type IPlatformHttpRouteContextFactoryInput,
  type IPlatformHttpRouteRegistration,
  PlatformHttpError,
  type PlatformHttpMethodType,
} from '@prosto/platform-sdk';

/** @internal */
export class DeferredRouteRegistration implements IPlatformHttpRouteRegistration {
  constructor(
    readonly method: PlatformHttpMethodType,
    readonly route: string,
    private readonly _getRoute: () =>
      | IPlatformHttpRouteRegistration
      | undefined,
  ) {}

  async execute(
    input: IPlatformHttpRouteContextFactoryInput,
  ): Promise<IPlatformHttpResponse> {
    const route = this._getRoute();

    if (route === undefined) {
      throw new PlatformHttpError(
        'IDENTITY_RESOLUTION_UNAVAILABLE',
        'Local authentication session module is not ready.',
      );
    }

    return route.execute(input);
  }
}
