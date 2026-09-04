import type { IPlatformRuntime } from './platform-runtime.interface.js';
import type { IRuntimeBuilderOptions } from './runtime-builder-options.interface.js';

/**
 * @alpha
 * Runtime builder contract.
 */
export interface IRuntimeBuilder {
  build(options: IRuntimeBuilderOptions): IPlatformRuntime;
}
