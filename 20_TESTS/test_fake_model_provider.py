"""test_fake_model_provider.py — A2 contract-compliance tests.

Verifies FakeModelProvider (a) satisfies the ModelProvider Protocol
structurally, (b) is deterministic, (c) records calls, (d) makes no
network calls (trivially true — no network-capable imports exist in
fake_model_provider.py).
"""
from cognitive_core.model_provider import ModelProvider, ModelRequest
from cognitive_core.fake_model_provider import FakeModelProvider


def test_fake_provider_satisfies_model_provider_protocol():
    provider = FakeModelProvider()
    assert isinstance(provider, ModelProvider)


def test_fake_provider_health_ok():
    provider = FakeModelProvider(provider_name="fake", model_name="fake-model")
    health = provider.health()
    assert health["status"] == "ok"
    assert health["provider"] == "fake"
    assert health["model"] == "fake-model"


def test_fake_provider_generate_is_deterministic():
    provider = FakeModelProvider()
    request = ModelRequest(prompt="hello world", model_tier="light")

    response_1 = provider.generate(request)
    response_2 = FakeModelProvider().generate(request)

    assert response_1.content == response_2.content
    assert response_1.usage.estimated_input == response_2.usage.estimated_input
    assert response_1.usage.estimated_output == response_2.usage.estimated_output


def test_fake_provider_usage_matches_formula():
    provider = FakeModelProvider()
    prompt = "x" * 30
    request = ModelRequest(prompt=prompt, model_tier="standard")

    response = provider.generate(request)

    expected_input = max(1, (len(prompt) + 2) // 3)
    assert response.usage.estimated_input == expected_input
    assert response.usage.actual_input == expected_input
    assert response.usage.effective_total == response.usage.total


def test_fake_provider_records_calls():
    provider = FakeModelProvider()
    assert provider.calls == []

    r1 = ModelRequest(prompt="first", model_tier="light")
    r2 = ModelRequest(prompt="second", model_tier="heavy")
    provider.generate(r1)
    provider.generate(r2)

    assert provider.calls == [r1, r2]


def test_fake_provider_model_tier_is_passthrough():
    provider = FakeModelProvider()
    request = ModelRequest(prompt="task", model_tier="heavy")
    response = provider.generate(request)
    assert response.model_tier == "heavy"


def test_token_usage_effective_total_falls_back_to_estimated():
    from cognitive_core.model_provider import TokenUsage

    usage = TokenUsage(estimated_input=10, estimated_output=5)
    assert usage.total is None
    assert usage.effective_total == 15


def test_token_usage_effective_total_prefers_actual_total():
    from cognitive_core.model_provider import TokenUsage

    usage = TokenUsage(
        estimated_input=10,
        estimated_output=5,
        actual_input=8,
        actual_output=4,
        total=12,
    )
    assert usage.effective_total == 12
