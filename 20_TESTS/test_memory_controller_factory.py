"""WP-09: the controller factory lives in the memory layer; recall_cli re-exports it."""
from cognitive_core import activation, recall_cli
from cognitive_core import controller_factory


def test_factory_symbols_are_identical_in_recall_cli():
    for name in (
        "get_memory_controller",
        "validate_hmac_secret",
        "MissingHMACSecretError",
        "InvalidHMACSecretError",
    ):
        assert getattr(recall_cli, name) is getattr(controller_factory, name)


def test_factory_importable_via_legacy_facade():
    from memory_controller.controller_factory import get_memory_controller
    from memory_controller import controller_factory as legacy

    assert legacy is controller_factory
    assert get_memory_controller is controller_factory.get_memory_controller


def test_factory_returns_cached_controller(monkeypatch, tmp_path):
    monkeypatch.setenv("MEMORY_VAULT_ROOT", str(tmp_path))
    monkeypatch.setattr(controller_factory, "_CACHED_CONTROLLER", None)
    monkeypatch.setattr(controller_factory, "_CACHED_CONTROLLER_ROOT", None)
    first = recall_cli.get_memory_controller()
    assert controller_factory.get_memory_controller() is first


def test_activation_engine_has_no_memory_import():
    assert not hasattr(activation, "MemoryController")
    assert hasattr(activation, "ActivationController")
