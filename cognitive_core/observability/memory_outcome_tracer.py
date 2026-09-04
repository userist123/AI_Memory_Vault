import os
import re
import json
import glob
from enum import Enum
from typing import List, Dict, Any, Optional
from dataclasses import dataclass, asdict

class MemoryUtilityTier(str, Enum):
    RETRIEVED_AND_UNUSED = "RETRIEVED_AND_UNUSED"
    RETRIEVED_AND_REFERENCED = "RETRIEVED_AND_REFERENCED"
    RETRIEVED_AND_FUNCTIONAL = "RETRIEVED_AND_FUNCTIONAL"
    RETRIEVED_AND_CAUSAL = "RETRIEVED_AND_CAUSAL"

@dataclass
class MemoryOutcomeLinkage:
    trace_id: str
    task_id: str
    memory_id: str
    memory_present_in_context: bool
    tokens_in_model_response: int
    functional_tool_influence: bool
    task_success: bool
    utility_tier: MemoryUtilityTier
    evidence_detail: str

    def to_dict(self) -> Dict[str, Any]:
        d = asdict(self)
        d["utility_tier"] = self.utility_tier.value
        return d


class MemoryOutcomeTracer:
    """
    Antigravity Memory-Use to Outcome Correlation Engine (R001).
    Bridges:
    MEMORY RETRIEVED -> MODEL CONTEXT -> ACTIONS EXECUTED -> REAL OUTCOME
    """
    def __init__(self, trace_dir: str = "telemetry/execution_traces"):
        self.trace_dir = trace_dir

    def scan_traces(self) -> List[MemoryOutcomeLinkage]:
        linkages = []
        files = glob.glob(os.path.join(self.trace_dir, "trace_*.json"))
        for fpath in files:
            try:
                with open(fpath, "r", encoding="utf-8") as f:
                    trace = json.load(f)
                res = self.analyze_trace(trace)
                linkages.extend(res)
            except Exception:
                continue
        return linkages

    def analyze_trace(self, trace: Dict[str, Any]) -> List[MemoryOutcomeLinkage]:
        trace_id = trace.get("trace_id", "unknown")
        task_id = trace.get("task_id", "unknown")
        memory_sec = trace.get("memory", {})
        
        # Support both memory_ids and retrieved_memory_ids
        retrieved_ids = memory_sec.get("memory_ids") or memory_sec.get("retrieved_memory_ids") or []
        raw_text = memory_sec.get("bounded_context_text") or memory_sec.get("query") or ""

        model_sec = trace.get("model", {})
        response_text = model_sec.get("response_text", "")
        
        # Support both actions list or actions.parsed_actions
        actions_raw = trace.get("actions", [])
        if isinstance(actions_raw, dict):
            actions_list = actions_raw.get("parsed_actions", [])
        else:
            actions_list = actions_raw

        verification = trace.get("verification", {})
        task_success = (
            verification.get("outcome") == "PASSED" or 
            verification.get("status") == "passed" or 
            verification.get("exit_code") == 0 or
            verification.get("returncode") == 0
        )

        # Collect action arguments
        tool_args_text = ""
        for a in actions_list:
            tool_args_text += " " + json.dumps(a)
        
        # Also check workspace created files
        workspace = trace.get("workspace", {})
        tool_args_text += " " + " ".join(workspace.get("files_created", []))

        linkages = []
        for mid in retrieved_ids:
            clean_mid = mid.lower().replace("-", " ")
            id_in_response = clean_mid in response_text.lower() or mid.lower() in response_text.lower()
            
            # Check key vocabulary
            mem_tokens = set(re.findall(r"\b[a-zA-Z_]{4,}\b", raw_text.lower()))
            overlap_in_actions = [t for t in mem_tokens if t in response_text.lower() or t in tool_args_text.lower()]

            if not retrieved_ids:
                tier = MemoryUtilityTier.RETRIEVED_AND_UNUSED
                evidence = "No memory IDs linked"
            elif overlap_in_actions and task_success:
                tier = MemoryUtilityTier.RETRIEVED_AND_FUNCTIONAL
                evidence = f"Tokens {overlap_in_actions[:3]} present in response/actions under verified success."
            elif id_in_response:
                tier = MemoryUtilityTier.RETRIEVED_AND_REFERENCED
                evidence = "Memory cited in model response text."
            else:
                tier = MemoryUtilityTier.RETRIEVED_AND_UNUSED
                evidence = "Memory present in prompt context but absent in tool execution parameters."

            linkages.append(MemoryOutcomeLinkage(
                trace_id=trace_id,
                task_id=task_id,
                memory_id=mid,
                memory_present_in_context=bool(retrieved_ids),
                tokens_in_model_response=len(re.findall(r"\b" + re.escape(mid) + r"\b", response_text, re.I)),
                functional_tool_influence=bool(overlap_in_actions),
                task_success=task_success,
                utility_tier=tier,
                evidence_detail=evidence
            ))
        return linkages
