"""test_actual_usage_telemetry.py — A5 contract tests.

Verifies ActualUsageTelemetry: (a) never imports/touches
council_token_telemetry.py, (b) correctly separates provider-reported
usage from estimated fallback, (c) aggregates specialist/synthesis
separately, (d) records per-call events with provider/model/tier.
"""
import cognitive_core.actual_usage_telemetry as aut_module
from cognitive_core.actual_usage_telemetry import ActualUsageTelemetry
from cognitive_core.model_provider import ModelRequest, TokenUsage
from cognitive_core.fake_model_provider import FakeModelProvider


def test_module_never_imports_frozen_council_telemetry():
    import inspect
    source = inspect.getsource(aut_module)
    assert "council_token_telemetry" not in source


def test_record_specialist_with_real_provider_usage():
    telemetry = ActualUsageTelemetry()
    usage = TokenUsage(
        estimated_input=10, estimated_output=5,
        actual_input=12, actual_output=6,
        cached_input=3, reasoning_tokens=2,
    )
    telemetry.record_specialist_actual(usage, provider="openai", model="gpt-x", model_tier="heavy")

    assert telemetry.specialist_actual_input == 12
    assert telemetry.specialist_actual_output == 6
    assert telemetry.specialist_cached_input == 3
    assert telemetry.specialist_reasoning_tokens == 2
    assert telemetry.events[0].source == "provider"
    assert telemetry.events[0].provider == "openai"
    assert telemetry.events[0].model_tier == "heavy"


def test_record_specialist_falls_back_to_estimated_when_no_actual():
    telemetry = ActualUsageTelemetry()
    usage = TokenUsage(estimated_input=10, estimated_output=5)
    telemetry.record_specialist_actual(usage, provider="local", model="llama", model_tier="light")

    assert telemetry.specialist_actual_input == 10
    assert telemetry.specialist_actual_output == 5
    assert telemetry.events[0].source == "estimated_fallback"


def test_partial_actual_usage_still_counts_as_fallback():
    telemetry = ActualUsageTelemetry()
    usage = TokenUsage(estimated_input=10, estimated_output=5, actual_input=12, actual_output=None)
    telemetry.record_specialist_actual(usage)

    assert telemetry.events[0].source == "estimated_fallback"
    assert telemetry.specialist_actual_input == 12
    assert telemetry.specialist_actual_output == 5


def test_specialist_and_synthesis_are_tracked_separately():
    telemetry = ActualUsageTelemetry()
    telemetry.record_specialist_actual(TokenUsage(estimated_input=10, estimated_output=5, actual_input=10, actual_output=5))
    telemetry.record_synthesis_actual(TokenUsage(estimated_input=20, estimated_output=8, actual_input=20, actual_output=8))

    assert telemetry.specialist_actual_input == 10
    assert telemetry.synthesis_actual_input == 20
    assert telemetry.actual_total_tokens == 10 + 5 + 20 + 8
    assert telemetry.actual_input_tokens == 30
    assert telemetry.actual_output_tokens == 13


def test_has_real_provider_usage_false_when_all_fallback():
    telemetry = ActualUsageTelemetry()
    telemetry.record_specialist_actual(TokenUsage(estimated_input=1, estimated_output=1))
    assert telemetry.has_real_provider_usage is False


def test_has_real_provider_usage_true_when_any_real():
    telemetry = ActualUsageTelemetry()
    telemetry.record_specialist_actual(TokenUsage(estimated_input=1, estimated_output=1))
    telemetry.record_synthesis_actual(
        TokenUsage(estimated_input=1, estimated_output=1, actual_input=1, actual_output=1)
    )
    assert telemetry.has_real_provider_usage is True


def test_invalid_kind_raises():
    telemetry = ActualUsageTelemetry()
    usage = TokenUsage(estimated_input=1, estimated_output=1)
    try:
        telemetry._record("bogus", usage, "p", "m", "t")
        assert False, "expected ValueError"
    except ValueError:
        pass


def test_end_to_end_with_fake_model_provider():
    telemetry = ActualUsageTelemetry()
    provider = FakeModelProvider(provider_name="fake", model_name="fake-light")
    response = provider.generate(ModelRequest(prompt="hello", model_tier="light"))

    telemetry.record_specialist_actual(
        response.usage,
        provider=response.provider,
        model=response.model,
        model_tier=response.model_tier,
    )

    assert telemetry.has_real_provider_usage is True
    assert telemetry.events[0].provider == "fake"
    assert telemetry.events[0].model == "fake-light"
    assert telemetry.actual_total_tokens == response.usage.effective_total
