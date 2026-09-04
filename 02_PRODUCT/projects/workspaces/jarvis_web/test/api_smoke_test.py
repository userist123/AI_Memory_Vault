"""JARVIS / Memory Vault API smoke test; stdlib only."""
from __future__ import annotations
import json, os, subprocess, sys, time
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import Request, urlopen
ROOT=Path(__file__).resolve().parents[3]; PORT='8001'; BASE=f'http://127.0.0.1:{PORT}/api/v1'

def get(path):
    with urlopen(f'{BASE}{path}',timeout=5) as r:return r.status,json.loads(r.read().decode('utf-8'))

def post(path,payload,timeout=10):
    req=Request(f'{BASE}{path}',data=json.dumps(payload).encode('utf-8'),headers={'Content-Type':'application/json'},method='POST')
    try:
        with urlopen(req,timeout=timeout) as r:return r.status,json.loads(r.read().decode('utf-8'))
    except HTTPError as e:return e.code,json.loads(e.read().decode('utf-8'))

def main()->int:
    env=dict(os.environ);env['AI_MEMORY_VAULT_ROOT']=str(ROOT)
    proc=subprocess.Popen([sys.executable,'-m','memory_controller.api_server',PORT],cwd=ROOT,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
    try:
        ready=False
        for _ in range(50):
            try:
                status,data=get('/status')
                if status==200 and data.get('status')=='online': ready=True;break
            except Exception: time.sleep(.1)
        if not ready: print('FAIL: API did not start');return 1
        checks=[]
        status,data=get('/metrics');checks.append((status==200 and data.get('engine')=='V6','metrics'))
        status,data=get('/agents');checks.append((status==200 and len(data.get('agents',[]))>=21,'agents'))
        status,data=get('/skills');checks.append((status==200 and data.get('total',0)>=1,'skills'))
        status,data=get('/proposals');checks.append((status==200 and 'pending' in data,'proposals'))
        status,data=get('/models');checks.append((status==200 and 'models' in data,'ollama-model-discovery'))
        status,data=post('/route',{'task':'redesign JARVIS web UI with accessibility and performance'});checks.append((status==200 and len(data.get('selected',[]))>=1,'agent-routing'))
        status,data=post('/chat',{'message':'Say READY in one word.'},timeout=15)
        chat_ok=(status==200 and isinstance(data.get('reply'),str) and data.get('reply')) or (status==503 and data.get('ollama')=='offline')
        checks.append((chat_ok,'jarvis-chat-contract'))
        failed=[name for ok,name in checks if not ok]
        for ok,name in checks: print(('PASS' if ok else 'FAIL')+': '+name)
        return 1 if failed else 0
    finally:
        proc.terminate()
        try:proc.wait(timeout=2)
        except subprocess.TimeoutExpired:proc.kill()
if __name__=='__main__':raise SystemExit(main())
