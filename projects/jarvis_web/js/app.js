const MEMORY_API = 'http://127.0.0.1:8000/api/v1';
const COUNCIL_URL = '/data/agent-council.json';
const $ = (selector) => document.querySelector(selector);
const timeline = [];

function escapeHtml(value) {
  return String(value).replace(/[&<>'"]/g, (char) => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', "'":'&#39;', '"':'&quot;' }[char]));
}

function logEvent(type, message, detail = '') {
  timeline.unshift({ time: new Date(), type, message, detail });
  renderTimeline();
}

function renderTimeline() {
  const el = $('#timeline');
  if (!el) return;
  el.innerHTML = timeline.slice(0, 8).map((item) => `
    <div class="timeline-item">
      <span class="timeline-dot ${escapeHtml(item.type)}"></span>
      <div><strong>${escapeHtml(item.message)}</strong><small>${item.time.toLocaleTimeString('ro-RO',{hour12:false})}${item.detail ? ` · ${escapeHtml(item.detail)}` : ''}</small></div>
    </div>`).join('') || '<div class="muted">No activity yet.</div>';
}

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
    $('#last-refresh').textContent = new Date().toLocaleTimeString('ro-RO', { hour12:false });
    logEvent('good', 'Memory API online');
  } catch (error) {
    setOnline(false);
    $('#last-refresh').textContent = new Date().toLocaleTimeString('ro-RO', { hour12:false });
    logEvent('danger', 'Memory API unavailable', error.message);
  }
}

async function loadCouncil() {
  try {
    const data = await fetchJson(COUNCIL_URL);
    const agents = Array.isArray(data.agents) ? data.agents : [];
    $('#metric-agents').textContent = agents.length;
    $('#council-count').textContent = `${agents.length} AGENTS`;
    $('#agent-grid').innerHTML = agents.map((agent) => `
      <article class="agent-card" data-agent="${escapeHtml(agent.id)}">
        <div class="agent-top"><span class="agent-icon">${agent.icon}</span><div><strong>${escapeHtml(agent.id)}</strong><small>${escapeHtml(agent.domain)}</small></div></div>
        <div class="skill-chips">${agent.skills.slice(0,7).map((skill) => `<span>${escapeHtml(skill)}</span>`).join('')}</div>
        <button class="agent-btn" data-agent-id="${escapeHtml(agent.id)}" type="button">Select Agent</button>
      </article>`).join('');
    document.querySelectorAll('.agent-btn').forEach((button) => button.addEventListener('click', () => selectAgent(button.dataset.agentId, agents)));
    logEvent('good', 'Agent Council loaded', `${agents.length} agents`);
  } catch (error) {
    $('#agent-grid').innerHTML = `<div class="results empty">Agent Council unavailable: ${escapeHtml(error.message)}</div>`;
    logEvent('danger', 'Agent Council unavailable', error.message);
  }
}

function selectAgent(agentId, agents) {
  const agent = agents.find((item) => item.id === agentId);
  if (!agent) return;
  document.querySelectorAll('.agent-card').forEach((card) => card.classList.toggle('selected', card.dataset.agent === agentId));
  $('#search-input').value = agent.domain;
  logEvent('agent', `Agent selected: ${agent.id}`, agent.domain);
}

function renderResults(data) {
  const container = $('#search-results');
  const results = Array.isArray(data.results) ? data.results : [];
  if (!results.length) { container.className = 'results empty'; container.textContent = 'No relevant notes found.'; return; }
  container.className = 'results';
  container.innerHTML = results.map((item) => {
    const title = item.title || item.name || item.id || 'Untitled note';
    const summary = item.content || item.text || item.preview || '';
    return `<article class="result-card"><strong>${escapeHtml(title)}</strong><small>${escapeHtml(String(item.id ?? ''))}</small><div>${escapeHtml(String(summary).slice(0,360))}</div></article>`;
  }).join('');
}

$('#search-form').addEventListener('submit', async (event) => {
  event.preventDefault();
  const query = $('#search-input').value.trim();
  if (!query) return;
  const container = $('#search-results');
  container.className = 'results empty';
  container.textContent = 'Retrieving relevant memory…';
  logEvent('retrieve', 'Memory retrieval started', query);
  try {
    const data = await fetchJson(`${MEMORY_API}/search?q=${encodeURIComponent(query)}`);
    renderResults(data);
    logEvent('good', 'Memory retrieval completed', `${data.total_results ?? (data.results || []).length} results`);
  } catch (error) {
    container.className = 'results empty';
    container.textContent = `Memory retrieval unavailable: ${error.message}`;
    logEvent('danger', 'Memory retrieval failed', error.message);
  }
});

$('#proposal-btn').addEventListener('click', async () => {
  const text = $('#proposal-input').value.trim();
  if (!text) return;
  const state = $('#proposal-state');
  state.textContent = 'Submitting proposal…';
  logEvent('write', 'Memory proposal submitted');
  try {
    await fetchJson(`${MEMORY_API}/propose`, { method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify({ type:'fact', content:text, source:'JARVIS Command Center' }) });
    state.textContent = 'Proposal accepted by the MemoryController queue.';
    $('#proposal-input').value = '';
    logEvent('good', 'Memory proposal accepted');
  } catch (error) {
    state.textContent = `Proposal failed: ${error.message}`;
    logEvent('danger', 'Memory proposal failed', error.message);
  }
});

$('#refresh-btn').addEventListener('click', () => { refreshStatus(); loadCouncil(); });
$('#quick-search').addEventListener('click', () => { $('#search-input').focus(); $('#search-panel').scrollIntoView({behavior:'smooth'}); });
$('#quick-council').addEventListener('click', () => $('#council-panel').scrollIntoView({behavior:'smooth'}));
setInterval(() => { $('#clock').textContent = new Date().toLocaleTimeString('ro-RO', {hour12:false}); }, 1000);
$('#clock').textContent = new Date().toLocaleTimeString('ro-RO', {hour12:false});
renderTimeline();
loadCouncil();
refreshStatus();
setInterval(refreshStatus, 15000);
