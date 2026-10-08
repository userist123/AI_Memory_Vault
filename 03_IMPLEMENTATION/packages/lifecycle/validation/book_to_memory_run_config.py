"""Explicit run configuration for every Book-to-Memory runner (PR #209 finding B08).

A comparison between two runs is only meaningful when everything except the variable under test is
held fixed. This module makes that checkable: every runner records a :class:`RunConfig` (model id,
temperature, seed, max tokens, prompt-template hash, plus free-form controls) in each result file,
and :func:`compare_results` / :func:`aggregate_results` refuse to combine results whose configs differ
in anything other than the declared variable(s) under test.

A result without a recorded config is *not comparable*: it is refused, never assumed equal.

Retrieval runners have no language model. They record ``model_id = "none:deterministic-retrieval"``,
temperature 0.0, seed 0, max tokens 0 and the hash of the literal string ``"no-prompt-template"``;
their real controls (principal, page size, ranking arm, graph expansion flag, ...) go in ``controls``.
Pure standard library.
"""
from __future__ import annotations

import hashlib
import json
import re
from dataclasses import dataclass, field
from typing import Any, Dict, Iterable, List, Mapping, Optional, Sequence, Tuple

RUN_CONFIG_KEY = "run_config"
NO_MODEL_ID = "none:deterministic-retrieval"
NO_TEMPLATE_HASH = hashlib.sha256(b"no-prompt-template").hexdigest()
_HEX64 = re.compile(r"^[0-9a-f]{64}$")
#: The fields a variable under test may name; ``controls.<key>`` names a free-form control.
CONFIG_FIELDS = ("model_id", "temperature", "seed", "max_tokens", "prompt_template_hash")


class RunConfigError(ValueError):
    """A run config is missing or malformed."""


class ConfigMismatchError(RunConfigError):
    """Two results were compared or aggregated although their configs differ beyond the variable under test."""


def prompt_template_hash(template: str) -> str:
    """SHA-256 of a prompt template, after normalising line endings and trailing whitespace."""
    canon = "\n".join(line.rstrip() for line in template.replace("\r\n", "\n").replace("\r", "\n").split("\n")).strip()
    return hashlib.sha256(canon.encode("utf-8")).hexdigest()


@dataclass(frozen=True)
class RunConfig:
    """Everything that must be held fixed for two runs to be comparable."""

    model_id: str
    temperature: float
    seed: int
    max_tokens: int
    prompt_template_hash: str
    controls: Tuple[Tuple[str, Any], ...] = field(default_factory=tuple)

    def __post_init__(self) -> None:
        if not isinstance(self.model_id, str) or not self.model_id.strip():
            raise RunConfigError("model_id must be a non-empty string")
        if isinstance(self.temperature, bool) or not isinstance(self.temperature, (int, float)) \
                or not 0.0 <= float(self.temperature) <= 2.0:
            raise RunConfigError("temperature must be a number in [0, 2]")
        if isinstance(self.seed, bool) or not isinstance(self.seed, int):
            raise RunConfigError("seed must be an integer")
        if isinstance(self.max_tokens, bool) or not isinstance(self.max_tokens, int) or self.max_tokens < 0:
            raise RunConfigError("max_tokens must be a non-negative integer")
        if not isinstance(self.prompt_template_hash, str) or not _HEX64.match(self.prompt_template_hash):
            raise RunConfigError("prompt_template_hash must be a lowercase 64-character SHA-256 hex digest")
        norm = tuple(sorted((str(k), v) for k, v in dict(self.controls).items()))
        for k, v in norm:
            if not isinstance(v, (str, int, float, bool)) and v is not None:
                raise RunConfigError(f"control {k!r} must be a scalar (str/int/float/bool/None)")
        object.__setattr__(self, "controls", norm)
        object.__setattr__(self, "temperature", float(self.temperature))

    # -- construction -------------------------------------------------------------------------
    @classmethod
    def build(cls, *, model_id: str, temperature: float, seed: int, max_tokens: int,
              prompt_template: Optional[str] = None, prompt_template_hash_value: Optional[str] = None,
              controls: Optional[Mapping[str, Any]] = None) -> "RunConfig":
        """Build a config from a template text (hashed here) or from an already computed hash."""
        if (prompt_template is None) == (prompt_template_hash_value is None):
            raise RunConfigError("give exactly one of prompt_template or prompt_template_hash_value")
        digest = prompt_template_hash(prompt_template) if prompt_template is not None else prompt_template_hash_value
        return cls(model_id, temperature, seed, max_tokens, digest, tuple((controls or {}).items()))

    @classmethod
    def for_retrieval(cls, controls: Optional[Mapping[str, Any]] = None, seed: int = 0) -> "RunConfig":
        """Config of a runner that calls no language model (deterministic retrieval)."""
        return cls(NO_MODEL_ID, 0.0, seed, 0, NO_TEMPLATE_HASH, tuple((controls or {}).items()))

    @classmethod
    def from_dict(cls, data: Mapping[str, Any]) -> "RunConfig":
        if not isinstance(data, Mapping):
            raise RunConfigError("run_config must be an object")
        missing = [k for k in CONFIG_FIELDS if k not in data]
        if missing:
            raise RunConfigError(f"run_config is missing {missing}")
        return cls(data["model_id"], data["temperature"], data["seed"], data["max_tokens"],
                   data["prompt_template_hash"], tuple(dict(data.get("controls") or {}).items()))

    # -- views --------------------------------------------------------------------------------
    def to_dict(self) -> Dict[str, Any]:
        return {
            "model_id": self.model_id,
            "temperature": self.temperature,
            "seed": self.seed,
            "max_tokens": self.max_tokens,
            "prompt_template_hash": self.prompt_template_hash,
            "controls": dict(self.controls),
        }

    def flat(self) -> Dict[str, Any]:
        out: Dict[str, Any] = {k: getattr(self, k) for k in CONFIG_FIELDS}
        for k, v in self.controls:
            out[f"controls.{k}"] = v
        return out

    def digest(self) -> str:
        return hashlib.sha256(json.dumps(self.to_dict(), sort_keys=True).encode("utf-8")).hexdigest()


def stamp(result: Dict[str, Any], config: RunConfig) -> Dict[str, Any]:
    """Record ``config`` in a result dict (in place) and return it."""
    if not isinstance(config, RunConfig):
        raise RunConfigError("stamp() needs a RunConfig; a runner without an explicit config cannot record one")
    result[RUN_CONFIG_KEY] = config.to_dict()
    result["run_config_sha256"] = config.digest()
    return result


def config_of(result: Mapping[str, Any]) -> RunConfig:
    """The config recorded in a result, or :class:`RunConfigError` when there is none."""
    raw = result.get(RUN_CONFIG_KEY)
    if raw is None:
        raise RunConfigError("result carries no run_config: it is not comparable with anything")
    cfg = RunConfig.from_dict(raw)
    recorded = result.get("run_config_sha256")
    if recorded is not None and recorded != cfg.digest():
        raise RunConfigError("run_config does not match its recorded digest: the result was edited")
    return cfg


def differing_fields(a: RunConfig, b: RunConfig) -> List[str]:
    fa, fb = a.flat(), b.flat()
    return sorted(k for k in set(fa) | set(fb) if fa.get(k, "<absent>") != fb.get(k, "<absent>"))


def _validate_variable(var: Iterable[str]) -> Tuple[str, ...]:
    out = tuple(var)
    for name in out:
        if name not in CONFIG_FIELDS and not name.startswith("controls."):
            raise RunConfigError(f"unknown variable under test {name!r}; use one of {CONFIG_FIELDS} or 'controls.<key>'")
    return out


def assert_comparable(configs: Sequence[RunConfig], variable_under_test: Iterable[str] = ()) -> None:
    """Raise :class:`ConfigMismatchError` unless the configs differ only in the declared variable(s)."""
    allowed = set(_validate_variable(variable_under_test))
    if len(configs) < 2:
        raise RunConfigError("need at least two configs to compare")
    first = configs[0]
    for other in configs[1:]:
        extra = [f for f in differing_fields(first, other) if f not in allowed]
        if extra:
            raise ConfigMismatchError(
                f"configs differ in {extra}, which is not the variable under test {sorted(allowed)}; "
                "the results are not comparable"
            )


def compare_results(a: Mapping[str, Any], b: Mapping[str, Any], variable_under_test: Iterable[str]) -> Dict[str, Any]:
    """Pairing guard for an A/B comparison: the declared variable must differ and nothing else may.

    Returns ``{"variable_under_test": [...], "differs_in": [...]}``. Refuses (raises) when either result
    has no config, when anything besides the variable differs, or when the variable does not differ at
    all (nothing is being tested).
    """
    var = _validate_variable(variable_under_test)
    if not var:
        raise RunConfigError("a comparison needs a declared variable under test")
    ca, cb = config_of(a), config_of(b)
    assert_comparable([ca, cb], var)
    differs = differing_fields(ca, cb)
    if not differs:
        raise ConfigMismatchError(
            f"the two configs are identical although {sorted(var)} was declared as the variable under test"
        )
    return {"variable_under_test": sorted(var), "differs_in": differs}


def aggregate_results(results: Sequence[Mapping[str, Any]], variable_under_test: Iterable[str] = ()) -> RunConfig:
    """Guard for pooling results (e.g. replicates): they must share one config, up to the declared variable(s).

    Returns the config of the first result. With the default empty ``variable_under_test`` the configs
    must be identical.
    """
    if not results:
        raise RunConfigError("nothing to aggregate")
    configs = [config_of(r) for r in results]
    if len(configs) > 1:
        assert_comparable(configs, variable_under_test)
    return configs[0]
