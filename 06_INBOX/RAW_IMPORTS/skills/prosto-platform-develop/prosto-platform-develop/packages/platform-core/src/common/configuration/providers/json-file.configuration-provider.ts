import type {
  IConfigurationProvider,
  IJsonFileOptions,
} from '../interfaces/index.js';
import { loadJsonFileSync } from '../../utils/index.js';

export class JsonFileConfigurationProvider implements IConfigurationProvider {
  constructor(
    private readonly _filePath: string,
    private readonly _options: IJsonFileOptions = {},
  ) {}

  load(): Record<string, unknown> {
    const { optional = false } = this._options;

    return loadJsonFileSync(this._filePath, optional);
  }
}
