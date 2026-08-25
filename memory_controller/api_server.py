"""Local REST API Gateway for AI Memory Vault and JARVIS Command Center."""
from __future__ import annotations
import datetime, json, os, sys, urllib.request, urllib.error
from pathlib import Path
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import parse_qs, urlparse

project_root = Path(__file__).resolve().parent.parent
if str(project_root) not in sys.path: sys.path.insert(0, str(project_root))
from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from memory_controller.storage.file_engine import FileStorageEngine
from cognitive_core.extraction import AtomicMemoryExtractor
from cognitive_core.proposal_queue import MemoryProposalQueue
from cognitive_core.queue_promoter import QueuePromoter

class APIJSONEncoder(json.JSONEncoder):
    def default(self,obj):
        if hasattr(obj,'value'): return obj.value
        if isinstance(obj,(datetime.date,datetime.datetime)): return obj.isoformat()
        return super().default(obj)

def _read_json(path:Path,fallback):
    try: return json.loads(path.read_text(encoding='utf-8'))
    except (OSError,json.JSONDecodeError): return fallback

def _skill_catalog(root:Path):
    skills=[]; base=root/'.agents'/'skills'
    if not base.exists(): return skills
    for f in sorted(base.glob('**/SKILL.md')):
        rel=f.parent.relative_to(base).as_posix(); head=f.read_text(encoding='utf-8',errors='ignore')[:500]; name=rel
        for line in head.splitlines():
            if line.lower().startswith('name:'): name=line.split(':',1)[1].strip().strip('"'); break
        skills.append({'id':rel,'name':name,'path':f.as_posix()})
    return skills

def _agents(root:Path): return _read_json(root/'projects'/'jarvis_web'/'data'/'agents.json',{'agents':[]}).get('agents',[])

def _route_agents(root:Path,task:str):
    tokens={t for t in task.lower().replace('/',' ').replace('-',' ').split() if len(t)>2}; scored=[]
    for agent in _agents(root):
        hay=' '.join([str(agent.get('id','')),str(agent.get('name','')),str(agent.get('domain','')),' '.join(agent.get('skills',[]))]).lower()
        matched=sorted(t for t in tokens if t in hay); scored.append({**agent,'route_score':len(matched),'matched_terms':matched})
    scored.sort(key=lambda x:(-x['route_score'],x.get('name',''))); return scored[:5]

def _ollama_get(path,timeout=20):
    host=os.getenv('OLLAMA_HOST','http://127.0.0.1:11434').rstrip('/')
    req=urllib.request.Request(host+path,method='GET')
    with urllib.request.urlopen(req,timeout=timeout) as r: return json.loads(r.read().decode('utf-8'))

def _ollama_post(path,payload,timeout=120):
    host=os.getenv('OLLAMA_HOST','http://127.0.0.1:11434').rstrip('/')
    req=urllib.request.Request(host+path,data=json.dumps(payload).encode('utf-8'),headers={'Content-Type':'application/json'},method='POST')
    with urllib.request.urlopen(req,timeout=timeout) as r: return json.loads(r.read().decode('utf-8'))

def _ollama_models():
    try: return [m.get('name') for m in _ollama_get('/api/tags').get('models',[]) if m.get('name')]
    except Exception: return []

class BrowserMemoryAPIHandler(BaseHTTPRequestHandler):
    vault_root=Path(os.getenv('AI_MEMORY_VAULT_ROOT',str(project_root))).resolve()
    storage=FileStorageEngine(str(vault_root)); controller=MemoryController(storage); queue=MemoryProposalQueue(vault_root/'06_INBOX'/'memory_proposals.jsonl')
    def log_message(self,format,*args): return
    def _set_headers(self,status=200):
        self.send_response(status); self.send_header('Content-Type','application/json; charset=utf-8'); self.send_header('Access-Control-Allow-Origin','*'); self.send_header('Access-Control-Allow-Methods','GET,POST,OPTIONS'); self.send_header('Access-Control-Allow-Headers','Content-Type,Authorization,Mcp-Version'); self.send_header('Cache-Control','no-store'); self.send_header('X-Content-Type-Options','nosniff'); self.end_headers()
    def _json(self,status,payload):
        self._set_headers(status); self.wfile.write(json.dumps(payload,ensure_ascii=False,cls=APIJSONEncoder).encode('utf-8'))
    def _body(self):
        n=int(self.headers.get('Content-Length',0) or 0); raw=self.rfile.read(n); return json.loads(raw.decode('utf-8')) if raw else {}
    def do_OPTIONS(self): self._set_headers(200)
    def do_GET(self):
        p=urlparse(self.path); path=p.path; q=parse_qs(p.query)
        if path in {'/','/api/v1/status'}:
            agents=_agents(self.vault_root); skills=_skill_catalog(self.vault_root); models=_ollama_models(); self._json(200,{'status':'online','service':'AI Memory Vault Browser Gateway','vault_root':str(self.vault_root),'indexed_notes':len(self.storage.id_to_path),'agents':len(agents),'skills':len(skills),'ollama':bool(models),'models':models[:20],'default_model':os.getenv('JARVIS_MODEL',models[0] if models else '')}); return
        if path=='/api/v1/metrics':
            agents=_agents(self.vault_root); skills=_skill_catalog(self.vault_root); counts=self.queue.status(); self._json(200,{'memory_items':len(self.storage.id_to_path),'agents_online':sum(1 for a in agents if a.get('status')=='ONLINE'),'agents_total':len(agents),'skills_operational':len(skills),'proposals_pending':counts.get('PENDING_REVIEW',0),'engine':'V6','retrieval':'MemoryController'}); return
        if path=='/api/v1/models':
            models=_ollama_models(); self._json(200,{'available':bool(models),'models':models,'default':os.getenv('JARVIS_MODEL',models[0] if models else '')}); return
        if path=='/api/v1/agents': self._json(200,_read_json(self.vault_root/'projects'/'jarvis_web'/'data'/'agents.json',{'agents':[]})); return
        if path=='/api/v1/skills':
            skills=_skill_catalog(self.vault_root); term=q.get('q',[''])[0].lower().strip();
            if term: skills=[s for s in skills if term in s['id'].lower() or term in s['name'].lower()]
            self._json(200,{'total':len(skills),'skills':skills[:1000]}); return
        if path=='/api/v1/proposals':
            records=self.queue._load(); pending=[x for x in records if x.get('queue_status')=='PENDING_REVIEW']; self._json(200,{'total':len(records),'pending':pending[:100],'status':{k:sum(1 for x in records if x.get('queue_status')==k) for k in ['PENDING_REVIEW','APPROVED','REJECTED','PROMOTED']}}); return
        if path=='/api/v1/search':
            term=q.get('q',[''])[0].strip(); notes=self.storage.query(intent=term); notes.sort(key=lambda n:str(n.get('updated',n.get('created',''))),reverse=True); self._json(200,{'query':term,'total_results':len(notes),'results':notes[:20]}); return
        if path.startswith('/api/v1/note/'):
            nid=path.replace('/api/v1/note/','',1); note=self.storage.get(nid); self._json(200 if note else 404,note if note else {'error':'Note not found','id':nid}); return
        self._json(404,{'error':'Endpoint not found'})
    def do_POST(self):
        path=urlparse(self.path).path
        try: data=self._body()
        except (UnicodeDecodeError,json.JSONDecodeError): self._json(400,{'error':'Invalid JSON body'}); return
        if path=='/api/v1/propose':
            content=str(data.get('content','')).strip(); kind=str(data.get('type','fact')).lower()
            if not content: self._json(400,{'error':'content is required'}); return
            if kind not in {'fact','decision','preference','task','lesson','procedure'}: kind='fact'
            candidate=AtomicMemoryExtractor._candidate(kind,content,'jarvis:web:proposal'); added=self.queue.enqueue([candidate]); self._json(201,{'status':'queued','candidate_id':candidate.candidate_id,'added':added,'queue_status':'PENDING_REVIEW'}); return
        if path.startswith('/api/v1/proposals/') and path.endswith('/decision'):
            cid=path.split('/api/v1/proposals/',1)[1].rsplit('/decision',1)[0]; decision=str(data.get('decision','')).upper()
            if decision not in {'APPROVED','REJECTED'}: self._json(400,{'error':'decision must be APPROVED or REJECTED'}); return
            try: self.queue.mark(cid,decision,reviewer='jarvis-human'); self._json(200,{'status':decision,'candidate_id':cid})
            except KeyError as exc: self._json(404,{'error':str(exc)})
            return
        if path=='/api/v1/proposals/promote-approved':
            try: promoted=QueuePromoter(self.queue,self.controller,Principal.ADMIN).promote_approved(); self._json(200,{'status':'promoted','ids':promoted})
            except Exception as exc: self._json(400,{'error':str(exc)})
            return
        if path=='/api/v1/route':
            task=str(data.get('task','')).strip();
            if not task: self._json(400,{'error':'task is required'}); return
            self._json(200,{'task':task,'selected':_route_agents(self.vault_root,task),'routing':'domain-and-skill-match'}); return
        if path=='/api/v1/chat':
            message=str(data.get('message','')).strip(); model=str(data.get('model','')).strip(); history=data.get('history') or []
            if not message: self._json(400,{'error':'message is required'}); return
            models=_ollama_models(); chosen=model or os.getenv('JARVIS_MODEL') or (models[0] if models else '')
            if not chosen: self._json(503,{'error':'No Ollama model available','ollama':'offline'}); return
            ranked=_route_agents(self.vault_root,message); selected=ranked[0] if ranked else None; memory=self.storage.query(intent=message)[:8]
            context='\n\n'.join([f"[{n.get('id','memory')}] {n.get('content','')}" for n in memory])[:12000]; agent_name=selected.get('name') if selected else 'Agent Council'
            system=("You are JARVIS, a local AI command-center assistant for the AI Memory Vault. Speak naturally and technically. Never claim an action was executed unless confirmed. Distinguish Vault memory from inference. Route specialized work to the Agent Council. Current routed specialist: " + agent_name + ".\n\nCANONICAL VAULT CONTEXT:\n" + (context or '(no matching memory)'))
            messages=[{'role':'system','content':system}]+[m for m in history[-8:] if isinstance(m,dict) and m.get('role') in {'user','assistant'}]+[{'role':'user','content':message}]
            try:
                result=_ollama_post('/api/chat',{'model':chosen,'messages':messages,'stream':False},180); reply=((result.get('message') or {}).get('content') or '').strip(); self._json(200,{'reply':reply,'model':chosen,'agent':agent_name,'memory_hits':len(memory)})
            except urllib.error.URLError as exc: self._json(503,{'error':f'Ollama unavailable: {exc}','ollama':'offline'})
            except Exception as exc: self._json(500,{'error':str(exc)})
            return
        self._json(404,{'error':'Endpoint not found'})

def run_server(port=8000):
    httpd=HTTPServer(('127.0.0.1',port),BrowserMemoryAPIHandler); print(f'[BROWSER GATEWAY] Running REST API server at http://127.0.0.1:{port}...')
    try: httpd.serve_forever()
    except KeyboardInterrupt: httpd.server_close()
if __name__=='__main__': run_server(int(sys.argv[1]) if len(sys.argv)>1 else 8000)
