import json

from cognitive_core.council_usage_audit import build_audit_report
from cognitive_core.executive_model_execution_bridge import execute_council_models
from cognitive_core.fake_model_provider import FakeModelProvider
from cognitive_core.model_provider import ModelRequest, ModelResponse, TokenUsage
from cognitive_core.council_model_execution import CouncilRunWithExecution


class _StubSubagentSpec:
    def __init__(self, model_tier):
        self.model_tier = model_tier


class _StubCouncilRun:
    def __init__(self, agent_packs, telemetry=None):
        self.agent_packs = agent_packs
        self.telemetry = telemetry


FAKE_FACTORIES = {"fake": lambda model_name: FakeModelProvider(provider_name="fake", model_name=model_name)}


def _run_fake_council(tmp_path):
    config_path = tmp_path / "model_tiers.json"
    config_path.write_text(json.dumps({
        "light": {"provider": "fake", "model": "fake-light"},
        "standard": {"provider": "fake", "model": "fake-standard"},
        "heavy": {"provider": "fake", "model": "fake-heavy"},
    }))
    council_run = _StubCouncilRun(agent_packs={"ROUTER": {"n": "a"}, "VERIFIER": {"n": "b"}})
    specs = {
        "ROUTER": _StubSubagentSpec("light"),
        "VERIFIER": _StubSubagentSpec("light"),
        "SYNTHESIZER": _StubSubagentSpec("heavy"),
    }
    return execute_council_models(
        council_run=council_run, subagent_specs=specs, task="analyze",
        model_execution_enabled=True, config_path=config_path,
        provider_factories=FAKE_FACTORIES,
    )


def test_audit_report_has_all_required_fields(tmp_path):
    result = _run_fake_council(tmp_path)
    report = build_audit_report(run_id="run-1", council_run_with_execution=result, wall_time_seconds=1.23)

    assert report.run_id == "run-1"
    assert report.specialist_calls == 2
    assert report.synthesis_calls == 1
    assert report.total_model_calls == 3
    assert report.wall_time_seconds == 1.23
    assert report.context_bytes > 0
    assert report.context_estimated_tokens > 0


def test_per_agent_and_per_tier_usage_populated(tmp_path):
    result = _run_fake_council(tmp_path)
    report = build_audit_report(run_id="run-2", council_run_with_execution=result)

    assert set(report.per_agent_usage) == {"ROUTER", "VERIFIER", "SYNTHESIS"}
    assert set(report.per_tier_usage) == {"light", "heavy"}
    assert report.per_tier_usage["light"]["calls"] == 2
    assert report.per_tier_usage["heavy"]["calls"] == 1


def test_estimated_and_actual_totals_independent_when_fake_reports_actual():
    council_run_with_execution = CouncilRunWithExecution(council_run=_StubCouncilRun(agent_packs={}))
    provider = FakeModelProvider()
    response = provider.generate(ModelRequest(prompt="x" * 30, model_tier="light"))
    council_run_with_execution.specialist_results["A"] = response

    report = build_audit_report(run_id="run-3", council_run_with_execution=council_run_with_execution)

    assert report.estimated_total == response.usage.estimated_input + response.usage.estimated_output
    assert report.actual_total == response.usage.effective_total


def test_estimated_vs_actual_diverge_on_fallback():
    council_run_with_execution = CouncilRunWithExecution(council_run=_StubCouncilRun(agent_packs={}))
    usage = TokenUsage(estimated_input=100, estimated_output=50)
    response = ModelResponse(
        content="x", provider="local", model="llama", model_tier="light", usage=usage,
    )
    council_run_with_execution.specialist_results["A"] = response

    report = build_audit_report(run_id="run-4", council_run_with_execution=council_run_with_execution)

    assert report.estimated_total == 150
    assert report.actual_total == 150
    assert report.calls[0].source == "estimated_fallback"


def test_report_serializes_to_json(tmp_path):
    result = _run_fake_council(tmp_path)
    report = build_audit_report(run_id="run-5", council_run_with_execution=result)
    parsed = json.loads(report.to_json())
    assert parsed["run_id"] == "run-5"
    assert "tokens_per_specialist" in parsed
    assert "tokens_per_synthesis" in parsed
    assert "tokens_per_council_run" in parsed


def test_tokens_per_specialist_and_synthesis_ratios(tmp_path):
    result = _run_fake_council(tmp_path)
    report = build_audit_report(run_id="run-6", council_run_with_execution=result)

    specialist_total = sum(c.actual_total for c in report.calls if c.kind == "specialist")
    assert report.tokens_per_specialist == specialist_total / 2

    synthesis_total = sum(c.actual_total for c in report.calls if c.kind == "synthesis")
    assert report.tokens_per_synthesis == synthesis_total
    assert report.tokens_per_council_run == float(report.actual_total)
