import os
import json
from typing import List, Dict, Any, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal

from .activation import ActivationEngine
from .working_memory import WorkingMemory
from .tool_router import ToolRouter, RiskLevel, ApprovalRequiredError
from .planning import Planner, ActivePlan
from .reasoning import ReasoningEngine
from .reflection import ReflectionPipeline
from .recall import RecallEngine
from .consolidation import Consolidator
from .deduplication import Deduplicator
from .learning import LearningEngine
from .semantic import DeterministicSemanticProvider
from .orchestrator import MultiAgentOrchestrator
from .council_budget_controller import CouncilBudgetController
from .plan_complexity_analyzer import PlanComplexityAnalyzer

class Executive:
    """
    Central Cognitive Loop Orchestrator.
    Manages OODA-like sequence using all cognitive modules.
    
    WIRE-2: All Phase 3 modules are wired in.
    WIRE-5: Automatic checkpointing after each step.
    WIRE-6: Error recovery and replanning.
    WIRE-MAO: MultiAgentOrchestrator dispatch report attached to process_intent results.
    WIRE-CBC: CouncilBudgetController gates Council dispatch by complexity/risk.
    WIRE-CBC-REAL: Complexity now derives from the Planner's real step count,
    not a pre-planning proxy (dispatch moved to after planning).
    """

    MAX_COUNCIL_MEMORY_RESULTS = 5
    COMPLEXITY_QUERY_WORD_THRESHOLD = 12
    COMPLEXITY_CONTEXT_SIZE_THRESHOLD = 3
    COMPLEXITY_PLAN_STEP_THRESHOLD = 2

    def __init__(self, memory_controller: MemoryController, checkpoint_dir: str = None,
                 orchestrator: Optional[MultiAgentOrchestrator] = None,
                 council_budget: Optional[CouncilBudgetController] = None):
        self.controller = memory_controller
        self.router = ToolRouter(self.controller)
        self.activation_engine = ActivationEngine(self.controller)
        self.working_memory = WorkingMemory(capacity=10)
        self.planner = Planner()
        self.reasoning_engine = ReasoningEngine(self.controller)
        self.reflection = ReflectionPipeline(self.controller)
        self.active_plan: Optional[ActivePlan] = None
        self.checkpoint_dir = checkpoint_dir

        self.semantic_provider = DeterministicSemanticProvider()
        self.recall_engine = RecallEngine(self.controller, self.semantic_provider)
        self.consolidator = Consolidator(self.controller, self.router)
        self.deduplicator = Deduplicator(self.controller, self.semantic_provider, self.router)
        self.learning_engine = LearningEngine(self.controller, self.router)

        self.orchestrator = orchestrator or MultiAgentOrchestrator(self.controller, self.router)
        self.council_budget = council_budget or CouncilBudgetController()

        self.complexity_analyzer = PlanComplexityAnalyzer()

        self._retry_count = 0
        self._max_retries = 2

    def save_state(self, base_dir: str = None):
        base_dir = base_dir or self.checkpoint_dir
        if not base_dir:
            return
        os.makedirs(base_dir, exist_ok=True)
        self.working_memory.save_state(os.path.join(base_dir, "wm.json"))
        if self.active_plan:
            self.active_plan.save_state(os.path.join(base_dir, "plan.json"))

    def load_state(self, base_dir: str, principal: Principal):
        self.checkpoint_dir = base_dir
        wm_path = os.path.join(base_dir, "wm.json")
        if os.path.exists(wm_path):
            self.working_memory.load_state(wm_path, self.controller, principal)
        plan_path = os.path.join(base_dir, "plan.json")
        if os.path.exists(plan_path):
            self.active_plan = ActivePlan.load_state(plan_path)

    def _auto_checkpoint(self):
        if self.checkpoint_dir:
            self.save_state()

    def _parse_intent(self, intent: str) -> Dict[str, Any]:
        return {"query": intent, "type": "task"}
        
    def step_loop(self, principal: Principal) -> Dict[str, Any]:
        if not self.active_plan or self.active_plan.is_complete():
            return {"status": "idle", "message": "No active plan."}
        context = self.working_memory.get_active_context()
        if not self.planner.evaluate_plan(self.active_plan, context):
            return {"status": "error", "error": "Active plan is no longer valid for the current context."}
        step = self.active_plan.get_next_step()
        decision = {
            "action": step.get("action", "search"),
            "kwargs": {"query": step.get("query", ""), "page_size": 5},
            "context_used": context
        }
        action_result = {}
        try:
            result = self.router.execute(principal, decision["action"], decision["kwargs"])
            action_result = {"status": "success", "result": result, "context": context}
            self.active_plan.complete_current_step()
            self._retry_count = 0
            self._auto_checkpoint()
            self._fire_synapses(principal, context)
            if self.active_plan.is_complete():
                self._run_maintenance(principal)
        except ApprovalRequiredError as e:
            action_result = {"status": "blocked", "reason": str(e), "context": context}
        except Exception as e:
            action_result = {"status": "error", "error": str(e)}
            if self._retry_count < self._max_retries:
                self._retry_count += 1
                new_plan = self.planner.replan(self.active_plan.goal, context, decision, str(e))
                self.active_plan = new_plan
                action_result["replanned"] = True
                self._auto_checkpoint()

                # WIRE-C1.5b: a replan can change step_count/destructive_steps
                # (e.g. escalate into a destructive recovery path), so the
                # Council tier decided against the PRE-failure plan is stale
                # and must be re-derived from the NEW plan, not carried
                # forward silently. This closes the "same tier after replan"
                # gap: a task that becomes riskier after a failure must not
                # keep the tier that was fine for the original, simpler plan.
                post_replan_complexity = self.complexity_analyzer.analyze(self.active_plan)
                post_replan_report = self._dispatch_via_orchestrator(
                    principal, self.active_plan.goal, context,
                    complexity=post_replan_complexity.council_complexity,
                    require_review=post_replan_complexity.require_review,
                )
                if post_replan_report is not None:
                    post_replan_report["plan_complexity"] = {
                        "step_count": post_replan_complexity.step_count,
                        "execution_mode": post_replan_complexity.execution_mode.value,
                        "destructive_steps": post_replan_complexity.destructive_steps,
                    }
                    action_result["post_replan_dispatch_report"] = post_replan_report
        intent_mock = {"query": self.active_plan.goal if self.active_plan else "unknown"}
        try:
            new_memory_id = self.reflection.evaluate_outcome(principal, intent_mock, decision, action_result)
            if new_memory_id:
                action_result["reflection_memory_generated"] = new_memory_id
        except Exception:
            pass
        return action_result

    def _fire_synapses(self, principal: Principal, context: List[Dict[str, Any]]):
        if len(context) < 2:
            return
        try:
            first_id = context[0].get("id")
            second_id = context[1].get("id")
            if first_id and second_id:
                self.reflection.propose_synapse(principal, first_id, second_id)
        except Exception:
            pass

    def _run_maintenance(self, principal: Principal):
        try:
            self.consolidator.consolidate_lessons(principal)
        except Exception:
            pass
        try:
            self.deduplicator.scan_for_duplicates(principal)
        except Exception:
            pass
        try:
            self.learning_engine.promote_memories(principal)
        except Exception:
            pass

    def _estimate_complexity(self, query: str, context: List[Dict[str, Any]],
                              plan: Optional[ActivePlan] = None) -> int:
        if plan is not None:
            return 2 if len(plan.steps) >= self.COMPLEXITY_PLAN_STEP_THRESHOLD else 1
        word_count = len(str(query).split())
        if word_count >= self.COMPLEXITY_QUERY_WORD_THRESHOLD or len(context) >= self.COMPLEXITY_CONTEXT_SIZE_THRESHOLD:
            return 2
        return 1

    def _dispatch_via_orchestrator(self, principal: Principal, query: str,
                                    context: List[Dict[str, Any]], *,
                                    complexity: int = 1,
                                    require_review: bool = False) -> Optional[Dict[str, Any]]:
        decision = self.council_budget.decide(query, complexity=complexity, require_review=require_review)
        if not decision.should_dispatch:
            return None
        try:
            report = self.orchestrator.route_and_dispatch(
                principal, query, context,
                skip_retrieval=not decision.run_retrieval,
                run_verifier=decision.run_verifier,
                max_context_items=self.MAX_COUNCIL_MEMORY_RESULTS,
            )
            report["council_tier"] = decision.tier.value
            report["council_reason"] = decision.reason
            return report
        except Exception:
            return None

    def process_intent(self, principal: Principal, intent_text: str) -> Dict[str, Any]:
        intent = self._parse_intent(intent_text)
        query = intent.get("query", "")
        activated_nodes = self.activation_engine.activate_from_query(principal, query)
        recalled = self.recall_engine.recall(principal, query, activated_nodes, self.working_memory)
        nodes_for_wm = [(node, score) for node, score in recalled] if recalled else activated_nodes
        self.working_memory.admit(nodes_for_wm)
        context = self.working_memory.get_active_context()
        reasoning = self.reasoning_engine.synthesize(principal, context, query)
        self.active_plan = self.planner.create_plan(query, context)
        self._retry_count = 0

        plan_complexity = self.complexity_analyzer.analyze(self.active_plan)
        dispatch_report = self._dispatch_via_orchestrator(
            principal, query, context,
            complexity=plan_complexity.council_complexity,
            require_review=plan_complexity.require_review,
        )
        if dispatch_report is not None:
            dispatch_report["plan_complexity"] = {
                "step_count": plan_complexity.step_count,
                "execution_mode": plan_complexity.execution_mode.value,
                "destructive_steps": plan_complexity.destructive_steps,
            }

        if not self.planner.evaluate_plan(self.active_plan, context):
            error_result = {"status": "error", "error": "Could not generate a valid plan."}
            if dispatch_report is not None:
                error_result["dispatch_report"] = dispatch_report
            return error_result
        self._auto_checkpoint()
        step_result = self.step_loop(principal)
        if dispatch_report is not None:
            step_result["dispatch_report"] = dispatch_report
        return step_result
