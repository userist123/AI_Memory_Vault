"""test_model_tier_router.py — A3 contract tests.

Verifies config loading, validation, and tier -> provider resolution
using only FakeModelProvider (no real provider, no network).
"""
import json

import pytest

from cognitive_core.model_tier_router import (
    ModelTierConfigError,
    ModelTierRouter,
    TierConfig,
    load_model_tier_config,
)
from cognitive_core.fake_model_provider import FakeModelProvider
from cognitive_core.model_provider import ModelRequest


FAKE_FACTORIES = {
    "fake": lambda model_name: FakeModelProvider(provider_name="fake", model_name=model_name),
}


def test_default_config_file_loads_and_has_required_tiers():
    config = load_model_tier_config()
    assert set(config.keys()) >= {"light", "standard", "heavy"}
    for tier in ("light", "standard", "heavy"):
        assert isinstance(config[tier], TierConfig)
        assert config[tier].provider
        assert config[tier].model


def test_missing_config_file_raises(tmp_path):
    missing_path = tmp_path / "does_not_exist.json"
    with pytest.raises(FileNotFoundError):
        load_model_tier_config(missing_path)


def test_config_missing_required_tier_raises(tmp_path):
    bad_config = tmp_path / "bad.json"
    bad_config.write_text(json.dumps({"light": {"provider": "fake", "model": "x"}}))
    with pytest.raises(ModelTierConfigError):
        load_model_tier_config(bad_config)


def test_config_invalid_entry_raises(tmp_path):
    bad_config = tmp_path / "bad2.json"
    bad_config.write_text(json.dumps({
        "light": {"provider": "fake", "model": "x"},
        "standard": {"provider": "fake", "model": "x"},
        "heavy": "not-an-object",
    }))
    with pytest.raises(ModelTierConfigError):
        load_model_tier_config(bad_config)


def test_router_resolves_tier_to_correct_provider_and_model():
    router = ModelTierRouter.from_config_file(FAKE_FACTORIES)
    provider = router.resolve("light")
    assert isinstance(provider, FakeModelProvider)
    assert provider.model_name == router.model_for_tier("light")


def test_router_caches_provider_instance_per_tier():
    router = ModelTierRouter.from_config_file(FAKE_FACTORIES)
    first = router.resolve("standard")
    second = router.resolve("standard")
    assert first is second


def test_router_unknown_tier_raises():
    router = ModelTierRouter.from_config_file(FAKE_FACTORIES)
    with pytest.raises(ModelTierConfigError):
        router.resolve("nonexistent_tier")


def test_router_unregistered_provider_raises(tmp_path):
    config_path = tmp_path / "model_tiers.json"
    config_path.write_text(json.dumps({
        "light": {"provider": "unregistered_provider", "model": "x"},
        "standard": {"provider": "fake", "model": "x"},
        "heavy": {"provider": "fake", "model": "x"},
    }))
    router = ModelTierRouter.from_config_file(FAKE_FACTORIES, config_path)
    with pytest.raises(ModelTierConfigError):
        router.resolve("light")


def test_resolved_provider_can_generate_end_to_end():
    router = ModelTierRouter.from_config_file(FAKE_FACTORIES)
    provider = router.resolve("heavy")
    request = ModelRequest(prompt="end to end test", model_tier="heavy")
    response = provider.generate(request)
    assert response.model_tier == "heavy"
    assert response.provider == "fake"
    assert response.usage.effective_total > 0
