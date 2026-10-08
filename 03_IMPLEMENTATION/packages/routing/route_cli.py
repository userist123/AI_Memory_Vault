from __future__ import annotations

import argparse,json,shutil
from pathlib import Path

from .agent_router import AgentRouter
from .dispatcher import AgentDispatcher
from .models import PrivacyMode,QualityTier,RiskLevel,TaskRequest
from .registry import RouteRegistry

ROOT=Path(__file__).resolve().parents[3]
REGISTRY=ROOT/"04_CONFIG"/"agent_router.json"

def _runtime_available(rt) -> bool:
    """A command runtime is available when the registry's `executable` is on PATH (same value the dispatcher runs)."""
    return rt.transport=="command" and bool(rt.executable) and shutil.which(rt.executable) is not None

def build_parser():
    p=argparse.ArgumentParser(prog="python -m routing.route_cli")
    sub=p.add_subparsers(dest="cmd",required=True)
    q=sub.add_parser("probe"); q.add_argument("--runtime",action="append")
    q.add_argument("--json",action="store_true")
    for name in ("route","dispatch"):
        x=sub.add_parser(name)
        x.add_argument("--goal",required=True); x.add_argument("--capability",action="append",default=[])
        x.add_argument("--runtime"); x.add_argument("--agent"); x.add_argument("--risk",choices=["low","medium","high","critical"],default="medium")
        x.add_argument("--min-quality",choices=["light","standard","heavy","frontier"],default="standard")
        x.add_argument("--local-only",action="store_true"); x.add_argument("--research",action="store_true")
        x.add_argument("--code-exec",action="store_true"); x.add_argument("--visual",action="store_true")
        x.add_argument("--write",action="store_true"); x.add_argument("--tool-use",action="store_true")
        x.add_argument("--verify",action="store_true"); x.add_argument("--source-agent",default="router_cli")
        x.add_argument("--accept",action="append",default=[]); x.add_argument("--execute",action="store_true")
    return p

def main():
    a=build_parser().parse_args()
    reg=RouteRegistry.from_file(REGISTRY)
    if a.cmd=="probe":
        ids=a.runtime or list(reg.runtimes)
        print(json.dumps({r:_runtime_available(reg.runtimes[r]) for r in ids},indent=2))
        return
    req=TaskRequest(goal=a.goal,capabilities=tuple(a.capability),risk=RiskLevel[a.risk.upper()],
        min_quality=QualityTier[a.min_quality.upper()],privacy=PrivacyMode.LOCAL_ONLY if a.local_only else PrivacyMode.STANDARD,
        requested_runtime=a.runtime,requested_agent=a.agent,external_research_required=a.research,
        code_execution_required=a.code_exec,visual_debug_required=a.visual,write_required=a.write,
        tool_use_required=a.tool_use,require_independent_verifier=a.verify)
    available={}
    for rid,rt in reg.runtimes.items():
        available[rid]=_runtime_available(rt)
    decision=AgentRouter(reg).route(req,available)
    print(json.dumps(decision.to_dict(),ensure_ascii=False,indent=2))
    if a.cmd=="dispatch" and a.execute:
        if decision.status.value!="ROUTED":
            raise SystemExit("route is not ROUTED; dispatch refused")
        dispatcher=AgentDispatcher(reg)
        packet=dispatcher.make_packet(decision,a.goal,a.source_agent,tuple(a.accept))
        result=dispatcher.dispatch(decision,packet)
        print(json.dumps(result.__dict__,ensure_ascii=False,indent=2))

if __name__=="__main__":
    main()
