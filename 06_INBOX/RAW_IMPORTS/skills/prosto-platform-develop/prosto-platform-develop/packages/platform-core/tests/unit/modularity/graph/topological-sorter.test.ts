import { describe, expect, it } from 'vitest';
import {
  DependencyCycleError,
  DependencyGraph,
  type IModuleEnvelope,
  TopologicalSorter,
} from '@/modularity/index.js';

describe('TopologicalSorter', () => {
  const topologicalSorter = TopologicalSorter.create();

  it('orders modules by dependency', () => {
    const modules = [
      {
        manifest: {
          id: 'c',
          dependencies: [{ id: 'b', version: '^1.0.0', optional: false }],
        },
      },
      { manifest: { id: 'a', dependencies: [] } },
      {
        manifest: {
          id: 'b',
          dependencies: [{ id: 'a', version: '^1.0.0', optional: false }],
        },
      },
    ] as unknown as readonly IModuleEnvelope[];

    const graph = new DependencyGraph(modules);
    const result = topologicalSorter.sort(graph);

    expect(
      result.orderedModules.map((moduleItem) => moduleItem.manifest.id),
    ).toEqual(['a', 'b', 'c']);
  });

  it('tracks missing required dependencies', () => {
    const modules = [
      {
        manifest: {
          id: 'a',
          dependencies: [{ id: 'missing', version: '^1.0.0', optional: false }],
        },
      },
    ] as unknown as readonly IModuleEnvelope[];

    const graph = new DependencyGraph(modules);
    const result = topologicalSorter.sort(graph);

    expect(result.missingDependencies.get('a')).toEqual(['missing']);
    expect(
      result.orderedModules.map((moduleItem) => moduleItem.manifest.id),
    ).toEqual(['a']);
  });

  it('throws when cycle is detected', () => {
    const modules = [
      {
        manifest: {
          id: 'a',
          dependencies: [{ id: 'b', version: '^1.0.0', optional: false }],
        },
      },
      {
        manifest: {
          id: 'b',
          dependencies: [{ id: 'a', version: '^1.0.0', optional: false }],
        },
      },
    ] as unknown as readonly IModuleEnvelope[];

    const graph = new DependencyGraph(modules);
    expect(() => topologicalSorter.sort(graph)).toThrow(DependencyCycleError);
  });
});
