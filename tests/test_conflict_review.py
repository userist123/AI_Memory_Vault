from memory_controller.conflict_review import ConflictReviewWorkflow


def test_open_case_is_deterministic_and_review_only():
    workflow = ConflictReviewWorkflow()
    case = workflow.open_case(
        memory_ids=["m2", "m1", "m2"],
        reasons=["opposite assertion", "opposite assertion"],
        conflict_type="semantic",
        evidence_ids=["e1"],
        as_of="2024-01-01",
        known_as_of="2024-02-01",
    )
    assert case.case_id.startswith("CR-")
    assert case.memory_ids == ("m1", "m2")
    assert case.evidence_ids == ("e1",)
    assert case.status == "OPEN"
    assert case.recommendation == "VERIFY_WITH_EVIDENCE"


def test_open_case_requires_two_memories_and_reason():
    workflow = ConflictReviewWorkflow()
    try:
        workflow.open_case(memory_ids=["m1"], reasons=["x"])
    except ValueError as exc:
        assert "At least two" in str(exc)
    else:
        raise AssertionError("expected memory cardinality validation")
