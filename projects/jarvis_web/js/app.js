const MEMORY_API = 'http://127.0.0.1:8000/api/v1';
const $ = (selector) => document.querySelector(selector);

function setOnline(online) {
  const pill = $('#vault-state');
  pill.classList.toggle('online', online);
  pill.classList.toggle('offline', !online);
  pill.innerHTML = `<span class="dot"></span> ${online ? 'ONLINE' : 'OFFLINE'}`;
  $('#memory-status').textContent = online ? 'ONLINE' : 'OFFLINE';
  $('#orb-label').textContent = online ? 'LINK' : 'WAIT';
}

async function fetchJson(url, options = {}) {
  const response = await fetch(url, options);
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return response.json();
}

async function refreshStatus() {
  try {
    const data = await fetchJson(`${MEMORY_API}/status`);
    setOnline(true);
    $('#metric-notes').textContent = Number(data.indexed_notes ?? 0).toLocaleString('en-US');
    $('#metric-service').textContent = data.service || 'Memory API';
    $('#vault-path').textContent = String(data.vault_root || 'AI_Memory_Vault').split('\\').pop();
    $('#last-refresh').textContent = new Date().toLocaleTimeString('ro-RO', { hour12: false });
  } catch (error) {
    setOnline(false);
    $('#last-refresh').textContent = new Date().toLocaleTimeString('ro-RO', { hour12: false });
  }
}

function renderResults(data) {
  const container = $('#search-results');
  const results = Array.isArray(data.results) ? data.results : [];
  if (!results.length) {
    container.className = 'results empty';
    container.textContent = 'No relevant notes found.';
    return;
  }
  container.className = 'results';
  container.innerHTML = results.map((item) => {
    const title = item.title || item.name || item.id || 'Untitled note';
    const id = item.id ? `<small>${escapeHtml(String(item.id))}</small>` : '';
    const summary = item.content || item.text || item.preview || '';
    return `<article class="result-card"><strong>${escapeHtml(String(title))}</strong>${id}<div>${escapeHtml(String(summary).slice(0, 360))}</div></article>`;
  }).join('');
}

function escapeHtml(value) {
  return value.replace(/[&<>'"]/g, (char) => ({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[char]));
}

$('#search-form').addEventListener('submit', async (event) => {
  event.preventDefault();
  const query = $('#search-input').value.trim();
  if (!query) return;
  const container = $('#search-results');
  container.className = 'results empty';
  container.textContent = 'Retrieving relevant memory…';
  try {
    const data = await fetchJson(`${MEMORY_API}/search?q=${encodeURIComponent(query)}`);
    renderResults(data);
  } catch (error) {
    container.className = 'results empty';
    container.textContent = `Memory retrieval unavailable: ${error.message}`;
  }
});

$('#refresh-btn').addEventListener('click', refreshStatus);
refreshStatus();
setInterval(refreshStatus, 15000);
