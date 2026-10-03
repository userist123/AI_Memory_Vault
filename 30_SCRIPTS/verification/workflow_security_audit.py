from __future__ import annotations
import re,sys
from pathlib import Path
import yaml
ROOT=Path(__file__).resolve().parents[2]
SHA_RE=re.compile(r'^[0-9a-fA-F]{40}$')
ACTION_RE=re.compile(r'uses:\s*([^\s#]+)')
SECRET_RE=re.compile(r'\bsecrets\.([A-Za-z_][A-Za-z0-9_]*)')

def _on(data): return data.get('on',data.get(True,{})) or {}
def _jobs(data): return data.get('jobs',{}) or {}
def _step_values(obj):
 if isinstance(obj,dict):
  for k,v in obj.items():
   yield k,v
   yield from _step_values(v)
 elif isinstance(obj,list):
  for x in obj: yield from _step_values(x)

def audit_file(path):
 data=yaml.safe_load(path.read_text(encoding='utf-8')) or {}
 findings=[]
 if 'permissions' not in data and True not in data:
  jobs=_jobs(data)
  if not jobs or any('permissions' not in (job or {}) for job in jobs.values() if isinstance(job,dict)):
   findings.append('missing permissions at workflow or every job')
 for line in path.read_text(encoding='utf-8').splitlines():
  m=ACTION_RE.search(line)
  if m:
   ref=m.group(1).split('@',1)[1] if '@' in m.group(1) else ''
   if not SHA_RE.fullmatch(ref): findings.append('unpinned action: '+m.group(1))
 on=_on(data)
 if 'pull_request_target' in on:
  text=path.read_text(encoding='utf-8')
  if 'github.event.pull_request.head.sha' in text or 'github.event.pull_request.head.ref' in text:
   findings.append('pull_request_target checks out PR head code')
 if 'pull_request' in on:
  for job in _jobs(data).values():
   if not isinstance(job,dict): continue
   for step in job.get('steps',[]) or []:
    if not isinstance(step,dict): continue
    condition=str(step.get('if',''))
    if 'github.event_name !=' in condition and 'pull_request' in condition: continue
    for value in step.values():
     if isinstance(value,str):
      for secret in SECRET_RE.findall(value):
       if secret!='GITHUB_TOKEN': findings.append('secret transmitted on pull_request: '+secret)
 for job_name,job in _jobs(data).items():
  if not isinstance(job,dict): continue
  commands=[]
  for step in job.get('steps',[]) or []:
   if isinstance(step,dict) and isinstance(step.get('run'),str): commands.append(step['run'])
  for command in commands:
   if re.search(r'(^|[;&|])\s*git\s+push\b',command):
    approved=('workflow_dispatch' in on and 'skills-import/' in command and 'github.event_name' in command)
    if not approved: findings.append('git push without explicit workflow_dispatch isolated-branch approval')
 jobs=_jobs(data)
 if jobs and any(isinstance(j,dict) and 'timeout-minutes' not in j for j in jobs.values()): findings.append('job missing timeout-minutes')
 return findings

def audit_all(root=ROOT/'.github/workflows'):
 failures={}
 for path in sorted(root.glob('*.y*ml')):
  f=audit_file(path)
  if f: failures[str(path.relative_to(ROOT))]=f
 return failures
if __name__=='__main__':
 failures=audit_all()
 for p,items in failures.items():
  for item in items: print(f'FAIL {p}: {item}')
 raise SystemExit(1 if failures else 0)
