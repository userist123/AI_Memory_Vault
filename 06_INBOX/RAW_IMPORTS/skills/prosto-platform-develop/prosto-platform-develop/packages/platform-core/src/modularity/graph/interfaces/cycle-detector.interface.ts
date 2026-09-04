import type { IDependencyGraph } from './dependency-graph.interface.js';

/**
 * @alpha
 * Result of cycle detection operation.
 */
export interface ICycleDetectionResult {
  /**
   * Whether a cycle was detected in the graph.
   */
  readonly hasCycle: boolean;

  /**
   * Module IDs that are part of the cycle.
   * Empty if no cycle was detected.
   */
  readonly cyclicModuleIds: readonly string[];
}

/**
 * @alpha
 * Cycle detector contract for dependency graph validation.
 */
export interface ICycleDetector {
  /**
   * Detects cycles in the dependency graph.
   * Returns information about any cycles found.
   */
  detect(graph: IDependencyGraph): ICycleDetectionResult;
}
