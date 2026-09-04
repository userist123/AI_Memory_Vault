import json
from unittest.mock import MagicMock, patch

from cognitive_core.ollama_extractor import OllamaExtractionAdapter


def test_returns_empty_list_on_connection_error():
    adapter = OllamaExtractionAdapter(host="http://127.0.0.1:1", timeout_seconds=1)
    with patch("urllib.request.urlopen", side_effect=OSError("connection refused")):
        assert list(adapter("orice text")) == []


def test_parses_valid_json_response():
    fake_response = MagicMock()
    fake_response.read.return_value = json.dumps({
        "response": '[{"type": "fact", "category": "test", "content": "exemplu extras"}]'
    }).encode("utf-8")
    fake_response.__enter__.return_value = fake_response
    with patch("urllib.request.urlopen", return_value=fake_response):
        adapter = OllamaExtractionAdapter()
        results = list(adapter("text"))
    assert results and results[0]["content"] == "exemplu extras"


def test_ignores_malformed_response():
    fake_response = MagicMock()
    fake_response.read.return_value = json.dumps({"response": "not json"}).encode("utf-8")
    fake_response.__enter__.return_value = fake_response
    with patch("urllib.request.urlopen", return_value=fake_response):
        adapter = OllamaExtractionAdapter()
        results = list(adapter("text"))
    assert results == []
