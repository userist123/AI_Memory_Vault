const API = 'http://127.0.0.1:8000/api/v1';
const $ = (s) => document.querySelector(s);
const $$ = (s) => [...document.querySelectorAll(s)];
const timeline = [];
let agents = [];
let skills = [];

function esc(value) {
  return String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}
function stamp() { return new Date().toLocaleTimeString('ro-RO',{hour12:false}); }
function logEvent(type, message, detail='') {
  timeline.unshift({time:stamp(),type,message,detail});
  if(timeline.length>30) timeline.length=30;
  const el=$('#timeline');
  if(el) el.innerHTML=timeline.slice(0,10).map(e=>`<div><b>${esc(e.time)}</b><span>${esc(e.message)}${e.detail?` · ${esc(e.detail)}`:''}</span><em class="${esc(e.type)}">${e.type==='error'?'FAILED':e.type==='write'?'QUEUED':e.type==='route'?'ROUTED':'COMPLETED'}</em></div>`).join('')||'<div class="muted">No activity yet.</div>';
}
async function request(path, options={}) {
  const response=await fetch(`${API}${path}`,options);
  if(!response.ok) throw new Error(`HTTP ${response.status}`);
  return response.json();
}
function setOnline(online) {
  const pill=$('#vault-state');
  pill.classList.toggle('online',online); pill.classList.toggle('offline',!online);
  pill.innerHTML=`<i></i> ${online?'ONLINE':'OFFLINE'}`;
  $('#s-memory').textContent=online?'ONLINE':'OFFLINE';
  $('#orb-label').textContent=online?'LINK':'WAIT';
  $('#core-state').textContent=online?'VAULT LINK ESTABLISHED':'WAITING FOR VAULT LINK';
}
async function refreshStatus() {
  const started=performance.now();
  try {
    const [status,metrics]=await Promise.all([request('/status'),request('/metrics')]);
    setOnline(true);
    $('#metric-notes').textContent=Number(metrics.memory_items??status.indexed_notes??0).toLocaleString('en-US');
    $('#metric-agents').textContent=`${metrics.agents_online??0}/${metrics.agents_total??status.agents??0}`;
    $('#metric-skills').textContent=Number(metrics.skills_operational??status.skills??0).toLocaleString('en-US');
    $('#metric-proposals').textContent=Number(metrics.proposals_pending??0).toLocaleString('en-US');
    $('#proposal-state-count').textContent=Number(metrics.proposals_pending??0).toLocaleString('en-US');
    $('#side-notes').textContent=Number(metrics.memory_items??0).toLocaleString('en-US');
    $('#side-agents').textContent=metrics.agents_online??0;
    $('#side-skills').textContent=metrics.skills_operational??0;
    $('#side-pending').textContent=metrics.proposals_pending??0;
    $('#footer-agents').textContent=metrics.agents_total??0;
    $('#footer-skills').textContent=metrics.skills_operational??0;
    $('#router-state').textContent='READY';
    $('#latency').textContent=`API ${Math.round(performance.now()-started)}ms`;
  } catch(error) {
    setOnline(false); $('#router-state').textContent='OFFLINE'; $('#latency').textContent='API —';
    logEvent('error','Vault API unavailable',error.message);
  }
  $('#clock').textContent=stamp(); $('#last-refresh').textContent=stamp();
}
function renderResults(data) {
  const box=$('#search-results'); const results=Array.isArray(data.results)?data.results:[];
  if(!results.length){box.className='results empty';box.textContent='No relevant memory found.';return;}
  box.className='results';
  box.innerHTML=results.map(item=>`<article class="result-card"><strong>${esc(item.title||item.name||item.id||'Untitled memory')}</strong><small>${esc(item.id||'canonical-memory')}</small><div>${esc(String(item.content||item.text||item.preview||'').slice(0,420))}</div></article>`).join('');
}
async function searchMemory(query, recent=false) {
  if(!query && !recent) return;
  const box=$('#search-results');
  if(!recent){box.className='results empty';box.textContent='Retrieving relevant memory…';logEvent('route','Memory retrieval started',query);}
  try {
    const data=await request(`/search?q=${encodeURIComponent(query)}`);
    if(recent){
      const recentBox=$('#recent-memory'); const rows=(data.results||[]).slice(0,5);
      recentBox.innerHTML=rows.map((x,i)=>`<article><i>${String(i+1).padStart(2,'0')}</i><div><b>${esc(x.title||x.name||x.id||'Memory')}</b><span>Canonical memory</span></div></article>`).join('')||'<div class="muted">No indexed memory.</div>';
    } else { renderResults(data); logEvent('ok','Memory retrieval completed',`${data.total_results??0} results`); }
  } catch(error) {
    if(recent) $('#recent-memory').innerHTML=`<div class="muted">Recent memory unavailable.</div>`;
    else {box.className='results empty';box.textContent=`Memory retrieval unavailable: ${error.message}`;logEvent('error','Memory retrieval failed',error.message);}
  }
}
function renderAgents() {
  const grid=$('#agent-grid');
  grid.innerHTML=agents.map((agent,i)=>`<article class="agent-card" data-agent="${esc(agent.id)}"><div class="agent-top"><span class="agent-icon">${String(i+1).padStart(2,'0')}</span><div><strong>${esc(agent.name)}</strong><small>${esc(agent.domain)}</small></div></div><div class="skill-chips">${(agent.skills||[]).slice(0,6).map(s=>`<span>${esc(s)}</span>`).join('')}</div><button class="agent-btn" type="button">ASSIGN AGENT</button></article>`).join('');
  $$('.agent-btn').forEach(btn=>btn.addEventListener('click',e=>{e.stopPropagation();selectAgent(btn.closest('.agent-card').dataset.agent);}));
  $$('.agent-card').forEach(card=>card.addEventListener('click',()=>selectAgent(card.dataset.agent)));
  $('#council-count').textContent=`${agents.length} AGENTS`;
}
function selectAgent(id) {
  const agent=agents.find(a=>a.id===id); if(!agent) return;
  $$('.agent-card').forEach(c=>c.classList.toggle('selected',c.dataset.agent===id));
  $('#route-input').value=agent.domain;
  $('#search-input').value=agent.name;
  logEvent('route','Agent selected',`${agent.name} · ${agent.domain}`);
}
async function loadAgents() {
  try { const data=await request('/agents'); agents=data.agents||[]; renderAgents(); logEvent('ok','Agent Council loaded',`${agents.length} agents`); }
  catch(e){$('#agent-grid').innerHTML=`<div class="loading">Agent registry unavailable: ${esc(e.message)}</div>`;logEvent('error','Agent registry unavailable',e.message);}
}
async function routeTask(task) {
  if(!task) return;
  const box=$('#route-results'); box.className='route-results empty'; box.textContent='Ranking agents and capabilities…';
  logEvent('route','Agent routing started',task);
  try {
    const data=await request('/route',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({task})});
    const rows=data.selected||[];
    box.className='route-results';
    box.innerHTML=rows.map((a,i)=>`<article class="route-card ${i===0?'selected':''}" data-id="${esc(a.id)}"><strong>${esc(a.name)}</strong><small>${esc(a.domain)}</small><div class="skill-chips">${(a.skills||[]).slice(0,3).map(s=>`<span>${esc(s)}</span>`).join('')}</div><div class="route-score">SCORE ${a.route_score}</div></article>`).join('')||'<div class="route-results empty">No compatible agent found.</div>';
    $$('.route-card').forEach(card=>card.addEventListener('click',()=>{selectAgent(card.dataset.id);$$('.route-card').forEach(c=>c.classList.remove('selected'));card.classList.add('selected');}));
    logEvent('route','Agent routing completed',`${rows.length} candidates`);
  } catch(e){box.className='route-results empty';box.textContent=`Routing unavailable: ${e.message}`;logEvent('error','Agent routing failed',e.message);}
}
async function loadSkills(query='') {
  try {
    const data=await request(`/skills${query?`?q=${encodeURIComponent(query)}`:''}`); skills=data.skills||[];
    $('#skill-total').textContent=String(data.total??skills.length);
    $('#skill-cloud').innerHTML=skills.slice(0,80).map(s=>`<span title="${esc(s.path)}">${esc(s.name||s.id)}</span>`).join('')||'<span>No skills found.</span>';
    logEvent('ok','Skill registry loaded',`${skills.length} returned`);
  } catch(e){$('#skill-cloud').innerHTML='<span>Skill registry unavailable.</span>';logEvent('error','Skill registry unavailable',e.message);}
}
function renderProposals(data){
  const pending=data.pending||[]; $('#proposal-state-count').textContent=pending.length;
  $('#metric-proposals').textContent=pending.length; $('#side-pending').textContent=pending.length;
  $('#pending-proposals').innerHTML=pending.slice(0,5).map(p=>`<div class="proposal"><b>${esc(String(p.content||p.type||'Proposal').slice(0,54))}</b><span>${esc(p.queue_status||'PENDING')}</span></div>`).join('')||'<div class="muted">Queue clear.</div>';
}
async function loadProposals(){try{renderProposals(await request('/proposals'));}catch(e){$('#pending-proposals').innerHTML='<div class="muted">Proposal queue unavailable.</div>';}}
function openProposal(){const dialog=$('#proposal-dialog');$('#proposal-feedback').textContent='Proposal goes through the canonical MemoryController.';$('#proposal-input').value='';if(typeof dialog.showModal==='function')dialog.showModal();else dialog.setAttribute('open','');}
async function submitProposal(e){e.preventDefault();const content=$('#proposal-input').value.trim();if(!content)return;const feedback=$('#proposal-feedback');feedback.textContent='Submitting to MemoryController…';try{const result=await request('/propose',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({type:$('#proposal-type').value,content,source:'JARVIS Command Center'})});feedback.textContent='Accepted into canonical proposal lifecycle.';logEvent('write','Memory proposal queued',result.result?.id||result.status);await loadProposals();await refreshStatus();setTimeout(()=>$('#proposal-dialog').close(),500);}catch(error){feedback.textContent=`Proposal failed: ${error.message}`;logEvent('error','Memory proposal failed',error.message);}}
async function diagnostics(){logEvent('route','Diagnostics started');const start=performance.now();try{const results=await Promise.all([request('/status'),request('/metrics'),request('/agents'),request('/skills')]);$('#latency').textContent=`DIAG ${Math.round(performance.now()-start)}ms`;logEvent('ok','Diagnostics passed',`${results[2].agents?.length||0} agents · ${results[3].total||0} skills`);}catch(e){$('#latency').textContent='DIAG FAILED';logEvent('error','Diagnostics failed',e.message);}}

$('#search-form').addEventListener('submit',e=>{e.preventDefault();searchMemory($('#search-input').value.trim());});
$('#quick-go').addEventListener('click',()=>searchMemory($('#quick-search').value.trim()));
$('#quick-search').addEventListener('keydown',e=>{if(e.key==='Enter')searchMemory(e.target.value.trim());});
$('#focus-search').addEventListener('click',()=>{$('#search-input').focus();$('#memory-panel').scrollIntoView({behavior:'smooth'});});
$('#new-proposal').addEventListener('click',openProposal);
$('#proposal-form').addEventListener('submit',submitProposal);
$('#diagnostics').addEventListener('click',diagnostics);
$('#route-form').addEventListener('submit',e=>{e.preventDefault();routeTask($('#route-input').value.trim());});
$('#skill-filter-btn').addEventListener('click',()=>loadSkills($('#skill-filter').value.trim()));
$('#skill-filter').addEventListener('keydown',e=>{if(e.key==='Enter')loadSkills(e.target.value.trim());});
$('#clear-timeline').addEventListener('click',()=>{timeline.length=0;logEvent('ok','Timeline cleared');});
$$('.chips button').forEach(btn=>btn.addEventListener('click',()=>{const q=btn.dataset.query||'';$('#quick-search').value=q;searchMemory(q); }));
$$('.tab').forEach(tab=>tab.addEventListener('click',()=>{ $$('.tab').forEach(x=>x.classList.remove('active'));tab.classList.add('active');const targets={overview:'.core-grid',memory:'#memory-panel',agents:'#agents-panel',skills:'#skills-panel',execution:'#execution-panel'};document.querySelector(targets[tab.dataset.view])?.scrollIntoView({behavior:'smooth',block:'center'}); }));

(async()=>{renderAgents();await Promise.all([loadAgents(),loadSkills(),loadProposals(),refreshStatus()]);await searchMemory('',true);})();
setInterval(refreshStatus,15000);setInterval(loadProposals,20000);setInterval(()=>{$('#clock').textContent=stamp();},1000);
