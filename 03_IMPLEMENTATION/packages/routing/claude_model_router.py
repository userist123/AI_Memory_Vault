"""claude_model_router.py — cost-aware Claude model + effort routing for Claude Code.

Decides which Claude model (fable / opus / sonnet / haiku) and effort level a task
should run on, from a task description and a token estimate, using the policy in
`04_CONFIG/claude_model_routing.json`.

This layer is distinct from the two routers that already exist:
  - `providers/model_tier_router.py` resolves the council's abstract light/standard/heavy
    tiers to a provider (protected core; untouched here).
  - `routing/agent_router.py` chooses an external *runtime* (codex, antigravity, ...).
This one answers a narrower question Claude Code can act on directly through subagent
frontmatter (`model:` / `effort:`), the `/model` and `/effort` commands and the Agent tool's
`model` parameter: *which Claude model should this piece of work run on, and what does it cost?*

Invariants:
  - policy is external JSON, never hardcoded;
  - no network calls, no provider imports;
  - an unclassified goal is reported as such (`confidence == "low"`) and routed to the
    policy default, never silently guessed into a cheaper tier;
  - risk floors and the verifier-independence rule are applied after classification and
    can only raise the tier, never lower it.
"""
from __future__ import annotations

import json
import re
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any, Mapping, Optional

ROOT = Path(__file__).resolve().parents[3]
DEFAULT_POLICY_PATH = ROOT / "04_CONFIG" / "claude_model_routing.json"

RISK_LEVELS = ("low", "medium", "high", "critical")


class ModelRoutingPolicyError(ValueError):
    pass


@dataclass(frozen=True)
class ModelPrice:
    input: float
    output: float
    cache_read: float
    cache_write_5m: float
    cache_write_1h: float


@dataclass(frozen=True)
class ModelSpec:
    alias: str
    model_id: str
    display: str
    tier_rank: int
    price: ModelPrice
    default_effort: str
    effort_levels: tuple[str, ...]
    notes: str


@dataclass(frozen=True)
class TaskClass:
    name: str
    model: str
    effort: str
    subagent: Optional[str]
    keywords: tuple[str, ...]
    rationale: str


@dataclass(frozen=True)
class CostEstimate:
    model: str
    input_tokens: int
    output_tokens: int
    cache_read_tokens: int
    cache_write_tokens: int
    usd: float


@dataclass(frozen=True)
class ModelRouteDecision:
    goal: str
    task_class: str
    confidence: str
    model: str
    model_id: str
    effort: str
    subagent: Optional[str]
    rationale: str
    applied_rules: tuple[str, ...]
    verifier_model: Optional[str]
    escalate_to: Optional[str]
    estimated_cost_usd: float
    cost_on_fable_usd: float
    savings_vs_fable_usd: float
    matched_keywords: tuple[str, ...] = field(default_factory=tuple)

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


class ClaudeModelPolicy:
    def __init__(self, raw: Mapping[str, Any]) -> None:
        self.raw = raw
        self.models: dict[str, ModelSpec] = {}
        for alias, entry in raw.get("models", {}).items():
            try:
                p = entry["usd_per_mtok"]
                self.models[alias] = ModelSpec(
                    alias=alias,
                    model_id=entry["model_id"],
                    display=entry.get("display", alias),
                    tier_rank=int(entry["tier_rank"]),
                    price=ModelPrice(
                        input=float(p["input"]), output=float(p["output"]),
                        cache_read=float(p["cache_read"]),
                        cache_write_5m=float(p["cache_write_5m"]),
                        cache_write_1h=float(p["cache_write_1h"]),
                    ),
                    default_effort=entry.get("default_effort", "medium"),
                    effort_levels=tuple(entry.get("effort_levels", ())),
                    notes=entry.get("notes", ""),
                )
            except (KeyError, TypeError, ValueError) as exc:
                raise ModelRoutingPolicyError(f"model '{alias}' is malformed: {exc}") from exc
        if not self.models:
            raise ModelRoutingPolicyError("policy defines no models")

        self.task_classes: dict[str, TaskClass] = {}
        for name, entry in raw.get("task_classes", {}).items():
            model = entry.get("model")
            if model not in self.models:
                raise ModelRoutingPolicyError(f"task class '{name}' names unknown model '{model}'")
            effort = entry.get("effort")
            if effort not in self.models[model].effort_levels:
                raise ModelRoutingPolicyError(f"task class '{name}' effort '{effort}' not valid for {model}")
            self.task_classes[name] = TaskClass(
                name=name, model=model, effort=effort, subagent=entry.get("subagent"),
                keywords=tuple(k.lower() for k in entry.get("keywords", ())),
                rationale=entry.get("rationale", ""),
            )
        if not self.task_classes:
            raise ModelRoutingPolicyError("policy defines no task classes")

        self.default_model = raw.get("default_model")
        if self.default_model not in self.models:
            raise ModelRoutingPolicyError(f"default_model '{self.default_model}' is not a defined model")
        self.default_effort = raw.get("default_effort", self.models[self.default_model].default_effort)
        self.rules: Mapping[str, Any] = raw.get("rules", {})
        for risk, alias in self.rules.get("risk_floor", {}).items():
            if risk not in RISK_LEVELS or alias not in self.models:
                raise ModelRoutingPolicyError(f"risk_floor entry {risk}->{alias} is invalid")
        self.escalation_order: tuple[str, ...] = tuple(self.rules.get("escalation_order", ()))
        for alias in self.escalation_order:
            if alias not in self.models:
                raise ModelRoutingPolicyError(f"escalation_order names unknown model '{alias}'")

    @classmethod
    def from_file(cls, path: Optional[Path] = None) -> "ClaudeModelPolicy":
        p = Path(path) if path is not None else DEFAULT_POLICY_PATH
        if not p.exists():
            raise FileNotFoundError(f"model routing policy not found: {p}")
        with p.open("r", encoding="utf-8") as fh:
            return cls(json.load(fh))

    # -- helpers -----------------------------------------------------------
    def rank(self, alias: str) -> int:
        return self.models[alias].tier_rank

    def higher_tier(self, alias: str) -> Optional[str]:
        """Next model up in escalation_order, or None at the top."""
        if alias not in self.escalation_order:
            return None
        i = self.escalation_order.index(alias)
        return self.escalation_order[i + 1] if i + 1 < len(self.escalation_order) else None

    def max_tier(self, a: str, b: str) -> str:
        return a if self.rank(a) >= self.rank(b) else b


# -- cost ------------------------------------------------------------------

def estimate_cost(
    policy: ClaudeModelPolicy,
    model: str,
    input_tokens: int = 0,
    output_tokens: int = 0,
    cache_read_tokens: int = 0,
    cache_write_tokens: int = 0,
    cache_ttl: str = "5m",
) -> CostEstimate:
    if model not in policy.models:
        raise ModelRoutingPolicyError(f"unknown model '{model}'")
    for n, v in (("input", input_tokens), ("output", output_tokens),
                 ("cache_read", cache_read_tokens), ("cache_write", cache_write_tokens)):
        if v < 0:
            raise ValueError(f"{n}_tokens must be >= 0")
    p = policy.models[model].price
    write_rate = p.cache_write_1h if cache_ttl == "1h" else p.cache_write_5m
    usd = (
        input_tokens * p.input
        + output_tokens * p.output
        + cache_read_tokens * p.cache_read
        + cache_write_tokens * write_rate
    ) / 1_000_000
    return CostEstimate(model, input_tokens, output_tokens, cache_read_tokens, cache_write_tokens, round(usd, 6))


# -- classification ----------------------------------------------------------

_WORD = re.compile(r"[a-z][a-z\-]+")


def classify(policy: ClaudeModelPolicy, goal: str) -> tuple[Optional[TaskClass], tuple[str, ...]]:
    """Return the best-matching task class and the keywords that matched.

    Scoring: each matched keyword counts once; multi-word keywords count double (they are
    more specific). Ties are broken toward the *more capable* model, so an ambiguous goal
    never lands on a cheaper tier by accident. No match -> (None, ()).
    """
    text = " " + re.sub(r"\s+", " ", goal.lower()) + " "
    best: Optional[TaskClass] = None
    best_score = 0
    best_hits: tuple[str, ...] = ()
    for tc in policy.task_classes.values():
        hits = []
        for kw in tc.keywords:
            if " " in kw or "-" in kw:
                if kw in text:
                    hits.append(kw)
            elif re.search(rf"\b{re.escape(kw)}\b", text):
                hits.append(kw)
        if not hits:
            continue
        score = sum(2 if (" " in h or "-" in h) else 1 for h in hits)
        if score > best_score or (
            score == best_score and best is not None and policy.rank(tc.model) > policy.rank(best.model)
        ):
            best, best_score, best_hits = tc, score, tuple(hits)
    return best, best_hits


# -- routing ---------------------------------------------------------------

def route(
    policy: ClaudeModelPolicy,
    goal: str,
    *,
    input_tokens: int = 20_000,
    output_tokens: int = 4_000,
    risk: str = "medium",
    requested_model: Optional[str] = None,
    for_subagent: bool = False,
) -> ModelRouteDecision:
    if risk not in RISK_LEVELS:
        raise ValueError(f"risk must be one of {RISK_LEVELS}")
    if requested_model is not None and requested_model not in policy.models:
        raise ModelRoutingPolicyError(f"requested model '{requested_model}' is not in the policy")

    applied: list[str] = []
    tc, hits = classify(policy, goal)
    if tc is None:
        task_class = "unclassified"
        confidence = str(policy.rules.get("unclassified_confidence", "low"))
        model, effort, subagent = policy.default_model, policy.default_effort, None
        rationale = "No task-class keyword matched; routed to the policy default rather than guessed cheaper."
        applied.append("unclassified->default")
    else:
        task_class, confidence = tc.name, "high" if len(hits) > 1 else "medium"
        model, effort, subagent, rationale = tc.model, tc.effort, tc.subagent, tc.rationale

    # Risk floor can only raise the tier.
    floor = policy.rules.get("risk_floor", {}).get(risk)
    if floor and policy.rank(floor) > policy.rank(model):
        applied.append(f"risk_floor:{risk}->{floor}")
        model = floor
        effort = policy.models[model].default_effort if effort not in policy.models[model].effort_levels else effort

    # Fable is a main-session choice, never a subagent model.
    if for_subagent and policy.rules.get("fable_never_for_subagents") and model == "fable":
        applied.append("fable_never_for_subagents->opus")
        model = "opus"

    # Explicit request wins over classification but still cannot go below the risk floor.
    if requested_model is not None:
        if floor and policy.rank(requested_model) < policy.rank(floor):
            applied.append(f"requested:{requested_model} rejected below risk floor {floor}")
        else:
            applied.append(f"requested:{requested_model}")
            model = requested_model
            if effort not in policy.models[model].effort_levels:
                effort = policy.models[model].default_effort

    # Independent verifier for review/security work: a different model, not lower tier.
    verifier: Optional[str] = None
    is_security = any(k in hits for k in ("security", "threat", "vulnerability", "audit"))
    if is_security and policy.rules.get("security_requires_independent_verifier"):
        verifier = policy.higher_tier(model) or model
        if for_subagent and verifier == "fable" and policy.rules.get("fable_never_for_subagents"):
            verifier = "opus"  # same tier, fresh context: independence comes from the separate run
        applied.append(f"security_requires_independent_verifier->{verifier}")

    escalate_to = policy.higher_tier(model)
    if for_subagent and escalate_to == "fable":
        escalate_to = None  # the main session, not a subagent, would take that step

    est = estimate_cost(policy, model, input_tokens, output_tokens)
    on_fable = estimate_cost(policy, "fable", input_tokens, output_tokens)
    return ModelRouteDecision(
        goal=goal,
        task_class=task_class,
        confidence=confidence,
        model=model,
        model_id=policy.models[model].model_id,
        effort=effort,
        subagent=subagent if for_subagent or subagent else None,
        rationale=rationale,
        applied_rules=tuple(applied),
        verifier_model=verifier,
        escalate_to=escalate_to,
        estimated_cost_usd=est.usd,
        cost_on_fable_usd=on_fable.usd,
        savings_vs_fable_usd=round(on_fable.usd - est.usd, 6),
        matched_keywords=hits,
    )
