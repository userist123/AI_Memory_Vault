// Linkul pentru client: clientul deschide prezentarea fără cont și lasă păreri pe camere; designerul le vede și le bifează.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { createShare, revokeShare } from '../lib/share';
import { addFeedback, listFeedbackForShare, listFeedback, setFeedbackResolved, FEEDBACK_LIMITS } from '../lib/feedback';
beforeAll(() => resetDbForTests());
const A = '55555555-5555-4555-8555-555555555555', B = '66666666-6666-4666-8666-666666666666';

async function shared(){
  const id = await repo.createProject(A, 'Pentru client', 'demo'), { number } = await repo.createRevision(A, id, 'Predat clientului');
  const { token } = await createShare(A, id, number), room = (await repo.getProject(A, id)).draft.floor.rooms[0]!.id;
  return { id, token, room };
}

test('clientul lasă păreri pe o cameră și pe tot proiectul; le vede și designerul', async () => {
  const { id, token, room } = await shared();
  const f1 = await addFeedback(token, { roomId: room, verdict: 'change', author: '  Ana  ', text: 'Aș vrea o canapea mai deschisă la culoare.' });
  assert.equal(f1.author, 'Ana'); assert.equal(f1.roomId, room);
  await addFeedback(token, { roomId: null, verdict: 'like', author: 'Ana', text: '' });
  const pub = await listFeedbackForShare(token); assert.equal(pub.length, 2); assert.deepEqual(pub.map(f => f.verdict).sort(), ['change', 'like']);
  const own = await listFeedback(A, id); assert.equal(own.length, 2); assert.ok(own.every(f => f.revisionNumber === 1 && f.resolvedAt === null));
  const r = await setFeedbackResolved(A, id, f1.id, true); assert.ok(r.resolvedAt);
  assert.ok((await listFeedbackForShare(token)).find(f => f.id === f1.id)!.resolvedAt, 'clientul vede că a fost rezolvat');
  await setFeedbackResolved(A, id, f1.id, false); assert.equal((await listFeedback(A, id)).find(f => f.id === f1.id)!.resolvedAt, null);
});

test('intrări invalide, linkuri revocate și alți proprietari sunt refuzate', async () => {
  const { id, token, room } = await shared();
  const bad = [
    { roomId: room, verdict: 'meh', author: 'Ana', text: 'x' },                                  // verdict necunoscut
    { roomId: 'nu-exista', verdict: 'like', author: 'Ana', text: '' },                            // camera nu e în revizia partajată
    { roomId: room, verdict: 'change', author: 'Ana', text: '   ' },                               // „aș schimba” fără text
    { roomId: room, verdict: 'question', author: '', text: 'De ce?' },                            // fără nume
    { roomId: room, verdict: 'question', author: 'A'.repeat(FEEDBACK_LIMITS.author + 1), text: 'x' },
    { roomId: room, verdict: 'question', author: 'Ana', text: 'x'.repeat(FEEDBACK_LIMITS.text + 1) },
    null, 'text',
  ];
  for (const b of bad) await assert.rejects(addFeedback(token, b as any), (e: any) => e.status === 400, JSON.stringify(b));
  await assert.rejects(addFeedback('x'.repeat(43), { roomId: null, verdict: 'like', author: 'Ana', text: '' }), (e: any) => e.status === 404);
  await assert.rejects(listFeedback(B, id));
  const f = await addFeedback(token, { roomId: null, verdict: 'question', author: 'Ana', text: 'Cât durează?\u0000\u0007' });
  assert.equal(f.text, 'Cât durează?', 'caracterele de control se scot');
  await assert.rejects(setFeedbackResolved(B, id, f.id, true));
  await revokeShare(A, id, token);
  await assert.rejects(addFeedback(token, { roomId: null, verdict: 'like', author: 'Ana', text: '' }), (e: any) => e.status === 404);
  await assert.rejects(listFeedbackForShare(token), (e: any) => e.status === 404);
  assert.equal((await listFeedback(A, id)).length, 1, 'părerile rămân la designer după revocare');
});

test('un link nu poate fi folosit ca să umple baza de date', async () => {
  const { token } = await shared();
  for (let i = 0; i < FEEDBACK_LIMITS.perShare; i++) await addFeedback(token, { roomId: null, verdict: 'like', author: 'Bot', text: '' });
  await assert.rejects(addFeedback(token, { roomId: null, verdict: 'like', author: 'Bot', text: '' }), (e: any) => e.status === 429);
});

test('butonul „Link pentru client”: salvează versiunea doar dacă s-a schimbat și refolosește linkul activ', async () => {
  const { clientLink } = await import('../lib/share');
  const id = await repo.createProject(A, 'Un buton', 'demo');
  const l1 = await clientLink(A, id); assert.equal(l1.revisionNumber, 1); assert.equal(l1.newRevision, true); assert.match(l1.path, /^\/share\/[\w-]{43}$/); assert.equal(l1.url, null);
  const l2 = await clientLink(A, id); assert.equal(l2.token, l1.token, 'fără schimbări: același link'); assert.equal(l2.newRevision, false);
  const p = await repo.getProject(A, id); await repo.saveDraft(A, id, { ...p.draft, name: 'Un buton (v2)' });
  const l3 = await clientLink(A, id); assert.equal(l3.revisionNumber, 2); assert.notEqual(l3.token, l1.token);
  process.env.CASA3D_PUBLIC_URL = 'https://casa.example.com/'; try { assert.equal((await clientLink(A, id)).url, `https://casa.example.com${l3.path}`); } finally { delete process.env.CASA3D_PUBLIC_URL; }
  await assert.rejects(clientLink(B, id));
});

test('limita pe link ține și la cereri simultane (fără cursă între numărare și inserare)', async () => {
  const { token, id } = await shared();
  for (let i = 0; i < FEEDBACK_LIMITS.perShare - 5; i++) await addFeedback(token, { roomId: null, verdict: 'like', author: 'Bot', text: '' });
  const rs = await Promise.allSettled(Array.from({ length: 30 }, () => addFeedback(token, { roomId: null, verdict: 'like', author: 'Bot', text: '' })));
  assert.equal(rs.filter(r => r.status === 'fulfilled').length, 5);
  assert.ok(rs.filter(r => r.status === 'rejected').every(r => (r as PromiseRejectedResult).reason.status === 429));
  assert.equal((await listFeedback(A, id)).length, FEEDBACK_LIMITS.perShare);
});

test('numele nu poate ascunde alt nume (caractere invizibile / inversare), id-uri strict UUID, adresa publică doar origine', async () => {
  const { token, id } = await shared();
  const f = await addFeedback(token, { roomId: null, verdict: 'question', author: 'An‮a​﻿', text: 'De⁦ ce?' });
  assert.equal(f.author, 'Ana'); assert.equal(f.text, 'De ce?');
  await assert.rejects(setFeedbackResolved(A, id, '-'.repeat(36), true), (e: any) => e.status === 404);
  const { publicBase } = await import('../lib/share');
  const pb = (u: string | undefined) => { if (u === undefined) delete process.env.CASA3D_PUBLIC_URL; else process.env.CASA3D_PUBLIC_URL = u; try { return publicBase(); } finally { delete process.env.CASA3D_PUBLIC_URL; } };
  assert.equal(pb('https://casa.example.com/'), 'https://casa.example.com'); assert.equal(pb('http://x.trycloudflare.com'), 'http://x.trycloudflare.com');
  for (const bad of ['https://h#', 'https://h/?a=1', 'https://u:p@h', 'https://h/cale', 'ftp://h', 'nu e url', undefined]) assert.equal(pb(bad), null, String(bad));
});

test('aprobare pe produs: doar produse din camera partajată, numele îl pune serverul', async () => {
  const { presentation, approvalItems } = await import('../core/presentation');
  const { getCatalog, getMaterials } = await import('../lib/repo');
  const id = await repo.createProject(A, 'Aprobări', 'demo'), { number } = await repo.createRevision(A, id, 'v1'), { token } = await createShare(A, id, number);
  const snap = (await repo.getProject(A, id)).draft, slide = presentation(snap, await getCatalog(), await getMaterials()).rooms.find(r => r.products.length > 0)!;
  const items = approvalItems(slide), prod = items.find(i => i.key.startsWith('p:'))!, fin = items.find(i => i.key.startsWith('f:'))!;
  const a = await addFeedback(token, { roomId: slide.id, verdict: 'like', author: 'Ana', text: '', item: { key: prod.key, label: 'ETICHETA FALSĂ' } });
  assert.equal(a.item?.key, prod.key); assert.equal(a.item?.label, prod.label, 'eticheta vine de pe server');
  const b = await addFeedback(token, { roomId: slide.id, verdict: 'change', author: 'Ana', text: 'altă culoare', item: { key: fin.key } }); assert.equal(b.item?.role, fin.role);
  for (const bad of [{ roomId: slide.id, item: { key: 'p:nu-exista' } }, { roomId: null, item: { key: prod.key } }, { roomId: slide.id, item: { key: 'x'.repeat(200) } }, { roomId: slide.id, item: 'p:1' }])
    await assert.rejects(addFeedback(token, { verdict: 'like', author: 'Ana', text: '', ...bad } as any), (e: any) => e.status === 400, JSON.stringify(bad));
  const other = presentation(snap, await getCatalog(), await getMaterials()).rooms.find(r => r.id !== slide.id && r.products.length)!;
  await assert.rejects(addFeedback(token, { roomId: other.id, verdict: 'like', author: 'Ana', text: '', item: { key: prod.key } }), (e: any) => e.status === 400, 'produsul altei camere');
  assert.equal((await listFeedback(A, id)).filter(f => f.item).length, 2);
});

import { addApprovals, APPROVE_MAX } from '../lib/feedback';
import { presentation, approvalItems } from '../core/presentation';
test('„Aprobă camera”: toate produsele camerei într-o cerere, doar produse reale din camera partajată, în plafonul linkului', async () => {
  const { id, token, room } = await shared();
  const view = (await repo.getProject(A, id)).draft, [cat, mc] = await Promise.all([repo.getCatalog(), repo.getMaterials()]);
  const keys = approvalItems(presentation(view, cat, mc).rooms.find(r => r.id === room)!).map(i => i.key); assert.ok(keys.length >= 2);
  const out = await addApprovals(token, { roomId: room, author: ' Ana ', items: keys.map(key => ({ key })) });
  assert.equal(out.length, keys.length); assert.ok(out.every(f => f.verdict === 'like' && f.author === 'Ana' && f.item && f.text === ''));
  assert.deepEqual(out.map(f => f.item!.key), keys, 'numele produselor le pune serverul, pe cheile din cameră');
  assert.equal((await listFeedbackForShare(token)).length, keys.length);
  const bad = [{ roomId: room, author: 'Ana', items: [{ key: 'f:inventat' }] }, { roomId: room, author: 'Ana', items: [] }, { roomId: null, author: 'Ana', items: [{ key: keys[0] }] },
    { roomId: 'nu-exista', author: 'Ana', items: [{ key: keys[0] }] }, { roomId: room, author: '', items: [{ key: keys[0] }] }, { roomId: room, author: 'Ana', items: Array.from({ length: APPROVE_MAX + 1 }, (_, i) => ({ key: `k${i}` })) },
    { roomId: room, author: 'Ana', items: [{ key: keys[0] }, { key: 'f:inventat' }] }, { roomId: room, author: 'Ana', items: 'nu' }];
  for (const b of bad) await assert.rejects(addApprovals(token, b), (e: any) => e.status === 400, JSON.stringify(b).slice(0, 80));
  assert.equal((await listFeedbackForShare(token)).length, keys.length, 'o cerere cu o singură cheie greșită nu scrie nimic');
  // plafonul aprobărilor pe link include fiecare rând nou: după un „aș schimba” pe fiecare produs, reaprobarea ar scrie
  // keys.length rânduri noi, peste un plafon de keys.length + 1 → 429 și nu scrie nimic
  for (const key of keys) await addFeedback(token, { roomId: room, verdict: 'change', author: 'Ana', text: 'altfel', item: { key } });
  const cap = FEEDBACK_LIMITS.approvalsPerShare, before = (await listFeedbackForShare(token)).length; FEEDBACK_LIMITS.approvalsPerShare = keys.length + 1;
  try { await assert.rejects(addApprovals(token, { roomId: room, author: 'Ana', items: keys.map(key => ({ key })) }), (e: any) => e.status === 429, 'plafonul pe link include fiecare rând'); }
  finally { FEEDBACK_LIMITS.approvalsPerShare = cap; }
  assert.equal((await listFeedbackForShare(token)).length, before, 'cererea refuzată nu scrie nimic');
  assert.equal((await addApprovals(token, { roomId: room, author: 'Ana', items: keys.map(key => ({ key })) })).length, keys.length, 'după „aș schimba” produsul se poate aproba din nou');
});

test('„Aprobă camera” repetat nu dublează rândurile, iar aprobările nu pot umple plafonul întrebărilor', async () => {
  const { id, token, room } = await shared();
  const view = (await repo.getProject(A, id)).draft, [cat, mc] = await Promise.all([repo.getCatalog(), repo.getMaterials()]);
  const keys = approvalItems(presentation(view, cat, mc).rooms.find(r => r.id === room)!).map(i => i.key);
  const first = await addApprovals(token, { roomId: room, author: 'Ana', items: keys.slice(0, 1).map(key => ({ key })) }); assert.equal(first.length, 1);
  const rest = await addApprovals(token, { roomId: room, author: 'Ana', items: keys.map(key => ({ key })) });
  assert.deepEqual(rest.map(f => f.item!.key), keys.slice(1), 'doar produsele încă neaprobate primesc rânduri');
  for (let i = 0; i < 10; i++) assert.deepEqual(await addApprovals(token, { roomId: room, author: 'Bot', items: keys.map(key => ({ key })) }), [], 'cererea repetată nu adaugă nimic');
  const single = await addFeedback(token, { roomId: room, verdict: 'like', author: 'Bot', text: '', item: { key: keys[0] } });
  assert.equal(single.id, first[0]!.id, 'aprobarea unui produs deja aprobat întoarce rândul existent');
  assert.equal((await listFeedbackForShare(token)).length, keys.length, 'un singur rând de aprobare pe produs');
  // aprobările (oricâte) nu intră în plafonul întrebărilor: clientul poate trimite încă perShare întrebări
  for (let i = 0; i < FEEDBACK_LIMITS.perShare; i++) await addFeedback(token, { roomId: null, verdict: 'question', author: 'Ana', text: `întrebarea ${i}` });
  await assert.rejects(addFeedback(token, { roomId: null, verdict: 'question', author: 'Ana', text: 'încă una' }), (e: any) => e.status === 429);
  // nici invers: plafonul întrebărilor plin nu blochează aprobarea unui produs schimbat între timp
  const cap = FEEDBACK_LIMITS.perShare; FEEDBACK_LIMITS.perShare = Number.MAX_SAFE_INTEGER;
  try { await addFeedback(token, { roomId: room, verdict: 'change', author: 'Ana', text: 'altă culoare', item: { key: keys[0] } }); } finally { FEEDBACK_LIMITS.perShare = cap; }
  assert.equal((await addApprovals(token, { roomId: room, author: 'Ana', items: [{ key: keys[0] }] })).length, 1);
});
