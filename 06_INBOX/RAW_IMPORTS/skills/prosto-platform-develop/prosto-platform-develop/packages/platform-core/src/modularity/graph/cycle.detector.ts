import type {
  ICycleDetectionResult,
  ICycleDetector,
  IDependencyGraph,
} from './interfaces/index.js';

/**
 * @alpha
 * Cycle detector implementation using DFS-based cycle detection.
 * Identifies modules involved in dependency cycles.
 */
export class CycleDetector implements ICycleDetector {
  /**
   * Creates a new CycleDetector instance.
   */
  static create(): CycleDetector {
    return new CycleDetector();
  }

  /**
   * Detects cycles in the dependency graph.
   * Uses depth-first search to find all modules involved in cycles.
   *
   * @returns ICycleDetectionResult with cycle information
   */
  detect(graph: IDependencyGraph): ICycleDetectionResult {
    const visited = new Set<string>();
    const recursionStack = new Set<string>();
    const cyclicModules = new Set<string>();

    const moduleIds = graph.getModuleIds();

    for (const moduleId of moduleIds) {
      if (!visited.has(moduleId)) {
        this.detectCycleFromModule(
          graph,
          moduleId,
          visited,
          recursionStack,
          cyclicModules,
        );
      }
    }

    const cyclicModuleIds = [...cyclicModules].sort((left, right) =>
      left.localeCompare(right),
    );

    return {
      hasCycle: cyclicModuleIds.length > 0,
      cyclicModuleIds,
    };
  }

  /**
   * Helper method for DFS traversal to detect cycles.
   */
  private detectCycleFromModule(
    graph: IDependencyGraph,
    moduleId: string,
    visited: Set<string>,
    recursionStack: Set<string>,
    cyclicModules: Set<string>,
  ): boolean {
    visited.add(moduleId);
    recursionStack.add(moduleId);

    const dependencies = graph.getDependencies(moduleId);

    for (const dependencyId of dependencies) {
      // Skip missing dependencies
      if (!graph.hasModule(dependencyId)) {
        continue;
      }

      if (!visited.has(dependencyId)) {
        if (
          this.detectCycleFromModule(
            graph,
            dependencyId,
            visited,
            recursionStack,
            cyclicModules,
          )
        ) {
          // If cycle found in subtree, mark current module as cyclic
          cyclicModules.add(moduleId);

          return true;
        }
      } else if (recursionStack.has(dependencyId)) {
        // Found a back edge - cycle detected
        cyclicModules.add(moduleId);
        cyclicModules.add(dependencyId);

        return true;
      }
    }

    recursionStack.delete(moduleId);

    return false;
  }
}
