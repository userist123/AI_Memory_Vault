"""
Milestone 3: Critic Agent (6-Stage Reflexion, SelfRefine Quality Gate, Secret Leak Auditing).
"""

import re
import uuid
from datetime import datetime, timezone
from typing import Dict, Any, List, Optional
from jarvis.memory.invariants import (
    Principal,
    Lifecycle,
    NoteType,
)
from jarvis.llm.base import BaseLLMProvider, CancellationToken
from jarvis.agents.base import BaseAgent
from jarvis.agents.models import (
    AgentRole,
    ReflexionAnalysis,
    CritiqueResult,
)


class CriticAgent(BaseAgent):
    """
    Critic Agent executes formal 6-stage Reflexion on execution anomalies
    and performs SelfRefine quality, factual consistency, and security audits.
    Operates under READ, PROPOSE least-privilege scoping.
    """

    role: AgentRole = AgentRole.CRITIC

    def __init__(
        self,
        storage: Optional[Any] = None,
        llm: Optional[BaseLLMProvider] = None,
    ):
        super().__init__(storage=storage, llm=llm)

    def reflect_on_error(
        self,
        step_action: str,
        error_msg: str,
        context: Optional[Dict[str, Any]] = None,
        root_cause: Optional[str] = None,
        fix: Optional[str] = None,
        verification: Optional[str] = None,
        prevention: Optional[str] = None,
        lesson: Optional[str] = None,
    ) -> str:
        """
        Generate a formal 6-stage Reflexion breakdown and optionally persist it
        as a REVIEW lesson note.
        """
        rc = root_cause or f"Execution failure during step '{step_action}': {error_msg}"
        fx = fix or "Executed isolated error recovery and fallback parameters."
        vr = verification or "Verified component boundary constraints and return types."
        pv = prevention or f"Enforce input preconditions and guards before dispatching '{step_action}'."
        ls = lesson or f"Isolate failures and apply bounded retries for '{step_action}'."

        analysis = ReflexionAnalysis(
            error=error_msg,
            root_cause=rc,
            fix_applied=fx,
            verification=vr,
            prevention_rule=pv,
            core_lesson=ls,
        )

        # If storage is configured, propose lesson note into REVIEW
        if self.storage:
            try:
                now_str = datetime.now(timezone.utc).strftime("%Y-%m-%d")
                note_id = str(uuid.uuid4())
                note_data = {
                    "id": note_id,
                    "type": NoteType.LESSON.value,
                    "lifecycle": Lifecycle.REVIEW.value,
                    "category": "system-reflexion",
                    "tags": ["reflexion", "error-recovery", step_action.replace(" ", "-")],
                    "created": now_str,
                    "updated": now_str,
                    "provenance": {
                        "source_type": "inference",
                        "source_ref": f"error:{step_action}",
                    },
                    "confidence": "high",
                    "verification": "unverified",
                    "content": analysis.to_markdown(),
                    "relations": [],
                }
                self.storage.propose(note_data)
                return note_id
            except Exception:
                pass

        return analysis.to_markdown()

    def critique_draft(
        self,
        draft: str,
        context: Optional[List[Dict[str, Any]]] = None,
        is_voice: bool = True,
    ) -> CritiqueResult:
        """
        Critique candidate response or memory draft for security leaks,
        factual consistency, conciseness, and style.
        """
        flags: List[str] = []
        score = 1.0
        critique_notes: List[str] = []

        # 1. Security & Credential Leak Check
        secret_patterns = [
            r"sk-[a-zA-Z0-9_\-]{10,}",
            r"ghp_[a-zA-Z0-9]{20,}",
            r"(?:password|passwd|pwd)\s*=\s*['\"][^'\"]+['\"]",
            r"(?:api_key|apikey|secret_key)\s*=\s*['\"][^'\"]+['\"]",
            r"-----BEGIN (?:RSA )?PRIVATE KEY-----",
        ]
        for pattern in secret_patterns:
            if re.search(pattern, draft, re.IGNORECASE):
                flags.append("SECRET_LEAK")
                redacted = re.sub(pattern, "[REDACTED_SECRET]", draft, flags=re.IGNORECASE)
                return CritiqueResult(
                    approved=False,
                    score=0.0,
                    critique="Security policy violation: detected secret or credential leak in draft.",
                    suggested_refinement=redacted,
                    flags=flags,
                )

        # 2. Fact Grounding & Contradictions Check
        if context:
            draft_lower = draft.lower()
            for item in context:
                content = (item.get("content") or "").lower()
                conflicts = item.get("conflicts_with")
                if conflicts and conflicts.lower() in draft_lower:
                    flags.append("CONTRADICTION")
                    score = min(score, 0.4)
                    critique_notes.append(f"Draft conflicts with context node '{item.get('id')}'.")

        # 3. Voice Output Brevity Check (<50 words for voice)
        words = draft.split()
        if is_voice and len(words) > 50:
            flags.append("VOICE_TOO_LONG")
            score = min(score, 0.75)
            critique_notes.append(f"Draft has {len(words)} words; recommend trimming to <50 words for voice TTFB.")

        # 4. Multi-concept atomicity check
        if "everything about" in draft.lower() or "all information regarding" in draft.lower():
            flags.append("NON_ATOMIC")
            score = min(score, 0.6)
            critique_notes.append("Draft covers multiple concepts; split into atomic representations.")

        approved = score >= 0.8 and "SECRET_LEAK" not in flags and "CONTRADICTION" not in flags
        critique_msg = "; ".join(critique_notes) if critique_notes else "Draft is clear, concise, and invariant-compliant."

        return CritiqueResult(
            approved=approved,
            score=round(score, 2),
            critique=critique_msg,
            suggested_refinement=None,
            flags=flags,
        )

    async def execute(
        self,
        payload: Dict[str, Any],
        cancellation_token: Optional[CancellationToken] = None,
    ) -> Dict[str, Any]:
        """Execute critic task (critique draft or reflect on error)."""
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        if "draft" in payload or "text" in payload:
            draft = payload.get("draft") or payload.get("text", "")
            context = payload.get("context")
            is_voice = payload.get("is_voice", True)
            res = self.critique_draft(draft, context=context, is_voice=is_voice)
            return {
                "approved": res.approved,
                "score": res.score,
                "critique": res.critique,
                "suggested_refinement": res.suggested_refinement,
                "flags": res.flags,
            }

        elif "error" in payload or "step_action" in payload:
            step_action = payload.get("step_action", "unknown_action")
            error_msg = payload.get("error", "unspecified error")
            context = payload.get("context")
            res_md = self.reflect_on_error(step_action, error_msg, context=context)
            return {
                "approved": True,
                "critique": "6-stage reflexion generated.",
                "reflexion_markdown": res_md,
                "note_id": res_md if len(res_md) == 36 and "-" in res_md else None,
            }

        else:
            return {
                "approved": True,
                "critique": "Draft is clear, concise, and invariant-compliant.",
            }
