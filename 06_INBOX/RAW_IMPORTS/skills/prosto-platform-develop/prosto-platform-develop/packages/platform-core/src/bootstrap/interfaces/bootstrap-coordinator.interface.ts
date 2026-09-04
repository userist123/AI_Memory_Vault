import type { IBootstrapContext } from './bootstrap-context.interface.js';
import type { IBootstrapInput } from './bootstrap-input.interface.js';

/**
 * @alpha
 * Bootstrap coordinator contract.
 */
export interface IBootstrapCoordinator {
  coordinate(input: IBootstrapInput): Promise<IBootstrapContext>;
}
