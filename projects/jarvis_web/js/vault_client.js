export const VaultClient = {
  baseUrl: 'http://127.0.0.1:8000/api/v1',
  async status() {
    const response = await fetch(`${this.baseUrl}/status`);
    if (!response.ok) throw new Error(`Vault status ${response.status}`);
    return response.json();
  },
  async search(query) {
    const response = await fetch(`${this.baseUrl}/search?q=${encodeURIComponent(query)}`);
    if (!response.ok) throw new Error(`Vault search ${response.status}`);
    return response.json();
  },
  async propose(memory) {
    const response = await fetch(`${this.baseUrl}/propose`, {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify(memory)
    });
    if (!response.ok) throw new Error(`Vault propose ${response.status}`);
    return response.json();
  }
};
