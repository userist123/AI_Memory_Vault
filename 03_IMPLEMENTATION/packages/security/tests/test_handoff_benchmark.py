"""Tests for the A/B knowledge-handoff benchmark contract."""

from security.handoff_benchmark import HandoffBenchmark, RunObservation


def test_benchmark_compares_both_arms():
    def runner(name, context, task, principal):
        if name == "baseline_raw_context":
            return RunObservation(1000, 400, 6, 3, True)
        return RunObservation(420, 250, 3, 0, True)

    result = HandoffBenchmark(runner).compare(
        task="continue the implementation",
        principal="agent-b",
        raw_context={"content": "large source context"},
        handoff_context={"content": "knowledge capsule"},
    )

    assert result.same_task is True
    assert result.same_principal is True
    assert result.input_tokens_saved == 580
    assert result.output_tokens_saved == 150
    assert result.tool_calls_saved == 3
    assert result.repeated_discoveries_saved == 3


def test_treatment_failure_is_reported():
    def runner(name, context, task, principal):
        if name == "treatment_knowledge_capsule":
            return RunObservation(0, 0, 0, 0, False, ("treatment_unavailable",))
        return RunObservation(100, 10, 1, 1, True)

    try:
        HandoffBenchmark(runner).compare(
            task="measure handoff",
            principal="agent-b",
            raw_context={"content": "raw"},
            handoff_context={"content": "capsule"},
        )
    except RuntimeError as exc:
        assert str(exc) == "treatment_arm_failed:treatment_unavailable"
    else:
        raise AssertionError("treatment failure must be reported")
