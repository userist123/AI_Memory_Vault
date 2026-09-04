"""
Modular Cloud LLM Providers for Google Gemini (Antigravity SDK) and Anthropic Claude.
"""

from typing import AsyncIterator, List, Dict, Any, Optional
import os
import httpx

from jarvis.llm.base import (
    BaseLLMProvider,
    CancellationToken,
    CancellationError,
    ProviderUnavailableError,
)


class GeminiProvider(BaseLLMProvider):
    """Google Gemini / Antigravity LLM Provider."""

    def __init__(
        self,
        api_key: Optional[str] = None,
        model: str = "gemini-2.0-flash",
        timeout: float = 30.0,
    ):
        if api_key is not None:
            self.api_key = api_key
        else:
            self.api_key = os.environ.get("GEMINI_API_KEY") or os.environ.get("GOOGLE_API_KEY")
        self.model = model
        self.timeout = timeout

    async def generate(
        self,
        prompt: str,
        system_prompt: Optional[str] = None,
        cancellation_token: Optional[CancellationToken] = None,
        **kwargs: Any,
    ) -> str:
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        if not self.api_key:
            raise ProviderUnavailableError("Gemini API key is not configured.")

        url = f"https://generativelanguage.googleapis.com/v1beta/models/{self.model}:generateContent?key={self.api_key}"
        contents = []
        if system_prompt:
            contents.append({"role": "user", "parts": [{"text": f"System Instructions: {system_prompt}"}]})
        contents.append({"role": "user", "parts": [{"text": prompt}]})

        payload = {"contents": contents}

        async with httpx.AsyncClient(timeout=self.timeout) as client:
            try:
                resp = await client.post(url, json=payload)
                resp.raise_for_status()
                data = resp.json()
                candidates = data.get("candidates", [])
                if candidates:
                    parts = candidates[0].get("content", {}).get("parts", [])
                    return "".join(p.get("text", "") for p in parts)
                return ""
            except Exception as exc:
                raise ProviderUnavailableError(f"Gemini API request failed: {exc}") from exc

    async def chat(
        self,
        messages: List[Dict[str, str]],
        cancellation_token: Optional[CancellationToken] = None,
        **kwargs: Any,
    ) -> str:
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        if not self.api_key:
            raise ProviderUnavailableError("Gemini API key is not configured.")

        contents = []
        for m in messages:
            role = "model" if m.get("role") in ["assistant", "model"] else "user"
            contents.append({"role": role, "parts": [{"text": m.get("content", "")}]})

        url = f"https://generativelanguage.googleapis.com/v1beta/models/{self.model}:generateContent?key={self.api_key}"
        payload = {"contents": contents}

        async with httpx.AsyncClient(timeout=self.timeout) as client:
            try:
                resp = await client.post(url, json=payload)
                resp.raise_for_status()
                data = resp.json()
                candidates = data.get("candidates", [])
                if candidates:
                    parts = candidates[0].get("content", {}).get("parts", [])
                    return "".join(p.get("text", "") for p in parts)
                return ""
            except Exception as exc:
                raise ProviderUnavailableError(f"Gemini API chat request failed: {exc}") from exc

    async def stream(
        self,
        prompt: str,
        system_prompt: Optional[str] = None,
        cancellation_token: Optional[CancellationToken] = None,
        **kwargs: Any,
    ) -> AsyncIterator[str]:
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        if not self.api_key:
            raise ProviderUnavailableError("Gemini API key is not configured.")

        full_text = await self.generate(prompt, system_prompt=system_prompt, cancellation_token=cancellation_token, **kwargs)
        for token in full_text.split(" "):
            if cancellation_token and cancellation_token.is_cancelled:
                raise CancellationError("Gemini stream cancelled.")
            yield token + " "


class ClaudeProvider(BaseLLMProvider):
    """Anthropic Claude LLM Provider."""

    def __init__(
        self,
        api_key: Optional[str] = None,
        model: str = "claude-3-5-sonnet-20241022",
        timeout: float = 30.0,
    ):
        if api_key is not None:
            self.api_key = api_key
        else:
            self.api_key = os.environ.get("ANTHROPIC_API_KEY")
        self.model = model
        self.timeout = timeout

    async def generate(
        self,
        prompt: str,
        system_prompt: Optional[str] = None,
        cancellation_token: Optional[CancellationToken] = None,
        **kwargs: Any,
    ) -> str:
        return await self.chat(
            messages=[{"role": "user", "content": prompt}],
            cancellation_token=cancellation_token,
            system_prompt=system_prompt,
            **kwargs,
        )

    async def chat(
        self,
        messages: List[Dict[str, str]],
        cancellation_token: Optional[CancellationToken] = None,
        system_prompt: Optional[str] = None,
        **kwargs: Any,
    ) -> str:
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        if not self.api_key:
            raise ProviderUnavailableError("Claude API key is not configured.")

        url = "https://api.anthropic.com/v1/messages"
        headers = {
            "x-api-key": self.api_key,
            "anthropic-version": "2023-06-01",
            "content-type": "application/json",
        }
        payload = {
            "model": self.model,
            "max_tokens": kwargs.get("max_tokens", 1024),
            "messages": messages,
        }
        if system_prompt:
            payload["system"] = system_prompt

        async with httpx.AsyncClient(timeout=self.timeout) as client:
            try:
                resp = await client.post(url, headers=headers, json=payload)
                resp.raise_for_status()
                data = resp.json()
                contents = data.get("content", [])
                return "".join(c.get("text", "") for c in contents if c.get("type") == "text")
            except Exception as exc:
                raise ProviderUnavailableError(f"Claude API chat request failed: {exc}") from exc

    async def stream(
        self,
        prompt: str,
        system_prompt: Optional[str] = None,
        cancellation_token: Optional[CancellationToken] = None,
        **kwargs: Any,
    ) -> AsyncIterator[str]:
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        full_text = await self.generate(prompt, system_prompt=system_prompt, cancellation_token=cancellation_token, **kwargs)
        for token in full_text.split(" "):
            if cancellation_token and cancellation_token.is_cancelled:
                raise CancellationError("Claude stream cancelled.")
            yield token + " "
