import type { IConfigurationProvider } from '../interfaces/index.js';

export class InMemoryConfigurationProvider implements IConfigurationProvider {
  constructor(private readonly _config: Record<string, unknown>) {}

  load(): Record<string, unknown> {
    return structuredClone(this._config);
  }
}
