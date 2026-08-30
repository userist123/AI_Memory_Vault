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

class Executive:
    """
    Central Cognitive Loop Orchestrator.
    Manages OODA-like sequence using all cognitive modules.
    
    WIRE-2: All Phase 3 modules are wired in.
    WIRE-5: Automatic checkpointing after each step.
    WIRE-6: Error recovery and replanning.
    WIRE-MAO: MultiAgentOrchestrator dispatch report attached to process_intent results.
    """
    def __init__(self, memory_controller: MemoryController, checkpoint_dir: str = None,
                 orchestrator: Optional[MultiAgentOrchestrator] = None):
        self.controller = memory_controller
        self.router = ToolRouter(self.controller)
        self.activation_engine = ActivationEngine(self.controller)
        self.working_memory = WorkingMemory(capacity=10)
        self.planner = Planner()
        self.reasoning_engine = ReasoningEngine(self.controller)
        self.reflection = ReflectionPipeline(self.controller)
        self.active_plan: Optional[ActivePlan] = None
        self.checkpoint_dir = checkpoint_dir

        # Phase 3 modules (WIRE-2)
        self.semantic_provider = DeterministicSemanticProvider()
        self.recall_engine = RecallEngine(self.controller, self.semantic_provider)
        self.consolidator = Consolidator(self.controller, self.router)
        self.deduplicator = Deduplicator(self.controller, self.semantic_provider, self.router)
        self.learning_engine = LearningEngine(self.controller, self.router)

        # WIRE-MAO: MultiAgentOrchestrator was implemented (Phase 4) with real
        # worker roles and least-privilege enforcement, but process_intent()
        # never called it -- route_and_dispatch() only had unit-test callers.
        # It is wired in here, sharing this Executive's own ToolRouter so its
        # RETRIEVAL worker's "search" call goes through the exact same
        # authorization/audit path as every other action in this loop, rather
        # than a second independent router instance with separate state.
        self.orchestrator = orchestrator or MultiAgentOrchestrator(self.controller, self.router)

        # WIRE-6: retry tracking
        self._retry_count = 0
        self._max_retries = 2

    def save_state(self, base_dir: str = None):
        """Saves WM and ActivePlan."""
        base_dir = base_dir or self.checkpoint_dir
        if not base_dir:
            return
        os.makedirs(base_dir, exist_ok=True)
        self.working_memory.save_state(os.path.join(base_dir, "wm.json"))
        if self.active_plan:
            self.active_plan.save_state(os.path.join(base_dir, "plan.json"))

    def load_state(self, base_dir: str, principal: Principal):
        """Loads WM and ActivePlan."""
        self.checkpoint_dir = base_dir
        wm_path = os.path.join(base_dir, "wm.json")
        if os.path.exists(wm_path):
            self.working_memory.load_state(wm_path, self.controller, principal)
            
        plan_path = os.path.join(base_dir, "plan.json")
        if os.path.exists(plan_path):
            self.active_plan = ActivePlan.load_state(plan_path)

    def _auto_checkpoint(self):
        """WIRE-5: Automatically checkpoint after each step completion."""
        if self.checkpoint_dir:
            self.save_state()

    def _parse_intent(self, intent: str) -> Dict[str, Any]:
        return {"query": intent, "type": "task"}
        
    def step_loop(self, principal: Principal) -> Dict[str, Any]:
        """
        Executes the next step of the active plan.
        WIRE-5: Auto-checkpoints after each successful step.
        WIRE-6: Replans on failure up to max_retries.
        """
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
        
        # Act
        action_result = {}
        try:
            result = self.router.execute(principal, decision["action"], decision["kwargs"])
            action_result = {
                "status": "success",
                "result": result,
                "context": context
            }
            self.active_plan.complete_current_step()
            self._retry_count = 0  # Reset on success

            # WIRE-5: Auto-checkpoint after successful step
            self._auto_checkpoint()

            # WIRE-2: Fire dynamic synapses on success
            self._fire_synapses(principal, context)

            # Trigger maintenance if plan completed
            if self.active_plan.is_complete():
                self._run_maintenance(principal)
            
        except ApprovalRequiredError as e:
            action_result = {
                "status": "blocked",
                "reason": str(e),
                "context": context
            }
        except Exception as e:
            action_result = {
                "status": "error",
                "error": str(e)
            }
            
            # WIRE-6: Attempt replanning on error
            if self._retry_count < self._max_retries:
                self._retry_count += 1
                new_plan = self.planner.replan(
                    self.active_plan.goal, context, decision, str(e)
                )
                self.active_plan = new_plan
                action_result["replanned"] = True
                self._auto_checkpoint()
            
        # Reflect & Learn
        intent_mock = {"query": self.active_plan.goal if self.active_plan else "unknown"}
        try:
            new_memory_id = self.reflection.evaluate_outcome(principal, intent_mock, decision, action_result)
            if new_memory_id:
                action_result["reflection_memory_generated"] = new_memory_id
        except Exception:
            # WIRE-6: Reflection failure must not kill the loop
            pass
            
        return action_result

    def _fire_synapses(self, principal: Principal, context: List[Dict[str, Any]]):
        """WIRE-2: Create dynamic synapses between co-activated nodes."""
        if len(context) < 2:
            return
        # Link the first node to the second (minimal synapse creation)
        try:
            first_id = context[0].get("id")
            second_id = context[1].get("id")
            if first_id and second_id:
                self.reflection.propose_synapse(principal, first_id, second_id)
        except Exception:
            pass

    def _run_maintenance(self, principal: Principal):
        """WIRE-2: Run post-task maintenance (consolidation, dedup, learning)."""
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

    def _dispatch_via_orchestrator(self, principal: Principal, query: str,
                                    context: List[Dict[str, Any]]) -> Optional[Dict[str, Any]]:
        """WIRE-MAO: Run the Router/Retrieval/Verifier/Synthesis worker pipeline.

        This is additive telemetry, not a security gate: MultiAgentOrchestrator
        already enforces least-privilege internally (a worker attempting an
        action outside its allowed_actions raises PermissionError), so a
        failure here is a bug or unexpected internal state, not an intentional
        authorization block. Consistent with _fire_synapses/_run_maintenance
        above, a failure must not kill the primary cognitive loop -- it is
        caught and the loop proceeds without a dispatch_report rather than
        aborting process_intent entirely.
        """
        try:
            # skip_retrieval=True: `context` here already comes from this
            # Executive's own ActivationEngine + RecallEngine pass over this
            # exact `query` (see process_intent steps 2-3, just above the
            # call site). Without this flag, any query containing a
            # deep-retrieval keyword would make route_and_dispatch fire a
            # second, redundant live "search" through the same ToolRouter for
            # the same query -- doubling retrieval cost with no new signal.
            return self.orchestrator.route_and_dispatch(principal, query, context, skip_retrieval=True)
        except Exception:
            return None

    def process_intent(self, principal: Principal, intent_text: str) -> Dict[str, Any]:
        """
        Full cognitive loop:
        1. Observe (Parse Intent)
        2. Retrieve & Activate (with RecallEngine scoring)
        3. Attend & Hold in WM
        3b. Dispatch via MultiAgentOrchestrator (Router/Retrieval/Verifier/Synthesis)
        4. Reason (marks unverified context)
        5. Plan (multi-step, context-aware)
        6. Execute first step
        """
        # 1. Observe
        intent = self._parse_intent(intent_text)
        query = intent.get("query", "")
        
        # 2. Retrieve & Activate
        activated_nodes = self.activation_engine.activate_from_query(principal, query)
        
        # WIRE-9/WIRE-2: Apply RecallEngine scoring on top of activation
        recalled = self.recall_engine.recall(
            principal, query, activated_nodes, self.working_memory
        )
        # Use recalled ordering for WM admission
        nodes_for_wm = [(node, score) for node, score in recalled] if recalled else activated_nodes
        
        # 3. Attend & Hold in WM
        self.working_memory.admit(nodes_for_wm)
        context = self.working_memory.get_active_context()

        # 3b. WIRE-MAO: Multi-Agent Orchestrator dispatch (previously unwired).
        # Uses the same `query` and `context` already assembled above; does not
        # duplicate the activation/recall work, only adds Router-triage,
        # (conditional) deep-retrieval, Verifier tally, and Synthesis summary.
        dispatch_report = self._dispatch_via_orchestrator(principal, query, context)

        # 4. Reason (READ-ONLY, aware of unverified status)
        reasoning = self.reasoning_engine.synthesize(principal, context, query)
        
        # 5. Plan (multi-step, context-aware)
        self.active_plan = self.planner.create_plan(query, context)
        self._retry_count = 0
        
        if not self.planner.evaluate_plan(self.active_plan, context):
            error_result = {"status": "error", "error": "Could not generate a valid plan."}
            if dispatch_report is not None:
                error_result["dispatch_report"] = dispatch_report
            return error_result

        # WIRE-5: Checkpoint the initial plan
        self._auto_checkpoint()
            
        # 6. Execute first step
        step_result = self.step_loop(principal)
        if dispatch_report is not None:
            step_result["dispatch_report"] = dispatch_report
        return step_result
