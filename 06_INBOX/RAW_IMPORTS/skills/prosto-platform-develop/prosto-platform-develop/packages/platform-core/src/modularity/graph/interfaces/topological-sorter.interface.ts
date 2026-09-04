import type { IDependencyGraph } from './dependency-graph.interface.js';
import type { IModuleEnvelope } from '../../loader/index.js';

/**
 * @alpha
 * Result of topological sort operation.
 */
export interface ITopologicalSortResult {
  readonly orderedModules: readonly IModuleEnvelope[];
  readonly missingDependencies: ReadonlyMap<string, readonly string[]>;
}

/**
 * @alpha
 * Topological sorter contract for dependency ordering.
 */
export interface ITopologicalSorter {
  sort(graph: IDependencyGraph): ITopologicalSortResult;
}
