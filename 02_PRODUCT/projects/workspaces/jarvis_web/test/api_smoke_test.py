"""JARVIS / Memory Vault API smoke test; stdlib only."""
from __future__ import annotations
import json, os, subprocess, sys, time
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import Request, urlopen
ROOT=Path(__file__).resolve().parents[5]; PORT='8001'; BASE=f'http://127.0.0.1:{PORT}/api/v1'

def get(path,token=''):
    headers={'Authorization':f'Bearer {token}'} if token else {}
    req=Request(f'{BASE}{path}',headers=headers)
    with urlopen(req,timeout=5) as r:return r.status,json.loads(r.read().decode('utf-8'))

def post(path,payload,token='',timeout=10):
    headers={'Content-Type':'application/json'}
    if token: headers['Authorization']=f'Bearer {token}'
    req=Request(f'{BASE}{path}',data=json.dumps(payload).encode('utf-8'),headers=headers,method='POST')
    try:
        with urlopen(req,timeout=timeout) as r:return r.status,json.loads(r.read().decode('utf-8'))
    except HTTPError as e:return e.code,json.loads(e.read().decode('utf-8'))
    except Exception as e:return 503,{'ollama':'offline','error':str(e)}

def main()->int:
    token=os.environ.get('AI_MEMORY_VAULT_API_TOKEN','jarvis-smoke-test-token')
    env=dict(os.environ);env['AI_MEMORY_VAULT_ROOT']=str(ROOT)
    env['AI_MEMORY_VAULT_API_TOKEN']=token
    proc=subprocess.Popen([sys.executable,'-m','interfaces.api_server',PORT],cwd=ROOT/'03_IMPLEMENTATION'/'packages',env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
    try:
        ready=False
        for _ in range(50):
            try:
                status,data=get('/status',token=token)
                if status==200 and data.get('status')=='online': ready=True;break
            except Exception: time.sleep(.1)
        if not ready:
            stderr=proc.stderr.read().strip() if proc.stderr else ''
            if stderr: print('API STDERR:\n'+stderr)
            print('FAIL: API did not start');return 1
        checks=[]
        status,data=get('/metrics',token=token);checks.append((status==200 and data.get('engine')=='V6','metrics'))
        status,data=get('/agents',token=token);checks.append((status==200 and len(data.get('agents',[]))>=21,'agents'))
        status,data=get('/skills',token=token);checks.append((status==200 and data.get('total',0)>=1,'skills'))
        status,data=get('/proposals',token=token);checks.append((status==200 and 'pending' in data,'proposals'))
        status,data=get('/models',token=token);checks.append((status==200 and 'models' in data,'ollama-model-discovery'))
        status,data=post('/route',{'task':'redesign JARVIS web UI with accessibility and performance'},token=token);checks.append((status==200 and len(data.get('selected',[]))>=1,'agent-routing'))
        status,data=post('/chat',{'message':'Say READY in one word.'},token=token,timeout=15)
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
