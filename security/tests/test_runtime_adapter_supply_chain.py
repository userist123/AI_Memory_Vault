from security.runtime_adapter import RuntimeAdapter
from security.supply_chain_policy import ComponentProvenance, ComponentType, SoftwareAISupplyChainPolicy
from security.tool_integrity import ToolDefinition


def provenance(country):
    return ComponentProvenance("tool-1", ComponentType.TOOL, "https://example.invalid/tool", "Example", country, country, "trusted-key", "a" * 64)


def test_runtime_registration_blocks_blacklisted_origin():
    adapter = RuntimeAdapter(
        supply_chain_policy=SoftwareAISupplyChainPolicy()
    )
    definition = ToolDefinition("server", "tool", "test", {})
    try:
        adapter.register(definition, lambda request: "ok", provenance=provenance("Russia"))
    except PermissionError as exc:
        assert "blocked" in str(exc)
    else:
        raise AssertionError("blacklisted tool registration was accepted")


def test_runtime_registration_requires_provenance_when_policy_enabled():
    adapter = RuntimeAdapter(
        supply_chain_policy=SoftwareAISupplyChainPolicy()
    )
    definition = ToolDefinition("server", "tool", "test", {})
    try:
        adapter.register(definition, lambda request: "ok")
    except PermissionError as exc:
        assert "provenance" in str(exc)
    else:
        raise AssertionError("tool without provenance was accepted")
