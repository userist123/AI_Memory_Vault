"""
ACT-R Motivational & Utility Module for Cognitive Core.

Theoretical Foundation:
Implements a computational reward and utility system inspired by ACT-R production utility:
    U_i = P_i * G - C_i
where P_i is the estimated probability of success, G is the goal value, and C_i is expected cost.

Provides dynamic utility-weighted action selection and emotional/motivational bonus scoring.
"""

import time
from typing import Dict, List, Optional, Any

class RewardSignal:
    """
    Represents a reward feedback signal from execution, verifier validation, or human feedback.
    """
    def __init__(self, score: float, source: str, action_type: str, timestamp: Optional[float] = None):
        self.score: float = float(score)  # Positive for success, negative for failure
        self.source: str = source          # e.g., 'VerifierAgent', 'execution_test', 'user_feedback'
        self.action_type: str = action_type  # e.g., 'search', 'propose', 'refine', 'reconsolidate'
        self.timestamp: float = timestamp if timestamp is not None else time.time()


class UtilityTracker:
    """
    Tracks action type utility using an Exponential Moving Average (EMA) and ACT-R probability scaling.
    """
    _instance: Optional['UtilityTracker'] = None

    def __init__(self, alpha: float = 0.2, base_goal_value: float = 1.0):
        self.alpha = alpha
        self.base_goal_value = base_goal_value
        # Default utility per action type initialized to neutral 0.5
        self.utilities: Dict[str, float] = {}
        self.rewards_history: Dict[str, List[RewardSignal]] = {}

    @classmethod
    def get_instance(cls) -> 'UtilityTracker':
        if cls._instance is None:
            cls._instance = cls()
        return cls._instance

    def update_utility(self, action_type: str, reward: float, source: str = "execution") -> float:
        """
        Updates the utility of an action_type given a new reward signal (-1.0 to 1.0).
        """
        signal = RewardSignal(score=reward, source=source, action_type=action_type)
        if action_type not in self.rewards_history:
            self.rewards_history[action_type] = []
            self.utilities[action_type] = 0.5
            
        self.rewards_history[action_type].append(signal)
        
        # Exponential Moving Average update: U_{t} = (1-alpha) * U_{t-1} + alpha * reward
        current_u = self.utilities[action_type]
        # Normalize reward from [-1, 1] to [0, 1] for utility calculation
        norm_reward = max(0.0, min(1.0, (reward + 1.0) / 2.0))
        new_u = (1.0 - self.alpha) * current_u + self.alpha * norm_reward
        self.utilities[action_type] = new_u
        return new_u

    def get_utility(self, action_type: str) -> float:
        """
        Returns calculated utility score bounded between 0.0 and 1.0 (default 0.5 for new actions).
        """
        return self.utilities.get(action_type, 0.5)

    def reset(self):
        self.utilities.clear()
        self.rewards_history.clear()
