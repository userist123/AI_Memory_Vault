import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import { newSnapshot } from '../core/project';
import type { Catalog, MaterialsCatalog, Snapshot } from '../core/types';
import { buildTraceabilityGraph } from '../core/traceability';

const cat = catalogJson as unknown as Catalog;
const mc = materialsJson as unknown as MaterialsCatalog;
const demo = (): Snapshot => newSnapshot(cat, 'Demo Project', 'demo');

test('traceability graph builds requirements from project brief', () => {
  const s = demo();
  s.brief = {
    propertyType: 'apartament',
    occupants: 3,
    children: true,
    pets: false,
    accessibility: false,
    style: 'scandinav',
    colors: 'bej, alb',
    priorities: ['birou', 'depozitare'],
    budget: 50000,
    suppliers: ['IKEA', 'Dedeman'],
    avoid: '',
    notes: 'Lucru de acasa',
  };

  const graph = buildTraceabilityGraph(s, cat, mc);
  assert.ok(graph.requirements.length >= 4);
  assert.equal(typeof graph.summary.total, 'number');
  assert.ok(graph.summary.total >= 4);

  // Requirement for 'birou' should be satisfied because demo snapshot has a desk
  const deskReq = graph.requirements.find(r => r.id === 'req-wfh');
  assert.ok(deskReq);
  assert.equal(deskReq.status, 'satisfied');
  assert.ok(deskReq.matchedPlacementIds.length > 0);

  // Requirement for 'depozitare' should be satisfied or partial because demo has wardrobes
  const storageReq = graph.requirements.find(r => r.id === 'req-storage');
  assert.ok(storageReq);
  assert.ok(['satisfied', 'partial'].includes(storageReq.status));
  assert.ok(storageReq.matchedPlacementIds.length > 0);

  // Budget requirement
  const budgetReq = graph.requirements.find(r => r.id === 'req-budget');
  assert.ok(budgetReq);
  assert.ok(['satisfied', 'partial', 'conflict'].includes(budgetReq.status));
});

test('traceability graph detects unresolved requirement when feature is missing', () => {
  const s = demo();
  // Remove all desks from placements
  s.placements = s.placements.filter(p => p.group !== 'birou' && p.group !== 'scaunBirou');
  s.brief = {
    propertyType: 'apartament',
    occupants: 2,
    children: false,
    pets: false,
    accessibility: false,
    style: 'modern',
    colors: '',
    priorities: ['birou'],
    budget: null,
    suppliers: ['IKEA'],
    avoid: '',
    notes: '',
  };

  const graph = buildTraceabilityGraph(s, cat, mc);
  const deskReq = graph.requirements.find(r => r.id === 'req-wfh');
  assert.ok(deskReq);
  assert.equal(deskReq.status, 'unresolved');
  assert.equal(deskReq.matchedPlacementIds.length, 0);
  assert.ok(deskReq.explanation.ro.includes('Lipsește') || deskReq.explanation.ro.includes('Niciun birou'));
});
