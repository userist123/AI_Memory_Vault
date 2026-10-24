#!/usr/bin/env python3
"""Local LLM tier of the cost-router skill (Ollama on the owner's computer).

    python local_llm.py --probe [--endpoint URL]
    python local_llm.py [--kind text|code|fast] [--file PATH ...] [--endpoint URL] "instruction"

Endpoint: --endpoint, else $COST_ROUTER_OLLAMA_ENDPOINT, else `local_llm.endpoint` in the policy.

Sends the instruction, followed by the text of the given files, to the first configured model of
that kind that the local Ollama server actually has, and prints only the model's answer. The files
are read here, so their content never enters the Claude conversation: only the short answer does.

Exit codes: 0 answered; 3 LOCAL_LLM_UNAVAILABLE (no server on loopback, or none of the configured
models installed: the caller falls back to a Claude Haiku subagent); 4 INPUT_TOO_LARGE (never
truncated silently); 2 usage error. Only loopback endpoints are accepted; proxies are bypassed.
Stdlib only.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.request
from pathlib import Path
from urllib.parse import urlparse

sys.path.insert(0, str(Path(__file__).resolve().parent))

DEFAULT = {
    "endpoint": "http://127.0.0.1:11434",
    "timeout_seconds": 300,
    "num_ctx": 16384,
    "max_input_chars": 60000,
    "models": {
        "text": ["qwen2.5:7b-instruct", "mistral:7b-instruct"],
        "code": ["qwen2.5-coder:7b", "qwen2.5-coder:3b"],
        "fast": ["qwen2.5-coder:3b", "qwen2.5:7b-instruct"],
    },
}
LOOPBACK = {"127.0.0.1", "localhost", "::1"}
_OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))  # never route loopback via a proxy


def config() -> dict:
    cfg = dict(DEFAULT)
    try:
        from route import policy_path
        section = json.loads(policy_path().read_text(encoding="utf-8")).get("local_llm") or {}
        cfg.update({k: v for k, v in section.items() if k in DEFAULT})
    except Exception:
        pass
    if os.environ.get("COST_ROUTER_OLLAMA_ENDPOINT"):  # another port on this machine (still loopback-checked)
        cfg["endpoint"] = os.environ["COST_ROUTER_OLLAMA_ENDPOINT"]
    return cfg


def _check_loopback(endpoint: str) -> None:
    host = urlparse(endpoint).hostname or ""
    if host not in LOOPBACK:
        raise ValueError(f"endpoint {endpoint!r} is not loopback; the local tier never sends data off the machine")


def available_models(endpoint: str, timeout: float = 0.5) -> list[str] | None:
    """Model names the server has, or None when nothing answers on the endpoint."""
    try:
        with _OPENER.open(endpoint.rstrip("/") + "/api/tags", timeout=timeout) as r:
            tags = json.loads(r.read().decode("utf-8"))
    except Exception:
        return None
    return [m.get("name") for m in tags.get("models", []) if isinstance(m, dict) and m.get("name")]


def pick(cfg: dict, kind: str, have: list[str]) -> str | None:
    have_set = set(have)
    for name in cfg["models"].get(kind, []):
        if name in have_set or f"{name}:latest" in have_set:
            return name
    return None


def generate(cfg: dict, endpoint: str, model: str, prompt: str) -> str:
    body = json.dumps({"model": model, "prompt": prompt, "stream": False,
                       "options": {"num_ctx": int(cfg["num_ctx"])}}).encode("utf-8")
    req = urllib.request.Request(endpoint.rstrip("/") + "/api/generate", data=body,
                                 headers={"Content-Type": "application/json"})
    with _OPENER.open(req, timeout=float(cfg["timeout_seconds"])) as r:
        return str(json.loads(r.read().decode("utf-8")).get("response", ""))


def probe(endpoint: str | None = None) -> dict:
    cfg = config()
    ep = endpoint or cfg["endpoint"]
    _check_loopback(ep)
    have = available_models(ep)
    if have is None:
        return {"reachable": False, "endpoint": ep, "models": [], "chosen": {}}
    return {"reachable": True, "endpoint": ep, "models": have,
            "chosen": {k: pick(cfg, k, have) for k in cfg["models"]}}


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="local_llm.py", description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("instruction", nargs="?")
    ap.add_argument("--kind", choices=["text", "code", "fast"], default="text")
    ap.add_argument("--file", action="append", default=[])
    ap.add_argument("--endpoint")
    ap.add_argument("--probe", action="store_true")
    a = ap.parse_args(argv)
    try:
        if a.probe:
            p = probe(a.endpoint)
            print(json.dumps(p, ensure_ascii=False))
            return 0 if p["reachable"] else 3
        if not a.instruction:
            ap.print_usage(sys.stderr)
            return 2
        cfg = config()
        ep = a.endpoint or cfg["endpoint"]
        _check_loopback(ep)
        have = available_models(ep)
        model = pick(cfg, a.kind, have) if have is not None else None
        if not model:
            print("LOCAL_LLM_UNAVAILABLE: " + ("no server on " + ep if have is None
                  else f"none of {cfg['models'][a.kind]} installed"), file=sys.stderr)
            return 3
        parts = [a.instruction]
        for f in a.file:
            parts.append(f"\n\n--- FILE {Path(f).name} ---\n" + Path(f).read_text(encoding="utf-8", errors="replace"))
        prompt = "".join(parts)
        if len(prompt) > int(cfg["max_input_chars"]):
            print(f"INPUT_TOO_LARGE: {len(prompt)} chars > {cfg['max_input_chars']}; split the input", file=sys.stderr)
            return 4
        answer = generate(cfg, ep, model, prompt)
    except ValueError as exc:
        print(f"REFUSED: {exc}", file=sys.stderr)
        return 2
    except Exception as exc:
        print(f"LOCAL_LLM_UNAVAILABLE: {type(exc).__name__}: {exc}", file=sys.stderr)
        return 3
    print(f"[local:{model}] {answer.strip()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
