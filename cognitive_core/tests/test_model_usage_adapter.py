from cognitive_core.model_provider import ModelRequest, ModelResponse, TokenUsage
from cognitive_core.model_usage_adapter import (
    record_specialist_response,
    record_synthesis_response,
)

import importlib.util
from pathlib import Path

TELEMETRY_PATH = Path(__file__).parents[2] / "99_SYSTEM" / "council_token_telemetry.py"
spec = importlib.util.spec_from_file_location("council_token_telemetry", TELEMETRY_PATH)
telemetry_module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(telemetry_module)
CouncilTokenTelemetry = telemetry_module.CouncilTokenTelemetry


def _response(*, model="local-model", tier="light"):
    return ModelResponse(
        content="generated answer",
        provider="local",
        model=model,
        model_tier=tier,
        usage=TokenUsage(
            estimated_input=100,
            estimated_output=20,
            actual_input=42,
            actual_output=9,
            cached_input=7,
            reasoning_tokens=3,
            total=51,
        ),
    )


def test_specialist_adapter_preserves_real_request_and_usage():
    telemetry = CouncilTokenTelemetry()
    request = ModelRequest(prompt="the real specialist prompt", model_tier="light")
    response = _response()

    record_specialist_response(telemetry, request, response)

    assert telemetry.specialist_input_tokens > 0
    assert telemetry.specialist_output_tokens > 0
    assert telemetry.actual_input_tokens == 42
    assert telemetry.actual_output_tokens == 9
    assert telemetry.cached_input_tokens == 7
    assert telemetry.reasoning_tokens == 3
    assert telemetry.actual_total_tokens == 51
    assert telemetry.events[-1]["kind"] == "specialist"
    assert telemetry.events[-1]["provider"] == "local"


def test_synthesis_adapter_maps_usage_to_synthesis_event():
    telemetry = CouncilTokenTelemetry()
    request = ModelRequest(prompt="the synthesis prompt", model_tier="heavy")
    response = _response(model="heavy-local", tier="heavy")

    record_synthesis_response(telemetry, request, response)

    assert telemetry.synthesis_input_tokens > 0
    assert telemetry.synthesis_output_tokens > 0
    assert telemetry.actual_input_tokens == 42
    assert telemetry.actual_output_tokens == 9
    assert telemetry.actual_total_tokens == 51
    assert telemetry.events[-1]["kind"] == "synthesis"
    assert telemetry.events[-1]["model_tier"] == "heavy"


def test_missing_actual_usage_does_not_corrupt_estimates():
    telemetry = CouncilTokenTelemetry()
    request = ModelRequest(prompt="prompt without provider usage", model_tier="light")
    response = ModelResponse(
        content="answer",
        provider="local",
        model="local-model",
        model_tier="light",
        usage=TokenUsage(estimated_input=12, estimated_output=4),
    )

    record_specialist_response(telemetry, request, response)

    assert telemetry.actual_input_tokens == 0
    assert telemetry.actual_output_tokens == 0
    assert telemetry.actual_total_tokens == 0
    assert telemetry.estimated_total_tokens > 0
