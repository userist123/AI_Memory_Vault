// Proforma emisă (înghețată): ce a primit clientul nu se mai schimbă tăcut. Testele urmează modelul de amenințări:
// doar proprietarul, imutabilă (schimbările din proiect / profil / prețuri nu o ating), număr unic (inclusiv emiteri simultane),
// numărul următor, CSV și export din copia înghețată, ștergerea proiectului o ia cu el, linkul clientului nu o citește.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests, getDb } from '../lib/db';
import * as repo from '../lib/repo';
import { saveProfile } from '../lib/studio-profile';
import { saveCommercial, getProforma, issueProforma, listIssuedProformas, getIssuedProforma, getProformaCsv, getCommercialData } from '../lib/commercial';
import { exportProject } from '../lib/project-export';
beforeAll(() => resetDbForTests());
const A = 'owner-issued-a', B = 'owner-issued-b';

test('emiterea îngheață documentul; numărul nu se refolosește; doar proprietarul; CSV și export din copia emisă', async () => {
  const id = await repo.createProject(A, 'Emitere', 'demo');
  await repo.saveMeta(A, id, { status: 'offer', client: { name: 'Client Unu', address: 'Strada 1' }, fee: { kind: 'fixed', amount: 4000 } });
  await saveProfile(A, { name: 'Studio Unu', cui: '2816464' });
  await saveCommercial(A, id, { proforma: { series: 'PF', number: 7 } });
  const before = await getProforma(A, id, '2026-11-17');
  const r = await issueProforma(A, id, '2026-11-17'); assert.deepEqual([r.series, r.number], ['PF', 7]);
  // numărul următor și data golită
  const cd = await getCommercialData(A, id); assert.equal(cd.proforma.number, 8); assert.equal(cd.proforma.issueDate, undefined);
  // schimbăm tot ce intră în document: planul (o piesă mai puțin), clientul, studioul, onorariul
  const p = await repo.getProject(A, id), d = { ...p.draft, placements: p.draft.placements.slice(1) };
  await repo.saveDraft(A, id, d, p.draftRev);
  await repo.saveMeta(A, id, { status: 'offer', client: { name: 'Alt Client' }, fee: { kind: 'fixed', amount: 9999 } }); await saveProfile(A, { name: 'Alt Studio' });
  const frozen = await getIssuedProforma(A, id, 'PF', 7), now = await getProforma(A, id, '2026-11-18');
  assert.equal(frozen.client?.name, 'Client Unu'); assert.equal(frozen.seller.name, 'Studio Unu'); assert.equal(frozen.proforma.gross, before.proforma.gross);
  assert.deepEqual(frozen.proforma.groups, before.proforma.groups, 'liniile, inclusiv sursa și data prețurilor, neschimbate');
  assert.equal(frozen.frozen, true); assert.ok(frozen.issuedAt);
  assert.notEqual(now.proforma.gross, frozen.proforma.gross, 'ciorna se recalculează'); assert.equal(now.client?.name, 'Alt Client'); assert.equal(now.number, 8);
  assert.ok(!JSON.stringify(frozen).includes('"chain"'), 'bara internă a designerului nu intră în documentul emis');
  // același număr încă o dată → 409, prima rămâne neatinsă; emiteri simultane: una singură reușește
  await saveCommercial(A, id, { proforma: { series: 'PF', number: 7 } });
  await assert.rejects(issueProforma(A, id), (e: any) => e.status === 409 && /deja emisă/.test(e.message));
  assert.equal((await getIssuedProforma(A, id, 'PF', 7)).client?.name, 'Client Unu');
  await saveCommercial(A, id, { proforma: { series: 'PF', number: 20 } });
  const both = await Promise.allSettled([issueProforma(A, id), issueProforma(A, id)]);
  assert.equal(both.filter(x => x.status === 'fulfilled').length, 1); assert.equal((both.find(x => x.status === 'rejected') as PromiseRejectedResult).reason.status, 409);
  // lista, CSV-ul emis, alt proprietar
  const list = await listIssuedProformas(A, id); assert.deepEqual(list.map(x => `${x.series}${x.number}`).sort(), ['PF20', 'PF7']);
  assert.equal(list.find(x => x.number === 7)!.gross, frozen.proforma.gross);
  const csv = await getProformaCsv(A, id, { series: 'PF', number: 7 }); assert.equal(csv.filename, 'proforma-PF-7.csv'); assert.ok(csv.data.startsWith('﻿cod;'));
  for (const f of [() => issueProforma(B, id), () => listIssuedProformas(B, id), () => getIssuedProforma(B, id, 'PF', 7), () => getProformaCsv(B, id, { series: 'PF', number: 7 })])
    await assert.rejects(f(), (e: any) => e.status === 404);
  await assert.rejects(getIssuedProforma(A, id, 'PF', 999), (e: any) => e.status === 404);
  // exportul proiectului conține documentul înghețat
  const zip = (await exportProject(A, id)).data.toString('latin1'); assert.ok(zip.includes('proforme-emise/proforma-PF-7.json'));
  // ștergerea proiectului o ia cu el (cascadă)
  const { q } = await getDb(); await q('delete from projects where id=$1', [id]);
  assert.equal((await q('select count(*)::int as n from issued_proformas where project_id=$1', [id])).rows[0].n, 0);
});

test('rutele publice ale clientului nu citesc proformele emise; nicio rută nu le modifică sau șterge', async () => {
  const root = path.join(import.meta.dirname, '..'), files: string[] = []; const walk = (d: string) => { for (const f of readdirSync(d)){ const x = path.join(d, f); if (statSync(x).isDirectory()) walk(x); else if (/\.tsx?$/.test(f)) files.push(x); } };
  walk(path.join(root, 'app', 'api')); walk(path.join(root, 'lib'));
  for (const f of files){ const src = readFileSync(f, 'utf8');
    if (/[\\/]share[\\/]/.test(f) || /^share/.test(path.basename(f))) assert.ok(!/issued_proformas|getIssuedProforma|listIssuedProformas/.test(src), `${f} nu citește proformele emise`);
    assert.ok(!/(update|delete\s+from)\s+issued_proformas/i.test(src), `${f} nu modifică proformele emise`); }
});

test('poarta: rutele noi nu sunt publice și nici în demo nu se emite; numărul următor păstrează celelalte setări și golește scadența', async () => {
  const { gate } = await import('../lib/public-gate');
  for (const [path, method] of [['/api/projects/x/commercial/proforma/issue', 'POST'], ['/api/projects/x/commercial/proformas', 'GET'], ['/api/projects/x/commercial/proforma', 'GET'], ['/api/projects/x/commercial/proforma.csv', 'GET']] as const)
    assert.notEqual(gate({ local: false, path, method, session: false }), 'allow', path);
  assert.equal(gate({ local: false, path: '/api/projects/x/commercial/proforma/issue', method: 'POST', session: false, demo: true }), 'notfound', 'demo: fără emitere');
  assert.equal(gate({ local: false, path: '/api/projects/x/commercial/proformas', method: 'GET', session: false, demo: true }), 'allow', 'demo: lista se poate citi');
  const id = await repo.createProject(A, 'Setări', 'demo');
  await saveCommercial(A, id, { defaults: { discountPct: 15, markupPct: 25 }, milestones: [{ id: 'av', label: 'Avans', pct: 100 }], proforma: { series: 'SR', number: 5, issueDate: '2026-11-01', dueDate: '2026-11-10' } });
  const r = await issueProforma(A, id); assert.equal(r.nextSaved, true);
  const cd = await getCommercialData(A, id); assert.equal(cd.proforma.number, 6); assert.equal(cd.proforma.issueDate, undefined); assert.equal(cd.proforma.dueDate, undefined);
  assert.equal(cd.defaults.discountPct, 15); assert.equal(cd.milestones.length, 1, 'celelalte setări rămân');
  assert.equal((await getIssuedProforma(A, id, 'SR', 5)).issueDate, '2026-11-01', 'documentul păstrează data lui de emitere');
  // numărul maxim: emiterea reușește, numărul următor nu se poate salva și răspunsul o spune
  await saveCommercial(A, id, { proforma: { series: 'MX', number: 1e9 } }); const m = await issueProforma(A, id); assert.equal(m.nextSaved, false);
});
