import type { IModuleEnvelope } from '@/modularity/index.js';

/**
 * @alpha
 * Graph node representing a module and its dependencies.
 */
export interface IGraphNode {
  readonly moduleEnvelope: IModuleEnvelope;
  readonly dependencyIds: readonly string[];
}

/**
 * @alpha
 * Dependency graph contract for module dependency management.
 */
export interface IDependencyGraph {
  readonly modules: readonly IModuleEnvelope[];
  readonly size: number;
  addModule(moduleEnvelope: IModuleEnvelope): void;
  removeModule(moduleId: string): void;
  getDependencies(moduleId: string): readonly string[];
  getDependents(moduleId: string): readonly string[];
  hasModule(moduleId: string): boolean;
  getModule(moduleId: string): IModuleEnvelope | undefined;
  getModuleIds(): readonly string[];
}
