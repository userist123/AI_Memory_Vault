"""WP-11: the default provider configuration is local or offline and nothing reaches the network.

Read-only test: no network call is made. The config path is a parameter of the checker so the
failure case is shown on a temporary copy, never on the real file.
"""
import importlib
import json
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
REAL_CONFIG = REPO / "04_CONFIG" / "model_tiers.json"

OFFLINE_PROVIDERS = frozenset({"fake", "deterministic", "local"})

# model_tiers.json has no per-tier field that marks an external provider as explicitly enabled
# (the schema is {tier: {provider, model}}), so an external provider must be listed here.
# Adding an entry is a policy change: requires the owner's approval.
EXPLICITLY_APPROVED_EXTERNAL_PROVIDERS: frozenset = frozenset()


def external_tiers(config_path: Path) -> dict:
    data = json.loads(Path(config_path).read_text(encoding="utf-8"))
    return {
        tier: entry.get("provider")
        for tier, entry in data.items()
        if entry.get("provider") not in OFFLINE_PROVIDERS
        and entry.get("provider") not in EXPLICITLY_APPROVED_EXTERNAL_PROVIDERS
    }


def test_every_tier_of_the_real_config_is_local_or_offline():
    assert REAL_CONFIG.is_file()
    data = json.loads(REAL_CONFIG.read_text(encoding="utf-8"))
    assert {"light", "standard", "heavy"} <= set(data)
    assert external_tiers(REAL_CONFIG) == {}


@pytest.mark.parametrize("provider", ["openai", "anthropic"])
def test_an_external_default_provider_is_rejected(tmp_path, provider):
    data = json.loads(REAL_CONFIG.read_text(encoding="utf-8"))
    data["standard"] = {"provider": provider, "model": "x"}
    copy = tmp_path / "model_tiers.json"
    copy.write_text(json.dumps(data), encoding="utf-8")
    assert external_tiers(copy) == {"standard": provider}


def test_the_harness_defaults_to_offline_deterministic_mode():
    from interfaces.real_execution_harness import AgentModelExecutor

    assert AgentModelExecutor().provider_mode == "deterministic"


def test_openai_provider_makes_no_network_call_on_import_or_construction(monkeypatch):
    import urllib.request

    def boom(*args, **kwargs):
        raise AssertionError("network call attempted")

    monkeypatch.setattr(urllib.request, "urlopen", boom)
    monkeypatch.delenv("OPENAI_API_KEY", raising=False)
    import cognitive_core.openai_provider as module

    module = importlib.reload(module)
    provider = module.OpenAIProvider(model_name="m")
    assert provider.model_name == "m"
    with pytest.raises(module.OpenAIAuthenticationError):
        provider._resolve_api_key()
