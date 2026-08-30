from __future__ import annotations

from pathlib import Path
from typing import Any, Dict, Iterable, Mapping, Optional

from .council_model_execution import CouncilRunWithExecution, run_council_with_model_execution
from .fake_model_provider import FakeModelProvider
from .model_tier_router import ModelTierConfigError, ModelTierRouter, load_model_tier_config

DEFAULT_ALLOWED_PROVIDERS = ("fake", "local")


class BridgeConfigError(ValueError):
    pass


def derive_agent_model_tiers(subagent_specs):
    tiers = {}
    for agent_id, spec in subagent_specs.items():
        tier = getattr(spec, "model_tier", None)
        if not tier:
            raise BridgeConfigError(
                "SubagentSpec for agent has no model_tier set: " + str(agent_id)
            )
        tiers[agent_id] = tier
    return tiers


def _default_fake_factories():
    return {"fake": lambda model_name: FakeModelProvider(provider_name="fake", model_name=model_name)}


def build_model_tier_router(config_path=None, allowed_providers=DEFAULT_ALLOWED_PROVIDERS, provider_factories=None):
    tier_config = load_model_tier_config(config_path)
    allowed = set(allowed_providers)

    disallowed = {tier: entry.provider for tier, entry in tier_config.items() if entry.provider not in allowed}
    if disallowed:
        message = (
            "Refusing to build ModelTierRouter: tiers configured outside allowed providers "
            + str(sorted(allowed)) + ": " + str(disallowed)
            + ". This bridge blocks unapproved providers such as openai until explicitly widened."
        )
        raise BridgeConfigError(message)

    factories = dict(provider_factories) if provider_factories else _default_fake_factories()
    missing_factories = {entry.provider for entry in tier_config.values() if entry.provider not in factories}
    if missing_factories:
        raise ModelTierConfigError(
            "No provider factory registered for: " + str(sorted(missing_factories))
            + ". Registered factories: " + str(sorted(factories))
        )

    return ModelTierRouter(tier_config, factories)


def execute_council_models(
    council_run,
    subagent_specs,
    task,
    synthesis_role="SYNTHESIZER",
    model_execution_enabled=False,
    config_path=None,
    allowed_providers=DEFAULT_ALLOWED_PROVIDERS,
    provider_factories=None,
):
    router = build_model_tier_router(
        config_path=config_path,
        allowed_providers=allowed_providers,
        provider_factories=provider_factories,
    )

    if not model_execution_enabled:
        return run_council_with_model_execution(
            council_run=council_run,
            model_tier_router=router,
            task=task,
            agent_model_tiers={},
            synthesis_model_tier="light",
            model_execution_enabled=False,
        )

    agent_model_tiers = derive_agent_model_tiers(subagent_specs)

    synthesis_spec = subagent_specs.get(synthesis_role)
    if synthesis_spec is None:
        raise BridgeConfigError(
            "No SubagentSpec found for synthesis_role " + str(synthesis_role)
            + ". Available roles: " + str(sorted(subagent_specs))
        )
    synthesis_model_tier = getattr(synthesis_spec, "model_tier", None)
    if not synthesis_model_tier:
        raise BridgeConfigError(
            "SubagentSpec for synthesis_role " + str(synthesis_role) + " has no model_tier set."
        )

    return run_council_with_model_execution(
        council_run=council_run,
        model_tier_router=router,
        task=task,
        agent_model_tiers=agent_model_tiers,
        synthesis_model_tier=synthesis_model_tier,
        model_execution_enabled=True,
    )
