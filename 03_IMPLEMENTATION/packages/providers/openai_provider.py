"""openai_provider.py — A6: OpenAI Responses API adapter.

Implements ModelProvider (A1) using the OpenAI Responses API
(POST /v1/responses) via urllib from the standard library — no `openai`
SDK dependency, symmetric with LocalProvider (A4).

Invariants:
  - OPENAI_API_KEY is read from environment only, never hardcoded,
    never logged, never placed in ModelRequest/ModelResponse/metadata.
  - store=False and truncation="disabled" are always sent explicitly —
    the Vault's ContextPackBuilder owns the token budget, not OpenAI.
    We never let the provider silently drop context items to fit its
    window; if it would not fit, the request must fail loudly instead.
  - tools are explicitly rejected in A6 (OpenAIToolsNotSupportedError).
    Tool/function-call execution is reserved for a future stage (A7+)
    so model inference and tool orchestration are never mixed into
    one change.
  - Usage fields absent in the response map to None, never to 0 —
    "not reported" and "reported as zero" are different facts and
    must never be conflated when comparing local vs OpenAI usage.
"""
from __future__ import annotations

import json
import os
import urllib.error
import urllib.request
from typing import Any, Dict, Optional

from .model_provider import ModelRequest, ModelResponse, TokenUsage

DEFAULT_BASE_URL = "https://api.openai.com/v1"
DEFAULT_TIMEOUT_SECONDS = 30.0


class OpenAIProviderError(RuntimeError):
    """Base error for OpenAIProvider failures (network, HTTP, or API-level)."""


class OpenAIToolsNotSupportedError(OpenAIProviderError):
    """Raised when a ModelRequest carries tools.

    A6 is model-inference-only. Tool/function calling via the Responses
    API is reserved for a future stage (A7+) so that model execution
    and tool orchestration are never mixed into a single change.
    """


class OpenAIAuthenticationError(OpenAIProviderError):
    """Raised when OPENAI_API_KEY is missing and no api_key was provided."""


class OpenAIProvider:
    """ModelProvider implementation backed by the OpenAI Responses API."""

    def __init__(
        self,
        model_name: str,
        api_key: Optional[str] = None,
        base_url: str = DEFAULT_BASE_URL,
        timeout_seconds: float = DEFAULT_TIMEOUT_SECONDS,
        provider_name: str = "openai",
        api_key_env_var: str = "OPENAI_API_KEY",
    ) -> None:
        self.model_name = model_name
        self.base_url = base_url.rstrip("/")
        self.timeout_seconds = timeout_seconds
        self.provider_name = provider_name
        self._api_key_env_var = api_key_env_var
        self._explicit_api_key = api_key

    def _resolve_api_key(self) -> str:
        key = self._explicit_api_key or os.getenv(self._api_key_env_var)
        if not key:
            raise OpenAIAuthenticationError(
                f"{self._api_key_env_var} is not set and no api_key was "
                "provided. Refusing to call the OpenAI API without credentials."
            )
        return key

    def _build_payload(self, request: ModelRequest) -> Dict[str, Any]:
        payload: Dict[str, Any] = {
            "model": self.model_name,
            "input": request.prompt,
            "store": False,
            "truncation": "disabled",
        }
        if request.system_prompt:
            payload["instructions"] = request.system_prompt
        return payload

    @staticmethod
    def _estimate_tokens(text: str) -> int:
        return max(1, (len(text) + 2) // 3)

    def generate(self, request: ModelRequest) -> ModelResponse:
        if request.tools:
            raise OpenAIToolsNotSupportedError(
                "OpenAIProvider (A6) does not support tool/function calling. "
                "Tool execution is reserved for a future stage (A7+); this "
                "provider is model-inference-only. Received "
                f"{len(request.tools)} tool definition(s)."
            )

        api_key = self._resolve_api_key()
        payload = self._build_payload(request)
        body = json.dumps(payload).encode("utf-8")

        req = urllib.request.Request(
            url=f"{self.base_url}/responses",
            data=body,
            method="POST",
            headers={
                "Content-Type": "application/json",
                "Authorization": f"Bearer {api_key}",
            },
        )

        try:
            with urllib.request.urlopen(req, timeout=self.timeout_seconds) as resp:
                raw = resp.read().decode("utf-8")
        except urllib.error.HTTPError as e:
            error_body = e.read().decode("utf-8", errors="replace") if e.fp else ""
            raise OpenAIProviderError(
                f"OpenAI API returned HTTP {e.code}: {error_body}"
            ) from e
        except urllib.error.URLError as e:
            raise OpenAIProviderError(f"Failed to reach OpenAI API: {e.reason}") from e

        try:
            data = json.loads(raw)
        except json.JSONDecodeError as e:
            raise OpenAIProviderError(f"OpenAI API returned invalid JSON: {e}") from e

        if data.get("status") == "failed":
            error = data.get("error") or {}
            raise OpenAIProviderError(
                f"OpenAI response failed: {error.get('message', 'unknown error')}"
            )

        content = self._extract_output_text(data)
        usage = self._map_usage(data.get("usage"), request, content)

        return ModelResponse(
            content=content,
            provider=self.provider_name,
            model=data.get("model", self.model_name),
            model_tier=request.model_tier,
            usage=usage,
            metadata={
                "response_id": data.get("id"),
                "status": data.get("status"),
            },
        )

    @staticmethod
    def _extract_output_text(data: Dict[str, Any]) -> str:
        output_text = data.get("output_text")
        if isinstance(output_text, str):
            return output_text

        chunks = []
        for item in data.get("output", []) or []:
            if item.get("type") != "message":
                continue
            for part in item.get("content", []) or []:
                if part.get("type") == "output_text" and "text" in part:
                    chunks.append(part["text"])
        return "".join(chunks)

    def _map_usage(
        self,
        usage_obj: Optional[Dict[str, Any]],
        request: ModelRequest,
        content: str,
    ) -> TokenUsage:
        estimated_input = self._estimate_tokens(
            request.prompt + (request.system_prompt or "")
        )
        estimated_output = self._estimate_tokens(content)

        if not usage_obj:
            return TokenUsage(
                estimated_input=estimated_input,
                estimated_output=estimated_output,
            )

        input_details = usage_obj.get("input_tokens_details") or {}
        output_details = usage_obj.get("output_tokens_details") or {}

        return TokenUsage(
            estimated_input=estimated_input,
            estimated_output=estimated_output,
            actual_input=usage_obj.get("input_tokens"),
            actual_output=usage_obj.get("output_tokens"),
            cached_input=input_details.get("cached_tokens"),
            reasoning_tokens=output_details.get("reasoning_tokens"),
            total=usage_obj.get("total_tokens"),
        )

    def health(self) -> Dict[str, Any]:
        try:
            api_key = self._resolve_api_key()
        except OpenAIAuthenticationError:
            return {
                "status": "error",
                "provider": self.provider_name,
                "model": self.model_name,
                "reason": "missing_api_key",
            }

        req = urllib.request.Request(
            url=f"{self.base_url}/models/{self.model_name}",
            method="GET",
            headers={"Authorization": f"Bearer {api_key}"},
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout_seconds) as resp:
                if resp.status == 200:
                    return {
                        "status": "ok",
                        "provider": self.provider_name,
                        "model": self.model_name,
                    }
                return {
                    "status": "error",
                    "provider": self.provider_name,
                    "model": self.model_name,
                    "reason": f"unexpected_status_{resp.status}",
                }
        except urllib.error.HTTPError as e:
            return {
                "status": "error",
                "provider": self.provider_name,
                "model": self.model_name,
                "reason": f"http_{e.code}",
            }
        except urllib.error.URLError as e:
            return {
                "status": "error",
                "provider": self.provider_name,
                "model": self.model_name,
                "reason": str(e.reason),
            }
