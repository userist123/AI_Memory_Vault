// Avertismentele validatorului aplicației (de ex. spațiul de circulație în fața piesei) trec prin aceeași
// poartă de confirmare ca ale Geometry Engine. Validatorul e înlocuit cu unul care avertizează mereu în dormitor.
import { test, beforeAll, vi } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
vi.mock('../core/validate', async importOriginal => {
  const real = await importOriginal<typeof import('../core/validate')>();
  return { ...real, validatePlacement: (s: any, c: any, p: any) => [...real.validatePlacement(s, c, p),
    ...(p.roomId === 'dormitor' ? [{ code: 'CLEARANCE', severity: 'WARNING', message: 'Spațiu de circulație redus.' }] : [])] };
});
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import * as design from '../lib/design';
beforeAll(() => resetDbForTests());
const A = '77777777-7777-4777-8777-777777777777';

test('avertismentele aplicației apar în variantă și cer confirmare la aplicare', async () => {
  const id = await repo.createProject(A, 'Avertismente', 'demo');
  const r = await design.generateDesign(A, id, { roomId: 'dormitor', wants: ['pat'], replace: true });
  assert.ok(r.alternatives[0]!.issues.some(i => i.code === 'APP_CLEARANCE' && i.severity === 'WARNING'));
  await assert.rejects(design.decideDesign(A, id, r.id, 0, 'apply', false), (e: any) => e.status === 409 && /confirm/.test(e.message));
  assert.equal((await repo.getProject(A, id)).currentRevision, 0);
  const out = await design.decideDesign(A, id, r.id, 0, 'apply', true);
  assert.equal(out.revision, 1); assert.ok(out.issues.some(i => i.code === 'APP_CLEARANCE'));
});
