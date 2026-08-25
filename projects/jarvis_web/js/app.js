const MEMORY_API = 'http://127.0.0.1:8000/api/v1';
const COUNCIL_URL = '/data/agents.json';
const $ = (selector) => document.querySelector(selector);

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (char) => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[char]));
}

function setOnline(online) {
  const pill = $('#vault-state');
  pill.classList.toggle('online', online);
  pill.classList.toggle('offline', !online);
  pill.innerHTML = `<i></i> ${online ? 'ONLINE' : 'OFFLINE'}`;
  $('#s-memory').textContent = online ? 'ONLINE' : 'OFFLINE';
  $('#orb-label').textContent = online ? 'LINK' : 'WAIT';
  $('#core-state').textContent = online ? 'VAULT LINK ESTABLISHED' : 'WAITING FOR VAULT LINK';
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
  } catch (error) {
    setOnline(false);
  }
  $('#clock').textContent = new Date().toLocaleTimeString('ro-RO', { hour12:false });
  $('#last-refresh').textContent = new Date().toLocaleTimeString('ro-RO', { hour12:false });
}

function renderResults(data) {
  const container = $('#search-results');
  const results = Array.isArray(data.results) ? data.results : [];
  if (!results.length) {
    container.className = 'results empty';
    container.textContent = 'No relevant memory found.';
    return;
  }
  container.className = 'results';
  container.innerHTML = results.map((item) => {
    const title = item.title || item.name || item.id || 'Untitled memory';
    const summary = item.content || item.text || item.preview || '';
    return `<article class="result-card"><strong>${escapeHtml(title)}</strong><small>${escapeHtml(item.id || 'canonical-memory')}</small><div>${escapeHtml(String(summary).slice(0,360))}</div></article>`;
  }).join('');
}

async function searchMemory(query) {
  if (!query) return;
  const container = $('#search-results');
  container.className = 'results empty';
  container.textContent = 'Retrieving relevant memory…';
  try {
    renderResults(await fetchJson(`${MEMORY_API}/search?q=${encodeURIComponent(query)}`));
  } catch (error) {
    container.textContent = `Memory retrieval unavailable: ${error.message}`;
  }
}

async function loadCouncil() {
  const grid = $('#agent-grid');
  try {
    const data = await fetchJson(COUNCIL_URL);
    const agents = Array.isArray(data.agents) ? data.agents : [];
    grid.innerHTML = agents.map((agent, index) => `
      <article class="agent-card" data-agent="${escapeHtml(agent.id)}">
        <div class="agent-top"><span class="agent-icon">${String(index + 1).padStart(2,'0')}</span><div><strong>${escapeHtml(agent.name)}</strong><small>${escapeHtml(agent.domain)}</small></div></div>
        <div class="skill-chips"><span>Dynamic routing</span><span>Verified</span></div>
        <b style="display:block;color:var(--green);font-size:7px;letter-spacing:.12em">${escapeHtml(agent.status)}</b>
        <button class="agent-btn" type="button">ASSIGN AGENT</button>
      </article>`).join('');

    grid.querySelectorAll('.agent-card').forEach((card) => card.addEventListener('click', () => {
      grid.querySelectorAll('.agent-card').forEach((x) => x.classList.remove('selected'));
      card.classList.add('selected');
      $('#search-input').value = card.querySelector('strong').textContent;
    }));
  } catch (error) {
    grid.innerHTML = `<div class="loading">Agent registry unavailable: ${escapeHtml(error.message)}</div>`;
  }
}

$('#search-form').addEventListener('submit', (event) => {
  event.preventDefault();
  searchMemory($('#search-input').value.trim());
});

$('#quick-go').addEventListener('click', () => searchMemory($('#quick-search').value.trim()));
$('#quick-search').addEventListener('keydown', (event) => {
  if (event.key === 'Enter') searchMemory(event.target.value.trim());
});
$('#focus-search').addEventListener('click', () => {
  $('#search-input').focus();
  $('#search-input').scrollIntoView({ behavior:'smooth', block:'center' });
});
$('#new-proposal').addEventListener('click', () => {
  $('#search-input').value = 'Memory V6 proposal queue';
  searchMemory('Memory V6 proposal queue');
});
$('#diagnostics').addEventListener('click', refreshStatus);

document.querySelectorAll('.tab').forEach((tab) => tab.addEventListener('click', () => {
  document.querySelectorAll('.tab').forEach((x) => x.classList.remove('active'));
  tab.classList.add('active');
  const views = { overview:'.core-grid', memory:'#memory-panel', agents:'#agents-panel', skills:'#skills-panel', execution:'#execution-panel' };
  document.querySelector(views[tab.dataset.view] || '.core-grid')?.scrollIntoView({ behavior:'smooth', block:'center' });
}));

loadCouncil();
refreshStatus();
setInterval(refreshStatus, 15000);
setInterval(() => { $('#clock').textContent = new Date().toLocaleTimeString('ro-RO',{hour12:false}); }, 1000);
