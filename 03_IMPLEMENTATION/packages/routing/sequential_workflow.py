from __future__ import annotations
import hashlib, json, os, re, tempfile, uuid
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any, Callable, Iterable, Mapping, Optional

class WorkflowError(RuntimeError): pass
@dataclass(frozen=True)
class StepSpec:
    step_id: str; objective: str; acceptance: tuple[str,...]=(); memory_refs: tuple[str,...]=(); evidence: tuple[str,...]=(); max_attempts: int=2
    def __post_init__(self):
        if not self.step_id or not self.objective.strip(): raise ValueError('step_id and objective are required')
        if self.max_attempts<1: raise ValueError('max_attempts must be positive')
@dataclass(frozen=True)
class AgentObservation:
    status: str; summary: str; evidence: tuple[str,...]=(); failures: tuple[str,...]=(); unknowns: tuple[str,...]=(); metadata: Mapping[str,Any]=field(default_factory=dict)
    def __post_init__(self):
        if self.status not in {'passed','failed','blocked','unknown'}: raise ValueError('invalid observation status')
def observation_from_text(text: str, *, required_terms: Iterable[str]=(), evidence: Iterable[str]=()) -> AgentObservation:
    raw=str(text or ''); norm=raw.casefold(); ev=tuple(str(x) for x in evidence)
    matches=list(re.finditer(r'^\s*(?:\*\*)?status(?:\*\*)?\s*:\s*([a-z_-]+)\s*$',norm,re.M))
    if len(matches)!=1 or len(re.findall(r'\bstatus\s*:', raw, re.I)) != 1:
        return AgentObservation('failed' if matches else 'unknown',raw,ev,failures=('exactly one STATUS line is required',) if matches else (),unknowns=() if matches else ('exactly one STATUS line is required',))
    if re.search(r'\bstatus\b.{0,20}\b(?:not|isn.t|does\s+not)\b',norm): return AgentObservation('failed',raw,ev,failures=('negated status contract',))
    sections={'status','result','evidence','changes','failures','unknowns'}
    seen={l.split(':',1)[0].strip().rstrip(':').casefold() for l in raw.splitlines() if ':' in l}
    if seen!=sections: return AgentObservation('unknown',raw,ev,unknowns=('structured execution contract is incomplete',))
    if re.search(r'(?:exit\s*code\s*[:=]?\s*[1-9]|\b\d+\s+failed\b)',norm): return AgentObservation('failed',raw,ev,failures=('verification reports failure',))
    token=matches[0].group(1)
    if token=='blocked': return AgentObservation('failed',raw,ev,failures=('blocked execution',))
    evidence_text=norm.split('evidence:',1)[-1].split('changes:',1)[0]; terms=tuple(str(x).casefold() for x in required_terms if str(x).strip())
    def present(term):
        if term=='sha-256': return bool(re.search(r'sha\s*[- ]?\s*256',evidence_text))
        if term=='63 bits': return bool(re.search(r'63\s*[- ]?\s*bits?',evidence_text))
        if term=='fail closed': return bool(re.search(r'(?<!not\s)fail(?:s|ed)?\s*[- ]?\s*closed',evidence_text))
        if term=='status exactly ok': return bool(re.search(r'status.{0,35}(?:exactly|is).{0,12}ok',evidence_text))
        return term in evidence_text and not re.search(r'(?:not|never|does\s+not).{0,15}'+re.escape(term),evidence_text)
    if token in {'pass','passed'} and all(present(t) for t in terms): return AgentObservation('passed',raw,ev)
    if token in {'fail','failed','failure','unsafe','incomplete'} or not all(present(t) for t in terms): return AgentObservation('failed',raw,ev,failures=('acceptance evidence missing',))
    return AgentObservation('unknown',raw,ev,unknowns=('unrecognized STATUS token',))
@dataclass
class WorkflowState:
    task_id: str; goal: str; current_index: int=0; attempts: dict[str,int]=field(default_factory=dict); outputs: dict[str,AgentObservation]=field(default_factory=dict); reviews: dict[str,AgentObservation]=field(default_factory=dict); history: dict[str,list[dict[str,Any]]]=field(default_factory=dict); status: str='pending'
    def to_dict(self,plan_sha256=''):
        return {'schema':'agent-workflow.state.v2','task_id':self.task_id,'goal_sha256':hashlib.sha256(self.goal.encode()).hexdigest(),'goal_chars':len(self.goal),'plan_sha256':plan_sha256,'current_index':self.current_index,'attempts':dict(self.attempts),'outputs':{k:asdict(v) for k,v in self.outputs.items()},'reviews':{k:asdict(v) for k,v in self.reviews.items()},'history':self.history,'status':self.status}
@dataclass(frozen=True)
class WorkflowResult:
    task_id: str; status: str; completed_steps: tuple[str,...]; failed_step: Optional[str]; state_path: Optional[str]; outputs: Mapping[str,AgentObservation]; reviews: Mapping[str,AgentObservation]
Executor=Callable[[StepSpec,str],AgentObservation]; Reviewer=Callable[[StepSpec,AgentObservation,str],AgentObservation]; Escalator=Callable[[StepSpec,AgentObservation,AgentObservation,str],Any]
class SequentialAgentWorkflow:
    def __init__(self,goal:str,steps:Iterable[StepSpec],*,task_id:Optional[str]=None,state_path:Optional[str|Path]=None,max_context_chars:int=12000,resume:bool=False):
        if not goal.strip(): raise ValueError('goal must not be empty')
        self.goal=goal.strip(); self.steps=tuple(steps)
        if not self.steps: raise ValueError('at least one step is required')
        if len({s.step_id for s in self.steps})!=len(self.steps): raise ValueError('step_id values must be unique')
        self.task_id=task_id or f'task_{uuid.uuid4().hex[:12]}'; self.state_path=Path(state_path) if state_path else None; self.max_context_chars=max(1000,int(max_context_chars)); self._plan_sha256=hashlib.sha256(json.dumps([asdict(s) for s in self.steps],sort_keys=True,separators=(',',':')).encode()).hexdigest(); self.state=WorkflowState(self.task_id,self.goal)
        if self.state_path and self.state_path.exists():
            if not resume: raise WorkflowError('checkpoint exists; pass resume=True explicitly')
            self._load_checkpoint()
    def _load_checkpoint(self):
        try:
            data=json.loads(self.state_path.read_text(encoding='utf-8'))
            if data.get('schema')!='agent-workflow.state.v2' or data.get('task_id')!=self.task_id or data.get('goal_sha256')!=hashlib.sha256(self.goal.encode()).hexdigest() or data.get('plan_sha256')!=self._plan_sha256: raise ValueError('checkpoint identity/plan mismatch')
            self.state.current_index=int(data['current_index']); self.state.attempts={str(k):int(v) for k,v in data.get('attempts',{}).items()}; self.state.status=str(data.get('status','interrupted')); self.state.history=dict(data.get('history',{}))
            def obs(value):
                if not isinstance(value,dict): raise ValueError('invalid observation')
                return AgentObservation(str(value['status']),str(value.get('summary','')),tuple(value.get('evidence',())),tuple(value.get('failures',())),tuple(value.get('unknowns',())),dict(value.get('metadata',{})))
            self.state.outputs={str(k):obs(v) for k,v in data.get('outputs',{}).items()}
            self.state.reviews={str(k):obs(v) for k,v in data.get('reviews',{}).items()}
        except (OSError,ValueError,TypeError,json.JSONDecodeError) as exc: raise WorkflowError(f'invalid checkpoint: {exc}') from exc
    def _context(self,index):
        step=self.steps[index]; mandatory={'goal':self.goal,'current_step':{'step_id':step.step_id,'objective':step.objective,'acceptance':list(step.acceptance),'memory_refs':list(step.memory_refs),'evidence':list(step.evidence)},'history':[]}; out=json.dumps(mandatory,ensure_ascii=False,separators=(',',':'))
        if len(out)>self.max_context_chars: raise WorkflowError('mandatory workflow context exceeds max_context_chars')
        for sid,items in self.state.history.items():
            for item in items:
                candidate=json.loads(out); candidate['history'].append({'step_id':sid,'attempt':item}); rendered=json.dumps(candidate,ensure_ascii=False,separators=(',',':'))
                if len(rendered)>self.max_context_chars: return out
                out=rendered
        return out
    def _save(self):
        if not self.state_path: return
        self.state_path.parent.mkdir(parents=True,exist_ok=True); fd,tmp=tempfile.mkstemp(prefix='.workflow_',dir=str(self.state_path.parent))
        try:
            with os.fdopen(fd,'w',encoding='utf-8') as h: json.dump(self.state.to_dict(self._plan_sha256),h,ensure_ascii=False,indent=2); h.flush(); os.fsync(h.fileno())
            os.replace(tmp,self.state_path)
        except Exception:
            try: os.unlink(tmp)
            except OSError: pass
            raise
    def run(self,executor:Executor,reviewer:Reviewer,*,escalator:Optional[Escalator]=None):
        completed=[s.step_id for s in self.steps[:self.state.current_index]]; self.state.status='running'; self._save()
        for index in range(self.state.current_index,len(self.steps)):
            step=self.steps[index]; accepted=False; last_output=None; last_review=None
            for attempt in range(self.state.attempts.get(step.step_id,0),step.max_attempts):
                self.state.attempts[step.step_id]=attempt+1
                try: context=self._context(index); output=executor(step,context); review=reviewer(step,output,context)
                except WorkflowError as exc: output=AgentObservation('blocked',f'workflow blocked: {exc}',failures=(type(exc).__name__,)); review=AgentObservation('blocked','review blocked',failures=(type(exc).__name__,)); self.state.status='blocked'
                except Exception as exc: output=AgentObservation('failed',f'exception: {exc}',failures=(type(exc).__name__,)); review=AgentObservation('failed','review interrupted',failures=(type(exc).__name__,)); self.state.status='interrupted'
                self.state.outputs[step.step_id]=output; self.state.reviews[step.step_id]=review; self.state.history.setdefault(step.step_id,[]).append({'attempt':attempt+1,'output':asdict(output),'review':asdict(review)}); self._save(); last_output,last_review=output,review
                if output.status=='passed' and review.status=='passed': accepted=True; break
            if not accepted and escalator and last_output and last_review:
                try: escalated=escalator(step,last_output,last_review,self._context(index))
                except Exception: escalated=None
                if isinstance(escalated,(tuple,list)) and len(escalated)==2 and all(isinstance(x,AgentObservation) for x in escalated):
                    new_output,new_review=escalated; self.state.history.setdefault(step.step_id,[]).append({'attempt':'escalation','output':asdict(new_output),'review':asdict(new_review)}); self.state.outputs[step.step_id]=new_output; self.state.reviews[step.step_id]=new_review; self._save(); accepted=new_output.status=='passed' and new_review.status=='passed'
            if not accepted:
                if self.state.status not in {'interrupted', 'blocked'}: self.state.status='failed'
                self.state.current_index=index; self._save(); return WorkflowResult(self.task_id,self.state.status,tuple(completed),step.step_id,str(self.state_path) if self.state_path else None,dict(self.state.outputs),dict(self.state.reviews))
            completed.append(step.step_id); self.state.current_index=index+1; self._save()
        self.state.status='completed'; self._save(); return WorkflowResult(self.task_id,'completed',tuple(completed),None,str(self.state_path) if self.state_path else None,dict(self.state.outputs),dict(self.state.reviews))
