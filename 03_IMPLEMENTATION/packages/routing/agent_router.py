from __future__ import annotations

import hashlib, re, uuid
from pathlib import Path
from typing import Any

from .models import *
from .registry import RouteRegistry, RegistryError

class RoutingError(ValueError): pass

class AgentRouter:
    def __init__(self, registry: RouteRegistry, feedback_store=None):
        self.registry=registry
        self.feedback_store=feedback_store

    @staticmethod
    def _fingerprint(text:str)->str:
        return hashlib.sha256(text.encode("utf-8")).hexdigest()

    def normalize(self, request: TaskRequest) -> dict[str,Any]:
        if not request.goal.strip(): raise RoutingError("goal must not be empty")
        caps=set(request.capabilities)
        low=request.goal.casefold()
        for cap, words in self.registry.signals.capabilities.items():
            if any(re.search(rf"(?<!\w){re.escape(w.casefold())}(?!\w)",low) for w in words):
                caps.add(cap)
        risk=request.risk
        for level, words in self.registry.signals.risk_keywords.items():
            if any(w.casefold() in low for w in words):
                risk=max(risk,RiskLevel[level.upper()])
        return {"goal":request.goal,"capabilities":caps,"risk":risk}

    def _eligible_runtime(self, rt:RuntimeDescriptor, req:TaskRequest, rejected:list[str])->bool:
        if not rt.enabled: rejected.append(f"runtime:{rt.id}:disabled"); return False
        if req.requested_runtime and rt.id != req.requested_runtime: rejected.append(f"runtime:{rt.id}:not_requested"); return False
        if req.privacy in {PrivacyMode.LOCAL_ONLY,PrivacyMode.NO_EXTERNAL_EGRESS} and (not rt.model_local or rt.network):
            rejected.append(f"runtime:{rt.id}:privacy"); return False
        if req.network_required and not rt.network: rejected.append(f"runtime:{rt.id}:network"); return False
        if req.tool_use_required and not rt.tool_use: rejected.append(f"runtime:{rt.id}:tool_use"); return False
        if req.external_research_required and not rt.research: rejected.append(f"runtime:{rt.id}:research"); return False
        if req.code_execution_required and not rt.code_execution: rejected.append(f"runtime:{rt.id}:code_execution"); return False
        if req.visual_debug_required and not rt.visual: rejected.append(f"runtime:{rt.id}:visual"); return False
        if req.write_required and not rt.writable: rejected.append(f"runtime:{rt.id}:write"); return False
        if rt.quality < req.min_quality: rejected.append(f"runtime:{rt.id}:quality"); return False
        if req.requested_model_tier and rt.quality < req.requested_model_tier: rejected.append(f"runtime:{rt.id}:tier"); return False
        if rt.cost_score > req.max_cost_score or rt.latency_score > req.max_latency_score: rejected.append(f"runtime:{rt.id}:budget"); return False
        if rt.max_context_tokens < req.max_context_tokens: rejected.append(f"runtime:{rt.id}:context"); return False
        return True

    def _candidate(self, agent:AgentDescriptor, rt:RuntimeDescriptor, caps:set[str], req:TaskRequest)->RouteCandidate|None:
        if req.requested_agent and agent.id != req.requested_agent: return None
        if req.risk > agent.max_risk: return None
        covered=agent.capabilities | rt.capabilities
        missing=caps-covered
        hard_missing = missing & {"coding","repo","testing","ci","research","external_research","architecture","visual","security","memory","offline"}
        if hard_missing: return None
        if any(s not in agent.default_skills and s not in req.requested_skills for s in req.requested_skills): return None
        tier=max(rt.quality,agent.min_quality,req.min_quality)
        skills=tuple(dict.fromkeys(list(req.requested_skills)+list(agent.default_skills)))[:agent.max_skills]
        cap_score=100.0 if not caps else 100.0*len(caps & (agent.capabilities | rt.capabilities))/len(caps)
        quality_score=100.0*(1.0-(tier.value-QualityTier.LIGHT.value)/(QualityTier.FRONTIER.value-QualityTier.LIGHT.value))
        cost_score=100.0-rt.cost_score
        latency_score=100.0-rt.latency_score
        pref=20.0 if rt.id in agent.preferred_runtimes else 0.0
        specialized=0.0
        if "visual" in caps and rt.visual: specialized += 25.0
        if "external_research" in caps and rt.research: specialized += 25.0
        if "offline" in caps and rt.model_local: specialized += 25.0
        if "repo" in caps and rt.code_execution: specialized += 5.0
        history=0.0
        if self.feedback_store is not None:
            prior=self.feedback_store.prior(agent.id,rt.id)
            history=max(0.0,min(10.0,prior.quality_score*0.05+prior.verification_rate*0.03+prior.success_rate*0.02))
        return RouteCandidate(agent.id,rt.id,tier,skills,
            round(cap_score*.45+quality_score*.2+cost_score*.1+latency_score*.1+pref+specialized+history,3),
            round(cap_score,3),round(quality_score,3),round(cost_score,3),round(latency_score,3),history,
            ("capability_match","quality_ok","runtime_eligible")+(( "preferred_runtime",) if pref else ())+(( "specialized_runtime",) if specialized else ()),agent.independence_group,rt.transport)

    def route(self, request:TaskRequest, runtime_available:dict[str,bool]|None=None)->RouteDecision:
        n=self.normalize(request); caps=set(n["capabilities"]); risk=n["risk"]
        rejected=[]; candidates=[]
        for aid,agent in self.registry.agents.items():
            if request.requested_agent and aid != request.requested_agent: continue
            for rid,rt in self.registry.runtimes.items():
                if self._eligible_runtime(rt,request,rejected):
                    c=self._candidate(agent,rt,caps,request)
                    if c: candidates.append(c)
        candidates.sort(key=lambda x:x.score,reverse=True)
        dispatchable = candidates
        if runtime_available is not None:
            available_candidates=[x for x in candidates if x.transport != "manual" and runtime_available.get(x.runtime_id,False)]
            if available_candidates:
                dispatchable=available_candidates
        primary=dispatchable[0] if dispatchable else (candidates[0] if candidates else None)
        needs_verifier=request.require_independent_verifier or risk>=RiskLevel.HIGH
        verifier=None
        if primary and needs_verifier:
            verifier_pool=[c for c in dispatchable if not primary or c is not primary]
            for c in verifier_pool:
                a=self.registry.agents[c.agent_id]; r=self.registry.runtimes[c.runtime_id]
                pa=self.registry.agents[primary.agent_id]; pr=self.registry.runtimes[primary.runtime_id]
                if a.independence_group!=pa.independence_group and r.independence_group!=pr.independence_group:
                    verifier=VerifierDescriptor(c.agent_id,c.runtime_id,c.model_tier,a.independence_group,"independent review required")
                    break
            if verifier is None: rejected.append("verifier:no_independent_route")
        qp=self._fingerprint(request.goal)
        memory=MemoryPlan(qp,len(request.goal),int(self.registry.defaults.get("max_memory_results",5)),
            3,int(self.registry.defaults.get("max_graph_hops",1)),False,True,"evidence")
        status=RouteStatus.BLOCKED if not primary or (needs_verifier and verifier is None) else RouteStatus.PLANNED
        if runtime_available and primary and primary.transport != "manual" and runtime_available.get(primary.runtime_id,False):
            status=RouteStatus.ROUTED if (not needs_verifier or (verifier and verifier.runtime_id in runtime_available and runtime_available.get(verifier.runtime_id,False))) else RouteStatus.BLOCKED
        if request.requested_runtime and primary is None: status=RouteStatus.BLOCKED
        task_fp=qp
        receipt=RouteDecision(
            decision_id=f"dec_{uuid.uuid4().hex[:12]}",route_id=f"route_{uuid.uuid4().hex[:12]}",
            status=status,primary=primary,verifier=verifier,
            support=tuple(candidates[1:3] if request.allow_parallel else ()),
            alternatives=tuple(candidates[1:6]),memory_plan=memory,
            candidate_count=len(candidates),rejected=tuple(rejected),
            policy_gates=("risk_gate","privacy_gate","capability_gate","authority_gate","verifier_gate"),
            warnings=(() if status==RouteStatus.ROUTED else ("runtime availability not confirmed",)),
            registry_fingerprint=self.registry.fingerprint,task_fingerprint=task_fp,
            execution_contract={"execute_via":"dispatcher","prompt_profile":primary and self.registry.agents[primary.agent_id].prompt_profile})
        return receipt
