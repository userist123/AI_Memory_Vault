from __future__ import annotations

import hashlib, json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .models import AgentDescriptor, QualityTier, RiskLevel, RuntimeDescriptor

class RegistryError(ValueError): pass


def _reject_unknown(obj: dict[str, Any], allowed: set[str], label: str) -> None:
    extra=sorted(set(obj)-allowed)
    if extra: raise RegistryError(f"{label}: unknown fields: {extra}")

def _enum(cls, value: Any, label: str):
    try: return cls[str(value).strip().upper()]
    except Exception as exc: raise RegistryError(f"{label}: invalid value {value!r}") from exc

def _strict_obj(value: Any, label: str) -> dict[str, Any]:
    if not isinstance(value, dict): raise RegistryError(f"{label}: expected object")
    return value

@dataclass(frozen=True)
class RouteSignals:
    capabilities: dict[str, tuple[str, ...]]
    risk_keywords: dict[str, tuple[str, ...]]
    task_classes: dict[str, tuple[str, ...]]

@dataclass(frozen=True)
class RouteRegistry:
    runtimes: dict[str, RuntimeDescriptor]
    agents: dict[str, AgentDescriptor]
    defaults: dict[str, int|float]
    signals: RouteSignals
    fingerprint: str

    @classmethod
    def from_file(cls, path: str|Path) -> "RouteRegistry":
        p=Path(path)
        if not p.exists(): raise RegistryError(f"registry not found: {p}")
        try: raw=json.loads(p.read_text(encoding="utf-8"))
        except json.JSONDecodeError as exc: raise RegistryError(f"invalid JSON: {exc}") from exc
        if not isinstance(raw,dict) or raw.get("schema_version") != 1:
            raise RegistryError("registry must be an object with schema_version=1")
        _reject_unknown(raw,{"schema_version","defaults","signals","runtimes","agents"},"root")
        for k in ("defaults","signals","runtimes","agents"):
            if k not in raw: raise RegistryError(f"missing registry field: {k}")

        runtimes={}
        for item in raw["runtimes"]:
            x=_strict_obj(item,"runtime"); _reject_unknown(x,{"id","capabilities","quality","cost_score","latency_score","model_local","network","tool_use","code_execution","visual","research","writable","max_context_tokens","independence_group","transport","adapter_ref","enabled"},"runtime"); rid=str(x.get("id","")).strip()
            if not rid or rid in runtimes: raise RegistryError(f"duplicate/empty runtime id: {rid!r}")
            if x.get("transport") not in {"command","a2a","manual"}: raise RegistryError(f"{rid}: invalid transport")
            runtimes[rid]=RuntimeDescriptor(
                id=rid, capabilities=frozenset(map(str,x.get("capabilities",[]))),
                quality=_enum(QualityTier,x["quality"],f"{rid}.quality"),
                cost_score=float(x["cost_score"]), latency_score=float(x["latency_score"]),
                model_local=bool(x["model_local"]), network=bool(x["network"]),
                tool_use=bool(x["tool_use"]), code_execution=bool(x["code_execution"]),
                visual=bool(x["visual"]), research=bool(x["research"]), writable=bool(x["writable"]),
                max_context_tokens=int(x["max_context_tokens"]), independence_group=str(x["independence_group"]),
                transport=str(x["transport"]), adapter_ref=str(x.get("adapter_ref","")), enabled=bool(x.get("enabled",True)))
        agents={}
        for item in raw["agents"]:
            x=_strict_obj(item,"agent"); _reject_unknown(x,{"id","profile_ref","capabilities","preferred_runtimes","min_quality","max_risk","default_skills","max_skills","independence_group","prompt_profile"},"agent"); aid=str(x.get("id","")).strip()
            if not aid or aid in agents: raise RegistryError(f"duplicate/empty agent id: {aid!r}")
            agents[aid]=AgentDescriptor(
                id=aid, profile_ref=str(x["profile_ref"]), capabilities=frozenset(map(str,x.get("capabilities",[]))),
                preferred_runtimes=tuple(map(str,x.get("preferred_runtimes",[]))),
                min_quality=_enum(QualityTier,x["min_quality"],f"{aid}.min_quality"),
                max_risk=_enum(RiskLevel,x["max_risk"],f"{aid}.max_risk"),
                default_skills=tuple(map(str,x.get("default_skills",[]))),
                max_skills=int(x.get("max_skills",2)), independence_group=str(x["independence_group"]),
                prompt_profile=str(x["prompt_profile"]))
        for aid, agent in agents.items():
            missing=sorted(set(agent.preferred_runtimes)-set(runtimes))
            if missing: raise RegistryError(f"{aid}: unknown preferred runtimes: {missing}")
        sig=_strict_obj(raw["signals"],"signals")
        _reject_unknown(sig,{"capabilities","risk_keywords","task_classes"},"signals")
        signals=RouteSignals(
            capabilities={str(k):tuple(map(str,v)) for k,v in _strict_obj(sig.get("capabilities",{}),"signals.capabilities").items()},
            risk_keywords={str(k):tuple(map(str,v)) for k,v in _strict_obj(sig.get("risk_keywords",{}),"signals.risk_keywords").items()},
            task_classes={str(k):tuple(map(str,v)) for k,v in _strict_obj(sig.get("task_classes",{}),"signals.task_classes").items()})
        canonical=json.dumps(raw,sort_keys=True,separators=(",",":"),ensure_ascii=False).encode()
        return cls(runtimes,agents,{str(k):v for k,v in raw["defaults"].items()},signals,hashlib.sha256(canonical).hexdigest())
