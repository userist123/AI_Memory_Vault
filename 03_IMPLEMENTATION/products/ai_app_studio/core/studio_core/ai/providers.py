"""AI providers (spec §4.3): Claude Code CLI, Anthropic API, OpenAI API, Google Gemini API.

Claude Code (`claude -p`) and the Anthropic API are DIFFERENT capabilities: Claude Code uses the owner's
Claude Code login and can drive the studio through MCP tools; the Anthropic API needs API credentials and
is used here for single requests (text / image analysis). Nothing assumes a subscription includes API credits.
Credentials are read from the environment / the provider's own login — never stored by the studio, never
logged, never put into prompts.
"""

from __future__ import annotations

import base64
import json
import os
import shutil
import subprocess
import tempfile
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import httpx

from ..errors import Unavailable, ValidationFailed

ANTHROPIC_MODEL = "claude-opus-5-5"
# $ per 1M tokens (input, output) — from the Anthropic model table; used only to report/limit spend
ANTHROPIC_PRICES = {"claude-opus-5-5": (4.0, 20.0), "claude-sonnet-5-5": (2.0, 10.0), "claude-haiku-5-5": (0.10, 0.50),
                    "claude-opus-4-8": (5.0, 25.0), "claude-opus-5": (5.0, 25.0)}


@dataclass
class Result:
    provider: str
    model: str
    text: str
    input_tokens: int = 0
    output_tokens: int = 0
    cost_usd: float | None = None
    seconds: float = 0.0
    stop_reason: str | None = None
    extra: dict[str, Any] = field(default_factory=dict)


class Provider:
    name = "base"
    supports_images = False
    supports_tools = False

    def available(self) -> bool:
        raise NotImplementedError

    def complete(self, system: str, prompt: str, images: list[bytes] | None = None, *, max_tokens: int = 4000,
                 timeout: float = 300, budget_usd: float | None = None) -> Result:
        raise NotImplementedError


# ---------------------------------------------------------------- Claude Code CLI
class ClaudeCodeProvider(Provider):
    """`claude -p` (the owner's Claude Code). Images are written to a private temp folder that is the
    only directory the run may Read. Agent runs get the studio MCP server with an explicit tool allow-list."""

    name = "claude-code"
    supports_images = True
    supports_tools = True

    def __init__(self, exe: str | None = None, model: str | None = None) -> None:
        self.exe = exe or shutil.which("claude")
        self.model = model or os.environ.get("AAS_CLAUDE_CODE_MODEL") or None

    def available(self) -> bool:
        return bool(self.exe)

    def _run(self, args: list[str], prompt: str, cwd: Path, timeout: float) -> dict[str, Any]:
        if not self.exe:
            raise Unavailable("Claude Code CLI not found")
        cmd = [self.exe, "-p", prompt, "--output-format", "json", *args]
        if self.model:
            cmd += ["--model", self.model]
        t0 = time.monotonic()
        try:
            r = subprocess.run(cmd, cwd=str(cwd), capture_output=True, text=True, encoding="utf-8", errors="replace",  # noqa: S603
                               timeout=timeout)
        except subprocess.TimeoutExpired as e:
            raise ValidationFailed("Claude Code run exceeded its time limit", seconds=timeout) from e
        try:
            data = json.loads(r.stdout.strip().splitlines()[-1]) if r.stdout.strip() else {}
        except ValueError:
            data = {}
        if not data:
            raise ValidationFailed("Claude Code returned no result", exitCode=r.returncode, stderr=r.stderr[-400:])
        data["_seconds"] = round(time.monotonic() - t0, 2)
        return data

    def complete(self, system: str, prompt: str, images: list[bytes] | None = None, *, max_tokens: int = 4000,
                 timeout: float = 300, budget_usd: float | None = None) -> Result:
        with tempfile.TemporaryDirectory(prefix="aws-cc-") as d:
            paths = []
            for i, img in enumerate(images or []):
                p = Path(d) / f"image_{i}.png"
                p.write_bytes(img)
                paths.append(p.name)
            full = prompt
            if paths:
                full += "\n\nThe images to analyse are in the current folder: " + ", ".join(paths) + ". Use the Read tool on them."
            args = ["--append-system-prompt", system, "--max-turns", "6", "--strict-mcp-config",
                    "--allowedTools", "Read", "--disallowedTools", "Bash,Edit,Write,WebFetch,WebSearch"]
            if budget_usd:
                args += ["--max-budget-usd", f"{budget_usd:.2f}"]
            data = self._run(args, full, Path(d), timeout)
        usage = data.get("usage") or {}
        return Result(self.name, str(data.get("model") or self.model or "claude-code"), str(data.get("result", "")),
                      int(usage.get("input_tokens") or 0), int(usage.get("output_tokens") or 0),
                      data.get("total_cost_usd"), data["_seconds"], data.get("subtype"),
                      {"numTurns": data.get("num_turns"), "isError": data.get("is_error")})

    def run_agent(self, system: str, task: str, mcp_config: Path, allowed_tools: list[str], *, cwd: Path,
                  max_turns: int, budget_usd: float, timeout: float) -> Result:
        allowed = ",".join(f"mcp__ai-website-studio__{t}" for t in allowed_tools)
        args = ["--append-system-prompt", system, "--mcp-config", str(mcp_config), "--strict-mcp-config",
                "--allowedTools", allowed, "--disallowedTools", "Bash,Edit,Write,NotebookEdit,WebFetch,WebSearch",
                "--max-turns", str(max_turns), "--max-budget-usd", f"{budget_usd:.2f}"]
        data = self._run(args, task, cwd, timeout)
        usage = data.get("usage") or {}
        return Result(self.name, str(data.get("model") or self.model or "claude-code"), str(data.get("result", "")),
                      int(usage.get("input_tokens") or 0), int(usage.get("output_tokens") or 0),
                      data.get("total_cost_usd"), data["_seconds"], data.get("subtype"),
                      {"numTurns": data.get("num_turns"), "isError": data.get("is_error"), "sessionId": data.get("session_id")})


# ---------------------------------------------------------------- Anthropic API (official SDK)
class AnthropicProvider(Provider):
    name = "anthropic"
    supports_images = True

    def __init__(self, model: str | None = None, client: Any = None) -> None:
        self.model = model or os.environ.get("AAS_ANTHROPIC_MODEL") or ANTHROPIC_MODEL
        self._client = client

    def available(self) -> bool:
        if self._client is not None:
            return True
        return bool(os.environ.get("ANTHROPIC_API_KEY") or os.environ.get("ANTHROPIC_AUTH_TOKEN")
                    or os.environ.get("ANTHROPIC_PROFILE") or (Path.home() / ".config" / "anthropic").is_dir())

    def client(self) -> Any:
        if self._client is None:
            import anthropic
            self._client = anthropic.Anthropic()  # resolves env key / token / `ant auth login` profile
        return self._client

    def complete(self, system: str, prompt: str, images: list[bytes] | None = None, *, max_tokens: int = 4000,
                 timeout: float = 300, budget_usd: float | None = None) -> Result:
        import anthropic
        content: list[dict[str, Any]] = [
            {"type": "image", "source": {"type": "base64", "media_type": "image/png",
                                         "data": base64.standard_b64encode(img).decode("ascii")}} for img in images or []]
        content.append({"type": "text", "text": prompt})
        t0 = time.monotonic()
        try:
            resp = self.client().with_options(timeout=timeout).beta.messages.create(
                model=self.model, max_tokens=max_tokens, system=system,
                messages=[{"role": "user", "content": content}],
                output_config={"effort": "medium"},
                betas=["server-side-fallback-2026-07-01"], fallbacks="default")  # refusal fallback (opt-in, on by default)
        except anthropic.AuthenticationError as e:
            raise Unavailable("Anthropic API credentials missing or invalid") from e
        except anthropic.RateLimitError as e:
            raise Unavailable("Anthropic API rate limited; retry later") from e
        except anthropic.APIStatusError as e:
            raise ValidationFailed(f"Anthropic API error {e.status_code}", apiMessage=str(e.message)[:300]) from e
        except anthropic.APIConnectionError as e:
            raise Unavailable("Anthropic API not reachable") from e
        if resp.stop_reason == "refusal":
            raise ValidationFailed("the model declined this request", category=getattr(resp.stop_details, "category", None))
        text = "".join(b.text for b in resp.content if getattr(b, "type", "") == "text")
        pin, pout = ANTHROPIC_PRICES.get(str(resp.model), ANTHROPIC_PRICES[ANTHROPIC_MODEL])
        cost = resp.usage.input_tokens * pin / 1e6 + resp.usage.output_tokens * pout / 1e6
        return Result(self.name, str(resp.model), text, resp.usage.input_tokens, resp.usage.output_tokens, round(cost, 6),
                      round(time.monotonic() - t0, 2), resp.stop_reason)


# ---------------------------------------------------------------- OpenAI / Gemini (REST)
class OpenAIProvider(Provider):
    name = "openai"
    supports_images = True

    def __init__(self, model: str | None = None, transport: httpx.BaseTransport | None = None) -> None:
        self.model = model or os.environ.get("AAS_OPENAI_MODEL") or "gpt-5"
        self.transport = transport

    def available(self) -> bool:
        return bool(os.environ.get("OPENAI_API_KEY")) or self.transport is not None

    def complete(self, system: str, prompt: str, images: list[bytes] | None = None, *, max_tokens: int = 4000,
                 timeout: float = 300, budget_usd: float | None = None) -> Result:
        parts: list[dict[str, Any]] = [{"type": "text", "text": prompt}]
        for img in images or []:
            parts.append({"type": "image_url", "image_url": {"url": "data:image/png;base64," + base64.b64encode(img).decode()}})
        body = {"model": self.model, "messages": [{"role": "system", "content": system}, {"role": "user", "content": parts}],
                "max_completion_tokens": max_tokens}
        t0 = time.monotonic()
        with httpx.Client(timeout=timeout, transport=self.transport) as c:
            r = c.post("https://api.openai.com/v1/chat/completions", json=body,
                       headers={"Authorization": f"Bearer {os.environ.get('OPENAI_API_KEY', '')}"})
        if r.status_code in (401, 403):
            raise Unavailable("OpenAI API credentials missing or invalid")
        if r.status_code >= 400:
            raise ValidationFailed(f"OpenAI API error {r.status_code}", apiMessage=r.text[:300])
        d = r.json()
        u = d.get("usage") or {}
        return Result(self.name, str(d.get("model", self.model)), d["choices"][0]["message"].get("content") or "",
                      int(u.get("prompt_tokens") or 0), int(u.get("completion_tokens") or 0), None,
                      round(time.monotonic() - t0, 2), d["choices"][0].get("finish_reason"))


class GeminiProvider(Provider):
    name = "gemini"
    supports_images = True

    def __init__(self, model: str | None = None, transport: httpx.BaseTransport | None = None) -> None:
        self.model = model or os.environ.get("AAS_GEMINI_MODEL") or "gemini-2.5-pro"
        self.transport = transport

    def available(self) -> bool:
        return bool(os.environ.get("GEMINI_API_KEY")) or self.transport is not None

    def complete(self, system: str, prompt: str, images: list[bytes] | None = None, *, max_tokens: int = 4000,
                 timeout: float = 300, budget_usd: float | None = None) -> Result:
        parts: list[dict[str, Any]] = [{"inline_data": {"mime_type": "image/png", "data": base64.b64encode(i).decode()}}
                                       for i in images or []]
        parts.append({"text": prompt})
        body = {"systemInstruction": {"parts": [{"text": system}]}, "contents": [{"role": "user", "parts": parts}],
                "generationConfig": {"maxOutputTokens": max_tokens}}
        t0 = time.monotonic()
        url = f"https://generativelanguage.googleapis.com/v1beta/models/{self.model}:generateContent"
        with httpx.Client(timeout=timeout, transport=self.transport) as c:
            r = c.post(url, json=body, headers={"x-goog-api-key": os.environ.get("GEMINI_API_KEY", "")})
        if r.status_code in (401, 403):
            raise Unavailable("Gemini API credentials missing or invalid")
        if r.status_code >= 400:
            raise ValidationFailed(f"Gemini API error {r.status_code}", apiMessage=r.text[:300])
        d = r.json()
        cand = (d.get("candidates") or [{}])[0]
        text = "".join(p.get("text", "") for p in (cand.get("content") or {}).get("parts", []))
        u = d.get("usageMetadata") or {}
        return Result(self.name, self.model, text, int(u.get("promptTokenCount") or 0), int(u.get("candidatesTokenCount") or 0),
                      None, round(time.monotonic() - t0, 2), cand.get("finishReason"))


def parse_json_reply(text: str) -> dict[str, Any]:
    """Models sometimes wrap JSON in prose or fences; take the outermost object."""
    s, e = text.find("{"), text.rfind("}")
    if s < 0 or e <= s:
        raise ValidationFailed("model reply contained no JSON object", reply=text[:300])
    try:
        out = json.loads(text[s:e + 1])
    except ValueError as err:
        raise ValidationFailed("model reply JSON could not be parsed", reply=text[:300]) from err
    if not isinstance(out, dict):
        raise ValidationFailed("model reply JSON is not an object")
    return out
