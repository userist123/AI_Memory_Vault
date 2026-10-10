"""claude_usage_report.py — measure real Claude Code token consumption per model.

Claude Code writes one JSONL transcript per session under
`~/.claude/projects/<project-slug>/<session>.jsonl`. Every assistant message carries
`message.model` and `message.usage` (input, output, cache_read_input_tokens,
cache_creation_input_tokens, output_tokens_details.thinking_tokens). A request that
produced several content blocks is written as several lines sharing one `requestId`
with identical usage, so lines are de-duplicated by `requestId` before aggregation.

The report prices every model's tokens with the policy's rate card and shows what the
same tokens would have cost on each other model. That counterfactual assumes equal token
counts; a cheaper model that needs more turns is not cheaper, so read it as an upper
bound on savings, not a promise.

Stdlib only; reads local files only; never sends anything anywhere.
"""
from __future__ import annotations

import json
import os
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterable, Iterator, Optional

from .claude_model_router import ClaudeModelPolicy, estimate_cost


@dataclass
class ModelUsage:
    model_id: str
    requests: int = 0
    input_tokens: int = 0
    output_tokens: int = 0
    thinking_tokens: int = 0
    cache_read_tokens: int = 0
    cache_write_5m_tokens: int = 0
    cache_write_1h_tokens: int = 0
    sessions: set = field(default_factory=set)

    @property
    def cache_write_tokens(self) -> int:
        return self.cache_write_5m_tokens + self.cache_write_1h_tokens

    @property
    def cache_hit_ratio(self) -> float:
        denom = self.input_tokens + self.cache_read_tokens + self.cache_write_tokens
        return round(self.cache_read_tokens / denom, 4) if denom else 0.0


def default_projects_dir() -> Path:
    return Path(os.environ.get("CLAUDE_CONFIG_DIR", Path.home() / ".claude")) / "projects"


def project_slug(project_dir: Path) -> str:
    """Claude Code names the transcript folder after the absolute path with every
    non-alphanumeric character replaced by `-` (`/home/u/AI_Vault` -> `-home-u-AI-Vault`)."""
    import re
    return re.sub(r"[^A-Za-z0-9]", "-", str(Path(project_dir).resolve()))


def iter_transcripts(projects_dir: Path, project_dir: Optional[Path] = None) -> Iterator[Path]:
    if not projects_dir.exists():
        return iter(())
    if project_dir is not None:
        folder = projects_dir / project_slug(project_dir)
        return iter(sorted(folder.glob("*.jsonl")) if folder.exists() else [])
    return iter(sorted(projects_dir.glob("*/*.jsonl")))


def iter_usage_records(paths: Iterable[Path]) -> Iterator[dict[str, Any]]:
    """Yield one {session, request_id, model, usage} per distinct requestId."""
    for path in paths:
        seen: set[str] = set()
        with path.open("r", encoding="utf-8", errors="replace") as fh:
            for line in fh:
                line = line.strip()
                if not line:
                    continue
                try:
                    rec = json.loads(line)
                except json.JSONDecodeError:
                    continue
                if rec.get("type") != "assistant":
                    continue
                msg = rec.get("message") or {}
                usage = msg.get("usage")
                model = msg.get("model")
                if not isinstance(usage, dict) or not model:
                    continue
                rid = rec.get("requestId") or msg.get("id") or f"{path.name}:{fh.tell()}"
                if rid in seen:
                    continue
                seen.add(rid)
                yield {"session": path.stem, "request_id": rid, "model": model, "usage": usage}


def aggregate(records: Iterable[dict[str, Any]]) -> dict[str, ModelUsage]:
    out: dict[str, ModelUsage] = {}
    for r in records:
        u = r["usage"]
        m = out.setdefault(r["model"], ModelUsage(model_id=r["model"]))
        m.requests += 1
        m.sessions.add(r["session"])
        m.input_tokens += int(u.get("input_tokens") or 0)
        m.output_tokens += int(u.get("output_tokens") or 0)
        m.thinking_tokens += int((u.get("output_tokens_details") or {}).get("thinking_tokens") or 0)
        m.cache_read_tokens += int(u.get("cache_read_input_tokens") or 0)
        cc = u.get("cache_creation") or {}
        w5 = int(cc.get("ephemeral_5m_input_tokens") or 0)
        w1 = int(cc.get("ephemeral_1h_input_tokens") or 0)
        if not cc:
            w5 = int(u.get("cache_creation_input_tokens") or 0)
        m.cache_write_5m_tokens += w5
        m.cache_write_1h_tokens += w1
    return out


def _alias_for(policy: ClaudeModelPolicy, model_id: str) -> Optional[str]:
    for alias, spec in policy.models.items():
        if spec.model_id == model_id:
            return alias
    return None


def priced_report(policy: ClaudeModelPolicy, usage: dict[str, ModelUsage]) -> dict[str, Any]:
    rows = []
    total = 0.0
    for model_id, m in sorted(usage.items(), key=lambda kv: -kv[1].output_tokens):
        alias = _alias_for(policy, model_id)
        row: dict[str, Any] = {
            "model_id": model_id, "alias": alias, "requests": m.requests, "sessions": len(m.sessions),
            "input_tokens": m.input_tokens, "cache_read_tokens": m.cache_read_tokens,
            "cache_write_5m_tokens": m.cache_write_5m_tokens, "cache_write_1h_tokens": m.cache_write_1h_tokens,
            "output_tokens": m.output_tokens, "thinking_tokens": m.thinking_tokens,
            "cache_hit_ratio": m.cache_hit_ratio, "usd": None, "same_tokens_on": {},
        }
        if alias is not None:
            cost = (
                estimate_cost(policy, alias, m.input_tokens, m.output_tokens, m.cache_read_tokens, m.cache_write_5m_tokens, "5m").usd
                + estimate_cost(policy, alias, 0, 0, 0, m.cache_write_1h_tokens, "1h").usd
            )
            row["usd"] = round(cost, 4)
            total += cost
            for other in policy.models:
                alt = (
                    estimate_cost(policy, other, m.input_tokens, m.output_tokens, m.cache_read_tokens, m.cache_write_5m_tokens, "5m").usd
                    + estimate_cost(policy, other, 0, 0, 0, m.cache_write_1h_tokens, "1h").usd
                )
                row["same_tokens_on"][other] = round(alt, 4)
        rows.append(row)
    return {"pricing_as_of": policy.raw.get("pricing_as_of"), "total_usd": round(total, 4), "models": rows}


def format_table(report: dict[str, Any]) -> str:
    lines = [f"Claude Code usage (prices as of {report['pricing_as_of']}; counterfactuals assume equal token counts)"]
    hdr = f"{'model':<20}{'req':>6}{'input':>10}{'cache_rd':>11}{'cache_wr':>11}{'output':>9}{'think':>8}{'hit%':>6}{'usd':>9}"
    lines.append(hdr)
    for r in report["models"]:
        wr = r["cache_write_5m_tokens"] + r["cache_write_1h_tokens"]
        usd = f"{r['usd']:.2f}" if r["usd"] is not None else "n/a"
        lines.append(
            f"{r['model_id']:<20}{r['requests']:>6}{r['input_tokens']:>10}{r['cache_read_tokens']:>11}{wr:>11}"
            f"{r['output_tokens']:>9}{r['thinking_tokens']:>8}{r['cache_hit_ratio']*100:>5.0f}%{usd:>9}"
        )
        if r["same_tokens_on"]:
            alts = ", ".join(f"{k} ${v:.2f}" for k, v in r["same_tokens_on"].items())
            lines.append(f"{'':<20}same tokens on: {alts}")
    lines.append(f"TOTAL ${report['total_usd']:.2f}")
    return "\n".join(lines)
