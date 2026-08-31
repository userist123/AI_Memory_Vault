/**
 * mock_fetch.js - Standalone deterministic HTTP fetch mock for AI Memory Vault REST API
 * Simulates http://127.0.0.1:8000/api/v1/* endpoints, latency, and network outages.
 */

export class MockHeaders {
  constructor(init = {}) {
    this._map = new Map();
    if (init) {
      if (Array.isArray(init)) {
        for (const [k, v] of init) this.set(k, v);
      } else if (typeof init.forEach === 'function') {
        init.forEach((v, k) => this.set(k, v));
      } else {
        for (const [k, v] of Object.entries(init)) this.set(k, v);
      }
    }
  }

  get(name) {
    return this._map.get(name.toLowerCase()) || null;
  }

  set(name, value) {
    this._map.set(name.toLowerCase(), String(value));
  }

  has(name) {
    return this._map.has(name.toLowerCase());
  }

  delete(name) {
    this._map.delete(name.toLowerCase());
  }

  forEach(callback) {
    this._map.forEach(callback);
  }
}

export class MockResponse {
  constructor(body = null, init = {}) {
    this._body = body;
    this.status = init.status || 200;
    this.statusText = init.statusText || (this.status === 200 ? 'OK' : 'Error');
    this.ok = this.status >= 200 && this.status < 300;
    this.headers = new MockHeaders(init.headers || { 'content-type': 'application/json' });
  }

  async json() {
    if (typeof this._body === 'string') {
      return JSON.parse(this._body);
    }
    return this._body;
  }

  async text() {
    if (typeof this._body === 'string') {
      return this._body;
    }
    return JSON.stringify(this._body);
  }

  clone() {
    return new MockResponse(this._body, {
      status: this.status,
      statusText: this.statusText,
      headers: this.headers
    });
  }
}

export const MOCK_VAULT_KNOWLEDGE_BASE = [
  {
    id: 'note-001-protocol',
    title: 'AI Operating Protocol & Memory Rules',
    type: 'knowledge',
    category: '00_CORE',
    lifecycle: 'ACTIVE',
    confidence: 'very_high',
    verification: 'verified',
    summary: 'Core rules governing cognitive memory persistence, confidence models, and invariant constraints P0-P15.',
    tags: ['protocol', 'memory', 'core', 'invariants'],
    relations: ['[[Confidence Model]]', '[[System Architecture]]'],
    provenance: { source_type: 'official', source_ref: '00_CORE/Rules.md' }
  },
  {
    id: 'note-002-speech',
    title: 'Web Speech & Neural Audio Architecture',
    type: 'procedure',
    category: '01_KNOWLEDGE',
    lifecycle: 'ACTIVE',
    confidence: 'high',
    verification: 'verified',
    summary: 'Continuous SpeechRecognition and SpeechSynthesis with Romanian/English auto-selection and zero external cost.',
    tags: ['voice', 'stt', 'tts', 'audio', 'jarvis'],
    relations: ['[[Tactical Audio Engine]]', '[[Holographic UI]]'],
    provenance: { source_type: 'execution', source_ref: 'projects/jarvis_web' }
  },
  {
    id: 'note-003-hologram',
    title: 'Three.js Holographic Arc Reactor 3D State',
    type: 'knowledge',
    category: '01_KNOWLEDGE',
    lifecycle: 'ACTIVE',
    confidence: 'high',
    verification: 'verified',
    summary: 'Arc-Reactor WebGL rendering at 60 FPS with 6 dynamic reactive states and 2D canvas fallback.',
    tags: ['threejs', 'webgl', '3d', 'hologram'],
    relations: ['[[State Machine]]', '[[Tactical Audio Engine]]'],
    provenance: { source_type: 'execution', source_ref: 'projects/jarvis_web' }
  },
  {
    id: 'note-004-subagents',
    title: 'Subagent Council Telemetry & Dispatch',
    type: 'knowledge',
    category: '00_CORE',
    lifecycle: 'ACTIVE',
    confidence: 'very_high',
    verification: 'verified',
    summary: 'Telemetry status meters and task dispatching across Router, Retrieval, Verifier, Consolidator, and Critic agents.',
    tags: ['subagents', 'council', 'telemetry', 'dispatch'],
    relations: ['[[Multi-Agent Least Privilege]]'],
    provenance: { source_type: 'official', source_ref: '00_CORE/Identity.md' }
  }
];

export class MockFetchClient {
  constructor() {
    this.isOffline = false;
    this.latencyMs = 5;
    this.customRoutes = new Map();
    this.callLog = [];
  }

  setOffline(offline) {
    this.isOffline = Boolean(offline);
  }

  setLatency(ms) {
    this.latencyMs = Math.max(0, Number(ms) || 0);
  }

  addRoute(pathRegexOrString, handler) {
    this.customRoutes.set(pathRegexOrString, handler);
  }

  clearRoutes() {
    this.customRoutes.clear();
  }

  async fetch(url, options = {}) {
    const urlStr = String(url);
    const method = (options.method || 'GET').toUpperCase();
    this.callLog.push({ url: urlStr, method, options, timestamp: Date.now() });

    if (this.latencyMs > 0) {
      await new Promise(r => setTimeout(r, this.latencyMs));
    }

    if (this.isOffline) {
      const error = new TypeError('Failed to fetch (offline network mode)');
      error.code = 'ENOTFOUND';
      throw error;
    }

    // Check custom route overrides first
    for (const [routePattern, handler] of this.customRoutes) {
      if (typeof routePattern === 'string' && urlStr.includes(routePattern)) {
        return handler(urlStr, options);
      } else if (routePattern instanceof RegExp && routePattern.test(urlStr)) {
        return handler(urlStr, options);
      }
    }

    // Default API endpoints
    const parsedUrl = new URL(urlStr, 'http://127.0.0.1:8000');

    // 1. GET /api/v1/search?q=...
    if (parsedUrl.pathname === '/api/v1/search' && method === 'GET') {
      const query = (parsedUrl.searchParams.get('q') || '').toLowerCase().trim();
      const roEnSynonyms = {
        'memorie': 'memory',
        'memoria': 'memory',
        'reguli': 'rules',
        'regulile': 'rules',
        'protocoale': 'protocol',
        'proiecte': 'projects',
        'subagenti': 'subagents',
        'holograma': 'hologram',
        'proceduri': 'procedure'
      };

      const searchTerms = query.split(/\s+/).filter(t => t.length > 2);
      const expandedTerms = new Set(searchTerms);
      for (const t of searchTerms) {
        if (roEnSynonyms[t]) expandedTerms.add(roEnSynonyms[t]);
      }

      const matchedNotes = MOCK_VAULT_KNOWLEDGE_BASE.filter(note => {
        if (!query) return true;
        const text = `${note.title} ${note.summary} ${note.tags.join(' ')} ${note.category}`.toLowerCase();
        if (text.includes(query)) return true;
        for (const term of expandedTerms) {
          if (text.includes(term)) return true;
        }
        return false;
      }).map(note => ({
        ...note,
        relevance: 0.92
      }));

      return new MockResponse({
        query,
        count: matchedNotes.length,
        latencyMs: this.latencyMs,
        results: matchedNotes
      }, { status: 200 });
    }

    // 2. GET /api/v1/status
    if (parsedUrl.pathname === '/api/v1/status' && method === 'GET') {
      return new MockResponse({
        online: true,
        version: '6.0.0',
        indexedNotes: MOCK_VAULT_KNOWLEDGE_BASE.length,
        status: 'HEALTHY'
      }, { status: 200 });
    }

    // 3. POST /api/v1/propose
    if (parsedUrl.pathname === '/api/v1/propose' && method === 'POST') {
      let bodyObj = {};
      try {
        bodyObj = typeof options.body === 'string' ? JSON.parse(options.body) : (options.body || {});
      } catch (err) {
        return new MockResponse({ error: 'Invalid JSON payload' }, { status: 400 });
      }

      if (!bodyObj.title) {
        return new MockResponse({ error: 'Missing title in proposal payload' }, { status: 422 });
      }

      const generatedId = `proposal-${Date.now().toString(36)}-${Math.random().toString(36).substring(2, 6)}`;
      return new MockResponse({
        success: true,
        noteId: generatedId,
        lifecycle: 'REVIEW',
        verification: 'unverified',
        created: new Date().toISOString()
      }, { status: 201 });
    }

    // 404 for unknown endpoints
    return new MockResponse({ error: `Not found: ${parsedUrl.pathname}` }, { status: 404 });
  }
}

function safeDefine(target, prop, value) {
  try {
    target[prop] = value;
  } catch (e) {
    try {
      Object.defineProperty(target, prop, { value, configurable: true, writable: true });
    } catch (e2) {
      // Best effort
    }
  }
}

export function installFetchMock(target = globalThis) {
  const client = (target.fetch && target.fetch.client instanceof MockFetchClient) ? target.fetch.client : new MockFetchClient();
  const mockFetch = (url, options) => client.fetch(url, options);
  mockFetch.client = client;
  mockFetch.setOffline = (offline) => client.setOffline(offline);
  mockFetch.setLatency = (ms) => client.setLatency(ms);
  mockFetch.addRoute = (path, handler) => client.addRoute(path, handler);
  mockFetch.clearRoutes = () => client.clearRoutes();

  safeDefine(target, 'fetch', mockFetch);
  safeDefine(target, 'Headers', MockHeaders);
  safeDefine(target, 'Response', MockResponse);

  if (target.window && target.window !== target) {
    safeDefine(target.window, 'fetch', mockFetch);
    safeDefine(target.window, 'Headers', MockHeaders);
    safeDefine(target.window, 'Response', MockResponse);
  }

  return client;
}
