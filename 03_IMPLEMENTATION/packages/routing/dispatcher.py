from __future__ import annotations

import json, os, shutil, subprocess, tempfile, uuid, urllib.request
from pathlib import Path
from typing import Protocol

from .models import SAFE_ID_RE, DispatchResult, DispatchStatus, RouteDecision, RouteStatus, WorkPacket
from .registry import RouteRegistry

class DispatchError(RuntimeError): pass


def default_artifact_root() -> Path:
    """Per-user, owner-only directory for dispatch receipts (never the shared temp directory).

    Briefs and receipts hold the full task goal; %TEMP% / /tmp are readable by other local
    processes and are swept unpredictably. AI_MEMORY_VAULT_HOME overrides (tests).
    """
    override=os.environ.get("AI_MEMORY_VAULT_HOME")
    if override:
        base=Path(override)
    elif os.name=="nt":
        base=Path(os.environ.get("LOCALAPPDATA") or Path.home()/"AppData"/"Local")/"ai-memory-vault"
    else:
        base=Path(os.environ.get("XDG_STATE_HOME") or Path.home()/".local"/"state")/"ai-memory-vault"
    root=base/"dispatch"
    root.mkdir(parents=True,exist_ok=True)
    if os.name!="nt":
        os.chmod(root,0o700)
    return root


def _digest(text: str) -> str:
    import hashlib
    return "sha256:"+hashlib.sha256(text.encode("utf-8")).hexdigest()


def _receipt_packet(packet: WorkPacket) -> dict:
    """The packet as a receipt may hold it: identity and shape, with the goal as a digest."""
    fields=dict(packet.__dict__)
    goal=fields.pop("goal","")
    fields["goal_sha256"]=_digest(goal)
    fields["goal_chars"]=len(goal)
    return fields


def _run_dir(root: Path, task_id: str) -> Path:
    """root/task_id, refusing anything that is not a plain identifier inside root."""
    if not SAFE_ID_RE.fullmatch(task_id or ""):
        raise DispatchError("unsafe task id")
    base=root.resolve()
    run=(base/task_id).resolve()
    if not run.is_relative_to(base):
        raise DispatchError("dispatch path escapes its root")
    run.mkdir(parents=True,exist_ok=True)
    return run

class RuntimeAdapter(Protocol):
    def dispatch(self, packet: WorkPacket) -> DispatchResult: ...

class CommandAdapter:
    BINARIES = {"claude_code": "claude", "codex": "codex", "antigravity": "agy", "local_llm": "ollama"}

    def __init__(self, runtime_id: str, adapter_ref: str|None=None, model: str|None=None, artifact_root: str|Path|None=None, working_directory: str|Path|None=None):
        self.runtime_id=runtime_id
        self.adapter_ref=adapter_ref
        self.model=model
        self.artifact_root=Path(artifact_root)/"ai-memory-vault-dispatch" if artifact_root else default_artifact_root()
        self.working_directory=Path(working_directory or Path.cwd()).resolve()

    @property
    def binary(self) -> str|None:
        if self.runtime_id not in self.BINARIES:
            return None
        return self.adapter_ref or self.BINARIES[self.runtime_id]

    @staticmethod
    def _brief(p:WorkPacket)->str:
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
        if self.binary is None:
            return DispatchResult(p.task_id,p.route_id,DispatchStatus.FAILED,p.target_runtime,p.target_agent,None,"",error=f"unknown command adapter: {self.runtime_id}")
        if shutil.which(self.binary) is None:
            return DispatchResult(p.task_id,p.route_id,DispatchStatus.FAILED,p.target_runtime,p.target_agent,None,"",error=f"{self.binary} unavailable")
        run=_run_dir(self.artifact_root,p.task_id)
        # The brief holds the goal: it goes to the runtime on stdin and is never persisted.
        # Receipts keep only its digest (protocol: sensitive goal text is not copied into
        # durable receipts).
        text=self._brief(p)
        brief=_digest(text)
        if self.runtime_id=="codex":
            cmd=[self.binary,"exec","--json","-o",str(run/"final.txt"),"-"]; stdin=text
        elif self.runtime_id=="claude_code":
            cmd=[self.binary,"-p","--output-format","text"]; stdin=text
        elif self.runtime_id=="antigravity":
            cmd=[self.binary,"--input-format","stream-json","--output-format","stream-json"]
            stdin=json.dumps({"event":"user","message":{"content":text}},ensure_ascii=False)+"\n"
        elif self.runtime_id=="local_llm":
            if not self.model:
                return DispatchResult(p.task_id,p.route_id,DispatchStatus.FAILED,p.target_runtime,p.target_agent,None,"",error="local_llm model is not configured")
            cmd=[self.binary,"run",self.model]; stdin=text
        else:
            return DispatchResult(p.task_id,p.route_id,DispatchStatus.FAILED,p.target_runtime,p.target_agent,None,"",error=f"unsupported command adapter: {self.runtime_id}")
        result=run/"result.json"
        try:
            proc=subprocess.run(cmd,input=stdin,text=True,capture_output=True,timeout=p.timeout_seconds,check=False,cwd=self.working_directory)
        except subprocess.TimeoutExpired:
            result.write_text(json.dumps({"schema":"agent-dispatch.result.v1","task_id":p.task_id,"route_id":p.route_id,
                "status":DispatchStatus.FAILED.value,"exit_code":None,"final_message":"","brief_sha256":brief,"error":"timeout"},
                ensure_ascii=False,indent=2),encoding="utf-8")
            return DispatchResult(p.task_id,p.route_id,DispatchStatus.FAILED,p.target_runtime,p.target_agent,None,"",str(result),error="timeout")
        except OSError as exc:
            result.write_text(json.dumps({"schema":"agent-dispatch.result.v1","task_id":p.task_id,"route_id":p.route_id,
                "status":DispatchStatus.FAILED.value,"exit_code":None,"final_message":"","brief_sha256":brief,"error":str(exc)},
                ensure_ascii=False,indent=2),encoding="utf-8")
            return DispatchResult(p.task_id,p.route_id,DispatchStatus.FAILED,p.target_runtime,p.target_agent,None,"",str(result),error=str(exc))
        raw_output=(proc.stdout or proc.stderr or "").strip()
        if self.runtime_id=="antigravity" and proc.stdout:
            try:
                events=[json.loads(line) for line in proc.stdout.splitlines() if line.strip()]
                result_event=next(event for event in reversed(events) if event.get("event")=="result")
                final=str(result_event.get("result",{}).get("response","")).strip()
            except (ValueError, StopIteration, TypeError, AttributeError):
                return DispatchResult(p.task_id,p.route_id,DispatchStatus.FAILED,p.target_runtime,p.target_agent,proc.returncode,"",
                                       error="invalid antigravity stream-json response")
        else:
            final=raw_output
        status=DispatchStatus.COMPLETED if proc.returncode==0 else DispatchStatus.FAILED
        result.write_text(json.dumps({"schema":"agent-dispatch.result.v1","task_id":p.task_id,"route_id":p.route_id,
            "status":status.value,"exit_code":proc.returncode,"final_message":final,"brief_sha256":brief},ensure_ascii=False,indent=2),encoding="utf-8")
        return DispatchResult(p.task_id,p.route_id,status,p.target_runtime,p.target_agent,proc.returncode,final,str(result),metadata={"brief_sha256":brief})

class A2AAdapter:
    def __init__(self, endpoint:str, timeout_seconds:int=3600, protocol_version:str="1.0"):
        self.endpoint=endpoint.rstrip("/")
        self.timeout_seconds=timeout_seconds
        self.protocol_version=protocol_version

    @staticmethod
    def _state(value:str) -> DispatchStatus:
        normalized=value.strip().lower().replace("task_state_", "")
        aliases={"submitted":DispatchStatus.SUBMITTED,"working":DispatchStatus.WORKING,"input_required":DispatchStatus.INPUT_REQUIRED,
                 "auth_required":DispatchStatus.AUTH_REQUIRED,"completed":DispatchStatus.COMPLETED,"failed":DispatchStatus.FAILED,
                 "canceled":DispatchStatus.CANCELED,"rejected":DispatchStatus.FAILED}
        return aliases.get(normalized, DispatchStatus.FAILED)

    def dispatch(self,p:WorkPacket)->DispatchResult:
        legacy=self.protocol_version.startswith("0.")
        method="message/send" if legacy else "SendMessage"
        role="user" if legacy else "ROLE_USER"
        # The remote agent must receive the same contract as a local one: acceptance criteria,
        # constraints and memory references travel both as the text brief and as structured data.
        brief=CommandAdapter._brief(p)
        payload={"jsonrpc":"2.0","id":p.task_id,"method":method,"params":{
            "message":{"role":role,"parts":[{"text":brief}],"messageId":p.task_id},
            "configuration":{"acceptedOutputModes":["text/plain"],"returnImmediately":False},
            "metadata":{"route_id":p.route_id,"prompt_profile":p.prompt_profile,
                        "profile_ref":p.metadata.get("profile_ref",""),
                        "goal":p.goal,
                        "acceptance_criteria":list(p.acceptance_criteria),
                        "constraints":list(p.constraints),
                        "memory_refs":list(p.memory_refs),
                        "timeout_seconds":p.timeout_seconds}}}
        headers={"Content-Type":"application/json","A2A-Version":self.protocol_version}
        req=urllib.request.Request(self.endpoint,data=json.dumps(payload).encode(),headers=headers,method="POST")
        try:
            with urllib.request.urlopen(req,timeout=self.timeout_seconds) as r:
                data=json.loads(r.read().decode())
        except Exception as exc:
            return DispatchResult(p.task_id,p.route_id,DispatchStatus.FAILED,p.target_runtime,p.target_agent,None,"",
                                   error=str(exc),metadata={"transport":"a2a","protocol_version":self.protocol_version})
        if data.get("error"):
            return DispatchResult(p.task_id,p.route_id,DispatchStatus.FAILED,p.target_runtime,p.target_agent,None,"",
                                   error=json.dumps(data["error"],ensure_ascii=False),
                                   metadata={"transport":"a2a","protocol_version":self.protocol_version,"response":data})
        result=data.get("result",{})
        task=result.get("task") if isinstance(result,dict) else None
        message=result.get("message") if isinstance(result,dict) else None
        if task is None and message is not None:
            parts=message.get("parts",[]) if isinstance(message,dict) else []
            msg="\n".join(str(part["text"]) for part in parts if isinstance(part,dict) and "text" in part)
            return DispatchResult(p.task_id,p.route_id,DispatchStatus.COMPLETED,p.target_runtime,p.target_agent,0,msg,
                                   metadata={"transport":"a2a","protocol_version":self.protocol_version,"response":data})
        if task is None:
            return DispatchResult(p.task_id,p.route_id,DispatchStatus.FAILED,p.target_runtime,p.target_agent,None,"",
                                   error="A2A response contains neither task nor message",
                                   metadata={"transport":"a2a","protocol_version":self.protocol_version,"response":data})
        status_obj=task.get("status",{}) if isinstance(task,dict) else {}
        status=self._state(str(status_obj.get("state","failed")))
        msg_parts=(status_obj.get("message") or {}).get("parts",[]) if isinstance(status_obj,dict) else []
        msg="\n".join(str(part["text"]) for part in msg_parts if isinstance(part,dict) and "text" in part)
        return DispatchResult(p.task_id,p.route_id,status,p.target_runtime,p.target_agent,
                               0 if status==DispatchStatus.COMPLETED else None,msg,
                               metadata={"transport":"a2a","protocol_version":self.protocol_version,
                                         "remote_task_id":task.get("id"),"response":data})

class AgentDispatcher:
    def __init__(self,registry:RouteRegistry,adapters:dict[str,RuntimeAdapter]|None=None,artifact_root:str|Path|None=None):
        self.registry=registry; self.adapters=adapters or {}
        self.artifact_root=Path(artifact_root)/"ai-memory-vault-dispatch" if artifact_root else default_artifact_root()
    def make_packet(self,decision:RouteDecision,goal:str,source_agent:str,acceptance_criteria:tuple[str,...]=(),constraints:tuple[str,...]=(),memory_refs:tuple[str,...]=(),timeout_seconds:int=3600)->WorkPacket:
        if decision.status==RouteStatus.BLOCKED or not decision.primary: raise DispatchError("route is not dispatchable")
        agent=self.registry.agents[decision.primary.agent_id]
        return WorkPacket(f"task_{uuid.uuid4().hex[:12]}",decision.route_id,source_agent,agent.id,decision.primary.runtime_id,agent.prompt_profile,decision.primary.transport,goal,acceptance_criteria,constraints,memory_refs,timeout_seconds,
            metadata={"profile_ref":agent.profile_ref,"selected_skills":list(decision.primary.selected_skills),"route_status":decision.status.value})
    def dispatch(self,decision:RouteDecision,packet:WorkPacket)->DispatchResult:
        if decision.status != RouteStatus.ROUTED or decision.primary is None:
            return DispatchResult(packet.task_id,packet.route_id,DispatchStatus.BLOCKED,
                                   packet.target_runtime,packet.target_agent,None,"",
                                   error=f"route status {decision.status.value} is not executable")
        primary=decision.primary
        if packet.route_id != decision.route_id:
            return DispatchResult(packet.task_id,packet.route_id,DispatchStatus.BLOCKED,
                                   packet.target_runtime,packet.target_agent,None,"",
                                   error="packet route_id does not match decision")
        if packet.target_runtime != primary.runtime_id or packet.target_agent != primary.agent_id:
            return DispatchResult(packet.task_id,packet.route_id,DispatchStatus.BLOCKED,
                                   packet.target_runtime,packet.target_agent,None,"",
                                   error="packet target does not match decision primary")
        rt=self.registry.runtimes[packet.target_runtime]; adapter=self.adapters.get(packet.target_runtime)
        run=_run_dir(self.artifact_root,packet.task_id)
        (run/"route.json").write_text(json.dumps({
            "schema":"agent-route.receipt.v1",
            "decision":decision.to_dict(),
            "packet":_receipt_packet(packet)
        },ensure_ascii=False,indent=2,default=str),encoding="utf-8")
        if adapter is None:
            if rt.transport=="command": adapter=CommandAdapter(packet.target_runtime, rt.adapter_ref, rt.model)
            elif rt.transport=="a2a": adapter=A2AAdapter(rt.adapter_ref,packet.timeout_seconds)
            else: return DispatchResult(packet.task_id,packet.route_id,DispatchStatus.BLOCKED,packet.target_runtime,packet.target_agent,None,"",error="no adapter configured")
        result=adapter.dispatch(packet)
        if decision.verifier is not None and result.status == DispatchStatus.COMPLETED:
            return DispatchResult(packet.task_id,packet.route_id,DispatchStatus.PENDING_VERIFICATION,
                                   packet.target_runtime,packet.target_agent,result.exit_code,result.final_message,
                                   result.result_path,result.session_id,result.error,
                                   metadata={**dict(result.metadata), "verifier_agent":decision.verifier.agent_id,
                                             "verifier_runtime":decision.verifier.runtime_id,
                                             "verification_reason":decision.verifier.reason})
        return result
