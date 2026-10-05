from __future__ import annotations

import json, shutil, subprocess, tempfile, uuid, urllib.request
from pathlib import Path
from typing import Protocol

from .models import DispatchResult, DispatchStatus, RouteDecision, RouteStatus, WorkPacket
from .registry import RouteRegistry

class DispatchError(RuntimeError): pass

class RuntimeAdapter(Protocol):
    def dispatch(self, packet: WorkPacket) -> DispatchResult: ...

class CommandAdapter:
    BINARIES = {"claude_code": "claude", "codex": "codex", "antigravity": "agy", "local_llm": "ollama"}

    def __init__(self, adapter_ref: str, artifact_root: str|Path|None=None, working_directory: str|Path|None=None):
        self.adapter_ref=adapter_ref
        self.artifact_root=Path(artifact_root or tempfile.gettempdir())/"ai-memory-vault-dispatch"
        self.working_directory=Path(working_directory or Path.cwd()).resolve()

    @property
    def binary(self) -> str:
        return self.BINARIES.get(self.adapter_ref, self.adapter_ref)

    def _brief(self,p:WorkPacket)->str:
        return "\n".join([
            f"ROUTED TASK ID: {p.task_id}",f"ROUTE ID: {p.route_id}",
            f"TARGET AGENT: {p.target_agent}",f"PROMPT PROFILE: {p.prompt_profile}",
            f"PROMPT PROFILE REF: {p.metadata.get('profile_ref', '')}","",
            "GOAL:",p.goal,"","ACCEPTANCE:",*("- "+x for x in p.acceptance_criteria),
            "","CONSTRAINTS:",*("- "+x for x in p.constraints),
            "","MEMORY REFERENCES:",*("- "+x for x in p.memory_refs),
            "","Return evidence, changes, failures and unknowns. Do not claim work was done unless it was executed."
        ])

    def dispatch(self,p:WorkPacket)->DispatchResult:
        if not self.working_directory.exists():
            return DispatchResult(p.task_id,p.route_id,DispatchStatus.FAILED,p.target_runtime,p.target_agent,None,"",error=f"working directory unavailable: {self.working_directory}")
        if shutil.which(self.binary) is None:
            return DispatchResult(p.task_id,p.route_id,DispatchStatus.FAILED,p.target_runtime,p.target_agent,None,"",error=f"{self.binary} unavailable")
        run=self.artifact_root/p.task_id; run.mkdir(parents=True,exist_ok=True)
        brief=run/"brief.txt"; brief.write_text(self._brief(p),encoding="utf-8")
        text=brief.read_text(encoding="utf-8")
        if self.adapter_ref=="codex":
            cmd=[self.binary,"exec","--json","-o",str(run/"final.txt"),"-"]; stdin=text
        elif self.adapter_ref=="claude_code":
            cmd=[self.binary,"-p","--output-format","text",text]; stdin=None
        elif self.adapter_ref=="antigravity":
            cmd=[self.binary,"--new-project","--print",f"--print-timeout={max(1,p.timeout_seconds//60)}m",f"--print={text}"]; stdin=None
        elif self.adapter_ref=="local_llm":
            cmd=[self.binary,"run","llama3.2"]; stdin=text
        else:
            raise DispatchError(f"unsupported command adapter: {self.adapter_ref}")
        try:
            proc=subprocess.run(cmd,input=stdin,text=True,capture_output=True,timeout=p.timeout_seconds,check=False,cwd=self.working_directory)
        except subprocess.TimeoutExpired:
            return DispatchResult(p.task_id,p.route_id,DispatchStatus.FAILED,p.target_runtime,p.target_agent,None,"",str(brief),error="timeout")
        final=(proc.stdout or proc.stderr or "").strip()
        status=DispatchStatus.COMPLETED if proc.returncode==0 else DispatchStatus.FAILED
        result=run/"result.json"
        result.write_text(json.dumps({"schema":"agent-dispatch.result.v1","task_id":p.task_id,"route_id":p.route_id,
            "status":status.value,"exit_code":proc.returncode,"final_message":final,"brief_path":str(brief)},ensure_ascii=False,indent=2),encoding="utf-8")
        return DispatchResult(p.task_id,p.route_id,status,p.target_runtime,p.target_agent,proc.returncode,final,str(result),metadata={"brief":str(brief)})

class A2AAdapter:
    def __init__(self, endpoint:str, timeout_seconds:int=3600):
        self.endpoint=endpoint.rstrip("/")
        self.timeout_seconds=timeout_seconds
    def dispatch(self,p:WorkPacket)->DispatchResult:
        payload={"jsonrpc":"2.0","id":p.task_id,"method":"message/send","params":{
            "message":{"role":"user","parts":[{"text":p.goal}]},
            "configuration":{"blocking":True,"acceptedOutputModes":["text"]},
            "metadata":{"route_id":p.route_id,"prompt_profile":p.prompt_profile}}}
        req=urllib.request.Request(self.endpoint,data=json.dumps(payload).encode(),headers={"Content-Type":"application/json"},method="POST")
        try:
            with urllib.request.urlopen(req,timeout=self.timeout_seconds) as r: data=json.loads(r.read().decode())
        except Exception as exc:
            return DispatchResult(p.task_id,p.route_id,DispatchStatus.FAILED,p.target_runtime,p.target_agent,None,"",error=str(exc))
        result=data.get("result",{})
        task=result.get("task",result)
        state=str(task.get("status",{}).get("state","completed")).lower()
        states={x.value:x for x in DispatchStatus}
        status=states.get(state,DispatchStatus.COMPLETED if "error" not in data else DispatchStatus.FAILED)
        msg=""
        st=task.get("status",{}) if isinstance(task,dict) else {}
        for part in (st.get("message") or {}).get("parts",[]) if isinstance(st,dict) else []:
            if isinstance(part,dict) and "text" in part: msg=str(part["text"])
        return DispatchResult(p.task_id,p.route_id,status,p.target_runtime,p.target_agent,0 if status==DispatchStatus.COMPLETED else None,msg,metadata={"transport":"a2a","response":data})

class AgentDispatcher:
    def __init__(self,registry:RouteRegistry,adapters:dict[str,RuntimeAdapter]|None=None):
        self.registry=registry; self.adapters=adapters or {}
    def make_packet(self,decision:RouteDecision,goal:str,source_agent:str,acceptance_criteria:tuple[str,...]=(),constraints:tuple[str,...]=(),memory_refs:tuple[str,...]=(),timeout_seconds:int=3600)->WorkPacket:
        if decision.status==RouteStatus.BLOCKED or not decision.primary: raise DispatchError("route is not dispatchable")
        agent=self.registry.agents[decision.primary.agent_id]
        return WorkPacket(f"task_{uuid.uuid4().hex[:12]}",decision.route_id,source_agent,agent.id,decision.primary.runtime_id,agent.prompt_profile,decision.primary.transport,goal,acceptance_criteria,constraints,memory_refs,timeout_seconds,
            metadata={"profile_ref":agent.profile_ref,"selected_skills":list(decision.primary.selected_skills),"route_status":decision.status.value})
    def dispatch(self,decision:RouteDecision,packet:WorkPacket)->DispatchResult:
        if decision.status==RouteStatus.BLOCKED: raise DispatchError("BLOCKED routes cannot be dispatched")
        rt=self.registry.runtimes[packet.target_runtime]; adapter=self.adapters.get(packet.target_runtime)
        if adapter is None:
            if rt.transport=="command": adapter=CommandAdapter(rt.adapter_ref)
            elif rt.transport=="a2a": adapter=A2AAdapter(rt.adapter_ref,packet.timeout_seconds)
            else: return DispatchResult(packet.task_id,packet.route_id,DispatchStatus.BLOCKED,packet.target_runtime,packet.target_agent,None,"",error="no adapter configured")
        return adapter.dispatch(packet)
