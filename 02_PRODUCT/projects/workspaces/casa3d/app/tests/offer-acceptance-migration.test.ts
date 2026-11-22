// Actualizarea schemei pe o bază cu acceptări dinaintea coloanei name_key (inclusiv dubluri în vigoare pe aceeași persoană și
// amprentă) nu trebuie să oprească pornirea: cheia se completează din nume, dublurile mai noi trec la retrase.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
import { resetDbForTests, getDb } from '../lib/db';
import { SCHEMA } from '../lib/schema';
import * as repo from '../lib/repo';

beforeAll(() => resetDbForTests());
test('migrarea completează name_key și rezolvă dublurile înainte de indexul unic', async () => {
  const { q } = await getDb(), id = await repo.createProject('99999999-9999-4999-8999-999999999999', 'Migrare', 'blank');
  await q('drop index if exists offer_acceptances_person');
  const ins = (name: string, at: string) => q(`insert into offer_acceptances(project_id, share_token, revision_number, snapshot, hash, accepted_name, accepted_at, name_key) values($1,'t',1,'{}','h',$2,$3,'')`, [id, name, at]);
  await ins('Ana  Pop', '2026-11-01T10:00:00Z'); await ins('Ana Pop', '2026-11-02T10:00:00Z'); await ins('Ion Pop', '2026-11-03T10:00:00Z');
  for (const stmt of SCHEMA.split(';').map(s => s.trim()).filter(Boolean)) await q(stmt);
  const { rows } = await q('select accepted_name, name_key, withdrawn_at from offer_acceptances where project_id=$1 order by accepted_at', [id]);
  assert.deepEqual(rows.map((r: any) => [r.name_key, r.withdrawn_at == null]), [['ana pop', true], ['ana pop', false], ['ion pop', true]]);
  assert.equal((await q(`select count(*)::int as n from pg_indexes where indexname='offer_acceptances_person'`)).rows[0].n, 1);
});
