// Demo public: fără login, dar administrarea, parola și randarea Blender rămân închise din afară.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { gate } from '../lib/public-gate';
const g = (path: string, method = 'GET') => gate({ local: false, path, method, session: false, demo: true });
test('demo: editorul și API-ul proiectelor sunt deschise, restul rămâne închis', () => {
  assert.equal(g('/'), 'allow'); assert.equal(g('/p/abc'), 'allow'); assert.equal(g('/api/projects', 'POST'), 'allow');
  assert.equal(g('/api/projects/x/renders'), 'allow');
  assert.equal(g('/api/projects/x/renders', 'POST'), 'notfound'); assert.equal(g('/api/projects/x/renders/1/a.png'), 'notfound');
  assert.equal(g('/api/admin/stats'), 'notfound'); assert.equal(g('/api/auth/password', 'POST'), 'notfound'); assert.equal(g('/login'), 'notfound');
  assert.equal(gate({ local: false, path: '/p/abc', method: 'GET', session: false }), 'login', 'fără demo, regula veche rămâne');
});
