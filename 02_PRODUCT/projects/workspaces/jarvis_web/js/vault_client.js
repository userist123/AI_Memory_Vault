const DEFAULT_BASE_URL = typeof window !== 'undefined' ? '/api/v1' : 'http://127.0.0.1:8000/api/v1';

const stripDiacritics = (value) => String(value ?? '')
  .normalize('NFD')
  .replace(/[\u0300-\u036f]/g, '')
  .toLowerCase();

const escapeHtml = (value) => String(value ?? '')
  .replace(/&/g, '&amp;')
  .replace(/</g, '&lt;')
  .replace(/>/g, '&gt;')
  .replace(/"/g, '&quot;')
  .replace(/'/g, '&#39;');

const makeId = () => `proposal-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;

const escapeAttribute = (value) => String(value ?? '')
  .replace(/</g, '&lt;')
  .replace(/>/g, '&gt;')
  .replace(/\x22/g, '&quot;');

export class LRUCache {
  constructor(maxEntries = 50, ttlMs = 30000) {
    this.maxEntries = Math.max(1, Number(maxEntries) || 1);
    this.ttlMs = Math.max(0, Number(ttlMs) || 0);
    this.entries = new Map();
  }

  get(key) {
    const entry = this.entries.get(key);
    if (!entry) return null;
    if (this.ttlMs > 0 && Date.now() - entry.createdAt >= this.ttlMs) {
      this.entries.delete(key);
      return null;
    }
    this.entries.delete(key);
    this.entries.set(key, entry);
    return entry.value;
  }

  set(key, value) {
    this.entries.delete(key);
    this.entries.set(key, {value, createdAt: Date.now()});
    while (this.entries.size > this.maxEntries) {
      this.entries.delete(this.entries.keys().next().value);
    }
    return value;
  }

  clear() {
    this.entries.clear();
  }
}

export const OFFLINE_KNOWLEDGE_BANK = [
  {
    id: 'offline-protocol',
    title: 'AI Operating Protocol & Memory Rules',
    type: 'knowledge',
    category: '00_CORE',
    lifecycle: 'ACTIVE',
    confidence: 'very_high',
    verification: 'verified',
    summary: 'Core rules for memory persistence, provenance, confidence and invariant constraints.',
    tags: ['protocol', 'memory', 'rules', 'core'],
    relations: ['[[Confidence Model]]', '[[System Architecture]]'],
    provenance: {source_type: 'official', source_ref: '00_CORE/AI_Operating_Protocol.md'}
  },
  {
    id: 'offline-memory',
    title: 'Memory Vault Architecture',
    type: 'knowledge',
    category: '00_CORE',
    lifecycle: 'ACTIVE',
    confidence: 'high',
    verification: 'verified',
    summary: 'Layered working, episodic and semantic memory with provenance-preserving recall.',
    tags: ['memory', 'vault', 'semantic', 'recall'],
    relations: ['[[AI Operating Protocol & Memory Rules]]'],
    provenance: {source_type: 'official', source_ref: '00_CORE/Memory.md'}
  },
  {
    id: 'offline-audio',
    title: 'Jarvis Audio Cascade',
    type: 'procedure',
    category: '01_KNOWLEDGE',
    lifecycle: 'ACTIVE',
    confidence: 'high',
    verification: 'verified',
    summary: 'Voice activity detection, speech recognition, language model and streaming speech synthesis.',
    tags: ['audio', 'stt', 'tts', 'vad', 'barge-in'],
    relations: ['[[Jarvis System Architecture]]'],
    provenance: {source_type: 'execution', source_ref: 'projects/jarvis_cognitive_brain'}
  },
  {
    id: 'offline-agents',
    title: 'Jarvis Agent Council',
    type: 'knowledge',
    category: '00_CORE',
    lifecycle: 'ACTIVE',
    confidence: 'very_high',
    verification: 'verified',
    summary: 'Router, Retrieval, Verifier, Consolidator and Critic agents coordinate work with least privilege.',
    tags: ['agents', 'subagents', 'council', 'least-privilege'],
    relations: ['[[Jarvis System Architecture]]'],
    provenance: {source_type: 'official', source_ref: '00_CORE/Identity.md'}
  }
];

export class NoteInspector {
  static extractWikilinks(markdown) {
    return [...String(markdown ?? '').matchAll(/\[\[([^\]]+)\]\]/g)]
      .map((match) => match[1].trim())
      .filter(Boolean);
  }

  static getConfidenceBadge(confidence = 'unknown') {
    const normalized = String(confidence || 'unknown').toLowerCase().replace(/[^a-z0-9_-]/g, '_');
    return `<span class="confidence-badge conf-${escapeHtml(normalized)}">${escapeHtml(normalized.toUpperCase())}</span>`;
  }

  static generateSummary(summary = '', maxLength = 220) {
    const clean = String(summary ?? '').replace(/\s+/g, ' ').trim();
    if (clean.length <= maxLength) return clean;
    return `${clean.slice(0, Math.max(0, maxLength - 1)).trimEnd()}…`;
  }
}

export class VaultClient {
  constructor(options = {}) {
    const configuredBase = String(options.baseUrl || DEFAULT_BASE_URL).replace(/\/$/, '');
    this.baseUrl = configuredBase.endsWith('/api/v1') ? configuredBase : `${configuredBase}/api/v1`;
    this.timeoutMs = Math.max(50, Number(options.timeoutMs) || 1500);
    this.fetchImpl = options.fetchImpl || globalThis.fetch?.bind(globalThis);
    this.cache = options.cache || new LRUCache(options.cacheSize || 50, options.cacheTtlMs || 30000);
    this.offlineNotes = [];
  }

  async request(path, options = {}) {
    if (typeof this.fetchImpl !== 'function') throw new Error('Fetch API unavailable');
    const controller = typeof AbortController === 'function' ? new AbortController() : null;
    const requestOptions = {...options};
    if (controller) requestOptions.signal = controller.signal;
    let timer = null;
    try {
      const requestPromise = this.fetchImpl(`${this.baseUrl}${path}`, requestOptions);
      const timeoutPromise = new Promise((_, reject) => {
        timer = setTimeout(() => {
          controller?.abort();
          reject(new Error(`Vault request timeout after ${this.timeoutMs}ms`));
        }, this.timeoutMs);
      });
      const response = await Promise.race([requestPromise, timeoutPromise]);
      if (!response?.ok) throw new Error(`Vault API ${response?.status || 0}`);
      return await response.json();
    } finally {
      if (timer) clearTimeout(timer);
    }
  }

  async get(path, options = {}) {
    return this.request(path, options);
  }

  async status() {
    return this.request('/status');
  }

  async getStatus() {
    try {
      const response = await this.status();
      return {...response, online: true};
    } catch (error) {
      return {online: false, indexedNotes: 0, status: 'OFFLINE', error: error.message};
    }
  }

  async metrics() {
    return this.request('/metrics');
  }

  async agents() {
    return this.request('/agents');
  }

  async skills(query = '') {
    return this.request(`/skills${query ? `?q=${encodeURIComponent(query)}` : ''}`);
  }

  async proposals() {
    return this.request('/proposals');
  }

  async route(task) {
    return this.request('/route', {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({task})
    });
  }

  async search(query = '') {
    const normalizedQuery = String(query ?? '').trim();
    const cached = this.cache.get(normalizedQuery);
    if (cached) return {...cached, source: 'memory_cache'};
    const startedAt = performance?.now?.() ?? Date.now();
    try {
      const response = await this.request(`/search?q=${encodeURIComponent(normalizedQuery)}`);
      const result = {
        ...response,
        query: response.query ?? normalizedQuery,
        results: Array.isArray(response.results) ? response.results : [],
        latencyMs: Math.max(0, Math.round((performance?.now?.() ?? Date.now()) - startedAt)),
        source: 'live'
      };
      this.cache.set(normalizedQuery, result);
      return result;
    } catch (error) {
      const result = {
        query: normalizedQuery,
        count: 0,
        results: this.searchOffline(normalizedQuery),
        latencyMs: Math.max(0, Math.round((performance?.now?.() ?? Date.now()) - startedAt)),
        source: 'offline_cache',
        error: error.message
      };
      this.cache.set(normalizedQuery, result);
      return result;
    }
  }

  searchOffline(query = '') {
    const terms = stripDiacritics(query).match(/[a-z0-9_-]+/g) || [];
    const notes = [...this.offlineNotes, ...OFFLINE_KNOWLEDGE_BANK];
    if (terms.length === 0) return notes.filter((note) => note.lifecycle !== 'ARCHIVED');
    return notes.filter((note) => {
      if (note.lifecycle === 'ARCHIVED') return false;
      const text = stripDiacritics([
        note.title, note.summary, note.category, ...(note.tags || [])
      ].join(' '));
      return terms.some((term) => text.includes(term));
    });
  }

  inspectNote(note = {}) {
    const inspected = {...note};
    inspected.provenance = {...(note.provenance || {})};
    inspected.wikilinks = NoteInspector.extractWikilinks([
      note.summary || '',
      ...(note.relations || [])
    ].join(' '));
    inspected.summarySnippet = NoteInspector.generateSummary(note.summary || '');
    return inspected;
  }

  formatCitation(note = {}) {
    const safeNote = this.inspectNote(note);
    return {
      note: safeNote,
      toHtml() {
        const title = escapeHtml(safeNote.title || 'Untitled note');
        const category = escapeHtml(safeNote.category || 'UNKNOWN');
        const summary = escapeHtml(NoteInspector.generateSummary(safeNote.summary || ''));
        const confidence = NoteInspector.getConfidenceBadge(safeNote.confidence);
        const source = escapeHtml(safeNote.provenance?.source_ref || safeNote.id || 'unknown');
        return `<article class="citation-card" data-note-title="${escapeAttribute(safeNote.title || '')}"><header><strong>${title}</strong><span>${category}</span></header><p>${summary}</p><footer>${confidence}<small>${source}</small></footer></article>`;
      }
    };
  }

  async propose(memory) {
    return this.proposeNote(memory);
  }

  async proposeNote(memory) {
    if (!memory || typeof memory !== 'object') return {success: false, error: 'Proposal must be an object'};
    const proposal = {
      ...memory,
      id: memory.id || makeId(),
      lifecycle: 'REVIEW',
      verification: 'unverified',
      provenance: {
        ...(memory.provenance || {}),
        source_type: 'inference'
      }
    };
    try {
      const response = await this.request('/propose', {
        method: 'POST',
        headers: {'Content-Type': 'application/json'},
        body: JSON.stringify(proposal)
      });
      const staged = {...proposal, ...response, lifecycle: 'REVIEW', verification: 'unverified'};
      this.offlineNotes.unshift(staged);
      return response;
    } catch (error) {
      this.offlineNotes.unshift(proposal);
      return {
        success: true,
        status: 'staged_offline',
        noteId: proposal.id,
        lifecycle: proposal.lifecycle,
        verification: proposal.verification,
        error: error.message
      };
    }
  }
}

export default {VaultClient, LRUCache, OFFLINE_KNOWLEDGE_BANK, NoteInspector};

