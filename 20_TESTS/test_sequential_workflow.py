from routing.sequential_workflow import (
    AgentObservation,
    SequentialAgentWorkflow,
    StepSpec,
    observation_from_text,
    WorkflowError,
)
import json
import os
import subprocess
import sys
import pytest


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
        return (AgentObservation("passed", "fixed by escalation", evidence=("rerun",)),
                AgentObservation("passed", "independent rerun", evidence=("pytest exit code 0",)))

    result = workflow.run(execute, review, escalator=escalate)

    assert result.status == "completed"
    assert attempts["count"] == 2
    assert result.reviews["implement"].summary == "independent rerun"


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
        "STATUS: PASS\nRESULT: complete\nEVIDENCE: status exactly ok\nCHANGES: none\nFAILURES: none\nUNKNOWNS: none",
        required_terms=("status exactly ok",),
    )
    missing = observation_from_text(
        "STATUS: PASS\nRESULT: vague\nEVIDENCE: vague\nCHANGES: none\nFAILURES: none\nUNKNOWNS: none",
        required_terms=("status exactly ok",),
    )
    assert passed.status == "passed"
    assert missing.status == "failed"


def test_observation_parser_handles_safe_paraphrases():
    parsed = observation_from_text(
        "STATUS: PASS\nRESULT: complete\nEVIDENCE: the operation fails-closed when status is exactly ok\nCHANGES: none\nFAILURES: none\nUNKNOWNS: none",
        required_terms=("fail closed", "status exactly ok"),
    )
    assert parsed.status == "passed"


def test_duplicate_step_ids_are_rejected():
    with pytest.raises(ValueError):
        SequentialAgentWorkflow("x", (StepSpec("same", "a"), StepSpec("same", "b")))


def test_existing_checkpoint_requires_explicit_resume(tmp_path):
    path = tmp_path / "state.json"
    first = SequentialAgentWorkflow("goal", (StepSpec("one", "one"),), task_id="t", state_path=path)
    first.run(lambda s, c: AgentObservation("passed", "ok"), lambda s, o, c: AgentObservation("passed", "ok"))
    with pytest.raises(WorkflowError):
        SequentialAgentWorkflow("goal", (StepSpec("one", "one"),), task_id="t", state_path=path)
    resumed = SequentialAgentWorkflow("goal", (StepSpec("one", "one"),), task_id="t", state_path=path, resume=True)
    assert resumed.state.current_index == 1


def test_executor_exception_is_persisted(tmp_path):
    path = tmp_path / "state.json"
    workflow = SequentialAgentWorkflow("goal", (StepSpec("one", "one"),), state_path=path)
    result = workflow.run(lambda s, c: (_ for _ in ()).throw(RuntimeError("boom")), lambda s, o, c: AgentObservation("passed", "never"))
    assert result.status == "interrupted"
    assert "RuntimeError" in json.loads(path.read_text())["history"]["one"][0]["output"]["failures"]


def test_context_keeps_mandatory_fields_when_history_is_large():
    workflow = SequentialAgentWorkflow("goal", (StepSpec("one", "current objective", acceptance=("accept",)),), max_context_chars=1000)
    workflow.state.history["old"] = [{"output": {"summary": "x" * 5000}}]
    context = workflow._context(0)
    parsed = json.loads(context)
    assert parsed["goal"] == "goal" and parsed["current_step"]["step_id"] == "one"


def test_mandatory_context_overflow_is_blocked(tmp_path):
    workflow = SequentialAgentWorkflow("goal", (StepSpec("one", "x" * 15000),), state_path=tmp_path / "state.json")
    result = workflow.run(lambda s, c: AgentObservation("passed", "ok"), lambda s, o, c: AgentObservation("passed", "ok"))
    assert result.status == "blocked"


def test_observation_parser_rejects_adversarial_prose():
    text = "STATUS: PASS\nRESULT: quote says STATUS: FAIL\nEVIDENCE: does NOT fail closed\nCHANGES: none\nFAILURES: exit code 1, 7 failed\nUNKNOWNS: none"
    assert observation_from_text(text, required_terms=("fail closed",)).status == "failed"
    blocked = "STATUS: BLOCKED\nRESULT: blocked\nEVIDENCE: none\nCHANGES: none\nFAILURES: none\nUNKNOWNS: dependency"
    assert observation_from_text(blocked).status == "failed"


def test_resume_loads_checkpoint_in_a_fresh_python_process(tmp_path):
    path = tmp_path / "state.json"
    root = str(__file__).split("20_TESTS")[0]
    script = """
from routing.sequential_workflow import AgentObservation, SequentialAgentWorkflow, StepSpec
import sys
p = sys.argv[1]
w = SequentialAgentWorkflow('goal', (StepSpec('one', 'one'), StepSpec('two', 'two')), task_id='fresh', state_path=p, resume=True)
seen = []
def ex(s, c):
    seen.append(s.step_id)
    return AgentObservation('passed', 'ok')
def rv(s, o, c):
    return AgentObservation('passed', 'ok')
r = w.run(ex, rv)
print(r.status, ','.join(seen), r.completed_steps)
"""
    first = SequentialAgentWorkflow("goal", (StepSpec("one", "one"), StepSpec("two", "two")), task_id="fresh", state_path=path)
    first.run(lambda s, c: AgentObservation("passed", "ok"), lambda s, o, c: AgentObservation("passed", "ok"))
    env = os.environ.copy()
    env["PYTHONPATH"] = os.path.join(root, "03_IMPLEMENTATION", "packages")
    completed = subprocess.run([sys.executable, "-c", script, str(path)], cwd=root, env=env, capture_output=True, text=True)
    assert completed.returncode == 0, completed.stderr
    assert "completed  ('one', 'two')" in completed.stdout
