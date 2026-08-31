"""
Deterministic Mock LLM Provider for offline unit and integration tests.
"""

from typing import AsyncIterator, List, Dict, Any, Optional, Type, Union
import asyncio
import json
from pydantic import BaseModel

from jarvis.llm.base import (
    BaseLLMProvider,
    CancellationToken,
    CancellationError,
    ProviderUnavailableError,
    T,
)


class MockLLMProvider(BaseLLMProvider):
    """Deterministic mock provider for testing without external API or Ollama dependencies."""

    def __init__(
        self,
        default_response: str = "Mock response from Jarvis Brain.",
        streaming_delay: float = 0.001,
        should_fail: bool = False,
    ):
        self.default_response = default_response
        self.streaming_delay = streaming_delay
        self.should_fail = should_fail
        self.response_queue: List[str] = []
        self.structured_responses: Dict[str, Any] = {}
        self.calls: List[Dict[str, Any]] = []

    def set_next_response(self, response: str) -> None:
        """Enqueue a response for the next generate/chat call."""
        self.response_queue.append(response)

    def set_structured_response(self, schema_cls_name: str, response_obj: Union[BaseModel, Dict[str, Any]]) -> None:
        """Set a canned structured response keyed by the Pydantic schema class name."""
        if isinstance(response_obj, BaseModel):
            self.structured_responses[schema_cls_name] = response_obj.model_dump()
        else:
            self.structured_responses[schema_cls_name] = response_obj

    async def generate(
        self,
        prompt: str,
        system_prompt: Optional[str] = None,
        cancellation_token: Optional[CancellationToken] = None,
        **kwargs: Any,
    ) -> str:
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        if self.should_fail:
            raise ProviderUnavailableError("Mock provider configured to fail.")

        self.calls.append({
            "type": "generate",
            "prompt": prompt,
            "system_prompt": system_prompt,
            "kwargs": kwargs,
        })

        if self.response_queue:
            return self.response_queue.pop(0)
        return self.default_response

    async def chat(
        self,
        messages: List[Dict[str, str]],
        cancellation_token: Optional[CancellationToken] = None,
        **kwargs: Any,
    ) -> str:
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        if self.should_fail:
            raise ProviderUnavailableError("Mock provider configured to fail.")

        self.calls.append({
            "type": "chat",
            "messages": messages,
            "kwargs": kwargs,
        })

        if self.response_queue:
            return self.response_queue.pop(0)
        return self.default_response

    async def stream(
        self,
        prompt: str,
        system_prompt: Optional[str] = None,
        cancellation_token: Optional[CancellationToken] = None,
        **kwargs: Any,
    ) -> AsyncIterator[str]:
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        if self.should_fail:
            raise ProviderUnavailableError("Mock provider streaming configured to fail.")

        self.calls.append({
            "type": "stream",
            "prompt": prompt,
            "system_prompt": system_prompt,
            "kwargs": kwargs,
        })

        resp = self.response_queue.pop(0) if self.response_queue else self.default_response
        words = resp.split(" ")
        for i, word in enumerate(words):
            if cancellation_token and cancellation_token.is_cancelled:
                raise CancellationError("Stream cancelled by token.")
            if self.streaming_delay > 0:
                await asyncio.sleep(self.streaming_delay)
            token = word + (" " if i < len(words) - 1 else "")
            yield token

    async def generate_structured(
        self,
        prompt: str,
        schema: Type[T],
        system_prompt: Optional[str] = None,
        cancellation_token: Optional[CancellationToken] = None,
        **kwargs: Any,
    ) -> T:
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        if self.should_fail:
            raise ProviderUnavailableError("Mock provider configured to fail.")

        schema_name = schema.__name__
        if schema_name in self.structured_responses:
            data = self.structured_responses[schema_name]
            return schema.model_validate(data)

        # If next queued response is valid JSON, try parsing that
        if self.response_queue:
            raw = self.response_queue.pop(0)
            try:
                return self._extract_and_validate(raw, schema)
            except Exception:
                pass

        # Fallback to standard base class prompt composition
        return await super().generate_structured(
            prompt, schema, system_prompt=system_prompt, cancellation_token=cancellation_token, **kwargs
        )
