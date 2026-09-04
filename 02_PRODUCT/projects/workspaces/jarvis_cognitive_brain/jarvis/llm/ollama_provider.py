"""
Async Ollama LLM Provider targeting local models (e.g., qwen2.5-coder).
"""

from typing import AsyncIterator, List, Dict, Any, Optional, Type
import json
import httpx
from pydantic import BaseModel

from jarvis.llm.base import (
    BaseLLMProvider,
    CancellationToken,
    CancellationError,
    ProviderUnavailableError,
    T,
)


class OllamaProvider(BaseLLMProvider):
    """Local LLM provider calling the Ollama REST API."""

    def __init__(
        self,
        host: str = "http://localhost:11434",
        model: str = "qwen2.5-coder:7b",
        timeout: float = 30.0,
        client: Optional[httpx.AsyncClient] = None,
    ):
        self.host = host.rstrip("/")
        self.model = model
        self.timeout = timeout
        self._external_client = client

    def _get_client(self) -> httpx.AsyncClient:
        if self._external_client is not None:
            return self._external_client
        return httpx.AsyncClient(timeout=self.timeout)

    async def generate(
        self,
        prompt: str,
        system_prompt: Optional[str] = None,
        cancellation_token: Optional[CancellationToken] = None,
        **kwargs: Any,
    ) -> str:
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        url = f"{self.host}/api/generate"
        payload = {
            "model": self.model,
            "prompt": prompt,
            "system": system_prompt or "",
            "stream": False,
            "options": kwargs.get("options", {}),
        }
        if "format" in kwargs:
            payload["format"] = kwargs["format"]

        try:
            client = self._get_client()
            should_close = self._external_client is None
            try:
                resp = await client.post(url, json=payload)
                resp.raise_for_status()
                data = resp.json()
                return data.get("response", "")
            finally:
                if should_close:
                    await client.aclose()
        except (httpx.ConnectError, httpx.TimeoutException) as exc:
            raise ProviderUnavailableError(
                f"Ollama provider at {self.host} unavailable: {exc}"
            ) from exc
        except Exception as exc:
            if isinstance(exc, CancellationError):
                raise
            raise RuntimeError(f"Ollama generation failed: {exc}") from exc

    async def chat(
        self,
        messages: List[Dict[str, str]],
        cancellation_token: Optional[CancellationToken] = None,
        **kwargs: Any,
    ) -> str:
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        url = f"{self.host}/api/chat"
        payload = {
            "model": self.model,
            "messages": messages,
            "stream": False,
            "options": kwargs.get("options", {}),
        }

        try:
            client = self._get_client()
            should_close = self._external_client is None
            try:
                resp = await client.post(url, json=payload)
                resp.raise_for_status()
                data = resp.json()
                return data.get("message", {}).get("content", "")
            finally:
                if should_close:
                    await client.aclose()
        except (httpx.ConnectError, httpx.TimeoutException) as exc:
            raise ProviderUnavailableError(
                f"Ollama provider at {self.host} unavailable: {exc}"
            ) from exc
        except Exception as exc:
            if isinstance(exc, CancellationError):
                raise
            raise RuntimeError(f"Ollama chat failed: {exc}") from exc

    async def stream(
        self,
        prompt: str,
        system_prompt: Optional[str] = None,
        cancellation_token: Optional[CancellationToken] = None,
        **kwargs: Any,
    ) -> AsyncIterator[str]:
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        url = f"{self.host}/api/generate"
        payload = {
            "model": self.model,
            "prompt": prompt,
            "system": system_prompt or "",
            "stream": True,
            "options": kwargs.get("options", {}),
        }

        client = self._get_client()
        should_close = self._external_client is None
        try:
            async with client.stream("POST", url, json=payload) as response:
                response.raise_for_status()
                async for line in response.aiter_lines():
                    if cancellation_token and cancellation_token.is_cancelled:
                        raise CancellationError("Streaming cancelled by user or barge-in.")
                    if not line:
                        continue
                    try:
                        chunk = json.loads(line)
                        token = chunk.get("response", "")
                        if token:
                            yield token
                        if chunk.get("done", False):
                            break
                    except json.JSONDecodeError:
                        continue
        except (httpx.ConnectError, httpx.TimeoutException) as exc:
            raise ProviderUnavailableError(
                f"Ollama streaming provider at {self.host} unavailable: {exc}"
            ) from exc
        finally:
            if should_close:
                await client.aclose()
