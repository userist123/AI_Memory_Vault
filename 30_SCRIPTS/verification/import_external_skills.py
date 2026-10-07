from pathlib import Path
import json, hashlib, os, shutil, subprocess, sys
ROOT=Path(__file__).resolve().parents[2]
PINS=ROOT/'01_KNOWLEDGE/EXTERNAL_SKILLS/SOURCE_PINS.json'
STAGE=ROOT/'01_KNOWLEDGE/EXTERNAL_SKILLS/_sources'
TMP=Path(os.environ.get('EXTERNAL_SKILLS_TMP','/tmp/external-skills'))
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
#: Where the report of the files filtered out by design is written, next to the registry.
SKIPPED=ROOT/'01_KNOWLEDGE/EXTERNAL_SKILLS/SKIPPED_FILES.json'
#: The checkout's own version-control metadata, at the root of a cloned source. It is not part of the
#: source tree, so it is pruned; a hidden directory of this name anywhere deeper is rejected like any other.
VCS_METADATA={'.git'}
def copy_tree(src,dst,skipped=None):
 """Copy the importable files of `src` into `dst`, failing closed.

 Rejected, with every offending path listed in one error and nothing copied: symlinks, path traversal,
 hidden paths (other than the top-level version-control metadata), scripts and binaries (FORBIDDEN
 extensions) and any file with an executable bit. A file that is none of those but is not an ALLOWED
 type (an image, a font, LICENSE, ...) is not imported either, but it is never dropped silently: it is
 appended to `skipped` (relative paths) for the caller to report.
 """
 if src.is_symlink(): raise SystemExit('Rejected symlink: '+str(src))
 violations=[]; accepted=[]; filtered=[]
 for p in sorted(src.rglob('*')):
  rel=p.relative_to(src)
  if rel.parts and rel.parts[0] in VCS_METADATA: continue
  if p.is_symlink(): violations.append('symlink: '+str(rel)); continue
  if p.is_dir(): continue
  if any(x=='..' for x in rel.parts): violations.append('path traversal: '+str(rel)); continue
  if any(x.startswith('.') for x in rel.parts): violations.append('hidden path: '+str(rel)); continue
  suffix=p.suffix.lower()
  if suffix in FORBIDDEN: violations.append('script or binary ('+suffix+'): '+str(rel)); continue
  if p.stat().st_mode & 0o111: violations.append('executable bit: '+str(rel)); continue
  if suffix not in ALLOWED: filtered.append(str(rel).replace(os.sep,'/')); continue
  accepted.append((p,rel))
 if violations:
  raise SystemExit('Rejected import from '+str(src)+' (fail-closed), '+str(len(violations))+' offending path(s), nothing was copied:\n  '+'\n  '.join(violations))
 for p,rel in accepted:
  t=dst/rel; t.parent.mkdir(parents=True,exist_ok=True); shutil.copyfile(p,t)
 if skipped is not None: skipped.extend(filtered)
def write_skipped_report(skipped):
 """Record, and print, every file the filter left out because it is not an allowed type."""
 SKIPPED.parent.mkdir(parents=True,exist_ok=True)
 SKIPPED.write_text(json.dumps({'schema_version':'1.0','reason':'not an allowed type ('+', '.join(sorted(ALLOWED))+'); scripts, binaries, hidden paths, symlinks and executables abort the import instead','skipped':skipped},indent=2,sort_keys=True)+'\n')
 print('Filtered out '+str(sum(len(v) for v in skipped.values()))+' file(s) of a type that is not imported; listed in '+str(SKIPPED))
 for name in sorted(skipped):
  for rel in skipped[name]: print('  skipped '+name+'/'+rel)
def filter_imports():
 if STAGE.exists(): shutil.rmtree(STAGE)
 STAGE.mkdir(parents=True)
 skipped={}
 mappings=[('github-awesome-copilot','skills','github-awesome-copilot'),('web-quality-skills','skills','web-quality-skills'),('ui-sensei','.','ui-sensei'),('garden-skills','skills/web-design-engineer','garden-web-design-engineer'),('xiaopu-web-design','SKILL.md','xiaopu-web-design-SKILL.md'),('xiaopu-web-design','references','xiaopu-web-design-references'),('awesome-design-skills','skills','awesome-design-skills')]
 for sid,rel,name in mappings:
  root=TMP/sid
  if not any((root/n).is_file() for n in LICENSE_NAMES): raise SystemExit('License missing for '+sid)
  src=root/rel; dst=STAGE/name
  if not src.exists(): raise SystemExit('Missing expected source path: '+sid+'/'+rel)
  if src.is_dir():
   filtered=[]; copy_tree(src,dst,filtered)
   if filtered: skipped[name]=sorted(filtered)
  else:
   if src.is_symlink() or src.name.startswith('.') or src.suffix.lower() not in ALLOWED or src.stat().st_mode & 0o111: raise SystemExit('Rejected file: '+str(src))
   dst.parent.mkdir(parents=True,exist_ok=True); shutil.copyfile(src,dst)
 write_skipped_report(skipped)
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
