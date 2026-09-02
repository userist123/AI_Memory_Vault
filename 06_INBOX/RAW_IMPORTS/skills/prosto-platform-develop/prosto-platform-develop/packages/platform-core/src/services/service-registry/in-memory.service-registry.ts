import type { IServiceRegistry, ServiceTokenType } from '@prosto/platform-sdk';
import {
  ServiceAlreadyRegisteredError,
  ServiceNotFoundError,
} from './service-registry.errors.js';

export class InMemoryServiceRegistry implements IServiceRegistry {
  private readonly _registry = new Map<ServiceTokenType<unknown>, unknown>();

  register<TService>(
    token: ServiceTokenType<TService>,
    service: NoInfer<TService>,
  ): void {
    if (this._registry.has(token)) {
      throw new ServiceAlreadyRegisteredError(token.toString());
    }

    this._registry.set(token, service);
  }

  override<TService>(
    token: ServiceTokenType<TService>,
    service: NoInfer<TService>,
  ): void {
    if (!this._registry.has(token)) {
      throw new ServiceNotFoundError(token.toString());
    }

    this._registry.set(token, service);
  }

  resolve<TService>(token: ServiceTokenType<TService>): TService | undefined {
    return this._registry.get(token) as TService | undefined;
  }

  resolveRequired<TService>(token: ServiceTokenType<TService>): TService {
    if (!this._registry.has(token)) {
      throw new ServiceNotFoundError(token.toString());
    }

    return this._registry.get(token) as TService;
  }

  has<TService>(token: ServiceTokenType<TService>): boolean {
    return this._registry.has(token);
  }

  unregister<TService>(token: ServiceTokenType<TService>): void {
    this._registry.delete(token);
  }

  dispose(): void {
    this._registry.clear();
  }
}
