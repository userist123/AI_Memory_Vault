"""
Global Workspace Theory (GWT) Engine for Cognitive Core.

Theoretical Foundation:
Based on Bernard Baars' Global Workspace Theory (GWT) of consciousness.
Acts as a central competitive hub where distributed specialized worker agents
(Router, Retrieval, Consolidator, Verifier, Critic) submit memory/reasoning proposals.

Proposals compete based on a composite score combining:
    Final Score = Coherence (Agent) + ACT-R Activation + Utility Bonus
The winning proposal or coalition is broadcast globally to all agents,
converting localized sub-symbolic work into explicit, shared working memory context.
"""

import time
from typing import List, Dict, Any, Optional
from .activation import ActivationTracker
from .motivation import UtilityTracker

class WorkspaceProposal:
    """
    Represents a proposal submitted by a specialized subagent into the Global Workspace.
    """
    def __init__(self, agent_id: str, content: Dict[str, Any], coherence_score: float = 0.5, action_type: str = "general"):
        self.agent_id: str = agent_id
        self.content: Dict[str, Any] = content
        self.coherence_score: float = float(coherence_score)
        self.action_type: str = action_type
        self.timestamp: float = time.time()


class GlobalWorkspace:
    """
    Central competitive Global Workspace for multi-agent broadcast orchestration.
    """
    def __init__(self, max_slots: int = 3, score_tolerance: float = 0.05):
        self.max_slots: int = max_slots
        self.score_tolerance: float = score_tolerance
        self.proposals: List[WorkspaceProposal] = []
        self.current_broadcast: Optional[Dict[str, Any]] = None
        self.broadcast_history: List[Dict[str, Any]] = []

        self.activation_tracker = ActivationTracker.get_instance()
        self.utility_tracker = UtilityTracker.get_instance()

    def submit_proposal(self, proposal: WorkspaceProposal) -> None:
        """
        Submits an agent proposal into the current workspace competition cycle.
        """
        self.proposals.append(proposal)

    def compete_and_broadcast(self) -> Optional[Dict[str, Any]]:
        """
        Runs workspace competition across all submitted proposals.
        Computes composite score = Coherence + Activation + Utility.
        Broadcasts winning proposal/coalition to all agents.
        """
        if not self.proposals:
            self.current_broadcast = None
            return None

        # Fast-path single active agent compatibility
        if len(self.proposals) == 1:
            winning_prop = self.proposals[0]
            self.current_broadcast = {
                "winner_agent": winning_prop.agent_id,
                "content": winning_prop.content,
                "score": winning_prop.coherence_score,
                "coalition": [winning_prop.agent_id],
                "timestamp": time.time()
            }
            self.broadcast_history.append(self.current_broadcast)
            self.proposals.clear()
            return self.current_broadcast

        # Multi-proposal competition
        scored_proposals = []
        for prop in self.proposals:
            note_id = prop.content.get("id") if isinstance(prop.content, dict) else None
            
            act_score = self.activation_tracker.get_activation(note_id) if note_id else 0.0
            norm_act = max(0.0, min(1.0, (act_score + 2.0) / 5.0))
            
            u_score = self.utility_tracker.get_utility(prop.action_type)
            
            # Composite score calculation
            total_score = (prop.coherence_score * 0.5) + (norm_act * 0.3) + (u_score * 0.2)
            scored_proposals.append((prop, total_score))

        # Sort descending by composite score
        scored_proposals.sort(key=lambda x: x[1], reverse=True)
        top_prop, max_score = scored_proposals[0]

        # Coalition selection within score_tolerance
        coalition = [
            prop.agent_id for prop, score in scored_proposals
            if (max_score - score) <= self.score_tolerance
        ][:self.max_slots]

        self.current_broadcast = {
            "winner_agent": top_prop.agent_id,
            "content": top_prop.content,
            "score": max_score,
            "coalition": coalition,
            "timestamp": time.time()
        }
        self.broadcast_history.append(self.current_broadcast)
        self.proposals.clear()
        return self.current_broadcast

    def get_current_broadcast(self) -> Optional[Dict[str, Any]]:
        return self.current_broadcast

    def clear(self) -> None:
        self.proposals.clear()
        self.current_broadcast = None
