import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import type { Catalog, MaterialsCatalog, Snapshot } from '../core/types';
import { evaluateProjectHealth } from '../core/project-health';

const cat = catalogJson as unknown as Catalog;
const mc = materialsJson as unknown as MaterialsCatalog;
const demo = (): Snapshot => newSnapshot(cat, 'Demo Project', 'demo');

test('evaluateProjectHealth evaluates clean demo project with high health score', () => {
  const s = demo();
  const health = evaluateProjectHealth(s, cat, mc);

  assert.ok(health.score >= 70 && health.score <= 100);
  assert.equal(health.facts.errorsCount, 0);
  assert.ok(Array.isArray(health.actionsQueue));
});

test('evaluateProjectHealth catches geometric errors and flags blocker actions', () => {
  const s = demo();
  // Corrupt placement to be outside room
  s.placements[0]!.x = -50; // out of room
  const health = evaluateProjectHealth(s, cat, mc);

  assert.ok(health.facts.errorsCount > 0);
  assert.ok(health.score < 80);
  const blocker = health.actionsQueue.find(a => a.severity === 'BLOCKER');
  assert.ok(blocker);
  assert.equal(blocker.targetKind, 'placement');
});
