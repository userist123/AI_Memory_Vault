import { describe, expect, it } from 'vitest';
import {
  CycleDetector,
  DependencyGraph,
  type IModuleEnvelope,
} from '@/modularity/index.js';

describe('DependencyGraph', () => {
  it('creates graph from modules', () => {
    const modules = [
      { manifest: { id: 'a', dependencies: [] } },
      {
        manifest: {
          id: 'b',
          dependencies: [{ id: 'a', version: '^1.0.0', optional: false }],
        },
      },
    ] as unknown as readonly IModuleEnvelope[];

    const graph = new DependencyGraph(modules);

    expect(graph.size).toBe(2);
    expect(graph.modules).toHaveLength(2);
    expect(graph.getModuleIds()).toEqual(['a', 'b']);
  });

  it('returns dependencies for a module', () => {
    const modules = [
      { manifest: { id: 'a', dependencies: [] } },
      {
        manifest: {
          id: 'b',
          dependencies: [{ id: 'a', version: '^1.0.0', optional: false }],
        },
      },
    ] as unknown as readonly IModuleEnvelope[];

    const graph = new DependencyGraph(modules);

    expect(graph.getDependencies('b')).toEqual(['a']);
    expect(graph.getDependencies('a')).toEqual([]);
  });

  it('returns dependents for a module', () => {
    const modules = [
      { manifest: { id: 'a', dependencies: [] } },
      {
        manifest: {
          id: 'b',
          dependencies: [{ id: 'a', version: '^1.0.0', optional: false }],
        },
      },
      {
        manifest: {
          id: 'c',
          dependencies: [{ id: 'a', version: '^1.0.0', optional: false }],
        },
      },
    ] as unknown as readonly IModuleEnvelope[];

    const graph = new DependencyGraph(modules);

    expect(graph.getDependents('a')).toEqual(['b', 'c']);
    expect(graph.getDependents('b')).toEqual([]);
    expect(graph.getDependents('c')).toEqual([]);
  });

  it('checks if module exists', () => {
    const modules = [
      { manifest: { id: 'a', dependencies: [] } },
    ] as unknown as readonly IModuleEnvelope[];

    const graph = new DependencyGraph(modules);

    expect(graph.hasModule('a')).toBe(true);
    expect(graph.hasModule('b')).toBe(false);
  });

  it('adds and removes modules', () => {
    const modules = [
      { manifest: { id: 'a', dependencies: [] } },
    ] as unknown as readonly IModuleEnvelope[];

    const graph = new DependencyGraph(modules);

    expect(graph.size).toBe(1);

    graph.addModule({
      manifest: { id: 'b', dependencies: [] },
    } as unknown as IModuleEnvelope);
    expect(graph.size).toBe(2);

    graph.removeModule('a');
    expect(graph.size).toBe(1);
    expect(graph.hasModule('a')).toBe(false);
  });

  it('creates graph using static factory method', () => {
    const modules = [
      { manifest: { id: 'a', dependencies: [] } },
    ] as unknown as readonly IModuleEnvelope[];

    const graph = DependencyGraph.create(modules);

    expect(graph.size).toBe(1);
    expect(graph.hasModule('a')).toBe(true);
  });
});

describe('CycleDetector', () => {
  it('detects no cycle in acyclic graph', () => {
    const modules = [
      { manifest: { id: 'a', dependencies: [] } },
      {
        manifest: {
          id: 'b',
          dependencies: [{ id: 'a', version: '^1.0.0', optional: false }],
        },
      },
    ] as unknown as readonly IModuleEnvelope[];

    const graph = new DependencyGraph(modules);
    const detector = new CycleDetector();
    const result = detector.detect(graph);

    expect(result.hasCycle).toBe(false);
    expect(result.cyclicModuleIds).toEqual([]);
  });

  it('detects cycle in dependency graph', () => {
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
    const detector = new CycleDetector();
    const result = detector.detect(graph);

    expect(result.hasCycle).toBe(true);
    expect(result.cyclicModuleIds).toEqual(['a', 'b']);
  });
});
