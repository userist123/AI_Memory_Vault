export const VaultClient = {
  baseUrl: 'http://127.0.0.1:8000/api/v1',
  async get(path, options = {}) {
    const response = await fetch(`${this.baseUrl}${path}`, options);
    if (!response.ok) throw new Error(`Vault API ${response.status}`);
    return response.json();
  },
  status() { return this.get('/status'); },
  metrics() { return this.get('/metrics'); },
  agents() { return this.get('/agents'); },
  skills(query = '') { return this.get(`/skills${query ? `?q=${encodeURIComponent(query)}` : ''}`); },
  proposals() { return this.get('/proposals'); },
  search(query) { return this.get(`/search?q=${encodeURIComponent(query)}`); },
  route(task) {
    return this.get('/route', {method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify({task})});
  },
  propose(memory) {
    return this.get('/propose', {method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify(memory)});
  }
};
