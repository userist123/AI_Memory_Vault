"""Validate benchmark-enriched traces; exits non-zero on any invalid trace."""
from __future__ import annotations
import json, sys
from pathlib import Path

REQUIRED = {'trace_id','experiment_id','task_id','run_id','condition','started_at','finished_at','provider','model','retrieval_enabled','actions','workspace','verification','final_success','git_commit'}

def validate(path: Path):
    t=json.loads(path.read_text(encoding='utf-8')); errors=[]
    missing=REQUIRED-set(t)
    if missing: errors.append('missing:'+','.join(sorted(missing)))
    if t.get('condition') not in {'CONTROL','TREATMENT','FULL_CONTEXT_ORACLE'}: errors.append('condition')
    mem=t.get('memory',{})
    if t.get('retrieval_enabled') is False and (mem.get('retrieval_count') != 0 or mem.get('memory_ids')): errors.append('control_contains_memory')
    if t.get('condition') != 'CONTROL' and t.get('retrieval_enabled') is not True: errors.append('non_control_disabled')
    if mem.get('retrieval_count') != len(mem.get('memory_ids',[])): errors.append('retrieval_count_mismatch')
    if not isinstance(mem.get('context_hash'),str) or len(mem.get('context_hash')) != 64: errors.append('context_hash_mismatch')
    v=t.get('verification',{})
    if not isinstance(v.get('command'),str) or not isinstance(v.get('stdout'),str) or not isinstance(v.get('stderr'),str): errors.append('verification_capture')
    if not isinstance(v.get('exit_code'),int): errors.append('exit_code')
    if v.get('status') not in {'passed','failed'}: errors.append('verification_status')
    expected=v.get('status')=='passed' and v.get('exit_code')==0 and t.get('model',{}).get('response_status')!='failed' and not any(not a.get('validated',False) for a in t.get('actions',[]))
    if bool(t.get('final_success')) != expected: errors.append('success_mismatch')
    if not isinstance(t.get('workspace',{}).get('files_created'),list): errors.append('workspace_evidence')
    return errors

def main(argv=None):
    root=Path(argv[0] if argv else '.')
    paths=sorted(root.glob('*.benchmark.json'))
    failures={str(p):validate(p) for p in paths}; failures={k:v for k,v in failures.items() if v}
    result={'traces_checked':len(paths),'trace_integrity_pass':len(paths)-len(failures),'trace_integrity_fail':len(failures),'failures':failures}
    print(json.dumps(result,indent=2))
    return 1 if failures or len(paths)==0 else 0
if __name__=='__main__': raise SystemExit(main(sys.argv[1:]))
