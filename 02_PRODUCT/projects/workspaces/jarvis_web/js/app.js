import { createVoiceEngine, stopSpeaking } from './voice_engine.js';

const API = '/api/v1';
const VOICE_API = '';
const $ = (id) => document.getElementById(id);
const $$ = (selector) => [...document.querySelectorAll(selector)];
let agents = [];
let chatHistory = [];
let voice = null;
let voiceOutputEnabled = true;
let currentAudio = null;
const timeline = [];

function esc(value){return String(value ?? '').replace(/[&<>\"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','\"':'&quot;',"'":'&#39;'}[c]));}
function now(){return new Date().toLocaleTimeString('ro-RO',{hour12:false});}
async function request(base,path,options={}){const r=await fetch(`${base}${path}`,options);if(!r.ok){let detail='';try{detail=await r.text();}catch{}throw new Error(detail||`HTTP ${r.status}`);}return r.json();}
function logEvent(type,message,detail=''){timeline.unshift({time:now(),type,message,detail});while(timeline.length>24)timeline.pop();const el=$('timeline');if(!el)return;el.innerHTML=timeline.slice(0,8).map(e=>`<div><b>${esc(e.time)}</b><span>${esc(e.message)}${e.detail?` Â· ${esc(e.detail)}`:''}</span><em class="${esc(e.type)}">${e.type==='error'?'FAILED':e.type==='write'?'QUEUED':e.type==='route'?'ROUTED':'COMPLETED'}</em></div>`).join('')||'<div class="muted">No activity yet.</div>';}
function setMind(state){const el=$('mind-state');if(el)el.textContent=state;const status=$('chat-status');if(status&&state!=='STANDBY')status.textContent=state;}
function setOnline(online){const state=$('api-state');if(state){state.textContent=online?'ONLINE':'OFFLINE';state.classList.toggle('online',online);}if($('s-memory'))$('s-memory').textContent=online?'ONLINE':'OFFLINE';if($('core-state'))$('core-state').textContent=online?'ALL SYSTEMS OPERATIONAL':'WAITING FOR VAULT LINK';if($('s-llm')&&!online)$('s-llm').textContent='OFFLINE';}
async function refreshStatus(){const t=performance.now();try{const [status,metrics,models]=await Promise.all([request(API,'/status'),request(API,'/metrics'),request(API,'/models')]);setOnline(true);$('metric-notes').textContent=Number(metrics.memory_items??status.indexed_notes??0).toLocaleString('en-US');$('metric-agents').textContent=`${metrics.agents_online??0}/${metrics.agents_total??status.agents??0}`;$('metric-skills').textContent=Number(metrics.skills_operational??0).toLocaleString('en-US');$('metric-proposals').textContent=String(metrics.proposals_pending??0);$('metric-executions').textContent='â€”';$('metric-success').textContent='â€”';$('s-agents').textContent=metrics.agents_online!=null?`${metrics.agents_online} ONLINE`:'ONLINE';$('s-skills').textContent=metrics.skills_operational!=null?'OPERATIONAL':'â€”';$('s-llm').textContent=models.available?'ONLINE':'OFFLINE';$('model-label').textContent=`LOCAL MODEL ${models.default||'OFFLINE'}`;$('footer-agents').textContent=metrics.agents_total??0;$('footer-skills').textContent=metrics.skills_operational??0;$('footer-notes').textContent=metrics.memory_items??0;const select=$('chat-model');if(select&&select.options.length===1){(models.models||[]).forEach(m=>select.add(new Option(m,m)));}document.title=`JARVIS â€” ${models.default||'AI Command Center'}`;logEvent('ok','System telemetry refreshed',`${Math.round(performance.now()-t)}ms`);}catch(e){setOnline(false);setMind('OFFLINE');logEvent('error','Vault API unavailable',e.message);}$('clock').textContent=now();if($('today'))$('today').textContent=new Date().toLocaleDateString('ro-RO',{weekday:'long',day:'2-digit',month:'long',year:'numeric'});}
function renderMemory(rows){const box=$('recent-memory');if(!rows?.length){box.innerHTML='<div class="muted">No indexed memory.</div>';return;}box.innerHTML=rows.slice(0,5).map((x,i)=>`<article class="recent-item"><span class="tag">${esc(x.type||'MEMORY')}</span><strong>${esc(x.title||x.name||x.id||'Memory')}</strong><small>${esc(x.updated||x.created||'Canonical memory')}</small></article>`).join('');}
async function searchMemory(q){const box=$('search-results');if(!box)return;if(!q){await loadRecent();return;}box.className='results';box.innerHTML='<div class="muted">Searching canonical memoryâ€¦</div>';logEvent('route','Memory retrieval started',q);try{const d=await request(API,`/search?q=${encodeURIComponent(q)}`);const rows=d.results||[];box.innerHTML=rows.length?rows.map(x=>`<article class="result-card"><strong>${esc(x.title||x.name||x.id||'Untitled memory')}</strong><small>${esc(x.id||'canonical')}</small><div>${esc(String(x.content||'').slice(0,380))}</div></article>`).join(''):'<div class="results empty">No relevant memory found.</div>';logEvent('ok','Memory retrieval completed',`${d.total_results??0} results`);}catch(e){box.className='results empty';box.textContent=`Memory retrieval unavailable: ${e.message}`;logEvent('error','Memory retrieval failed',e.message);}}
async function loadRecent(){try{const d=await request(API,'/search?q=');renderMemory(d.results||[]);}catch{renderMemory([]);}}
function renderAgents(){const grid=$('agent-grid');if(!grid)return;grid.innerHTML=agents.slice(0,7).map((a,i)=>`<article class="agent-card" data-agent="${esc(a.id)}"><div class="agent-icon">${String(i+1).padStart(2,'0')}</div><strong>${esc(a.name)}</strong><small>${esc(a.domain)}</small><div class="skill-chips">${(a.skills||[]).slice(0,2).map(s=>`<span>${esc(s)}</span>`).join('')}</div></article>`).join('')+`<article class="agent-card more-card"><div class="agent-icon">+15</div><strong>More Agents</strong><small>Agent Council</small></article>`;$$('.agent-card[data-agent]').forEach(card=>card.addEventListener('click',()=>selectAgent(card.dataset.agent)));$('council-count').textContent=`(${agents.length})`;}
async function loadAgents(){try{const d=await request(API,'/agents');agents=d.agents||[];renderAgents();logEvent('ok','Agent Council loaded',`${agents.length} agents`);}catch(e){$('agent-grid').innerHTML='<div class="loading">Agent Council unavailable.</div>';logEvent('error','Agent Council unavailable',e.message);}}
function selectAgent(id){const a=agents.find(x=>x.id===id);if(!a)return;$$('.agent-card[data-agent]').forEach(c=>c.classList.toggle('selected',c.dataset.agent===id));if($('chat-agent'))$('chat-agent').textContent=`ROUTER: ${a.name}`;logEvent('route','Agent selected',`${a.name} Â· ${a.domain}`);}
async function loadSkills(q=''){try{const d=await request(API,`/skills${q?`?q=${encodeURIComponent(q)}`:''}`);const skills=d.skills||[];$('skill-total').textContent=d.total??skills.length;$('skill-cloud').innerHTML=skills.slice(0,7).map((s,i)=>`<article class="skill-card"><h4>${esc(s.name||s.id)}</h4><b>${i%2?'Operational':'Matched skill'}</b><p>${esc(s.id)}</p></article>`).join('');logEvent('ok','Skill registry loaded',`${skills.length} skills`);}catch(e){$('skill-cloud').innerHTML='<div class="loading">Skill registry unavailable.</div>';logEvent('error','Skill registry unavailable',e.message);}}
async function loadProposals(){try{const d=await request(API,'/proposals');const rows=d.pending||[];$('pending-proposals').innerHTML=rows.slice(0,3).map(p=>`<article class="proposal-item" data-id="${esc(p.candidate_id)}"><div><strong>${esc(String(p.content||p.type||'Proposal').slice(0,52))}</strong><span class="priority">${esc((p.type||'PENDING').toUpperCase())}</span></div><small>Pending review</small><div class="proposal-actions"><button class="tiny-btn approve" data-id="${esc(p.candidate_id)}">APPROVE</button><button class="tiny-btn danger-btn reject" data-id="${esc(p.candidate_id)}">REJECT</button></div></article>`).join('')||'<div class="muted">Queue clear.</div>';$$('.approve').forEach(b=>b.addEventListener('click',()=>decideProposal(b.dataset.id,'APPROVED')));$$('.reject').forEach(b=>b.addEventListener('click',()=>decideProposal(b.dataset.id,'REJECTED')));}catch{ $('pending-proposals').innerHTML='<div class="muted">Proposal queue unavailable.</div>';}}
async function decideProposal(id,decision){try{await request(API,`/proposals/${encodeURIComponent(id)}/decision`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({decision})});logEvent(decision==='APPROVED'?'write':'error',`Proposal ${decision.toLowerCase()}`,id);await loadProposals();await refreshStatus();}catch(e){logEvent('error','Proposal decision failed',e.message);}}
function openChat(){const p=$('chat-panel');if(!p)return;p.classList.add('open');p.setAttribute('aria-hidden','false');$('chat-input').focus();setMind('AWAKE');}
function closeChat(){const p=$('chat-panel');if(!p)return;p.classList.remove('open');p.setAttribute('aria-hidden','true');setMind('STANDBY');}
function addChat(role,text,meta=''){const log=$('chat-log');const node=document.createElement('div');node.className=`chat-message ${role}`;node.innerHTML=`<div class="avatar">${role==='assistant'?'J':'U'}</div><div><strong>${role==='assistant'?'JARVIS':'YOU'}</strong><p>${esc(text).replace(/\n/g,'<br>')}</p>${meta?`<small>${esc(meta)}</small>`:''}</div>`;log.appendChild(node);log.scrollTop=log.scrollHeight;}
function updateVoiceButton(){const b=$('voice-toggle');if(!b)return;b.textContent=voiceOutputEnabled?'VOICE ON':'VOICE OFF';b.classList.toggle('active',voiceOutputEnabled);b.setAttribute('aria-pressed',String(voiceOutputEnabled));}
async function speakJarvis(text){if(!voiceOutputEnabled)return;try{if(currentAudio){currentAudio.pause();currentAudio=null;}setMind('SPEAKING');$('chat-status').textContent='SPEAKING';const response=await fetch(`${VOICE_API}/tts`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({text})});if(!response.ok)throw new Error(`TTS HTTP ${response.status}`);const blob=await response.blob();const url=URL.createObjectURL(blob);currentAudio=new Audio(url);currentAudio.onended=()=>{URL.revokeObjectURL(url);currentAudio=null;setMind('AWAKE');$('chat-status').textContent='READY';};currentAudio.onerror=()=>{URL.revokeObjectURL(url);currentAudio=null;setMind('AWAKE');$('chat-status').textContent='READY';};await currentAudio.play();logEvent('ok','JARVIS voice response played','Piper ro_RO-mihai-medium');}catch(e){setMind('AWAKE');$('chat-status').textContent='READY';logEvent('error','Voice output unavailable',e.message);}}
async function sendChat(message) {
  if (!message) return;
  openChat();
  setMind('THINKING');
  $('chat-status').textContent = 'THINKING';
  addChat('user', message);
  $('chat-input').value = '';

  const streamContainer = document.createElement('div');
  streamContainer.className = 'chat-message assistant streaming';
  streamContainer.innerHTML = '<div class="avatar">J</div><div><strong>JARVIS</strong><p></p></div>';
  $('chat-log').appendChild(streamContainer);
  const streamParagraph = streamContainer.querySelector('p');

  try {
    const data = await request(API, '/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message, history: chatHistory, source: 'command-center' })
    });
    const reply = data.reply || data.response || 'Nu am primit un răspuns de la JARVIS.';
    streamParagraph.textContent = reply;
    streamContainer.classList.remove('streaming');
    const metadata = [
      data.model || 'Cognitive Executive',
      data.selected_agent?.name || 'Agent Council',
      String(data.memory_hits ?? 0) + ' memorii',
      String(data.duration_ms ?? 0) + ' ms'
    ].join(' · ');
    const metaElement = document.createElement('small');
    metaElement.textContent = metadata;
    streamContainer.querySelector('div').appendChild(metaElement);
    for (const event of data.events || []) {
      const type = event.state === 'ERROR' ? 'error' : event.name === 'RETRIEVE' ? 'route' : 'ok';
      logEvent(type, event.name.replaceAll('_', ' '), JSON.stringify(event.detail || {}));
    }
    chatHistory.push({ role: 'user', content: message }, { role: 'assistant', content: reply });
    chatHistory = chatHistory.slice(-12);
    $('chat-status').textContent = 'READY';
    $('chat-agent').textContent = 'ROUTER: ' + (data.selected_agent?.name || 'Cognitive Executive');
    setMind('AWAKE');
    if (voiceOutputEnabled) await speakJarvis(reply);
  } catch (error) {
    streamContainer.remove();
    $('chat-status').textContent = 'MODEL OFFLINE';
    setMind('ERROR');
    addChat('assistant', 'Nu pot procesa cererea prin creierul cognitiv. ' + error.message, 'Verifică Ollama sau setează JARVIS_LLM_PROVIDER=mock.');
    logEvent('error', 'Unified cognitive chat failed', error.message);
  }
}
function openProposal(){const d=$('proposal-dialog');if(d.showModal)d.showModal();$('proposal-input').focus();}
async function submitProposal(e){e.preventDefault();const content=$('proposal-input').value.trim();if(!content)return;try{const d=await request(API,'/propose',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({type:$('proposal-type').value,content})});$('proposal-feedback').textContent=`Queued: ${d.candidate_id}`;logEvent('write','Memory proposal queued',d.candidate_id);await loadProposals();await refreshStatus();setTimeout(()=>$('proposal-dialog').close(),600);}catch(e){$('proposal-feedback').textContent=`Proposal failed: ${e.message}`;logEvent('error','Memory proposal failed',e.message);}}
async function diagnostics(){setMind('DIAGNOSTICS');try{const [s,m,a,k,v]=await Promise.all([request(API,'/status'),request(API,'/metrics'),request(API,'/agents'),request(API,'/skills'),request('','/health')]);logEvent('ok','Unified diagnostics passed',a.agents.length+' agents · '+k.total+' skills · '+m.engine+' · '+(v.service||'JARVIS Unified'));setMind('AWAKE');}catch(e){logEvent('error','Unified diagnostics failed',e.message);setMind('ERROR');}}

$('focus-chat').addEventListener('click',openChat);$('close-chat').addEventListener('click',closeChat);$('chat-form').addEventListener('submit',e=>{e.preventDefault();sendChat($('chat-input').value.trim());});$('chat-input').addEventListener('keydown',e=>{if(e.key==='Enter'&&!e.shiftKey){e.preventDefault();$('chat-form').requestSubmit();}});$('new-proposal').addEventListener('click',openProposal);$('proposal-form').addEventListener('submit',submitProposal);$('focus-search').addEventListener('click',()=>{$('search-input').focus();$('memory-panel').scrollIntoView({behavior:'smooth',block:'center'});});$('diagnostics').addEventListener('click',diagnostics);$('search-form').addEventListener('submit',e=>{e.preventDefault();searchMemory($('search-input').value.trim());});$('quick-go')?.addEventListener('click',()=>searchMemory($('quick-search').value.trim()));$('quick-search')?.addEventListener('keydown',e=>{if(e.key==='Enter')searchMemory(e.target.value.trim());});$('skill-filter')?.addEventListener('keydown',e=>{if(e.key==='Enter')loadSkills(e.target.value.trim());});$$('.chips button').forEach(b=>b.addEventListener('click',()=>searchMemory(b.dataset.query||'')));$$('.tab').forEach(tab=>tab.addEventListener('click',()=>{if(tab.dataset.view==='chat'){openChat();return;}$$('.tab').forEach(t=>t.classList.remove('active'));tab.classList.add('active');const targets={overview:'.reactor-section',memory:'#memory-panel',agents:'#agents-panel',skills:'#skills-panel',council:'#agents-panel',execution:'#execution-panel',analytics:'#execution-panel',settings:'.workspace'};document.querySelector(targets[tab.dataset.view]||'.reactor-section')?.scrollIntoView({behavior:'smooth',block:'center'});}));
voice=createVoiceEngine({onText:(text)=>sendChat(text),onState:(state)=>{if($('voice-status'))$('voice-status').textContent=state;setMind(state==='LISTENING'?'LISTENING':'AWAKE');}});
if(!voice){$('voice-status').textContent='VOICE INPUT UNSUPPORTED';$('voice-toggle').disabled=true;}else{updateVoiceButton();}
$('voice-toggle').addEventListener('click',()=>{if(!voice)return;voiceOutputEnabled=!voiceOutputEnabled;updateVoiceButton();if(!voiceOutputEnabled){stopSpeaking();if(currentAudio){currentAudio.pause();currentAudio=null;}setMind('AWAKE');}else{voice.start();setMind('LISTENING');}});
(async()=>{await Promise.all([refreshStatus(),loadAgents(),loadSkills(),loadProposals(),loadRecent()]);logEvent('ok','JARVIS V2 Command Center ready');})();setInterval(refreshStatus,15000);setInterval(loadProposals,20000);setInterval(()=>{$('clock').textContent=now();},1000);

