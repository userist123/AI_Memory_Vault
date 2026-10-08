/**
 * VaultClient authentication: the REST gateway answers 401 on every /api/v1 route except /status
 * unless the request carries `Authorization: Bearer <AI_MEMORY_VAULT_API_TOKEN>`. The client must
 * send the token it is given (never one baked into the source) and report a 401 for what it is.
 */
import { describe, it, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

import { VaultClient, VaultAuthError, TOKEN_STORAGE_KEY } from '../js/vault_client.js';

const here = path.dirname(fileURLToPath(import.meta.url));

function fakeFetch({ expectedToken = 'owner-token', calls = [] } = {}) {
  const impl = async (url, options = {}) => {
    const headers = options.headers instanceof Headers ? Object.fromEntries(options.headers) : { ...(options.headers || {}) };
    const lower = Object.fromEntries(Object.entries(headers).map(([k, v]) => [k.toLowerCase(), v]));
    calls.push({ url, headers: lower, method: options.method || 'GET' });
    const isStatus = String(url).endsWith('/status');
    const authorised = lower.authorization === `Bearer ${expectedToken}`;
    if (!isStatus && !authorised) return { ok: false, status: 401, json: async () => ({ error: 'authentication required' }) };
    return { ok: true, status: 200, json: async () => ({ status: 'online', results: [], pending: [] }) };
  };
  impl.calls = calls;
  return impl;
}

function memoryStorage() {
  const map = new Map();
  return {
    getItem: (k) => (map.has(k) ? map.get(k) : null),
    setItem: (k, v) => void map.set(k, String(v)),
    removeItem: (k) => void map.delete(k),
    map,
  };
}

describe('VaultClient API token', () => {
  beforeEach(() => {
    delete globalThis.sessionStorage;
  });

  it('without a token, a protected route fails as an authentication error, not a generic one', async () => {
    const client = new VaultClient({ baseUrl: 'http://127.0.0.1:8000', fetchImpl: fakeFetch() });
    await assert.rejects(client.metrics(), (error) => {
      assert.ok(error instanceof VaultAuthError);
      assert.equal(error.status, 401);
      assert.equal(error.code, 'VAULT_AUTH_REQUIRED');
      assert.match(error.message, /token is required/);
      return true;
    });
  });

  it('sends Authorization: Bearer on every protected route, for GET and POST', async () => {
    const fetchImpl = fakeFetch();
    const client = new VaultClient({ baseUrl: 'http://127.0.0.1:8000', fetchImpl, token: 'owner-token' });
    await client.metrics();
    await client.proposals();
    await client.skills('x');
    await client.route('plan');
    await client.proposeNote({ title: 't', summary: 's' });
    const protectedCalls = fetchImpl.calls.filter((c) => !c.url.endsWith('/status'));
    assert.equal(protectedCalls.length, 5);
    for (const call of protectedCalls) assert.equal(call.headers.authorization, 'Bearer owner-token', call.url);
    // caller headers survive next to the injected one
    assert.equal(protectedCalls.at(-1).headers['content-type'], 'application/json');
  });

  it('does not send the token to /status, the one public route', async () => {
    const fetchImpl = fakeFetch();
    const client = new VaultClient({ baseUrl: 'http://127.0.0.1:8000', fetchImpl, token: 'owner-token' });
    await client.status();
    assert.equal(fetchImpl.calls[0].headers.authorization, undefined);
  });

  it('a wrong token is reported as rejected', async () => {
    const client = new VaultClient({ baseUrl: 'http://127.0.0.1:8000', fetchImpl: fakeFetch(), token: 'nope' });
    await assert.rejects(client.agents(), (error) => error instanceof VaultAuthError && /rejected/.test(error.message));
  });

  it('reads the token from a provider first, then the option, then sessionStorage', async () => {
    const storage = memoryStorage();
    globalThis.sessionStorage = storage;
    storage.setItem(TOKEN_STORAGE_KEY, 'owner-token');

    let fetchImpl = fakeFetch();
    await new VaultClient({ baseUrl: 'http://x:1', fetchImpl }).metrics();
    assert.equal(fetchImpl.calls[0].headers.authorization, 'Bearer owner-token');

    fetchImpl = fakeFetch({ expectedToken: 'from-provider' });
    await new VaultClient({ baseUrl: 'http://x:1', fetchImpl, token: 'ignored', getToken: () => 'from-provider' }).metrics();
    assert.equal(fetchImpl.calls[0].headers.authorization, 'Bearer from-provider');
  });

  it('setToken stores it for the tab only, and clears results cached while unauthenticated', async () => {
    const storage = memoryStorage();
    globalThis.sessionStorage = storage;
    const fetchImpl = fakeFetch();
    const client = new VaultClient({ baseUrl: 'http://x:1', fetchImpl });

    const before = await client.search('anything');
    assert.equal(before.source, 'offline_cache');
    assert.match(before.error, /401/);

    client.setToken('owner-token');
    assert.equal(storage.getItem(TOKEN_STORAGE_KEY), 'owner-token');
    const after = await client.search('anything');
    assert.equal(after.source, 'live');

    client.setToken('', { persist: true });
    assert.equal(storage.getItem(TOKEN_STORAGE_KEY), null);
  });

  it('survives a blocked sessionStorage', async () => {
    Object.defineProperty(globalThis, 'sessionStorage', {
      configurable: true,
      get() {
        throw new Error('SecurityError');
      },
    });
    try {
      const client = new VaultClient({ baseUrl: 'http://x:1', fetchImpl: fakeFetch() });
      assert.equal(client.getToken(), '');
      client.setToken('owner-token');
      assert.equal(client.getToken(), 'owner-token');
    } finally {
      delete globalThis.sessionStorage;
    }
  });
});

describe('no secret in the page source', () => {
  for (const file of ['../js/vault_client.js', '../js/app.js']) {
    it(`${path.basename(file)} carries no hard-coded bearer token`, () => {
      const source = readFileSync(path.join(here, file), 'utf8');
      assert.match(source, /Authorization/);
      assert.match(source, /sessionStorage/);
      // a Bearer header may only be built from a variable: no literal that looks like a credential
      for (const match of source.matchAll(/Bearer\s+[A-Za-z0-9._~+/=-]{12,}/g)) {
        assert.fail(`literal bearer credential in ${file}: ${match[0]}`);
      }
      assert.doesNotMatch(source, /AI_MEMORY_VAULT_API_TOKEN\s*[:=]\s*['"][^'"]+['"]/);
    });
  }
});
