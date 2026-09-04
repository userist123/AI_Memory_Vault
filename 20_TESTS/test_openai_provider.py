"""test_openai_provider.py — A6 contract tests, fully mocked HTTP.

Zero real API key required, zero outbound network call, zero cost.
All urllib.request.urlopen calls are mocked.
"""
import io
import json
import urllib.error
from unittest.mock import patch

import pytest

from cognitive_core.model_provider import ModelProvider, ModelRequest
from cognitive_core.openai_provider import (
    OpenAIAuthenticationError,
    OpenAIProvider,
    OpenAIProviderError,
    OpenAIToolsNotSupportedError,
)

RESPONSE_PAYLOAD = {
    "id": "resp_123",
    "status": "completed",
    "model": "gpt-4.1",
    "output": [
        {
            "type": "message",
            "role": "assistant",
            "content": [{"type": "output_text", "text": "hello from openai"}],
        }
    ],
    "usage": {
        "input_tokens": 42,
        "input_tokens_details": {"cached_tokens": 10},
        "output_tokens": 8,
        "output_tokens_details": {"reasoning_tokens": 3},
        "total_tokens": 50,
    },
}


class _FakeHTTPResponse:
    def __init__(self, payload, status=200):
        self.status = status
        self._body = json.dumps(payload).encode("utf-8")

    def read(self):
        return self._body

    def __enter__(self):
        return self

    def __exit__(self, *args):
        return False


@pytest.fixture(autouse=True)
def _clear_env(monkeypatch):
    monkeypatch.delenv("OPENAI_API_KEY", raising=False)


def test_missing_api_key_raises():
    provider = OpenAIProvider(model_name="gpt-4.1")
    with pytest.raises(OpenAIAuthenticationError):
        provider.generate(ModelRequest(prompt="hi", model_tier="standard"))


def test_tools_are_explicitly_rejected():
    provider = OpenAIProvider(model_name="gpt-4.1", api_key="sk-test")
    request = ModelRequest(
        prompt="hi", model_tier="standard", tools=({"type": "function"},)
    )
    with pytest.raises(OpenAIToolsNotSupportedError):
        provider.generate(request)


def test_payload_sends_store_false_and_truncation_disabled():
    provider = OpenAIProvider(model_name="gpt-4.1", api_key="sk-test")
    captured = {}

    def fake_urlopen(req, timeout=None):
        captured["payload"] = json.loads(req.data.decode("utf-8"))
        return _FakeHTTPResponse(RESPONSE_PAYLOAD)

    with patch("urllib.request.urlopen", side_effect=fake_urlopen):
        provider.generate(ModelRequest(prompt="hi", model_tier="standard"))

    assert captured["payload"]["store"] is False
    assert captured["payload"]["truncation"] == "disabled"
    assert captured["payload"]["model"] == "gpt-4.1"
    assert captured["payload"]["input"] == "hi"


def test_system_prompt_maps_to_instructions():
    provider = OpenAIProvider(model_name="gpt-4.1", api_key="sk-test")
    captured = {}

    def fake_urlopen(req, timeout=None):
        captured["payload"] = json.loads(req.data.decode("utf-8"))
        return _FakeHTTPResponse(RESPONSE_PAYLOAD)

    with patch("urllib.request.urlopen", side_effect=fake_urlopen):
        provider.generate(
            ModelRequest(prompt="hi", model_tier="standard", system_prompt="be nice")
        )

    assert captured["payload"]["instructions"] == "be nice"


def test_authorization_header_uses_bearer_key_and_key_not_leaked():
    provider = OpenAIProvider(model_name="gpt-4.1", api_key="sk-secret-value")
    captured = {}

    def fake_urlopen(req, timeout=None):
        captured["headers"] = dict(req.header_items())
        return _FakeHTTPResponse(RESPONSE_PAYLOAD)

    with patch("urllib.request.urlopen", side_effect=fake_urlopen):
        response = provider.generate(ModelRequest(prompt="hi", model_tier="standard"))

    assert captured["headers"]["Authorization"] == "Bearer sk-secret-value"
    assert "sk-secret-value" not in json.dumps(response.metadata)
    assert "sk-secret-value" not in response.content


def test_usage_mapping_from_responses_api():
    provider = OpenAIProvider(model_name="gpt-4.1", api_key="sk-test")

    with patch("urllib.request.urlopen", return_value=_FakeHTTPResponse(RESPONSE_PAYLOAD)):
        response = provider.generate(ModelRequest(prompt="hi", model_tier="standard"))

    assert response.usage.actual_input == 42
    assert response.usage.actual_output == 8
    assert response.usage.cached_input == 10
    assert response.usage.reasoning_tokens == 3
    assert response.usage.total == 50
    assert response.content == "hello from openai"
    assert response.model_tier == "standard"
    assert response.provider == "openai"


def test_missing_usage_details_map_to_none_not_zero():
    payload = dict(RESPONSE_PAYLOAD)
    payload["usage"] = {"input_tokens": 5, "output_tokens": 2, "total_tokens": 7}

    provider = OpenAIProvider(model_name="gpt-4.1", api_key="sk-test")
    with patch("urllib.request.urlopen", return_value=_FakeHTTPResponse(payload)):
        response = provider.generate(ModelRequest(prompt="hi", model_tier="standard"))

    assert response.usage.cached_input is None
    assert response.usage.reasoning_tokens is None


def test_missing_usage_object_entirely_yields_estimated_fallback():
    payload = dict(RESPONSE_PAYLOAD)
    payload.pop("usage")

    provider = OpenAIProvider(model_name="gpt-4.1", api_key="sk-test")
    with patch("urllib.request.urlopen", return_value=_FakeHTTPResponse(payload)):
        response = provider.generate(ModelRequest(prompt="hi", model_tier="standard"))

    assert response.usage.actual_input is None
    assert response.usage.actual_output is None
    assert response.usage.estimated_input > 0
    assert response.usage.estimated_output > 0


def test_response_failed_status_raises():
    payload = {"id": "resp_x", "status": "failed", "error": {"message": "boom"}}
    provider = OpenAIProvider(model_name="gpt-4.1", api_key="sk-test")
    with patch("urllib.request.urlopen", return_value=_FakeHTTPResponse(payload)):
        with pytest.raises(OpenAIProviderError):
            provider.generate(ModelRequest(prompt="hi", model_tier="standard"))


def test_http_error_raises_provider_error():
    provider = OpenAIProvider(model_name="gpt-4.1", api_key="sk-test")

    def raise_http_error(req, timeout=None):
        raise urllib.error.HTTPError(
            url="https://api.openai.com/v1/responses",
            code=401,
            msg="Unauthorized",
            hdrs=None,
            fp=io.BytesIO(b'{"error": {"message": "invalid api key"}}'),
        )

    with patch("urllib.request.urlopen", side_effect=raise_http_error):
        with pytest.raises(OpenAIProviderError):
            provider.generate(ModelRequest(prompt="hi", model_tier="standard"))


def test_health_reports_missing_key_without_network_call():
    provider = OpenAIProvider(model_name="gpt-4.1")
    with patch("urllib.request.urlopen") as mock_urlopen:
        health = provider.health()
    mock_urlopen.assert_not_called()
    assert health["status"] == "error"
    assert health["reason"] == "missing_api_key"


def test_health_ok_when_key_present_and_endpoint_reachable():
    provider = OpenAIProvider(model_name="gpt-4.1", api_key="sk-test")

    with patch("urllib.request.urlopen", return_value=_FakeHTTPResponse({}, status=200)):
        health = provider.health()

    assert health["status"] == "ok"


def test_satisfies_model_provider_protocol():
    provider = OpenAIProvider(model_name="gpt-4.1", api_key="sk-test")
    assert isinstance(provider, ModelProvider)
