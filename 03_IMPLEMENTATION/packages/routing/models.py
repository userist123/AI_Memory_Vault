from __future__ import annotations

from dataclasses import dataclass, field
from enum import IntEnum, StrEnum
from typing import Any, Mapping


class RiskLevel(IntEnum):
    LOW=10; MEDIUM=20; HIGH=30; CRITICAL=40


class QualityTier(IntEnum):
    LIGHT=10; STANDARD=20; HEAVY=30; FRONTIER=40


class RouteStatus(StrEnum):
    ROUTED="ROUTED"
    PLANNED="PLANNED"
    BLOCKED="BLOCKED"


class PrivacyMode(StrEnum):
    STANDARD="standard"
    LOCAL_ONLY="local_only"
    NO_EXTERNAL_EGRESS="no_external_egress"


class DispatchStatus(StrEnum):
    CREATED="created"
    SUBMITTED="submitted"
    WORKING="working"
    INPUT_REQUIRED="input-required"
    AUTH_REQUIRED="auth-required"
    PENDING_VERIFICATION="pending-verification"
    COMPLETED="completed"
    FAILED="failed"
    CANCELED="canceled"
    BLOCKED="blocked"


@dataclass(frozen=True)
class TaskRequest:
    goal:str
    capabilities:tuple[str,...]=()
    project:str|None=None
    risk:RiskLevel=RiskLevel.MEDIUM
    min_quality:QualityTier=QualityTier.STANDARD
    privacy:PrivacyMode=PrivacyMode.STANDARD
    network_required:bool=False
    tool_use_required:bool=False
    external_research_required:bool=False
    code_execution_required:bool=False
    visual_debug_required:bool=False
    write_required:bool=False
    external_side_effects_allowed:bool=False
    urgency:int=50
    max_cost_score:float=100.0
    max_latency_score:float=100.0
    max_context_tokens:int=3000
    requested_runtime:str|None=None
    requested_agent:str|None=None
    requested_model_tier:QualityTier|None=None
    requested_skills:tuple[str,...]=()
    require_independent_verifier:bool=False
    allow_parallel:bool=False
    metadata:Mapping[str,Any]=field(default_factory=dict)


@dataclass(frozen=True)
class RuntimeDescriptor:
    id:str
    capabilities:frozenset[str]
    quality:QualityTier
    cost_score:float
    latency_score:float
    model_local:bool
    network:bool
    tool_use:bool
    code_execution:bool
    visual:bool
    research:bool
    writable:bool
    max_context_tokens:int
    independence_group:str
    transport:str="manual"
    adapter_ref:str=""
    enabled:bool=True


@dataclass(frozen=True)
class AgentDescriptor:
    id:str
    profile_ref:str
    capabilities:frozenset[str]
    preferred_runtimes:tuple[str,...]
    min_quality:QualityTier
    max_risk:RiskLevel
    default_skills:tuple[str,...]
    max_skills:int
    independence_group:str
    prompt_profile:str


@dataclass(frozen=True)
class VerifierDescriptor:
    agent_id:str
    runtime_id:str
    quality:QualityTier
    independence_group:str
    reason:str


@dataclass(frozen=True)
class MemoryPlan:
    query_fingerprint:str
    query_length:int
    max_notes:int
    max_full_documents:int
    max_graph_hops:int
    include_review:bool
    require_provenance:bool
    evidence_mode:str


@dataclass(frozen=True)
class RouteCandidate:
    agent_id:str
    runtime_id:str
    model_tier:QualityTier
    selected_skills:tuple[str,...]
    score:float
    capability_score:float
    quality_score:float
    cost_score:float
    latency_score:float
    history_score:float
    reasons:tuple[str,...]
    independence_group:str=""
    transport:str="manual"

    def to_dict(self):
        return {"agent_id":self.agent_id,"runtime_id":self.runtime_id,
                "model_tier":self.model_tier.name.lower(),"selected_skills":list(self.selected_skills),
                "score":self.score,"capability_score":self.capability_score,
                "quality_score":self.quality_score,"cost_score":self.cost_score,
                "latency_score":self.latency_score,"history_score":self.history_score,
                "reasons":list(self.reasons),"independence_group":self.independence_group,"transport":self.transport}


@dataclass(frozen=True)
class RouteDecision:
    decision_id:str
    route_id:str
    status:RouteStatus
    primary:RouteCandidate|None
    verifier:VerifierDescriptor|None
    support:tuple[RouteCandidate,...]
    alternatives:tuple[RouteCandidate,...]
    memory_plan:MemoryPlan
    candidate_count:int
    rejected:tuple[str,...]
    policy_gates:tuple[str,...]
    warnings:tuple[str,...]
    registry_fingerprint:str
    task_fingerprint:str
    execution_contract:Mapping[str,Any]

    def to_dict(self):
        return {
            "decision_id":self.decision_id,"route_id":self.route_id,"status":self.status.value,
            "primary":self.primary.to_dict() if self.primary else None,
            "verifier":None if not self.verifier else {
                "agent_id":self.verifier.agent_id,"runtime_id":self.verifier.runtime_id,
                "quality":self.verifier.quality.name.lower(),
                "independence_group":self.verifier.independence_group,"reason":self.verifier.reason},
            "support":[x.to_dict() for x in self.support],
            "alternatives":[x.to_dict() for x in self.alternatives],
            "memory_plan":self.memory_plan.__dict__,
            "candidate_count":self.candidate_count,"rejected":list(self.rejected),
            "policy_gates":list(self.policy_gates),"warnings":list(self.warnings),
            "registry_fingerprint":self.registry_fingerprint,"task_fingerprint":self.task_fingerprint,
            "execution_contract":dict(self.execution_contract)}


@dataclass(frozen=True)
class WorkPacket:
    task_id:str
    route_id:str
    source_agent:str
    target_agent:str
    target_runtime:str
    prompt_profile:str
    command:str
    goal:str
    acceptance_criteria:tuple[str,...]=()
    constraints:tuple[str,...]=()
    memory_refs:tuple[str,...]=()
    timeout_seconds:int=3600
    metadata:Mapping[str,Any]=field(default_factory=dict)


@dataclass(frozen=True)
class DispatchResult:
    task_id:str
    route_id:str
    status:DispatchStatus
    runtime:str
    agent:str
    exit_code:int|None
    final_message:str
    result_path:str|None=None
    session_id:str|None=None
    error:str|None=None
    metadata:Mapping[str,Any]=field(default_factory=dict)


@dataclass(frozen=True)
class RouteFeedback:
    route_id:str
    agent_id:str
    runtime_id:str
    success:bool
    verification_passed:bool|None
    quality_score:float
    cost_score:float
    latency_score:float
    task_class:str="unknown"
    failure_class:str|None=None


@dataclass(frozen=True)
class RoutePrior:
    samples: int
    success_rate: float
    verification_rate: float
    quality_score: float
    cost_score: float
    latency_score: float
