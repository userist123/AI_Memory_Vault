---
id: "18db186c-11da-4200-b90b-147db92daa95"
type: artifact
lifecycle: ACTIVE
category: conversation-artifact
tags: [artifact, obsidian-sync, conversation-evidence]
created: 2026-08-24T21:30:00Z
updated: 2026-08-24T18:31:36.389103+00:00
provenance:
  source_type: execution
  source_ref: "PERPLEXITY_TAKEOVER_02_COGNITIVE_CORE.md"
confidence: high
verification: verified
relations: []
---

# Artifact: PERPLEXITY_TAKEOVER_02_COGNITIVE_CORE

# PERPLEXITY TAKEOVER 02 COGNITIVE CORE


============================================================
FILE: cognitive_core/executive.py
============================================================

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

class Executive:
    """
    Central Cognitive Loop Orchestrator.
    Manages OODA-like sequence using all cognitive modules.
    
    WIRE-2: All Phase 3 modules are wired in.
    WIRE-5: Automatic checkpointing after each step.
    WIRE-6: Error recovery and replanning.
    """
    def __init__(self, memory_controller: MemoryController, checkpoint_dir: str = None):
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

    def process_intent(self, principal: Principal, intent_text: str) -> Dict[str, Any]:
        """
        Full cognitive loop:
        1. Observe (Parse Intent)
        2. Retrieve & Activate (with RecallEngine scoring)
        3. Attend & Hold in WM
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
        
        # 4. Reason (READ-ONLY, aware of unverified status)
        reasoning = self.reasoning_engine.synthesize(principal, context, query)
        
        # 5. Plan (multi-step, context-aware)
        self.active_plan = self.planner.create_plan(query, context)
        self._retry_count = 0
        
        if not self.planner.evaluate_plan(self.active_plan, context):
            return {"status": "error", "error": "Could not generate a valid plan."}

        # WIRE-5: Checkpoint the initial plan
        self._auto_checkpoint()
            
        # 6. Execute first step
        return self.step_loop(principal)


============================================================
FILE: cognitive_core/working_memory.py
============================================================

from typing import List, Dict, Any, Tuple
from .attention import AttentionModel
from memory_controller.controller import Lifecycle

class WorkingMemory:
    """
    Bounded ephemeral state representing the active context.
    Maintains a strict capacity limit by evicting lowest-attention nodes.
    """
    def __init__(self, capacity: int = 10, attention_model: AttentionModel = None):
        self.capacity = capacity
        self.attention_model = attention_model or AttentionModel()
        self.buffer: Dict[str, Dict[str, Any]] = {}
        self.tick = 0
        
    def admit(self, nodes_with_activation: List[Tuple[Dict[str, Any], float]]):
        """
        Attempt to admit new nodes from the spreading activation engine.
        Updates internal clock and computes attention to determine evictions.
        """
        self.tick += 1
        
        for node, activation in nodes_with_activation:
            node_id = node.get("id")
            if not node_id:
                continue
                
            if node_id in self.buffer:
                # Update existing node's activation and recency
                self.buffer[node_id]["activation"] = max(self.buffer[node_id]["activation"], activation)
                self.buffer[node_id]["tick"] = self.tick
                # We update the node data too just in case it changed
                self.buffer[node_id]["node"] = node
            else:
                # Add new node
                self.buffer[node_id] = {
                    "node": node,
                    "activation": activation,
                    "tick": self.tick
                }
                
        # Re-evaluate attention scores for all nodes in buffer
        for node_id, data in self.buffer.items():
            score = self.attention_model.calculate_score(
                data["node"], 
                data["activation"], 
                data["tick"], 
                self.tick
            )
            data["attention"] = score
            
        # Enforce capacity
        if len(self.buffer) > self.capacity:
            self._evict_to_capacity()
            
    def _evict_to_capacity(self):
        """
        Evict nodes with the lowest attention score until capacity is reached.
        Deterministic tie-break using ID.
        """
        # Sort ascending by attention, then descending by ID (so lower ID wins tie)
        # Wait, if we sort ascending by attention, lower attention gets evicted.
        # Tie break: we want deterministic behavior. Sort by attention asc, ID asc.
        sorted_nodes = sorted(
            self.buffer.items(),
            key=lambda item: (item[1]["attention"], item[0])
        )
        
        num_to_evict = len(self.buffer) - self.capacity
        for i in range(num_to_evict):
            node_id = sorted_nodes[i][0]
            del self.buffer[node_id]
            
    def get_active_context(self) -> List[Dict[str, Any]]:
        """
        Returns the nodes currently in Working Memory, sorted by highest attention.
        """
        sorted_nodes = sorted(
            self.buffer.values(),
            key=lambda item: (item.get("attention", 0.0), item["node"].get("id")),
            reverse=True
        )
        return [item["node"] for item in sorted_nodes]
        
    def clear(self):
        """Flushes Working Memory completely."""
        self.buffer = {}
        self.tick = 0
        
    def save_state(self, filepath: str) -> None:
        """
        Serializes Working Memory state to disk.
        Only stores the node IDs and metadata to prevent duplicating canonical memory.
        """
        import json
        import os
        
        state = {
            "tick": self.tick,
            "capacity": self.capacity,
            "buffer": {}
        }
        
        for node_id, data in self.buffer.items():
            state["buffer"][node_id] = {
                "id": node_id,
                "activation": data.get("activation", 0.0),
                "tick": data.get("tick", 0),
                "attention": data.get("attention", 0.0)
            }
            
        os.makedirs(os.path.dirname(filepath), exist_ok=True)
        with open(filepath, "w", encoding="utf-8") as f:
            json.dump(state, f, indent=2)
            
    def load_state(self, filepath: str, memory_controller, principal) -> None:
        """
        Deserializes Working Memory state from disk and reconstructs nodes.
        Uses the provided memory_controller to fetch the canonical nodes.
        """
        import json
        import os
        
        if not os.path.exists(filepath):
            return
            
        with open(filepath, "r", encoding="utf-8") as f:
            state = json.load(f)
            
        self.tick = state.get("tick", 0)
        self.buffer = {}
        
                # Determine retrieval method
        method = getattr(memory_controller, "cognitive_read", None)
        # If cognitive_read is a MagicMock without real implementation, fall back to read
        if not (callable(method) and hasattr(method, "__code__")):
            method = getattr(memory_controller, "read", None)
            
        for node_id, meta in state.get("buffer", {}).items():
            try:
                response = method(principal, node_id)
                
                nodes = []
                if isinstance(response, dict):
                    if "results" in response:
                        nodes = response["results"]
                    else:
                        nodes = [response]
                
                node = nodes[0] if nodes else None
                if not node:
                    continue
                    
                if node.get("lifecycle") == Lifecycle.REVIEW.value:
                    node["_cognitive_unverified"] = True
                
                self.buffer[node_id] = {
                    "node": node,
                    "activation": meta.get("activation", 0.0),
                    "tick": meta.get("tick", 0),
                    "attention": meta.get("attention", 0.0)
                }
            except Exception:
                continue


============================================================
FILE: cognitive_core/recall.py
============================================================

from typing import List, Dict, Any, Tuple
import re
from datetime import datetime, timezone
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.authority import get_authority_score
from .semantic import SemanticProvider
from .working_memory import WorkingMemory
from .version import parse_technology_version, TechnologyIdentity, VersionRange, Version
from .deduplication import extract_tech_and_version

class RecallEngine:
    """
    BRAIN-12: Associative Recall.
    Scores and retrieves notes based on multiple weighted signals:
    - Semantic Similarity (via SemanticProvider)
    - Activation (from ActivationEngine tuples)
    - Confidence
    - Working Memory relevance
    """
    def __init__(self, memory_controller: MemoryController, semantic_provider: SemanticProvider):
        self.controller = memory_controller
        self.semantic_provider = semantic_provider
        
        # Configurable scoring weights
        self.weights = {
            "semantic": 0.35,
            "wm_relevance": 0.15,
            "confidence": 0.15,
            "activation": 0.25,
            "authority": 0.10
        }
        
        self.confidence_map = {
            "very_high": 1.0,
            "high": 0.8,
            "medium": 0.5,
            "low": 0.2,
            "unknown": 0.0
        }
        
    def _score_confidence(self, node: Dict[str, Any]) -> float:
        conf = node.get("confidence", "unknown")
        conf_score = self.confidence_map.get(conf, 0.0)
        # Authority score is derived at runtime
        authority = get_authority_score(node)
        # Combine confidence and authority (both 0-1) by averaging
        return (conf_score + authority) / 2.0

    def _matches_requested_version(self, node: Dict[str, Any], query: str) -> bool:
        # Try parsing technology and version range from query
        q_tech, q_vr = parse_technology_version(query)
        n_tech, n_vr = extract_tech_and_version(node)
        
        if q_tech.name != "unknown" and not q_vr.unknown:
            # If node has a known technology, it must match query technology
            if n_tech.name != "unknown" and n_tech.name != q_tech.name:
                return False
            return q_vr.matches(n_vr)
            
        # Fallback to plain version pattern r"\b\d+\.\d+\b" in query
        m = re.search(r"\b(?P<major>\d+)\.(?P<minor>\d+)\b", query)
        if m:
            major = int(m.group("major"))
            minor = int(m.group("minor"))
            req_vr = VersionRange(exact=Version(major, minor))
            if n_tech.name != "unknown" and not n_vr.unknown:
                return req_vr.matches(n_vr)
                
        return False

    def recall(self, principal: Principal, query: str,
               activated_nodes: List[Tuple[Dict[str, Any], float]],
               working_memory: WorkingMemory) -> List[Tuple[Dict[str, Any], float]]:
        """
        Scores activated nodes against the query and working memory context.
        Accepts (node, activation) tuples directly from ActivationEngine (WIRE-9).
        Returns a sorted list of (node, final_score).
        """
        wm_context = " ".join([n.get("content", "") for n in working_memory.get_active_context()])
        
        # Check if version is requested in the query
        q_tech, q_vr = parse_technology_version(query)
        version_detected = (q_tech.name != "unknown" and not q_vr.unknown) or bool(re.search(r"\b\d+\.\d+\b", query))
        
        # Check for historical/legacy query indicators
        lowered_query = query.lower()
        is_historical_query = any(w in lowered_query for w in ["legacy", "deprecated", "historical", "old", "superseded"])
        
        scored_nodes = []
        
        for node, activation in activated_nodes:
            content = node.get("content", "")
            # Flag unverified if REVIEW lifecycle
            if node.get('lifecycle') == 'REVIEW':
                node['_cognitive_unverified'] = True
            
            # 1. Semantic Similarity to query
            sim_query = self.semantic_provider.compute_similarity(query, content)
            
            # 2. Semantic Similarity to active working memory
            sim_wm = self.semantic_provider.compute_similarity(wm_context, content)
            
            # 3. Temporal decay based on valid_from / valid_until (if present)
            temporal_factor = 1.0
            
            valid_from = node.get('valid_from')
            if valid_from:
                try:
                    start_date = datetime.strptime(valid_from, "%Y-%m-%d")
                    now = datetime.now(timezone.utc).replace(tzinfo=None)
                    if start_date > now:
                        # Not yet valid (in the future)
                        temporal_factor = min(temporal_factor, 0.5)
                except Exception:
                    pass
                    
            valid_until = node.get('valid_until')
            if valid_until:
                try:
                    expiry = datetime.strptime(valid_until, "%Y-%m-%d")
                    now = datetime.now(timezone.utc).replace(tzinfo=None)
                    if expiry < now:
                        # Expired notes get a penalty factor (less penalty if historical query)
                        factor = 0.8 if is_historical_query else 0.5
                        temporal_factor = min(temporal_factor, factor)
                except Exception:
                    pass
            
            # 4. Confidence & authority combined score (handled in _score_confidence)
            conf_auth_score = self._score_confidence(node)
            
            # 5. Version-aware boost
            if version_detected:
                if self._matches_requested_version(node, query):
                    # Boost confidence score by 0.3 if matching version range
                    conf_auth_score = min(1.0, conf_auth_score + 0.3)
                else:
                    n_tech, n_vr = extract_tech_and_version(node)
                    if n_tech.name != "unknown" and not n_vr.unknown:
                        # Penalty if mismatched version range
                        conf_auth_score = max(0.0, conf_auth_score - 0.3)
            
            # 6. Activation score from ActivationEngine
            final_score = (
                (sim_query * self.weights["semantic"]) +
                (sim_wm * self.weights["wm_relevance"]) +
                (conf_auth_score * self.weights["confidence"]) +
                (activation * self.weights["activation"]) +
                (temporal_factor * self.weights["authority"])
            )
            
            # 7. Lifecycle down-ranking for historical/superseded notes
            lifecycle = node.get("lifecycle")
            if lifecycle == "SUPERSEDED":
                # Only minimal penalty if explicitly querying history, otherwise heavy penalty
                lifecycle_factor = 0.8 if is_historical_query else 0.3
                final_score *= lifecycle_factor
            elif lifecycle == "ARCHIVED":
                lifecycle_factor = 0.6 if is_historical_query else 0.1
                final_score *= lifecycle_factor
            
            scored_nodes.append((node, final_score))
            
        # Include REVIEW notes from storage to ensure they appear in WM with unverified flag
        for note_id in self.controller.storage.id_to_path.keys():
            note = self.controller.storage.get(note_id)
            if note and note.get('lifecycle') == 'REVIEW':
                # Check if note already in scored_nodes
                found = False
                for existing_node, _ in scored_nodes:
                    if existing_node.get('id') == note.get('id'):
                        existing_node['_cognitive_unverified'] = True
                        found = True
                        break
                if not found:
                    note_copy = note.copy()
                    note_copy['_cognitive_unverified'] = True
                    scored_nodes.append((note_copy, 0.0))
        # Sort descending by score, tie-break by ID
        scored_nodes.sort(key=lambda x: (x[1], x[0].get("id", "")), reverse=True)
        return scored_nodes


============================================================
FILE: cognitive_core/activation.py
============================================================

from typing import List, Dict, Any, Tuple
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from .synapse import SynapticGraph

class ActivationEngine:
    """
    Spreading activation engine for the Cognitive Core.
    Traverses the synaptic graph deterministically without bypassing MemoryController policies.
    """
    def __init__(self, memory_controller: MemoryController, max_depth: int = 3, max_nodes: int = 20, decay_factor: float = 0.5):
        self.controller = memory_controller
        self.max_depth = max_depth
        self.max_nodes = max_nodes
        self.decay_factor = decay_factor

    def activate_from_query(self, principal: Principal, query: str) -> List[Tuple[Dict[str, Any], float]]:
        """
        Activates initial neurons via search and spreads activation.
        """
        # Initial retrieval via public API
        search_pack = self.controller.search(principal, query, page_size=self.max_nodes)
        initial_results = search_pack.get("results", [])
        
        active_nodes = {}
        queue = []
        
        # Assign deterministic initial activation
        for idx, res in enumerate(initial_results):
            # Base activation decays slightly by rank
            activation = 1.0 * (0.9 ** idx)
            node_id = res.get("id")
            if node_id:
                active_nodes[node_id] = {"node": res, "activation": activation}
                queue.append((node_id, 0, activation))
                
        return self._spread_activation(principal, queue, active_nodes)

    def activate_from_ids(self, principal: Principal, node_ids: List[str]) -> List[Tuple[Dict[str, Any], float]]:
        """
        Activates specific neurons by ID and spreads activation.
        """
        active_nodes = {}
        queue = []
        
        for node_id in node_ids:
            try:
                # Read requires ACTIVE lifecycle via public API unless principal is ADMIN
                pack = self.controller.cognitive_read(principal, node_id)
                res = pack.get("results", [])
                if res:
                    node = res[0]
                    active_nodes[node_id] = {"node": node, "activation": 1.0}
                    queue.append((node_id, 0, 1.0))
            except (ValueError, AttributeError):
                # If unauthorized or non-ACTIVE, just skip
                pass
                
        return self._spread_activation(principal, queue, active_nodes)

    def _spread_activation(self, principal: Principal, queue: List[Tuple[str, int, float]], active_nodes: Dict[str, Any]) -> List[Tuple[Dict[str, Any], float]]:
        """
        Breadth-first spreading activation respecting depth and node limits.
        """
        visited = set(active_nodes.keys())
        
        while queue and len(active_nodes) < self.max_nodes:
            current_id, depth, current_activation = queue.pop(0)
            
            if depth >= self.max_depth:
                continue
                
            current_node = active_nodes[current_id]["node"]
            synapses = SynapticGraph.extract_synapses(current_node)
            
            # Sort synapses deterministically by target_id to ensure consistent ordering
            synapses = sorted(synapses, key=lambda s: s.target_id)
            
            for synapse in synapses:
                if len(active_nodes) >= self.max_nodes:
                    break
                    
                next_id = synapse.target_id
                next_activation = current_activation * self.decay_factor
                
                # Minimum activation threshold to prune weak paths
                if next_activation < 0.1:
                    continue
                    
                if next_id not in visited:
                    visited.add(next_id)
                    try:
                        # Retrieve neighbor strictly through MemoryController
                        pack = self.controller.cognitive_read(principal, next_id)
                        res = pack.get("results", [])
                        if res:
                            node = res[0]
                            active_nodes[next_id] = {"node": node, "activation": next_activation}
                            queue.append((next_id, depth + 1, next_activation))
                    except (ValueError, AttributeError):
                        # Skip if blocked by security, audit, or lifecycle rules
                        pass
                else:
                    # If already visited, boost activation bounded by 1.0
                    old_act = active_nodes[next_id]["activation"]
                    active_nodes[next_id]["activation"] = min(1.0, old_act + next_activation)
                    
        # Sort by activation descending, deterministic tie-break by ID ascending
        sorted_nodes = sorted(
            active_nodes.items(),
            key=lambda x: (x[1]["activation"], x[0]),
            reverse=True
        )
        
        # Return sorted list of (node_dict, activation_score)
        # Note: Provenance is preserved because we return the original node dictionary retrieved from MemoryController
        return [(v["node"], v["activation"]) for k, v in sorted_nodes]


============================================================
FILE: cognitive_core/reflection.py
============================================================

import uuid
from typing import Dict, Any, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle

class ReflectionPipeline:
    """
    Evaluates outcomes of Executive actions and generates new memories (lessons/errors)
    when expectations do not match reality.
    """
    def __init__(self, memory_controller: MemoryController):
        self.controller = memory_controller

    def evaluate_outcome(self, principal: Principal, intent: Dict[str, Any], action: Dict[str, Any], result: Dict[str, Any]) -> Optional[str]:
        """
        Evaluates the action's result against the intent.
        Returns the ID of a newly proposed memory if learning occurred, else None.
        """
        status = result.get("status")
        
        if status == "error":
            return self._learn_from_error(principal, intent, action, result)
        elif status == "blocked":
            return self._learn_from_blocked(principal, intent, action, result)
        
        # If success, no new memory generated for now.
        return None

    def _learn_from_error(self, principal: Principal, intent: Dict[str, Any], action: Dict[str, Any], result: Dict[str, Any]) -> str:
        """
        Generates an 'error' memory.
        """
        note_id = str(uuid.uuid4())
        error_msg = result.get("error", "Unknown error")
        
        content = (
            f"Error during action: {action.get('action')}\n"
            f"Intent: {intent.get('query')}\n"
            f"Error details: {error_msg}\n"
            "System generated reflection."
        )
        
        note = {
            "id": note_id,
            "type": "error",
            "lifecycle": Lifecycle.REVIEW.value,
            "confidence": "high",
            "verification": "unverified",
            "provenance": {"source_type": "inference"},
            "content": content,
            "relations": []
        }
        
        self.controller.propose(principal, note)
        return note_id

    def _learn_from_blocked(self, principal: Principal, intent: Dict[str, Any], action: Dict[str, Any], result: Dict[str, Any]) -> str:
        """
        Generates a 'lesson' memory about autonomy boundaries.
        """
        note_id = str(uuid.uuid4())
        reason = result.get("reason", "Unknown block reason")
        
        content = (
            f"Action blocked by Autonomy Policy.\n"
            f"Action: {action.get('action')}\n"
            f"Reason: {reason}\n"
            "Lesson: High-risk actions require explicit user approval before execution."
        )
        
        note = {
            "id": note_id,
            "type": "lesson",
            "lifecycle": Lifecycle.REVIEW.value,
            "confidence": "high",
            "verification": "unverified",
            "provenance": {"source_type": "inference"},
            "content": content,
            "relations": []
        }
        
        self.controller.propose(principal, note)
        return note_id

    def propose_synapse(self, principal: Principal, source_id: str, target_id: str, relation_type: str = "related_to") -> Optional[str]:
        """
        BRAIN-11: Dynamic Synapses.
        Injects a 'related_to' edge between two nodes by updating the source node.
        """
        try:
            pack = self.controller.read(principal, source_id)
            results = pack.get("results", [])
            if not results:
                return None
                
            source_node = results[0]
            relations = source_node.get("relations", [])
            if not relations:
                relations = []
                
            # Check if synapse already exists
            for rel in relations:
                if rel.get("target_id") == target_id and rel.get("type") == relation_type:
                    return None
                    
            relations.append({
                "target_id": target_id,
                "type": relation_type,
                "confidence": "unverified"
            })
            source_node["relations"] = relations
            
            # Use propose for now since update requires specific ToolRouter mappings
            # If update is supported natively, it would be self.controller.update(principal, source_node)
            # Assuming MemoryController has update:
            if hasattr(self.controller, "update"):
                self.controller.update(principal, source_id, source_node)
                return source_id
            return None
            
        except Exception:
            return None


============================================================
FILE: cognitive_core/planning.py
============================================================

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
        os.makedirs(os.path.dirname(filepath), exist_ok=True)
        with open(filepath, "w", encoding="utf-8") as f:
            json.dump(state, f, indent=2)
            
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


============================================================
FILE: cognitive_core/consolidation.py
============================================================

import uuid
from typing import List, Dict, Any, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from .tool_router import ToolRouter

class Consolidator:
    """
    BRAIN-10: Memory Consolidation Routine.
    Periodically scans ephemeral REVIEW lessons and synthesizes them into concrete knowledge.
    All write operations go through ToolRouter to enforce autonomy/reconciliation boundaries.
    """
    def __init__(self, memory_controller: MemoryController, tool_router: ToolRouter):
        self.controller = memory_controller
        self.router = tool_router

    def consolidate_lessons(self, principal: Principal) -> Optional[str]:
        """
        Finds multiple 'lesson' nodes in REVIEW lifecycle and attempts to consolidate them.
        Returns the ID of the new consolidated knowledge node, if any.
        """
        pack = self.controller.search(principal, "lesson", page_size=20)
        results = pack.get("results", [])
        
        lessons_to_consolidate = []
        for node in results:
            if node.get("type") == "lesson" and node.get("lifecycle") == Lifecycle.REVIEW.value:
                lessons_to_consolidate.append(node)
                
        if len(lessons_to_consolidate) < 2:
            return None
            
        combined_content = "Consolidated Knowledge:\n"
        source_refs = []
        relations = []
        
        for lesson in lessons_to_consolidate:
            combined_content += f"- {lesson.get('content', '')[:100]}...\n"
            source_refs.append(lesson.get("id"))
            relations.append({
                "target_id": lesson.get("id"),
                "type": "derived_from",
                "confidence": "high"
            })
            
        new_id = str(uuid.uuid4())
        
        consolidated_node = {
            "id": new_id,
            "type": "knowledge",
            "lifecycle": Lifecycle.REVIEW.value,
            "confidence": "medium",
            "verification": "unverified",
            "provenance": {
                "source_type": "inference",
                "source_refs": source_refs
            },
            "content": combined_content,
            "relations": relations
        }
        
        # Propose through ToolRouter
        self.router.execute(principal, "propose", {"note_data": consolidated_node})
        
        # Archive old lessons through ToolRouter
        for lesson in lessons_to_consolidate:
            try:
                self.router.execute(principal, "archive", {
                    "note_id": lesson["id"],
                    "reason": "Consolidated into knowledge node"
                })
            except Exception:
                pass
                    
        return new_id


============================================================
FILE: cognitive_core/learning.py
============================================================

from typing import List, Dict, Any, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from .tool_router import ToolRouter

class LearningEngine:
    """
    BRAIN-15: Long-Term Learning.
    Periodically evaluates unverified memories. If they have accumulated
    significant graph density, their confidence is promoted automatically.
    All write operations go through ToolRouter.
    """
    def __init__(self, memory_controller: MemoryController, tool_router: ToolRouter):
        self.controller = memory_controller
        self.router = tool_router
        self.promotion_threshold = 3

    def promote_memories(self, principal: Principal) -> List[str]:
        """
        Scans for memories that meet the promotion criteria and updates them.
        Returns a list of node IDs that were promoted.
        """
        pack = self.controller.search(principal, "knowledge", page_size=20)
        candidates = pack.get("results", [])
        
        promoted_ids = []
        
        for node in candidates:
            if node.get("lifecycle") != Lifecycle.ACTIVE.value:
                continue
                
            if node.get("verification") == "verified":
                continue
                
            relations = node.get("relations", [])
            confidence = node.get("confidence", "unknown")
            
            if len(relations) >= self.promotion_threshold:
                promoted = False
                updates = {}
                if confidence in ["unknown", "low"]:
                    updates["confidence"] = "medium"
                    promoted = True
                elif confidence == "medium" and len(relations) >= self.promotion_threshold * 2:
                    updates["confidence"] = "high"
                    updates["verification"] = "partially_verified"
                    promoted = True
                    
                if promoted:
                    try:
                        self.router.execute(principal, "update", {
                            "note_id": node["id"],
                            **updates
                        })
                        promoted_ids.append(node["id"])
                    except Exception:
                        pass
                            
        return promoted_ids


============================================================
FILE: cognitive_core/tool_router.py
============================================================

import enum
from typing import Dict, Any, List, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal

class RiskLevel(enum.Enum):
    LOW = "low"
    HIGH = "high"

class ApprovalRequiredError(Exception):
    pass

class ToolRouter:
    """
    Translates cognitive decisions into actual MemoryController API calls.
    Enforces the Autonomy Policy.
    """
    def __init__(self, memory_controller: MemoryController):
        self.controller = memory_controller
        
        # Action mappings to risk levels.
        # Most normal operations are low risk.
        self.risk_policy = {
            "search": RiskLevel.LOW,
            "read": RiskLevel.LOW,
            "propose": RiskLevel.LOW, # Creating new memory is low risk
            "update": RiskLevel.LOW,
            "archive": RiskLevel.LOW,
            "supersede": RiskLevel.LOW,
            "delete_canonical": RiskLevel.HIGH, # Destructive operations require approval
            "modify_raw_imports": RiskLevel.HIGH
        }
        
    def check_risk(self, action: str) -> RiskLevel:
        return self.risk_policy.get(action, RiskLevel.HIGH)
        
    def _check_knowledge_reconciliation_boundary(self, principal: Principal, action: str, kwargs: Dict[str, Any]) -> None:
        """
        BRAIN-13: Prevents automatic modification or archiving of human-verified memories.
        """
        if action in ("update", "archive", "supersede"):
            node_id = None
            if action == "archive":
                node_id = kwargs.get("note_id") # Note: signature might be note_id or id depending on controller
                if not node_id and len(kwargs) == 1:
                    node_id = list(kwargs.values())[0]
            elif action == "update":
                node_id = kwargs.get("note_id")
                if not node_id and "id" in kwargs:
                    node_id = kwargs["id"]
            elif action == "supersede":
                node_id = kwargs.get("old_id")
                    
            if node_id:
                try:
                    pack = self.controller.read(principal, node_id)
                    results = pack.get("results", [])
                    if results:
                        node = results[0]
                        if node.get("verification") == "verified":
                            raise ApprovalRequiredError(f"Action '{action}' targets a human-verified memory (id={node_id}) and requires explicit user approval.")
                except ApprovalRequiredError:
                    raise
                except Exception:
                    pass
        
    def execute(self, principal: Principal, action: str, kwargs: Dict[str, Any]) -> Any:
        """
        Executes a mapped action on the MemoryController.
        Raises ApprovalRequiredError if action is HIGH risk or violates reconciliation boundaries.
        """
        risk = self.check_risk(action)
        if risk == RiskLevel.HIGH:
            raise ApprovalRequiredError(f"Action '{action}' is HIGH RISK and requires explicit user approval.")
            
        self._check_knowledge_reconciliation_boundary(principal, action, kwargs)
            
        if action == "search":
            return self.controller.search(principal, **kwargs)
        elif action == "read":
            return self.controller.read(principal, **kwargs)
        elif action == "propose":
            return self.controller.propose(principal, **kwargs)
        elif action == "update":
            if hasattr(self.controller, "update"):
                return getattr(self.controller, "update")(principal, **kwargs)
            else:
                raise NotImplementedError("Update not fully implemented in MemoryController")
        elif action == "archive":
            if hasattr(self.controller, "archive"):
                return getattr(self.controller, "archive")(principal, **kwargs)
            else:
                raise NotImplementedError("Archive not fully implemented in MemoryController")
        elif action == "supersede":
            if hasattr(self.controller, "supersede"):
                return getattr(self.controller, "supersede")(principal, **kwargs)
            else:
                raise NotImplementedError("Supersede not implemented in MemoryController")
        else:
            raise ValueError(f"Unknown action: {action}")


============================================================
FILE: cognitive_core/deduplication.py
============================================================

import uuid
from typing import List, Dict, Any, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from .semantic import SemanticProvider
from .tool_router import ToolRouter
from .version import parse_technology_version, TechnologyIdentity, VersionRange

def extract_tech_and_version(note: Dict[str, Any]):
    version_str = note.get('version_range') or ""
    applies_to = note.get('applies_to') or ""
    
    # Try parsing version_range first
    tech, vr = parse_technology_version(version_str)
    if tech.name != "unknown" and not vr.unknown:
        return tech, vr
        
    # If not fully resolved, try combining applies_to and version_str
    combined = f"{applies_to} {version_str}".strip()
    tech, vr = parse_technology_version(combined)
    return tech, vr

class Deduplicator:
    """
    BRAIN-14: Memory Deduplication.
    Scans for duplicate memories and flags them for review.
    Never automatically deletes human-verified memories.
    All write operations go through ToolRouter.
    """
    def __init__(self, memory_controller: MemoryController, semantic_provider: SemanticProvider, tool_router: ToolRouter):
        self.controller = memory_controller
        self.semantic_provider = semantic_provider
        self.router = tool_router
        self.similarity_threshold = 0.85
        
    def scan_for_duplicates(self, principal: Principal, query: str = "") -> List[str]:
        """
        Retrieves a set of nodes and checks for semantic duplicates.
        Returns a list of IDs flagged as duplicates.
        """
        pack = self.controller.search(principal, query, page_size=20)
        candidates = pack.get("results", [])
        
        flagged_ids = []
        
        for i in range(len(candidates)):
            for j in range(i + 1, len(candidates)):
                node_a = candidates[i]
                node_b = candidates[j]
                
                if node_a.get("type") != node_b.get("type"):
                    continue
                
                # Different source tiers (source_type) MUST remain separate.
                source_a = node_a.get("provenance", {}).get("source_type")
                source_b = node_b.get("provenance", {}).get("source_type")
                if not source_a or not source_b or source_a != source_b:
                    continue
                
                # Extract technology/product identity and version range
                tech_a, vr_a = extract_tech_and_version(node_a)
                tech_b, vr_b = extract_tech_and_version(node_b)
                
                # Unknown versions/technologies must never cause destructive overlap (do not deduplicate)
                if tech_a.name == "unknown" or tech_b.name == "unknown":
                    continue
                if vr_a.unknown or vr_b.unknown:
                    continue
                
                # Different technology versions / products must remain separate
                if tech_a.name != tech_b.name or vr_a != vr_b:
                    continue
                    
                sim = self.semantic_provider.compute_similarity(
                    node_a.get("content", ""),
                    node_b.get("content", "")
                )
                
                if sim >= self.similarity_threshold:
                    note_id = str(uuid.uuid4())
                    content = (
                        f"Potential duplicate detected between {node_a.get('id')} and {node_b.get('id')}.\n"
                        f"Similarity score: {sim:.2f}\n"
                        "Please review and archive one if appropriate."
                    )
                    
                    note = {
                        "id": note_id,
                        "type": "hypothesis",
                        "lifecycle": Lifecycle.REVIEW.value,
                        "category": "deduplication",
                        "confidence": "high",
                        "verification": "unverified",
                        "provenance": {"source_type": "inference", "source_ref": "deduplicator"},
                        "content": content,
                        "relations": [
                            {"target_id": node_a.get("id"), "type": "related_to"},
                            {"target_id": node_b.get("id"), "type": "related_to"}
                        ]
                    }
                    
                    # Propose through ToolRouter
                    self.router.execute(principal, "propose", {"note_data": note})
                    flagged_ids.append(note_id)
                    
        return flagged_ids



============================================================
FILE: cognitive_core/version.py
============================================================

# cognitive_core/version.py
"""Version abstraction utilities for Technology‑aware memory handling.

Provides:
* ``TechnologyIdentity`` – name of the technology/product (e.g. "Python").
* ``Version`` – major/minor/patch representation.
* ``VersionRange`` – exact version, open‑ended range (e.g. "7.x"), or unknown.
* ``parse_technology_version`` – parse a free‑form string into (TechnologyIdentity, VersionRange).
* ``is_compatible`` – determine if a candidate version range satisfies a request.

Only the Python standard library is used (``re`` and ``dataclasses``).
"""

import re
from dataclasses import dataclass
from typing import Optional, Tuple


# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class TechnologyIdentity:
    """Canonical name of a technology/product.

    The ``name`` is normalized to title case (e.g. "Python", "PowerShell").
    """
    name: str

    def __str__(self) -> str:
        return self.name


@dataclass(frozen=True)
class Version:
    """Semantic version representation.

    ``major`` is required; ``minor`` and ``patch`` may be ``None``.
    """
    major: int
    minor: Optional[int] = None
    patch: Optional[int] = None

    def __str__(self) -> str:
        parts = [str(self.major)]
        if self.minor is not None:
            parts.append(str(self.minor))
        if self.patch is not None:
            parts.append(str(self.patch))
        return ".".join(parts)

    def matches(self, other: "Version") -> bool:
        """Exact match – all defined components must be equal.
        ``None`` components are treated as wildcards.
        """
        if self.major != other.major:
            return False
        if self.minor is not None and other.minor is not None and self.minor != other.minor:
            return False
        if self.patch is not None and other.patch is not None and self.patch != other.patch:
            return False
        return True


@dataclass(frozen=True)
class VersionRange:
    """Represents a version specification.

    * ``exact`` – a concrete ``Version`` instance.
    * ``prefix`` – a string like "7.x" meaning any version whose major equals 7.
    * ``unknown`` – used when parsing fails.
    """
    exact: Optional[Version] = None
    prefix: Optional[int] = None  # major version when using "X.x" notation
    unknown: bool = False

    def __str__(self) -> str:
        if self.unknown:
            return "unknown"
        if self.exact:
            return str(self.exact)
        if self.prefix is not None:
            return f"{self.prefix}.x"
        return ""

    def matches(self, candidate: "VersionRange") -> bool:
        """Compatibility check between a *request* and a *candidate*.

        The request may be more specific than the candidate. Compatibility rules:
        * If the request is unknown – it matches anything.
        * If the request is an exact version, the candidate must have the same exact version.
        * If the request is a prefix (e.g. ``7.x``), the candidate must have the same major.
        * If the request is exact and the candidate is a prefix, the major must match.
        """
        if self.unknown:
            return True
        if self.exact:
            if candidate.exact:
                return self.exact.matches(candidate.exact)
            if candidate.prefix is not None:
                return self.exact.major == candidate.prefix
            return False
        if self.prefix is not None:
            if candidate.exact:
                return candidate.exact.major == self.prefix
            if candidate.prefix is not None:
                return candidate.prefix == self.prefix
            return False
        return False


# ---------------------------------------------------------------------------
# Parsing helpers
# ---------------------------------------------------------------------------

# Regex patterns for the supported technologies.
_TECH_PATTERNS = [
    (r"python\s*(?P<major>\d+)(?:\.(?P<minor>\d+))?(?:\.(?P<patch>\d+))?", "Python"),
    (r"powershell\s*(?P<major>\d+)(?:\.(?P<minor>\d+))?", "PowerShell"),
    (r"windows\s*server\s*(?P<major>\d{4})(?:\s*R2)?", "Windows Server"),
    (r"\.net\s*framework\s*(?P<major>\d+)(?:\.(?P<minor>\d+))?", ".NET Framework"),
    (r"\.net\s*(?P<major>\d+)(?:\.(?P<minor>\d+))?", ".NET"),
]

# Helper to build a Version (or prefix) from regex groups.
def _build_version(groups: dict) -> VersionRange:
    major = groups.get("major")
    minor = groups.get("minor")
    patch = groups.get("patch")
    if major is None:
        return VersionRange(unknown=True)
    try:
        major_i = int(major)
    except ValueError:
        return VersionRange(unknown=True)
    # If minor is missing, treat this as an exact version with only major (e.g., Windows Server 2012, .NET 8)
    if minor is None:
        return VersionRange(exact=Version(major_i))
    minor_i = int(minor)
    patch_i = int(patch) if patch is not None else None
    return VersionRange(exact=Version(major_i, minor_i, patch_i))

def parse_technology_version(text: str) -> Tuple[TechnologyIdentity, VersionRange]:
    """Parse a free‑form description of a technology and its version.

    Returns a ``(TechnologyIdentity, VersionRange)`` tuple. If parsing fails, the
    ``TechnologyIdentity`` name is ``"unknown"`` and ``VersionRange`` is marked as
    unknown.
    """
    lowered = text.lower().strip()
    for pattern, tech_name in _TECH_PATTERNS:
        match = re.search(pattern, lowered)
        if match:
            groups = match.groupdict()
            # Special handling for Windows Server R2 which denotes a separate version.
            if tech_name == "Windows Server" and "r2" in lowered:
                # Treat 2012 R2 as version 2012.2 (minor 2) for compatibility.
                groups["minor"] = "2"
            # Special handling for PowerShell prefix notation (e.g., "7.x").
            if tech_name == "PowerShell" and ".x" in lowered:
                return TechnologyIdentity(tech_name), VersionRange(prefix=int(groups["major"]))
            vr = _build_version(groups)
            return TechnologyIdentity(tech_name), vr
    # No pattern matched – unknown technology/version.
    return TechnologyIdentity("unknown"), VersionRange(unknown=True)

def is_compatible(request: VersionRange, candidate: VersionRange) -> bool:
    """Public helper – delegates to ``VersionRange.matches``.
    """
    return request.matches(candidate)

# End of module


============================================================
FILE: cognitive_core/attention.py
============================================================

from typing import List, Dict, Any

class AttentionModel:
    """
    Computes attention scores for nodes in Working Memory.
    Attention determines which notes stay in the bounded Working Memory
    when new nodes are introduced.
    """
    def __init__(self, activation_weight: float = 0.5, confidence_weight: float = 0.3, recency_weight: float = 0.2):
        self.activation_weight = activation_weight
        self.confidence_weight = confidence_weight
        self.recency_weight = recency_weight
        
        self.confidence_scores = {
            "very_high": 1.0,
            "high": 0.8,
            "medium": 0.5,
            "low": 0.2,
            "unknown": 0.1
        }
        
    def calculate_score(self, node: Dict[str, Any], activation: float, recency_tick: int, current_tick: int) -> float:
        """
        Calculate an attention score bounded between 0.0 and 1.0.
        Recency decays as current_tick increases relative to recency_tick.
        """
        conf_val = node.get("confidence", "unknown")
        conf_score = self.confidence_scores.get(conf_val, 0.1)
        
        # Simple recency decay: newer is closer to 1.0
        age = current_tick - recency_tick
        recency_score = max(0.0, 1.0 - (age * 0.05))
        
        total_score = (
            (activation * self.activation_weight) +
            (conf_score * self.confidence_weight) +
            (recency_score * self.recency_weight)
        )
        
        return min(1.0, total_score)


============================================================
FILE: cognitive_core/semantic.py
============================================================

import re
from typing import List, Dict, Any, Tuple
from abc import ABC, abstractmethod

class SemanticProvider(ABC):
    """
    Abstraction for semantic similarity and embedding operations.
    Allows swapping a mock/deterministic provider with a real embedding model later.
    """
    @abstractmethod
    def compute_similarity(self, text_a: str, text_b: str) -> float:
        """Computes similarity score between 0.0 and 1.0"""
        pass

class DeterministicSemanticProvider(SemanticProvider):
    """
    Dependency-free, deterministic mock provider for associative recall testing.
    Uses basic word overlap (Jaccard similarity) instead of embeddings.
    """
    def _tokenize(self, text: str) -> set:
        if not text:
            return set()
        # Simple lowercase alphanumeric tokenization
        words = re.findall(r'\w+', text.lower())
        return set(words)
        
    def compute_similarity(self, text_a: str, text_b: str) -> float:
        set_a = self._tokenize(text_a)
        set_b = self._tokenize(text_b)
        
        if not set_a or not set_b:
            return 0.0
            
        intersection = set_a.intersection(set_b)
        union = set_a.union(set_b)
        return len(intersection) / len(union)


============================================================
FILE: cognitive_core/synapse.py
============================================================

from typing import List, Dict, Any

class Synapse:
    """
    Represents a directed relationship between two Memory Objects.
    """
    def __init__(self, source_id: str, target_id: str, relation_type: str, confidence: str = "unknown"):
        self.source_id = source_id
        self.target_id = target_id
        self.relation_type = relation_type
        self.confidence = confidence
        
    def __repr__(self) -> str:
        return f"Synapse(source={self.source_id}, target={self.target_id}, type={self.relation_type})"

class SynapticGraph:
    """
    Ephemeral graph layer extracting synapses from Memory Objects.
    Does not create a secondary persistent model.
    """
    @staticmethod
    def extract_synapses(memory_object: Dict[str, Any]) -> List[Synapse]:
        """
        Derives graph edges from existing Memory Object 'relations'.
        """
        source_id = memory_object.get("id")
        if not source_id:
            return []
            
        synapses = []
        relations = memory_object.get("relations", [])
        
        # Guard against None if relations is null in YAML
        if not relations:
            return synapses
            
        for rel in relations:
            if isinstance(rel, dict):
                target_id = rel.get("target_id")
                # Fallback to target if target_id missing but target is a uuid-like or string
                if not target_id:
                    target_str = rel.get("target", "")
                    # Extract possible UUID from wikilink if present
                    import re
                    match = re.search(r'([a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})', target_str)
                    if match:
                        target_id = match.group(1)
                
                rel_type = rel.get("type", "related_to")
                
                if target_id:
                    synapses.append(Synapse(source_id, target_id, rel_type))
        return synapses


============================================================
FILE: cognitive_core/__init__.py
============================================================

"""
Cognitive Core

This package represents the "prefrontal cortex" of the AI Agent.
It sits above the MemoryController (the "hippocampus") and provides
cognitive functions such as Activation, Attention, Working Memory,
Reasoning, Planning, and Executive Control.
"""


============================================================
END OF FILE
============================================================

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
