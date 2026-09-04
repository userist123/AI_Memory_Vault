export class DependencyCycleError extends Error {
  readonly moduleIds: readonly string[];

  constructor(moduleIds: readonly string[]) {
    super(`Dependency cycle detected for modules: ${moduleIds.join(', ')}`);
    this.name = 'DependencyCycleError';
    this.moduleIds = moduleIds;
  }
}
