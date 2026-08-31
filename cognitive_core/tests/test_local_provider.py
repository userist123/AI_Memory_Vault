import json

import pytest

from cognitive_core.local_provider import LocalProvider, LocalProviderError
from cognitive_core.model_provider import ModelRequest


class FakeHTTPResponse:
    def __init__(self, payload):
        self.payload = json.dumps(payload).encode("utf-8")

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb):
        return False

    def read(self):
        return self.payload


def test_generate_maps_ollama_usage(monkeypatch):
    captured = {}

    def fake_urlopen(request, timeout):
        captured["url"] = request.full_url
        captured["timeout"] = timeout
        captured["body"] = json.loads(request.data.decode("utf-8"))
        return FakeHTTPResponse({
            "model": "local-model",
            "response": "hello local",
            "done": True,
            "done_reason": "stop",
            "prompt_eval_count": 11,
            "eval_count": 7,
            "total_duration": 100,
            "load_duration": 20,
            "prompt_eval_duration": 30,
            "eval_duration": 50,
        })

    monkeypatch.setattr("cognitive_core.local_provider.urllib_request.urlopen", fake_urlopen)

    provider = LocalProvider("local-model", base_url="http://localhost:11434", timeout_seconds=17)
    response = provider.generate(
        ModelRequest(
            prompt="test prompt",
            model_tier="light",
            system_prompt="system",
            metadata={"local_options": {"temperature": 0}},
        )
    )

    assert captured["url"] == "http://localhost:11434/api/generate"
    assert captured["timeout"] == 17
    assert captured["body"]["model"] == "local-model"
    assert captured["body"]["prompt"] == "test prompt"
    assert captured["body"]["system"] == "system"
    assert captured["body"]["stream"] is False
    assert captured["body"]["options"] == {"temperature": 0, "num_ctx": 4096}
    assert response.provider == "local"
    assert response.model == "local-model"
    assert response.model_tier == "light"
    assert response.content == "hello local"
    assert response.usage.actual_input == 11
    assert response.usage.actual_output == 7
    assert response.usage.total == 18
    assert response.usage.effective_total == 18


def test_generate_explicit_num_ctx_in_payload_options(monkeypatch):
    captured = {}

    def fake_urlopen(request, timeout):
        captured["body"] = json.loads(request.data.decode("utf-8"))
        return FakeHTTPResponse({
            "model": "local-model",
            "response": "ok",
            "done": True,
        })

    monkeypatch.setattr("cognitive_core.local_provider.urllib_request.urlopen", fake_urlopen)

    provider = LocalProvider("local-model", num_ctx=8192)
    provider.generate(
        ModelRequest(
            prompt="short prompt",
            model_tier="balanced",
            metadata={"local_options": {"num_ctx": 2048}},
        )
    )

    assert captured["body"]["options"]["num_ctx"] == 2048


def test_generate_over_budget_fails_closed_before_http(monkeypatch):
    called = False

    def fake_urlopen(request, timeout):
        nonlocal called
        called = True
        return FakeHTTPResponse({})

    monkeypatch.setattr("cognitive_core.local_provider.urllib_request.urlopen", fake_urlopen)

    # num_ctx is 10 tokens (~30 chars)
    provider = LocalProvider("local-model", num_ctx=10)
    huge_prompt = "A" * 300  # ~100 tokens

    with pytest.raises(LocalProviderError, match="exceeds num_ctx limit 10"):
        provider.generate(ModelRequest(prompt=huge_prompt, model_tier="light"))

    assert not called, "No HTTP call should be made when request exceeds num_ctx"


def test_generate_within_budget_succeeds_normally(monkeypatch):
    def fake_urlopen(request, timeout):
        return FakeHTTPResponse({
            "model": "local-model",
            "response": "success within budget",
            "done": True,
        })

    monkeypatch.setattr("cognitive_core.local_provider.urllib_request.urlopen", fake_urlopen)

    provider = LocalProvider("local-model", num_ctx=1000)
    response = provider.generate(ModelRequest(prompt="short within budget prompt", model_tier="light"))
    assert response.content == "success within budget"


def test_generate_rejects_tools_instead_of_ignoring_them():
    provider = LocalProvider("local-model")
    request = ModelRequest(prompt="x", model_tier="light", tools=({"name": "tool"},))
    with pytest.raises(LocalProviderError, match="does not implement the ModelProvider tool contract"):
        provider.generate(request)


def test_health_reports_model_availability(monkeypatch):
    def fake_urlopen(request, timeout):
        assert request.full_url == "http://localhost:11434/api/tags"
        return FakeHTTPResponse({
            "models": [
                {"name": "local-model"},
                {"name": "other-model"},
            ]
        })

    monkeypatch.setattr("cognitive_core.local_provider.urllib_request.urlopen", fake_urlopen)

    provider = LocalProvider("local-model")
    health = provider.health()

    assert health["status"] == "ok"
    assert health["model_available"] is True
    assert health["available_model_count"] == 2


def test_health_is_non_throwing_when_ollama_is_unavailable(monkeypatch):
    def fake_urlopen(request, timeout):
        raise OSError("connection refused")

    monkeypatch.setattr("cognitive_core.local_provider.urllib_request.urlopen", fake_urlopen)

    provider = LocalProvider("local-model")
    health = provider.health()

    assert health["status"] == "unavailable"
    assert health["provider"] == "local"
