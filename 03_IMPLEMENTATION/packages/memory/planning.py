import json
import os
from typing import List, Dict, Any

class ActivePlan:
    """
    Stateful tracking of a multi-step plan.
    """
    def __init__(self, goal: str, steps: List[Dict[str, Any]]):
        self.goal = goal
        self.steps = steps
        self.current_step_index = 0
        
    def get_next_step(self) -> Dict[str, Any]:
        if self.current_step_index < len(self.steps):
            return self.steps[self.current_step_index]
        return None
        
    def complete_current_step(self) -> None:
        self.current_step_index += 1
        
    def is_complete(self) -> bool:
        return self.current_step_index >= len(self.steps)

    def remaining_steps(self) -> int:
        return max(0, len(self.steps) - self.current_step_index)
        
    def save_state(self, filepath: str) -> None:
        state = {
            "goal": self.goal,
            "steps": self.steps,
            "current_step_index": self.current_step_index
        }
        dir_path = os.path.dirname(os.path.abspath(filepath))
        os.makedirs(dir_path, exist_ok=True)
        import tempfile
        fd, temp_path = tempfile.mkstemp(dir=dir_path, prefix=".tmp_plan_")
        try:
            with os.fdopen(fd, "w", encoding="utf-8") as f:
                json.dump(state, f, indent=2)
                f.flush()
                os.fsync(f.fileno())
            os.replace(temp_path, filepath)
        except Exception as e:
            if os.path.exists(temp_path):
                try:
                    os.remove(temp_path)
                except Exception:
                    pass
            raise e
            
    @classmethod
    def load_state(cls, filepath: str) -> 'ActivePlan':
        if not os.path.exists(filepath):
            return None
        with open(filepath, "r", encoding="utf-8") as f:
            state = json.load(f)
            
        plan = cls(state["goal"], state["steps"])
        plan.current_step_index = state.get("current_step_index", 0)
        return plan

class Planner:
    """
    Decomposes goals into a sequence of actionable steps or subgoals.
    WIRE-7: Now generates multi-step plans based on context and goal analysis.
    """
    def __init__(self):
        self.max_retries = 2

    def create_plan(self, goal: str, context: List[Dict[str, Any]]) -> ActivePlan:
        """
        Creates an ActivePlan based on the goal and active context.
        Generates multi-step plans when context provides actionable information.
        """
        # Check for high-risk actions that should be blocked
        if "delete_canonical" in goal:
            steps = [{"step": 1, "action": "delete_canonical", "query": goal,
                       "description": "Attempt destructive operation"}]
            return ActivePlan(goal, steps)

        steps = []

        # Step 1: Always search for relevant information
        steps.append({
            "step": 1,
            "action": "search",
            "query": goal,
            "description": "Retrieve relevant memories"
        })

        # Step 2: If context contains unverified items, add a verification step
        has_unverified = any(
            n.get("_cognitive_unverified") or n.get("verification") == "unverified"
            for n in context
        )
        if has_unverified:
            steps.append({
                "step": len(steps) + 1,
                "action": "search",
                "query": f"verify {goal}",
                "description": "Cross-reference unverified context"
            })

        # Step 3: If context has related nodes, search for deeper connections
        has_relations = any(len(n.get("relations", [])) > 0 for n in context)
        if has_relations:
            steps.append({
                "step": len(steps) + 1,
                "action": "search",
                "query": f"related {goal}",
                "description": "Explore related knowledge"
            })

        return ActivePlan(goal, steps)

    def replan(self, goal: str, context: List[Dict[str, Any]],
               failed_action: Dict[str, Any], error: str) -> ActivePlan:
        """
        WIRE-6: Creates an alternative plan after a failure.
        """
        steps = []

        # Reformulate the query to avoid the previous failure
        original_query = failed_action.get("query", goal)
        steps.append({
            "step": 1,
            "action": "search",
            "query": f"alternative {original_query}",
            "description": f"Retry after failure: {error[:80]}"
        })

        return ActivePlan(goal, steps)

    def evaluate_plan(self, plan: ActivePlan, context: List[Dict[str, Any]]) -> bool:
        """
        Validates if the plan is still sound given the current context.
        """
        return plan is not None and not plan.is_complete()
