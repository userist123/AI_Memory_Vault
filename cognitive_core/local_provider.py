"""LocalModelProvider backed by a local Ollama HTTP endpoint.

This adapter deliberately depends only on the provider-neutral model contract
and Python's standard library. No cloud SDK, API key, or external network is
required. The default endpoint is Ollama's local generate API.
"""
from __future__ import annotations

import json
from typing import Any, Dict, Optional
from urllib import error as urllib_error
from urllib import request as urllib_request

from .model_provider import ModelProvider, ModelRequest, ModelResponse, TokenUsage


class LocalProviderError(RuntimeError):
    """Raised when the local model endpoint cannot satisfy a request."""


class LocalProvider:
    """ModelProvider implementation for a local Ollama instance."""

    def __init__(
        self,
        model_name: str,
        *,
        base_url: str = "http://localhost:11434",
        timeout_seconds: float = 120.0,
    ) -> None:
        model_name = str(model_name).strip()
        if not model_name:
            raise ValueError("model_name must not be empty")
        if timeout_seconds <= 0:
            raise ValueError("timeout_seconds must be positive")

        self.model_name = model_name
        self.base_url = base_url.rstrip("/")
        self.timeout_seconds = float(timeout_seconds)

    def _post_json(self, path: str, payload: Dict[str, Any]) -> Dict[str, Any]:
        url = f"{self.base_url}{path}"
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        request = urllib_request.Request(
            url,
            data=body,
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        try:
            with urllib_request.urlopen(request, timeout=self.timeout_seconds) as response:
                raw = response.read().decode("utf-8")
        except urllib_error.HTTPError as exc:
            details = exc.read().decode("utf-8", errors="replace")
            raise LocalProviderError(
                f"Local model HTTP {exc.code} from {url}: {details[:500]}"
            ) from exc
        except (urllib_error.URLError, TimeoutError, OSError) as exc:
            raise LocalProviderError(
                f"Could not reach local model endpoint {url}: {exc}"
            ) from exc

        try:
            data = json.loads(raw)
        except json.JSONDecodeError as exc:
            raise LocalProviderError(f"Invalid JSON response from {url}") from exc

        if not isinstance(data, dict):
            raise LocalProviderError("Local model response must be a JSON object")
        return data

    def _get_json(self, path: str) -> Dict[str, Any]:
        url = f"{self.base_url}{path}"
        request = urllib_request.Request(url, method="GET")
        try:
            with urllib_request.urlopen(request, timeout=self.timeout_seconds) as response:
                raw = response.read().decode("utf-8")
        except urllib_error.HTTPError as exc:
            details = exc.read().decode("utf-8", errors="replace")
            raise LocalProviderError(
                f"Local model HTTP {exc.code} from {url}: {details[:500]}"
            ) from exc
        except (urllib_error.URLError, TimeoutError, OSError) as exc:
            raise LocalProviderError(
                f"Could not reach local model endpoint {url}: {exc}"
            ) from exc

        try:
            data = json.loads(raw)
        except json.JSONDecodeError as exc:
            raise LocalProviderError(f"Invalid JSON response from {url}") from exc

        if not isinstance(data, dict):
            raise LocalProviderError("Local model response must be a JSON object")
        return data

    def generate(self, request: ModelRequest) -> ModelResponse:
        if request.tools:
            raise LocalProviderError(
                "LocalProvider /api/generate does not implement the ModelProvider "
                "tool contract yet; refusing to silently ignore request.tools."
            )

        payload: Dict[str, Any] = {
            "model": self.model_name,
            "prompt": request.prompt,
            "stream": False,
        }
        if request.system_prompt:
            payload["system"] = request.system_prompt

        # Allow provider-specific generation options without coupling the
        # provider-neutral ModelRequest schema to Ollama-specific fields.
        options = request.metadata.get("local_options")
        if options is not None:
            if not isinstance(options, dict):
                raise LocalProviderError("request.metadata['local_options'] must be an object")
            payload["options"] = dict(options)

        if "think" in request.metadata:
            payload["think"] = request.metadata["think"]
        if "keep_alive" in request.metadata:
            payload["keep_alive"] = request.metadata["keep_alive"]
        if "format" in request.metadata:
            payload["format"] = request.metadata["format"]

        data = self._post_json("/api/generate", payload)
        content = data.get("response", "")
        if not isinstance(content, str):
            raise LocalProviderError("Local model response field 'response' must be a string")

        prompt_eval_count = data.get("prompt_eval_count")
        eval_count = data.get("eval_count")
        prompt_tokens = int(prompt_eval_count) if isinstance(prompt_eval_count, int) else None
        output_tokens = int(eval_count) if isinstance(eval_count, int) else None
        actual_total = (
            prompt_tokens + output_tokens
            if prompt_tokens is not None and output_tokens is not None
            else None
        )

        usage = TokenUsage(
            actual_input=prompt_tokens,
            actual_output=output_tokens,
            total=actual_total,
            estimated_input=max(1, (len(request.prompt) + 2) // 3),
            estimated_output=max(1, (len(content) + 2) // 3),
        )

        metadata = {
            "done": data.get("done"),
            "done_reason": data.get("done_reason"),
            "total_duration_ns": data.get("total_duration"),
            "load_duration_ns": data.get("load_duration"),
            "prompt_eval_duration_ns": data.get("prompt_eval_duration"),
            "eval_duration_ns": data.get("eval_duration"),
            "thinking": data.get("thinking"),
        }

        return ModelResponse(
            content=content,
            provider="local",
            model=str(data.get("model") or self.model_name),
            model_tier=request.model_tier,
            usage=usage,
            metadata=metadata,
        )

    def health(self) -> Dict[str, Any]:
        try:
            data = self._get_json("/api/tags")
        except LocalProviderError as exc:
            return {
                "status": "unavailable",
                "provider": "local",
                "model": self.model_name,
                "error": str(exc),
            }

        models = data.get("models", [])
        available = False
        if isinstance(models, list):
            for entry in models:
                if isinstance(entry, dict) and entry.get("name") == self.model_name:
                    available = True
                    break

        return {
            "status": "ok" if available else "model_not_found",
            "provider": "local",
            "model": self.model_name,
            "model_available": available,
            "available_model_count": len(models) if isinstance(models, list) else 0,
        }
