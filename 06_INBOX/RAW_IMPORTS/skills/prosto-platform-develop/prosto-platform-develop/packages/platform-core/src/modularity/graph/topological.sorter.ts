import type {
  IDependencyGraph,
  ITopologicalSorter,
  ITopologicalSortResult,
} from './interfaces/index.js';
import { DependencyCycleError } from './dependency-graph.errors.js';
import type { IModuleEnvelope } from '@/modularity/index.js';

/**
 * @alpha
 * Topological sorter implementation using Kahn's algorithm.
 * Provides deterministic ordering of modules based on their dependencies.
 */
export class TopologicalSorter implements ITopologicalSorter {
  /**
   * Creates a new TopologicalSorter instance.
   */
  static create(): TopologicalSorter {
    return new TopologicalSorter();
  }

  /**
   * Performs topological sort on the dependency graph (Kahn's algorithm).
   * Returns ordered modules and any missing dependencies.
   *
   * @throws {DependencyCycleError} if a cycle is detected in the graph
   */
  sort(graph: IDependencyGraph): ITopologicalSortResult {
    const inDegree = new Map<string, number>();
    const outgoing = new Map<string, string[]>();
    const missingDependencies = new Map<string, string[]>();
    const moduleIds = graph.getModuleIds();

    // Initialize in-degree and outgoing maps for all modules
    for (const moduleId of moduleIds) {
      inDegree.set(moduleId, 0);
      outgoing.set(moduleId, []);
    }

    // Build the adjacency list and calculate in-degrees
    for (const moduleId of moduleIds) {
      const dependencyIds = graph.getDependencies(moduleId);

      for (const dependencyId of dependencyIds) {
        // Track missing dependencies
        if (!graph.hasModule(dependencyId)) {
          const missing = missingDependencies.get(moduleId) ?? [];

          missing.push(dependencyId);
          missingDependencies.set(
            moduleId,
            [...missing].sort((left, right) => left.localeCompare(right)),
          );

          continue;
        }

        // Increment in-degree for the dependent module
        const currentDegree = inDegree.get(moduleId) ?? 0;
        inDegree.set(moduleId, currentDegree + 1);

        // Add to outgoing edges
        const targets = outgoing.get(dependencyId) ?? [];
        targets.push(moduleId);
        targets.sort((left, right) => left.localeCompare(right));
        outgoing.set(dependencyId, targets);
      }
    }

    // Kahn's algorithm: start with nodes that have no incoming edges
    const queue = Array.from(inDegree.entries())
      .filter(([, degree]) => degree === 0)
      .map(([moduleId]) => moduleId)
      .sort((left, right) => left.localeCompare(right));

    const orderedIds: string[] = [];

    while (queue.length > 0) {
      const current = queue.shift();

      if (!current) {
        continue;
      }

      orderedIds.push(current);

      // Process all modules that depend on the current module
      for (const target of outgoing.get(current) ?? []) {
        const nextDegree = (inDegree.get(target) ?? 0) - 1;

        inDegree.set(target, nextDegree);

        if (nextDegree === 0) {
          queue.push(target);
          queue.sort((left, right) => left.localeCompare(right));
        }
      }
    }

    // Check for cycles: if not all modules are in the ordered list, there's a cycle
    if (orderedIds.length !== graph.size) {
      const cyclicModuleIds = moduleIds
        .filter((moduleId) => !orderedIds.includes(moduleId))
        .sort((left, right) => left.localeCompare(right));

      throw new DependencyCycleError(cyclicModuleIds);
    }

    // Build the ordered modules list
    const orderedModules: IModuleEnvelope[] = [];

    for (const moduleId of orderedIds) {
      const moduleEnvelope = graph.getModule(moduleId);

      if (!moduleEnvelope) {
        throw new Error(
          `Resolved module "${moduleId}" is missing from dependency graph.`,
        );
      }

      orderedModules.push(moduleEnvelope);
    }

    return {
      orderedModules,
      missingDependencies,
    };
  }
}
