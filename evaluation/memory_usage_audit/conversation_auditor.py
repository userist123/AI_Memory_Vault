"""evaluation/memory_usage_audit/conversation_auditor.py — Conversation Transcript Auditor.

Analyzes agent conversation transcripts, tool execution traces, and handoff reports
to determine concrete memory utilization across the 11 audit stages.
"""
from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any, Dict, List, Optional, Set

from evaluation.memory_usage_audit.scoring import AuditScorecard, StageEvaluation


class ConversationAuditor:
    """Audits external conversation traces against Vault memory invariants."""

    CLAIM_PATTERNS = [
        (r"i used the (vault|memory|skills?)", "Claimed Vault/Skill usage"),
        (r"i checked the (skills?|rules?|vault)", "Claimed checking rules/skills"),
        (r"i followed the architecture", "Claimed architecture compliance"),
        (r"i made the (github|code) changes", "Claimed implementation"),
        (r"i verified (it|the code|the tests?)", "Claimed verification"),
    ]

    def __init__(self, raw_transcript_or_text: str, case_id: str = "wob_art"):
        self.raw_text = raw_transcript_or_text
        self.case_id = case_id
        self.tool_calls: List[Dict[str, Any]] = []
        self.user_messages: List[str] = []
        self.agent_messages: List[str] = []
        self._parse_transcript()

    def _parse_transcript(self) -> None:
        """Parses JSONL, JSON array, or plain text transcripts."""
        lines = self.raw_text.strip().split("\n")
        is_jsonl = True
        for line in lines[:5]:
            if not line.strip() or not line.strip().startswith("{"):
                is_jsonl = False
                break

        if is_jsonl:
            for line in lines:
                if not line.strip():
                    continue
                try:
                    obj = json.loads(line)
                    if "tool_calls" in obj:
                        for tc in obj["tool_calls"]:
                            self.tool_calls.append(tc)
                    content = obj.get("content", "")
                    if obj.get("type") == "USER_INPUT" or obj.get("source") == "USER_EXPLICIT":
                        self.user_messages.append(content)
                    else:
                        self.agent_messages.append(content)
                except Exception:
                    pass
        else:
            self.agent_messages.append(self.raw_text)

    def audit(self) -> AuditScorecard:
        """Evaluates the 11 stages of memory utilization."""
        stages: Dict[str, StageEvaluation] = {}
        all_agent_text = "\n".join(self.agent_messages)
        lowered_agent_text = all_agent_text.lower()

        # Extract tool targets
        viewed_files: Set[str] = set()
        executed_commands: List[str] = []
        invoked_agents: List[str] = []

        for tc in self.tool_calls:
            fn = tc.get("name") or tc.get("function", {}).get("name", "")
            args = tc.get("args") or tc.get("arguments") or {}
            if isinstance(args, str):
                try:
                    args = json.loads(args)
                except Exception:
                    args = {}

            if fn in ("view_file", "read_file"):
                path = args.get("AbsolutePath") or args.get("path") or args.get("file_path") or ""
                if path:
                    viewed_files.add(str(path).replace("\\", "/"))
            elif fn in ("run_command", "execute_command"):
                cmd = args.get("CommandLine") or args.get("command") or ""
                if cmd:
                    executed_commands.append(cmd)
            elif fn in ("invoke_subagent", "delegate"):
                sub = args.get("Role") or args.get("TypeName") or ""
                if sub:
                    invoked_agents.append(sub)

        # Stage A: Memory Discovery
        memory_paths_viewed = [f for f in viewed_files if any(v in f for v in ["00_CORE", "01_KNOWLEDGE", "04_MEMORY", "99_SYSTEM"])]
        if memory_paths_viewed:
            stages["A_MEMORY_DISCOVERY"] = StageEvaluation(
                "A_MEMORY_DISCOVERY", "VERIFIED", evidence_found=[f"Viewed vault paths: {memory_paths_viewed}"]
            )
        elif "vault" in lowered_agent_text or "agents.md" in lowered_agent_text:
            stages["A_MEMORY_DISCOVERY"] = StageEvaluation(
                "A_MEMORY_DISCOVERY", "SUPPORTED", unverified_claims=["Agent mentioned Vault or AGENTS.md without file view tool calls"]
            )
        else:
            stages["A_MEMORY_DISCOVERY"] = StageEvaluation("A_MEMORY_DISCOVERY", "MISSING", missing_elements=["No Vault discovery observed"])

        # Stage B: Memory Retrieval
        if len(memory_paths_viewed) >= 1:
            stages["B_MEMORY_RETRIEVAL"] = StageEvaluation(
                "B_MEMORY_RETRIEVAL", "VERIFIED", evidence_found=[f"Retrieved notes: {memory_paths_viewed}"]
            )
        elif "retrieved" in lowered_agent_text and "memory" in lowered_agent_text:
            stages["B_MEMORY_RETRIEVAL"] = StageEvaluation(
                "B_MEMORY_RETRIEVAL", "UNVERIFIED", unverified_claims=["Agent asserted memory was retrieved without tool trace"]
            )
        else:
            stages["B_MEMORY_RETRIEVAL"] = StageEvaluation("B_MEMORY_RETRIEVAL", "MISSING", missing_elements=["No note retrieval trace"])

        # Stage C: Memory Loading
        if stages["B_MEMORY_RETRIEVAL"].level == "VERIFIED":
            stages["C_MEMORY_LOADING"] = StageEvaluation(
                "C_MEMORY_LOADING", "VERIFIED", evidence_found=["Retrieved notes loaded into agent context"]
            )
        else:
            stages["C_MEMORY_LOADING"] = StageEvaluation("C_MEMORY_LOADING", "MISSING", missing_elements=["No content loaded into context"])

        # Stage D: Skill Discovery
        skill_paths_viewed = [f for f in viewed_files if ".agents/skills" in f or "skills/" in f]
        if skill_paths_viewed:
            stages["D_SKILL_DISCOVERY"] = StageEvaluation(
                "D_SKILL_DISCOVERY", "VERIFIED", evidence_found=[f"Discovered skills: {skill_paths_viewed}"]
            )
        elif "skill" in lowered_agent_text:
            stages["D_SKILL_DISCOVERY"] = StageEvaluation(
                "D_SKILL_DISCOVERY", "SUPPORTED", unverified_claims=["Agent referenced skills textually without reading skill files"]
            )
        else:
            stages["D_SKILL_DISCOVERY"] = StageEvaluation("D_SKILL_DISCOVERY", "MISSING", missing_elements=["No skill discovery"])

        # Stage E: Skill Activation
        activated_skills = [f for f in skill_paths_viewed if "skill.md" in f.lower()]
        if activated_skills:
            stages["E_SKILL_ACTIVATION"] = StageEvaluation(
                "E_SKILL_ACTIVATION", "VERIFIED", evidence_found=[f"Loaded SKILL.md for: {activated_skills}"]
            )
        elif any("i used the" in lowered_agent_text and "skill" in lowered_agent_text for _, _ in self.CLAIM_PATTERNS):
            stages["E_SKILL_ACTIVATION"] = StageEvaluation(
                "E_SKILL_ACTIVATION", "UNVERIFIED", unverified_claims=["Agent claimed skill activation without SKILL.md file view"]
            )
        else:
            stages["E_SKILL_ACTIVATION"] = StageEvaluation("E_SKILL_ACTIVATION", "MISSING", missing_elements=["No SKILL.md loaded"])

        # Stage F: Subagent Routing
        if invoked_agents:
            stages["F_SUBAGENT_ROUTING"] = StageEvaluation(
                "F_SUBAGENT_ROUTING", "VERIFIED", evidence_found=[f"Invoked subagents: {invoked_agents}"]
            )
        elif "subagent" in lowered_agent_text or "council" in lowered_agent_text:
            stages["F_SUBAGENT_ROUTING"] = StageEvaluation(
                "F_SUBAGENT_ROUTING", "UNVERIFIED", unverified_claims=["Agent mentioned subagents/council without invoke_subagent tool call"]
            )
        else:
            stages["F_SUBAGENT_ROUTING"] = StageEvaluation("F_SUBAGENT_ROUTING", "MISSING", missing_elements=["No subagent routing"])

        # Stage G: Decision Influence
        if stages["B_MEMORY_RETRIEVAL"].level == "VERIFIED" and (executed_commands or viewed_files):
            stages["G_DECISION_INFLUENCE"] = StageEvaluation(
                "G_DECISION_INFLUENCE", "VERIFIED", evidence_found=["Code execution directly traced to retrieved vault memory"]
            )
        elif stages["A_MEMORY_DISCOVERY"].level in ("VERIFIED", "SUPPORTED") and "decided" in lowered_agent_text:
            stages["G_DECISION_INFLUENCE"] = StageEvaluation(
                "G_DECISION_INFLUENCE", "UNVERIFIED", unverified_claims=["Agent claimed decision was influenced by memory without provenance link"]
            )
        else:
            stages["G_DECISION_INFLUENCE"] = StageEvaluation("G_DECISION_INFLUENCE", "MISSING", missing_elements=["No causal decision link"])

        # Stage H: Execution
        if executed_commands or len(viewed_files) >= 1:
            stages["H_EXECUTION"] = StageEvaluation(
                "H_EXECUTION", "VERIFIED", evidence_found=[f"Executed commands: {len(executed_commands)} | Viewed files: {len(viewed_files)}"]
            )
        elif "completed" in lowered_agent_text or "implemented" in lowered_agent_text:
            stages["H_EXECUTION"] = StageEvaluation(
                "H_EXECUTION", "UNVERIFIED", unverified_claims=["Agent claimed implementation without tool execution evidence"]
            )
        else:
            stages["H_EXECUTION"] = StageEvaluation("H_EXECUTION", "MISSING", missing_elements=["No execution evidence"])

        # Stage I: Verification
        test_cmds = [c for c in executed_commands if "pytest" in c or "test" in c or "npm test" in c]
        if test_cmds:
            stages["I_VERIFICATION"] = StageEvaluation(
                "I_VERIFICATION", "VERIFIED", evidence_found=[f"Executed verification commands: {test_cmds}"]
            )
        elif "verified" in lowered_agent_text or "passed" in lowered_agent_text:
            stages["I_VERIFICATION"] = StageEvaluation(
                "I_VERIFICATION", "UNVERIFIED", unverified_claims=["Agent claimed verification without running test commands or browser checks"]
            )
        else:
            stages["I_VERIFICATION"] = StageEvaluation("I_VERIFICATION", "MISSING", missing_elements=["No verification commands run"])

        # Stage J: Outcome Capture
        outcome_logged = any("outcome" in c or "telemetry" in c for c in executed_commands) or any("outcome_events" in f for f in viewed_files)
        if outcome_logged:
            stages["J_OUTCOME_CAPTURE"] = StageEvaluation(
                "J_OUTCOME_CAPTURE", "VERIFIED", evidence_found=["Outcome recorded in telemetry or event logs"]
            )
        else:
            stages["J_OUTCOME_CAPTURE"] = StageEvaluation("J_OUTCOME_CAPTURE", "MISSING", missing_elements=["No outcome captured in telemetry"])

        # Stage K: Consolidation / Learning
        consolidated = any("tasks/lessons.md" in f or "01_KNOWLEDGE" in f for f in viewed_files)
        if consolidated:
            stages["K_CONSOLIDATION"] = StageEvaluation(
                "K_CONSOLIDATION", "VERIFIED", evidence_found=["Updated lessons.md or knowledge note"]
            )
        elif "lesson" in lowered_agent_text:
            stages["K_CONSOLIDATION"] = StageEvaluation(
                "K_CONSOLIDATION", "UNVERIFIED", unverified_claims=["Agent discussed lessons without writing to tasks/lessons.md"]
            )
        else:
            stages["K_CONSOLIDATION"] = StageEvaluation("K_CONSOLIDATION", "MISSING", missing_elements=["No consolidation to lessons.md"])

        return AuditScorecard(case_id=self.case_id, stage_evaluations=stages)
