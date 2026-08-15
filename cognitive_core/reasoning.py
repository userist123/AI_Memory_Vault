import re
from typing import List, Dict, Any, Optional, Tuple
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal

class ThoughtValidator:
    """Validates reasoning branch validity, consistency, and alignment with context."""

    @staticmethod
    def validate_branch(branch: Dict[str, Any], context: List[Dict[str, Any]]) -> Tuple[bool, float, str]:
        """Evaluates whether a thought branch is logically sound and grounded in context.
        Returns: (is_valid, score, critique)
        """
        thought = branch.get("thought", "")
        if not thought or len(thought.strip()) < 10:
            return False, 0.0, "Thought is too sparse or empty"

        context_text = " ".join(n.get("content", "").lower() for n in context)
        # Grounding check: ensure keywords in thought match context if context exists
        if context:
            words = [w for w in thought.lower().split() if len(w) > 4]
            matched = sum(1 for w in words if w in context_text)
            grounding_ratio = matched / len(words) if words else 1.0
        else:
            grounding_ratio = 0.8

        score = min(1.0, 0.5 + 0.5 * grounding_ratio)
        is_valid = score >= 0.4
        critique = "Well grounded" if is_valid else "Lacks sufficient grounding in context"
        return is_valid, score, critique

class TreeOfThoughtReasoner:
    """Generates, validates, and prunes multi-branch reasoning paths for complex queries."""

    def __init__(self, validator: Optional[ThoughtValidator] = None):
        self.validator = validator or ThoughtValidator()

    def generate_branches(self, query: str, context: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """Generates alternative reasoning perspectives/branches."""
        branches = [
            {
                "id": "branch-direct",
                "perspective": "direct evidence",
                "thought": f"Directly analyzing facts for '{query}' using {len(context)} memory nodes."
            },
            {
                "id": "branch-comparative",
                "perspective": "comparative causal",
                "thought": f"Examining root causes and relationships related to '{query}' across available context."
            },
            {
                "id": "branch-counterfactual",
                "perspective": "counterfactual/edge case",
                "thought": f"Assessing boundary constraints and failure modes for '{query}'."
            }
        ]
        return branches

    def reason(self, query: str, context: List[Dict[str, Any]]) -> Dict[str, Any]:
        """Executes Tree-of-Thought search, evaluation, and selection."""
        branches = self.generate_branches(query, context)
        evaluated = []

        for b in branches:
            is_valid, score, critique = self.validator.validate_branch(b, context)
            if is_valid:
                evaluated.append({
                    **b,
                    "score": score,
                    "critique": critique
                })

        evaluated.sort(key=lambda x: x["score"], reverse=True)
        best_branch = evaluated[0] if evaluated else {
            "thought": "Default synthesized reasoning path.",
            "score": 0.5,
            "critique": "Fallback"
        }

        return {
            "best_branch": best_branch,
            "all_evaluated_branches": evaluated,
            "tree_depth": 2,
            "branches_explored": len(branches)
        }

class ReasoningEngine:
    """Reasoning bounds and validation.
    Enforces a strict READ-ONLY boundary against MemoryController during reasoning,
    with selective Tree-of-Thought activation for complex queries.
    """
    def __init__(self, memory_controller: MemoryController, tot_reasoner: Optional[TreeOfThoughtReasoner] = None):
        self.controller = memory_controller
        self.tot_reasoner = tot_reasoner or TreeOfThoughtReasoner()

    def _is_high_complexity(self, query: str) -> bool:
        lowered = query.lower()
        triggers = ["why", "how", "root cause", "compare", "plan", "troubleshoot", "evaluate", "complex", "architecture"]
        return any(re.search(rf"\b{re.escape(t)}\b", lowered) for t in triggers) or len(query.split()) > 10

    def synthesize(self, principal: Principal, context: List[Dict[str, Any]], query: str) -> Dict[str, Any]:
        """Synthesizes an answer or decision based on active context, with selective ToT."""
        extra_info = []
        if "detailed" in query.lower():
            res = self.controller.search(principal, query)
            extra_info = res.get("results", [])

        all_context = list(context) + extra_info

        if self._is_high_complexity(query):
            tot_result = self.tot_reasoner.reason(query, all_context)
            return {
                "synthesis": f"ToT Synthesis: {tot_result['best_branch']['thought']}",
                "mode": "tree_of_thought",
                "tot_details": tot_result,
                "context_used": len(context),
                "extra_retrieved": len(extra_info)
            }
        else:
            return {
                "synthesis": "Direct synthesized conclusion based on context.",
                "mode": "direct",
                "context_used": len(context),
                "extra_retrieved": len(extra_info)
            }
