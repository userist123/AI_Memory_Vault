import type {
  IPlatformHttpRouteRegistration,
  IPlatformHttpRouteHandler,
  IPlatformHttpRouteContext,
  IPlatformHttpRouteContextFactory,
  IPlatformHttpRouteContextFactoryInput,
  IPlatformHttpResponse,
} from '../interfaces/index.js';
import type { PlatformHttpMethodType } from '../types/index.js';
import { PlatformHttpError } from '../errors/index.js';

/**
 * @alpha
 * Generic route registration that links a typed handler with its context factory.
 * Implements the non-generic {@link IPlatformHttpRouteRegistration} so the server
 * can call `execute` without knowing the concrete context type.
 */
export class PlatformHttpRouteRegistration<
  TContext extends IPlatformHttpRouteContext,
> implements IPlatformHttpRouteRegistration {
  readonly method: PlatformHttpMethodType;
  readonly route: string;

  private readonly ROUTE_PARAM_PATTERN = /^:[a-zA-Z_][a-zA-Z0-9_]*$/;
  private readonly ROUTE_SEGMENT_PATTERN = /^[a-zA-Z0-9\-_.~]+$/;

  constructor(
    private readonly _handler: IPlatformHttpRouteHandler<TContext>,
    private readonly _contextFactory: IPlatformHttpRouteContextFactory<TContext>,
  ) {
    this._validateRouteGrammar(_handler.route);

    this.method = _handler.method;
    this.route = _handler.route;
  }

  async execute(
    input: IPlatformHttpRouteContextFactoryInput,
  ): Promise<IPlatformHttpResponse> {
    const context = await this._contextFactory.create(input);

    return this._handler.handle(input.request, context);
  }

  private _validateRouteGrammar(route: string): void {
    if (!route.startsWith('/')) {
      throw new PlatformHttpError(
        'INVALID_ROUTE_GRAMMAR',
        `Route "${route}" must start with "/".`,
        { route },
      );
    }

    if (route.endsWith('/') && route.length > 1) {
      throw new PlatformHttpError(
        'INVALID_ROUTE_GRAMMAR',
        `Route "${route}" must not have trailing slash.`,
        { route },
      );
    }

    const segments = route.split('/').filter((s) => s.length);

    if (!segments.length) {
      throw new PlatformHttpError(
        'INVALID_ROUTE_GRAMMAR',
        `Route "${route}" must have at least one segment.`,
        { route },
      );
    }

    for (const segment of segments) {
      if (segment.startsWith(':')) {
        if (!this.ROUTE_PARAM_PATTERN.test(segment)) {
          throw new PlatformHttpError(
            'INVALID_ROUTE_GRAMMAR',
            `Route parameter "${segment}" in "${route}" must be a valid ASCII identifier.`,
            { route, segment },
          );
        }
      } else if (!this.ROUTE_SEGMENT_PATTERN.test(segment)) {
        throw new PlatformHttpError(
          'INVALID_ROUTE_GRAMMAR',
          `Route segment "${segment}" in "${route}" contains forbidden characters.`,
          { route, segment },
        );
      }
    }
  }
}
