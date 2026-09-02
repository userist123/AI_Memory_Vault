import type { IPlatformModuleManifest } from '@prosto/platform-sdk';
import type { IModuleEnvelope } from '../loader/index.js';
import type { IDependencyGraph, IGraphNode } from './interfaces/index.js';

/**
 * @alpha
 * Implementation of IDependencyGraph for module dependency management.
 */
export class DependencyGraph implements IDependencyGraph {
  private readonly _nodes = new Map<
    IPlatformModuleManifest['id'],
    IGraphNode
  >();

  constructor(modules: readonly IModuleEnvelope[]) {
    const sorted = [...modules].sort((left, right) =>
      left.manifest.id.localeCompare(right.manifest.id),
    );

    for (const module of sorted) {
      this.addModule(module);
    }
  }

  /**
   * Returns all modules in the graph.
   */
  get modules(): readonly IModuleEnvelope[] {
    return [...this._nodes.values()].map((node) => node.moduleEnvelope);
  }

  /**
   * Returns the number of modules in the graph.
   */
  get size(): number {
    return this._nodes.size;
  }

  /**
   * Creates a DependencyGraph from a collection of modules.
   * Static factory method for backward compatibility.
   */
  static create(modules: readonly IModuleEnvelope[]): DependencyGraph {
    return new DependencyGraph(modules);
  }

  /**
   * Adds a module to the graph with its dependencies.
   * If a module already exists, it will be updated.
   */
  addModule(moduleEnvelope: IModuleEnvelope): void {
    const dependencyIds = moduleEnvelope.manifest.dependencies
      .filter((dependency) => !dependency.optional)
      .map((dependency) => dependency.id)
      .sort((left, right) => left.localeCompare(right));

    this._nodes.set(moduleEnvelope.manifest.id, {
      moduleEnvelope,
      dependencyIds,
    });
  }

  /**
   * Removes a module from the graph by its ID.
   */
  removeModule(moduleId: string): void {
    this._nodes.delete(moduleId);
  }

  /**
   * Returns the dependencies of a module (modules it depends on).
   */
  getDependencies(moduleId: string): readonly string[] {
    const node = this._nodes.get(moduleId);
    return node ? node.dependencyIds : [];
  }

  /**
   * Returns the dependents of a module (modules that depend on it).
   */
  getDependents(moduleId: string): readonly string[] {
    const dependents: string[] = [];

    for (const [id, node] of Array.from(this._nodes)) {
      if (node.dependencyIds.includes(moduleId)) {
        dependents.push(id);
      }
    }

    return dependents.sort((left, right) => left.localeCompare(right));
  }

  /**
   * Checks if a module exists in the graph.
   */
  hasModule(moduleId: string): boolean {
    return this._nodes.has(moduleId);
  }

  /**
   * Gets a module by its ID.
   */
  getModule(moduleId: string): IModuleEnvelope | undefined {
    return this._nodes.get(moduleId)?.moduleEnvelope;
  }

  /**
   * Gets all module IDs in the graph.
   */
  getModuleIds(): readonly string[] {
    return [...this._nodes.keys()].sort((left, right) =>
      left.localeCompare(right),
    );
  }

  /**
   * Clears all modules from the graph.
   */
  clear(): void {
    this._nodes.clear();
  }
}
