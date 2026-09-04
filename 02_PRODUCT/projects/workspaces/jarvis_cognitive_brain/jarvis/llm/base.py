"""
Base LLM Provider Interface and Cancellation Primitives.
"""

from abc import ABC, abstractmethod
from typing import AsyncIterator, Type, TypeVar, Optional, Dict, Any, List, Callable
import threading
import json
import re
from pydantic import BaseModel

T = TypeVar("T", bound=BaseModel)


class CancellationToken:
    """Thread-safe cancellation token supporting Barge-In and timeout interruptions."""

    def __init__(self):
        self._is_cancelled = threading.Event()
        self._callbacks: List[Callable[[], None]] = []
        self._lock = threading.Lock()

    def cancel(self, reason: str = "cancelled") -> None:
        """Trigger cancellation and fire registered callbacks."""
        with self._lock:
            if not self._is_cancelled.is_set():
                self._is_cancelled.set()
                for cb in self._callbacks:
                    try:
                        cb()
                    except Exception:
                        pass

    @property
    def is_cancelled(self) -> bool:
        """Check if cancellation has been requested."""
        return self._is_cancelled.is_set()

    def register_callback(self, cb: Callable[[], None]) -> None:
        """Register a callback to be invoked immediately upon cancellation."""
        with self._lock:
            if self._is_cancelled.is_set():
                try:
                    cb()
                except Exception:
                    pass
            else:
                self._callbacks.append(cb)

    def raise_if_cancelled(self) -> None:
        """Raise CancellationError if cancellation token is tripped."""
        if self.is_cancelled:
            raise CancellationError("Operation cancelled by user or barge-in interruption.")


class CancellationError(Exception):
    """Raised when an ongoing LLM generation or audio stream is cancelled."""
    pass


class ProviderUnavailableError(Exception):
    """Raised when an LLM provider endpoint is unreachable or times out."""
    pass


class BaseLLMProvider(ABC):
    """Abstract interface for modular LLM backends."""

    @abstractmethod
    async def generate(
        self,
        prompt: str,
        system_prompt: Optional[str] = None,
        cancellation_token: Optional[CancellationToken] = None,
        **kwargs: Any,
    ) -> str:
        """Generate a complete text completion."""
        pass

    @abstractmethod
    async def chat(
        self,
        messages: List[Dict[str, str]],
        cancellation_token: Optional[CancellationToken] = None,
        **kwargs: Any,
    ) -> str:
        """Generate response from a multi-turn chat message history."""
        pass

    @abstractmethod
    async def stream(
        self,
        prompt: str,
        system_prompt: Optional[str] = None,
        cancellation_token: Optional[CancellationToken] = None,
        **kwargs: Any,
    ) -> AsyncIterator[str]:
        """Stream token chunks asynchronously for sub-300ms TTFB audio synthesis."""
        pass

    async def generate_structured(
        self,
        prompt: str,
        schema: Type[T],
        system_prompt: Optional[str] = None,
        cancellation_token: Optional[CancellationToken] = None,
        **kwargs: Any,
    ) -> T:
        """Generate JSON structured output validated against a Pydantic schema."""
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        schema_json = json.dumps(schema.model_json_schema(), indent=2)
        guidance = (
            f"\n\nSTRICT REQUIREMENT: Respond ONLY with valid JSON conforming to this schema:\n"
            f"```json\n{schema_json}\n```\n"
            f"Do not include any surrounding markdown or explanations outside the JSON."
        )
        full_prompt = prompt + guidance
        raw_text = await self.generate(
            full_prompt,
            system_prompt=system_prompt,
            cancellation_token=cancellation_token,
            **kwargs,
        )
        return self._extract_and_validate(raw_text, schema)

    def _extract_and_validate(self, raw_text: str, schema: Type[T]) -> T:
        """Extract JSON block from LLM output and validate against schema."""
        text = raw_text.strip()
        # Look for markdown code fence first
        match = re.search(r"```(?:json)?\s*(\{.*\}|\[.*\])\s*```", text, re.DOTALL)
        if match:
            json_str = match.group(1)
        else:
            # Look for outermost matching curly braces
            start = text.find("{")
            end = text.rfind("}")
            if start != -1 and end != -1 and end > start:
                json_str = text[start : end + 1]
            else:
                raise ValueError(f"No JSON object found in LLM response: {raw_text}")

        try:
            parsed = json.loads(json_str)
        except json.JSONDecodeError as err:
            raise ValueError(f"Failed to parse JSON from LLM response: {json_str}") from err

        return schema.model_validate(parsed)
