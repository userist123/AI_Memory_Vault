import pytest

from memory_controller.review_state import ReviewState, ReviewStateMachine


def test_valid_review_path():
    machine = ReviewStateMachine("CR-1")
    machine.transition(ReviewState.EVIDENCE_PENDING, actor="operator", reason="collect evidence")
    machine.transition(ReviewState.VERIFIED, actor="operator", reason="evidence verified")
    machine.transition(ReviewState.DECISION_PENDING, actor="operator", reason="ready for decision")
    machine.transition(ReviewState.APPROVED, actor="reviewer", reason="approved")
    assert machine.can_apply_mutation() is True


def test_invalid_transition_is_rejected():
    machine = ReviewStateMachine("CR-2")
    with pytest.raises(ValueError):
        machine.transition(ReviewState.APPROVED, actor="reviewer", reason="skip")


def test_closed_is_terminal():
    machine = ReviewStateMachine("CR-3", ReviewState.APPROVED)
    machine.transition(ReviewState.CLOSED, actor="reviewer", reason="done")
    with pytest.raises(ValueError):
        machine.transition(ReviewState.OPEN, actor="reviewer", reason="reopen")
