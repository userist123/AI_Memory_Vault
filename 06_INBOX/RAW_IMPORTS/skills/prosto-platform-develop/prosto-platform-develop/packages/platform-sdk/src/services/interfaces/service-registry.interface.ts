declare const SERVICE_TOKEN_BRAND: unique symbol;

/**
 * @alpha
 * Typed symbol identity for service registry entries.
 */
export type ServiceTokenType<TService> = symbol & {
  readonly [SERVICE_TOKEN_BRAND]: TService;
};

/**
 * @alpha
 * Typed service registry contract shared by runtime and modules.
 */
export interface IServiceRegistry {
  register<TService>(
    token: ServiceTokenType<TService>,
    service: NoInfer<TService>,
  ): void;
  override<TService>(
    token: ServiceTokenType<TService>,
    service: NoInfer<TService>,
  ): void;
  resolve<TService>(token: ServiceTokenType<TService>): TService | undefined;
  resolveRequired<TService>(token: ServiceTokenType<TService>): TService;
  has<TService>(token: ServiceTokenType<TService>): boolean;
  unregister<TService>(token: ServiceTokenType<TService>): void;
}
