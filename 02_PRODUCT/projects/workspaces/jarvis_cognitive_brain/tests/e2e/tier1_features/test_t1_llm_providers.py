"""
Tier 1 Feature Coverage: Modular LLM Provider Layer (R1).
Covers BaseLLMProvider, MockLLMProvider, OllamaProvider, Cloud Providers,
token streaming, structured JSON output validation, and Barge-In cancellation tokens.
"""

import pytest
import asyncio
from typing import AsyncIterator
from pydantic import BaseModel, Field

from jarvis.llm.base import (
    BaseLLMProvider,
    CancellationToken,
    CancellationError,
    ProviderUnavailableError,
)
from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.llm.ollama_provider import OllamaProvider
from jarvis.llm.cloud_providers import GeminiProvider, ClaudeProvider


class IntentSchema(BaseModel):
    intent_type: str = Field(description="Classified user intent")
    confidence: float = Field(description="Classification confidence score")
    entities: list[str] = Field(default_factory=list)


@pytest.mark.asyncio
async def test_mock_provider_generate_basic(mock_llm: MockLLMProvider):
    """Test standard synchronous text generation via MockLLMProvider."""
    prompt = "Hello Jarvis, what is your operational status?"
    response = await mock_llm.generate(prompt)

    assert isinstance(response, str)
    assert len(response) > 0
    assert len(mock_llm.calls) == 1
    assert mock_llm.calls[0]["type"] == "generate"
    assert mock_llm.calls[0]["prompt"] == prompt


@pytest.mark.asyncio
async def test_mock_provider_chat_multiturn(mock_llm: MockLLMProvider):
    """Test multi-turn chat conversation generation."""
    messages = [
        {"role": "system", "content": "You are Jarvis Cognitive Brain."},
        {"role": "user", "content": "Query lighting status in the kitchen."},
    ]
    mock_llm.set_next_response("The kitchen lights are currently turned off.")

    reply = await mock_llm.chat(messages)

    assert reply == "The kitchen lights are currently turned off."
    assert len(mock_llm.calls) == 1
    assert mock_llm.calls[0]["type"] == "chat"
    assert mock_llm.calls[0]["messages"] == messages


@pytest.mark.asyncio
async def test_mock_provider_streaming_tokens(mock_llm: MockLLMProvider):
    """Test token streaming for low-latency audio TTS feeding."""
    prompt = "Give me a brief response."
    mock_llm.set_next_response("Jarvis is online and ready.")

    tokens = []
    async for token in mock_llm.stream(prompt):
        tokens.append(token)

    assert len(tokens) >= 5
    reassembled = "".join(tokens).strip()
    assert reassembled == "Jarvis is online and ready."


@pytest.mark.asyncio
async def test_mock_provider_structured_output_validation(mock_llm: MockLLMProvider):
    """Test structured JSON schema extraction and Pydantic validation."""
    mock_llm.set_structured_response(
        "IntentSchema",
        {"intent_type": "IOT_CONTROL", "confidence": 0.98, "entities": ["light.kitchen"]},
    )

    result = await mock_llm.generate_structured("Turn on the kitchen light", IntentSchema)

    assert isinstance(result, IntentSchema)
    assert result.intent_type == "IOT_CONTROL"
    assert result.confidence == 0.98
    assert "light.kitchen" in result.entities


@pytest.mark.asyncio
async def test_mock_provider_cancellation_token_bargein(mock_llm: MockLLMProvider):
    """Test cancellation token halts ongoing streaming immediately on barge-in."""
    token = CancellationToken()
    mock_llm.set_next_response("One two three four five six seven eight nine ten")
    mock_llm.streaming_delay = 0.05

    stream_iter = mock_llm.stream("Count to ten", cancellation_token=token)
    first_token = await anext(stream_iter)
    assert first_token.strip() == "One"

    # Trigger barge-in
    token.cancel("User started speaking")
    assert token.is_cancelled

    with pytest.raises(CancellationError):
        await anext(stream_iter)


@pytest.mark.asyncio
async def test_ollama_provider_initialization_and_url():
    """Test OllamaProvider parameterization and fallback URL defaults."""
    provider = OllamaProvider(
        host="http://127.0.0.1:11434",
        model="qwen2.5-coder",
        timeout=15.0,
    )

    assert provider.host == "http://127.0.0.1:11434"
    assert provider.model == "qwen2.5-coder"
    assert provider.timeout == 15.0


@pytest.mark.asyncio
async def test_cloud_providers_missing_keys():
    """Test cloud provider initializations gracefully raise on missing API keys."""
    gemini = GeminiProvider(api_key="")
    claude = ClaudeProvider(api_key="")

    with pytest.raises(ProviderUnavailableError):
        await gemini.generate("Hello Gemini")

    with pytest.raises(ProviderUnavailableError):
        await claude.generate("Hello Claude")


@pytest.mark.asyncio
async def test_llm_provider_unavailable_error_handling(mock_llm: MockLLMProvider):
    """Test provider error propagation when backend is configured to fail."""
    mock_llm.should_fail = True

    with pytest.raises(ProviderUnavailableError):
        await mock_llm.generate("Test failure")

    with pytest.raises(ProviderUnavailableError):
        async for _ in mock_llm.stream("Test stream failure"):
            pass
