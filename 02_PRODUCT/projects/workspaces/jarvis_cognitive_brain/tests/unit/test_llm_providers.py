"""
Unit tests for modular LLM Provider subsystem (Base, Mock, Ollama, Cloud).
"""

import pytest
import httpx
from pydantic import BaseModel, Field
from typing import List

from jarvis.llm.base import (
    BaseLLMProvider,
    CancellationToken,
    CancellationError,
    ProviderUnavailableError,
)
from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.llm.ollama_provider import OllamaProvider
from jarvis.llm.cloud_providers import GeminiProvider, ClaudeProvider


class QueryAnalysis(BaseModel):
    intent: str
    confidence: float = Field(ge=0.0, le=1.0)
    keywords: List[str]


# ============================================================================
# 1. BaseLLMProvider & Cancellation Token Tests
# ============================================================================

def test_cancellation_token_callbacks():
    """Verify CancellationToken fires registered callbacks on cancellation."""
    token = CancellationToken()
    fired = []

    token.register_callback(lambda: fired.append(1))
    token.register_callback(lambda: fired.append(2))
    assert not token.is_cancelled

    token.cancel()
    assert token.is_cancelled
    assert fired == [1, 2]

    # Registering after cancellation fires immediately
    token.register_callback(lambda: fired.append(3))
    assert fired == [1, 2, 3]


@pytest.mark.asyncio
async def test_base_structured_output_extraction():
    """Verify structured output parsing handles markdown code blocks and raw JSON."""
    provider = MockLLMProvider()

    # Raw JSON inside markdown fence
    provider.set_next_response('```json\n{"intent": "search_vault", "confidence": 0.95, "keywords": ["ooda", "memory"]}\n```')
    res = await provider.generate_structured("Analyze this", QueryAnalysis)
    assert res.intent == "search_vault"
    assert res.confidence == 0.95
    assert "ooda" in res.keywords

    # Outermost curly braces without fence
    provider.set_next_response('Here is your plan: {"intent": "iot_light", "confidence": 0.88, "keywords": ["living_room"]} Hope it helps!')
    res2 = await provider.generate_structured("Analyze light", QueryAnalysis)
    assert res2.intent == "iot_light"
    assert res2.confidence == 0.88

    # Invalid JSON raises ValueError
    provider.set_next_response("Sorry, I cannot produce JSON today.")
    with pytest.raises(ValueError, match="No JSON object found"):
        await provider.generate_structured("Failing prompt", QueryAnalysis)


# ============================================================================
# 2. MockLLMProvider Tests
# ============================================================================

@pytest.mark.asyncio
async def test_mock_llm_generate_and_chat():
    """Verify MockLLMProvider generates canned responses and handles chat."""
    provider = MockLLMProvider(default_response="Default answer")

    # Default generation
    ans = await provider.generate("Hello")
    assert ans == "Default answer"

    # Queued response
    provider.set_next_response("Custom queued answer")
    ans2 = await provider.generate("Hello again")
    assert ans2 == "Custom queued answer"

    # Chat
    messages = [{"role": "user", "content": "Tell me a joke"}]
    provider.set_next_response("Why did the chicken cross the road?")
    chat_ans = await provider.chat(messages)
    assert chat_ans == "Why did the chicken cross the road?"
    assert len(provider.calls) == 3


@pytest.mark.asyncio
async def test_mock_llm_streaming_and_cancellation():
    """Verify MockLLMProvider streaming yields tokens and respects cancellation."""
    provider = MockLLMProvider(default_response="The quick brown fox jumps over the lazy dog")

    # Complete stream
    tokens = []
    async for chunk in provider.stream("Stream test"):
        tokens.append(chunk)
    assert len(tokens) == 9
    assert "".join(tokens) == "The quick brown fox jumps over the lazy dog"

    # Stream with cancellation midway
    token = CancellationToken()
    stream_tokens = []
    with pytest.raises(CancellationError):
        async for chunk in provider.stream("Stream cancel", cancellation_token=token):
            stream_tokens.append(chunk)
            if len(stream_tokens) == 3:
                token.cancel()


@pytest.mark.asyncio
async def test_mock_llm_failure_simulation():
    """Verify MockLLMProvider raises ProviderUnavailableError when should_fail=True."""
    provider = MockLLMProvider(should_fail=True)
    with pytest.raises(ProviderUnavailableError):
        await provider.generate("Test prompt")

    with pytest.raises(ProviderUnavailableError):
        await provider.chat([{"role": "user", "content": "Hi"}])


# ============================================================================
# 3. OllamaProvider Tests (Mocked Transport)
# ============================================================================

@pytest.mark.asyncio
async def test_ollama_provider_generate_and_chat():
    """Verify OllamaProvider correctly communicates with /api/generate and /api/chat."""
    def custom_handler(request: httpx.Request) -> httpx.Response:
        url_path = request.url.path
        if url_path == "/api/generate":
            return httpx.Response(200, json={"response": "Ollama generated text", "done": True})
        elif url_path == "/api/chat":
            return httpx.Response(200, json={"message": {"content": "Ollama chat text"}, "done": True})
        return httpx.Response(404)

    transport = httpx.MockTransport(custom_handler)
    async with httpx.AsyncClient(transport=transport) as client:
        ollama = OllamaProvider(host="http://localhost:11434", model="qwen2.5-coder", client=client)

        gen_resp = await ollama.generate("Test query")
        assert gen_resp == "Ollama generated text"

        chat_resp = await ollama.chat([{"role": "user", "content": "Hello Ollama"}])
        assert chat_resp == "Ollama chat text"


@pytest.mark.asyncio
async def test_ollama_provider_streaming():
    """Verify OllamaProvider token streaming using mocked transport."""
    stream_lines = [
        b'{"response": "Cognitive ", "done": false}\n',
        b'{"response": "Brain ", "done": false}\n',
        b'{"response": "Active.", "done": true}\n',
    ]

    def stream_handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, content=b"".join(stream_lines))

    transport = httpx.MockTransport(stream_handler)
    async with httpx.AsyncClient(transport=transport) as client:
        ollama = OllamaProvider(host="http://localhost:11434", model="qwen2.5-coder", client=client)

        chunks = []
        async for chunk in ollama.stream("Start stream"):
            chunks.append(chunk)

        assert "".join(chunks) == "Cognitive Brain Active."


@pytest.mark.asyncio
async def test_ollama_provider_connection_failure():
    """Verify OllamaProvider wraps connection failures into ProviderUnavailableError."""
    def error_handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("Connection refused")

    transport = httpx.MockTransport(error_handler)
    async with httpx.AsyncClient(transport=transport) as client:
        ollama = OllamaProvider(host="http://localhost:11434", client=client)
        with pytest.raises(ProviderUnavailableError, match="unavailable"):
            await ollama.generate("Test")


# ============================================================================
# 4. CloudProviders Fallback Tests
# ============================================================================

@pytest.mark.asyncio
async def test_cloud_providers_unconfigured_raise_error():
    """Verify Gemini and Claude providers raise ProviderUnavailableError when API keys are absent."""
    gemini = GeminiProvider(api_key="")
    with pytest.raises(ProviderUnavailableError, match="Gemini API key is not configured"):
        await gemini.generate("Prompt")

    claude = ClaudeProvider(api_key="")
    with pytest.raises(ProviderUnavailableError, match="Claude API key is not configured"):
        await claude.generate("Prompt")
