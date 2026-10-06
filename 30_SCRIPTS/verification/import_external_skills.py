from pathlib import Path
import json, hashlib, os, shutil, subprocess, sys
ROOT=Path(__file__).resolve().parents[2]
PINS=ROOT/'01_KNOWLEDGE/EXTERNAL_SKILLS/SOURCE_PINS.json'
STAGE=ROOT/'01_KNOWLEDGE/EXTERNAL_SKILLS/_sources'
TMP=Path('/tmp/external-skills')
ALLOWED={'.md','.json','.txt','.yaml'}
FORBIDDEN={'.sh','.ps1','.py','.js','.bat','.cmd','.exe','.dll','.so','.bin','.com'}
LICENSE_NAMES={'LICENSE','LICENSE.md','LICENSE.txt','COPYING','COPYING.md','COPYING.txt'}
REG=ROOT/'01_KNOWLEDGE/EXTERNAL_SKILLS/SKILL_REGISTRY.json'
def run(*args,cwd=None): return subprocess.run(args,check=True,text=True,capture_output=True,cwd=cwd).stdout.strip()
def pins():
 data=json.loads(PINS.read_text())
 if len(data.get('sources',[]))!=6: raise SystemExit('SOURCE_PINS.json must contain exactly 6 sources')
 for s in data['sources']:
  if len(s.get('ref',''))!=40: raise SystemExit('invalid source SHA')
 return data['sources']
def clone():
 TMP.mkdir(parents=True,exist_ok=True)
 for s in pins():
  d=TMP/s['id']
  if d.exists(): shutil.rmtree(d)
  run('git','clone','--no-tags',s['url'],str(d)); run('git','checkout','--detach',s['ref'],cwd=d)
  if run('git','rev-parse','HEAD',cwd=d)!=s['ref']: raise SystemExit('source pin mismatch: '+s['id'])
def copy_tree(src,dst):
 if src.is_symlink(): raise SystemExit('Rejected symlink: '+str(src))
 for p in src.rglob('*'):
  if p.is_symlink(): raise SystemExit('Rejected symlink: '+str(p))
  if p.is_dir(): continue
  rel=p.relative_to(src)
  if any(x=='..' for x in rel.parts): raise SystemExit('Rejected path traversal: '+str(rel))
  if any(x.startswith('.') for x in rel.parts): raise SystemExit('Rejected hidden path: '+str(rel))
  if p.suffix.lower() not in ALLOWED or p.suffix.lower() in FORBIDDEN: raise SystemExit('Rejected import file: '+str(rel))
  t=dst/rel; t.parent.mkdir(parents=True,exist_ok=True); shutil.copyfile(p,t)
def filter_imports():
 if STAGE.exists(): shutil.rmtree(STAGE)
 STAGE.mkdir(parents=True)
 mappings=[('github-awesome-copilot','skills','github-awesome-copilot'),('web-quality-skills','skills','web-quality-skills'),('ui-sensei','.','ui-sensei'),('garden-skills','skills/web-design-engineer','garden-web-design-engineer'),('xiaopu-web-design','SKILL.md','xiaopu-web-design-SKILL.md'),('xiaopu-web-design','references','xiaopu-web-design-references'),('awesome-design-skills','skills','awesome-design-skills')]
 for sid,rel,name in mappings:
  root=TMP/sid
  if not any((root/n).is_file() for n in LICENSE_NAMES): raise SystemExit('License missing for '+sid)
  src=root/rel; dst=STAGE/name
  if not src.exists(): raise SystemExit('Missing expected source path: '+sid+'/'+rel)
  if src.is_dir(): copy_tree(src,dst)
  else:
   if src.is_symlink() or src.name.startswith('.') or src.suffix.lower() not in ALLOWED: raise SystemExit('Rejected file: '+str(src))
   dst.parent.mkdir(parents=True,exist_ok=True); shutil.copyfile(src,dst)
def scan():
 sys.path.insert(0,str(ROOT)); from security.skill_exfiltration_scanner import scan_text
 for p in STAGE.rglob('*'):
  if p.is_file() and scan_text(p,p.read_text(encoding='utf-8')).verdict!='SAFE': raise SystemExit('Imported content rejected: '+str(p))
def registry():
 entries=[{'path':str(p.relative_to(ROOT)).replace(os.sep,'/'),'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'status':'raw'} for p in sorted(STAGE.rglob('*')) if p.is_file()]
 REG.parent.mkdir(parents=True,exist_ok=True); REG.write_text(json.dumps({'schema_version':'1.0','skills':entries},indent=2)+'\n')
 (REG.parent/'IMPORT_PR_DESCRIPTION.md').write_text('External skill import evidence\n\nSource pins are resolved from GitHub commit history and checked out by exact immutable SHA. Imported material is filtered to .md/.json/.txt/.yaml; scripts, binaries, hidden files, symlinks and traversal paths are rejected. License presence is required for every source repository. The import uses an isolated skills-import/<run_id> branch and does not push directly to main. Imported material is scanned fail-closed before commit. Registry entries contain SHA-256 and remain raw pending human approval. Repository settings, branch protection, required-review configuration and secrets were not verified by this workflow and require manual confirmation.\n')
if __name__=='__main__':
 if len(sys.argv)!=2 or sys.argv[1] not in {'clone','filter','scan','registry'}: raise SystemExit('usage: clone|filter|scan|registry')
 globals()[sys.argv[1]]()
