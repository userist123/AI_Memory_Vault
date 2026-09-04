import pytest
from cognitive_core.motivation import UtilityTracker, RewardSignal

def test_utility_tracker_rewards_and_decay():
    tracker = UtilityTracker.get_instance()
    tracker.reset()

    # Default utility for unrecorded action should be 0.5
    assert tracker.get_utility("search") == 0.5

    # Record 5 consecutive positive rewards (+1.0)
    for _ in range(5):
        tracker.update_utility("search", reward=1.0, source="VerifierAgent")

    u_success = tracker.get_utility("search")
    assert u_success > 0.5, "Utility should increase after positive rewards"

    # Record 5 consecutive failure rewards (-1.0) for a different action
    for _ in range(5):
        tracker.update_utility("bad_action", reward=-1.0, source="VerifierAgent")

    u_failure = tracker.get_utility("bad_action")
    assert u_failure < 0.5, "Utility should decrease after negative rewards"
    assert u_success > u_failure, "Successful actions must have higher utility than failing actions"
