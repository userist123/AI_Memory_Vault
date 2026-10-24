// Proiectul nou urmează limba aplicației de la creare (runda 8): numele camerelor și numele implicit al proiectului.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import { DEFAULT_PROJECT_NAME, newProjectRequest, projectLang, TEMPLATES } from '../core/templates';
import { newSnapshot } from '../core/project';
import { t } from '../lib/i18n';
import catalogSeed from '../data/catalog.v1.json';
import type { Catalog } from '../core/types';
beforeAll(() => resetDbForTests());
const A = '33333333-3333-4333-8333-333333333333';
const RO = ['Hol', 'Bucătărie', 'Living', 'Dormitor', 'Baie'], EN = ['Hall', 'Kitchen', 'Living room', 'Bedroom', 'Bathroom'];

test('limba proiectului: cea cerută de client, altfel prima limbă din Accept-Language, altfel româna', () => {
  assert.equal(projectLang('ro', 'en-US,en;q=0.9'), 'ro', 'clientul trimite limba interfeței: ea câștigă');
  assert.equal(projectLang('en', 'ro-RO'), 'en');
  assert.equal(projectLang(undefined, 'en-US,en;q=0.9,ro;q=0.8'), 'en');
  assert.equal(projectLang(undefined, 'ro-RO,ro;q=0.9,en;q=0.8'), 'ro');
  assert.equal(projectLang('fr', null), 'ro', 'limbă necunoscută și fără antet: româna, limba de rezervă');
  assert.equal(projectLang(undefined, 'de-DE'), 'ro');
});

test('numele implicit al proiectului e același cu textul din interfață pentru fiecare limbă', () => {
  assert.deepEqual(DEFAULT_PROJECT_NAME, { ro: 'Casa mea', en: 'My home' });
  assert.equal(t('ro', 'home.defaultName'), DEFAULT_PROJECT_NAME.ro); assert.equal(t('en', 'home.defaultName'), DEFAULT_PROJECT_NAME.en);
});

test('toate șabloanele pornesc cu nume românești și primesc nume englezești doar în engleză', () => {
  const cat = catalogSeed as unknown as Catalog;
  for (const tpl of TEMPLATES){
    const ro = newSnapshot(cat, 'x', tpl.id, 'ro').floor.rooms, en = newSnapshot(cat, 'x', tpl.id, 'en').floor.rooms;
    for (const r of ro) assert.doesNotMatch(r.name, /Kitchen|Hall|Living room|Bedroom|Bathroom/, `${tpl.id}: ${r.name}`);
    assert.equal(en.length, ro.length);
  }
});

test('cererea „proiect nou” din panou: fără nume, numele implicit în limba interfeței; un nume dat rămâne', () => {
  assert.deepEqual(newProjectRequest('  ', 'demo', 'ro'), { name: 'Casa mea', template: 'demo', lang: 'ro' });
  assert.deepEqual(newProjectRequest('', 'demo', 'en'), { name: 'My home', template: 'demo', lang: 'en' });
  assert.deepEqual(newProjectRequest(' Apartament Popescu ', 'studio', 'ro'), { name: 'Apartament Popescu', template: 'studio', lang: 'ro' });
});

test('POST /api/projects (repo.createProject): proiectul demo în română are camere și nume românești', async () => {
  const send = async (name: string, lang?: 'ro' | 'en') => { const b = newProjectRequest(name, 'demo', lang ?? 'ro'); return repo.getProject(A, await repo.createProject(A, b.name, b.template, lang ? b.lang : undefined)); };
  const ro = await send('', 'ro');
  assert.equal(ro.name, 'Casa mea'); assert.equal(ro.draft.name, 'Casa mea');
  assert.deepEqual(ro.draft.floor.rooms.map(r => r.name), RO);
  const en = await send('   ', 'en');
  assert.equal(en.name, 'My home'); assert.deepEqual(en.draft.floor.rooms.map(r => r.name), EN);
  const none = await send('Fără limbă');
  assert.deepEqual(none.draft.floor.rooms.map(r => r.name), RO, 'fără limbă: româna');
  await assert.rejects(repo.createProject(A, '  ', 'demo', 'ro'), (e: any) => e.status === 400, 'serverul refuză în continuare numele gol');
});
