from routing.sequential_workflow import (
    AgentObservation,
    SequentialAgentWorkflow,
    StepSpec,
    observation_from_text,
)


def test_workflow_runs_executor_then_reviewer_in_order(tmp_path):
    events = []
    steps = (StepSpec("plan", "make a plan"), StepSpec("build", "build it"))
    workflow = SequentialAgentWorkflow("ship a feature", steps, state_path=tmp_path / "state.json")

    def execute(step, context):
        events.append(("execute", step.step_id, context))
        return AgentObservation("passed", f"done {step.step_id}", evidence=(step.step_id,))

    def review(step, output, context):
        events.append(("review", step.step_id))
        return AgentObservation("passed", f"reviewed {output.summary}")

    result = workflow.run(execute, review)

    assert result.status == "completed"
    assert result.completed_steps == ("plan", "build")
    assert [event[:2] for event in events] == [
        ("execute", "plan"), ("review", "plan"),
        ("execute", "build"), ("review", "build"),
    ]
    assert (tmp_path / "state.json").exists()


def test_workflow_retries_then_escalates(tmp_path):
    attempts = {"count": 0}
    steps = (StepSpec("implement", "implement", max_attempts=2),)
    workflow = SequentialAgentWorkflow("implement safely", steps, state_path=tmp_path / "state.json")

    def execute(step, context):
        attempts["count"] += 1
        return AgentObservation("failed", "compile error")

    def review(step, output, context):
        return AgentObservation("failed", "not accepted", failures=("compile",))

    def escalate(step, output, review, context):
        return AgentObservation("passed", "fixed by escalation", evidence=("rerun",))

    result = workflow.run(execute, review, escalator=escalate)

    assert result.status == "completed"
    assert attempts["count"] == 2
    assert result.reviews["implement"].summary == "fixed by escalation"


def test_workflow_fails_closed_without_escalation(tmp_path):
    workflow = SequentialAgentWorkflow(
        "do not guess", (StepSpec("verify", "verify", max_attempts=1),),
        state_path=tmp_path / "state.json",
    )

    result = workflow.run(
        lambda step, context: AgentObservation("unknown", "cannot verify"),
        lambda step, output, context: AgentObservation("failed", "evidence missing"),
    )

    assert result.status == "failed"
    assert result.failed_step == "verify"
    assert result.completed_steps == ()


def test_current_step_evidence_is_explicitly_available_to_executor():
    workflow = SequentialAgentWorkflow(
        "use verified evidence",
        (StepSpec("extract", "extract invariant", evidence=("SHA-256 masked to 63 bits",)),),
    )
    seen = {}

    def execute(step, context):
        seen["context"] = context
        return AgentObservation("passed", "used evidence")

    result = workflow.run(
        execute,
        lambda step, output, context: AgentObservation("passed", "reviewed"),
    )

    assert result.status == "completed"
    assert "SHA-256 masked to 63 bits" in seen["context"]


def test_observation_parser_accepts_common_status_variants_only_with_evidence():
    passed = observation_from_text(
        "**STATUS**: Completed\nEVIDENCE: status exactly ok",
        required_terms=("status exactly ok",),
    )
    missing = observation_from_text(
        "STATUS: OK\nRESULT: vague",
        required_terms=("status exactly ok",),
    )
    assert passed.status == "passed"
    assert missing.status == "failed"


def test_observation_parser_handles_safe_paraphrases():
    parsed = observation_from_text(
        "STATUS: PASS\nEvidence: the operation fails-closed when status is exactly ok",
        required_terms=("fail closed", "status exactly ok"),
    )
    assert parsed.status == "passed"
