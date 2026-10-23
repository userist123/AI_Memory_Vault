import { describe, expect, test } from 'vitest';
import { parseImportedSnapshot, ProjectImportError } from '../core/project-import';

const snapshot = {
  name: 'Old name',
  floor: { id: 'f1', name: 'Parter', ceilingHeight: 2.7, rooms: [], walls: [] },
  placements: [],
};

describe('existing project import', () => {
  test('imports a Casa3D project export and applies the chosen project name', () => {
    const result = parseImportedSnapshot(JSON.stringify({
      format: 'casa3d-project-export', version: 1, draft: snapshot,
    }), 'Casa renovare');
    expect(result.name).toBe('Casa renovare');
    expect(result.floor.rooms).toEqual([]);
  });

  test('accepts a standalone snapshot', () => {
    expect(parseImportedSnapshot(JSON.stringify(snapshot), 'Import').name).toBe('Import');
  });

  test('rejects invalid JSON and unrelated JSON', () => {
    expect(() => parseImportedSnapshot('{', 'Import')).toThrow(ProjectImportError);
    expect(() => parseImportedSnapshot(JSON.stringify({ hello: 'world' }), 'Import')).toThrow(/Nu am găsit geometria/);
  });

  test('rejects oversized files and missing names', () => {
    expect(() => parseImportedSnapshot(' '.repeat(3_500_001), 'Import')).toThrow(/depășește/);
    expect(() => parseImportedSnapshot(JSON.stringify(snapshot), '   ')).toThrow(/Introdu un nume/);
  });
});
