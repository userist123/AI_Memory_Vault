"""model_tier_router.py — A3: model_tier -> provider/model config resolution.

Reads config/model_tiers.json (or an injected path) and resolves an
abstract model_tier ("light" | "standard" | "heavy") to a concrete
ModelProvider instance + model name.

This is the ONLY place where model_tier becomes a concrete provider.
Council, Planner, PlanComplexityAnalyzer and CouncilBudgetController
never see a provider name or a model name -- only "light"/"standard"/
"heavy".

Invariants:
  - config is external (JSON file), never hardcoded in Council/Planner
  - no network calls in this module itself
  - no Planner/Council/MemoryController imports
  - unknown tier -> explicit error, never silent fallback
  - unregistered provider -> explicit error, never silent fallback
"""
from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Dict, Optional

from .model_provider import ModelProvider

REQUIRED_TIERS = ("light", "standard", "heavy")

DEFAULT_CONFIG_PATH = Path(__file__).resolve().parent.parent / "config" / "model_tiers.json"


@dataclass(frozen=True)
class TierConfig:
    provider: str
    model: str


class ModelTierConfigError(ValueError):
    pass


def load_model_tier_config(path: Optional[Path] = None) -> Dict[str, TierConfig]:
    """Load and validate config/model_tiers.json.

    Raises FileNotFoundError if the file is missing, and
    ModelTierConfigError if the schema is invalid or a required tier
    is missing.
    """
    config_path = Path(path) if path is not None else DEFAULT_CONFIG_PATH
    if not config_path.exists():
        raise FileNotFoundError(f"Model tier config not found: {config_path}")

    with config_path.open("r", encoding="utf-8") as fh:
        raw = json.load(fh)

    if not isinstance(raw, dict):
        raise ModelTierConfigError("model_tiers.json must be a JSON object")

    missing = [tier for tier in REQUIRED_TIERS if tier not in raw]
    if missing:
        raise ModelTierConfigError(f"model_tiers.json is missing required tiers: {missing}")

    resolved: Dict[str, TierConfig] = {}
    for tier, entry in raw.items():
        if not isinstance(entry, dict) or "provider" not in entry or "model" not in entry:
            raise ModelTierConfigError(
                f"Tier '{tier}' must be an object with 'provider' and 'model' string fields"
            )
        provider = entry["provider"]
        model = entry["model"]
        if not isinstance(provider, str) or not isinstance(model, str):
            raise ModelTierConfigError(f"Tier '{tier}' provider/model must be strings")
        resolved[tier] = TierConfig(provider=provider, model=model)

    return resolved


ProviderFactory = Callable[[str], ModelProvider]


class ModelTierRouter:
    """Resolves model_tier -> ModelProvider instance, using cached instances.

    provider_factories maps a provider name (e.g. "fake", "local",
    "openai") to a factory function that takes a model name and
    returns a ModelProvider instance for that model.
    """

    def __init__(
        self,
        tier_config: Dict[str, TierConfig],
        provider_factories: Dict[str, ProviderFactory],
    ) -> None:
        self._tier_config = tier_config
        self._provider_factories = provider_factories
        self._instance_cache: Dict[str, ModelProvider] = {}

    @classmethod
    def from_config_file(
        cls,
        provider_factories: Dict[str, ProviderFactory],
        config_path: Optional[Path] = None,
    ) -> "ModelTierRouter":
        tier_config = load_model_tier_config(config_path)
        return cls(tier_config, provider_factories)

    def resolve(self, model_tier: str) -> ModelProvider:
        if model_tier not in self._tier_config:
            raise ModelTierConfigError(
                f"Unknown model_tier '{model_tier}'. Configured tiers: "
                f"{sorted(self._tier_config)}"
            )

        if model_tier in self._instance_cache:
            return self._instance_cache[model_tier]

        entry = self._tier_config[model_tier]
        factory = self._provider_factories.get(entry.provider)
        if factory is None:
            raise ModelTierConfigError(
                f"No provider factory registered for provider '{entry.provider}' "
                f"(required by tier '{model_tier}'). Registered factories: "
                f"{sorted(self._provider_factories)}"
            )

        provider_instance = factory(entry.model)
        self._instance_cache[model_tier] = provider_instance
        return provider_instance

    def model_for_tier(self, model_tier: str) -> str:
        if model_tier not in self._tier_config:
            raise ModelTierConfigError(f"Unknown model_tier '{model_tier}'")
        return self._tier_config[model_tier].model
