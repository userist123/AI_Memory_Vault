"""WP-09: the controller factory lives in the memory layer; recall_cli re-exports it."""
from pathlib import Path

from cognitive_core import activation, recall_cli
from cognitive_core import controller_factory

REPO = Path(__file__).resolve().parents[1]


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
    # Stronger than the attribute check (WP-13 review L3): no import of the memory controller
    # module under any name, so an aliased re-import cannot slip through.
    import ast
    tree = ast.parse(Path(activation.__file__).read_text(encoding="utf-8"))
    imported = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            imported |= {a.name for a in node.names}
        elif isinstance(node, ast.ImportFrom) and node.module:
            imported.add(node.module)
    assert not {m for m in imported if m.split(".")[-1] == "controller"}, imported


def test_controller_factory_is_used_through_a_facade_only():
    """The physical name loads a second module with its own cache (WP-13 review L1)."""
    import re
    pattern = re.compile(r"^\s*(from|import)\s+(packages\.)?memory\.controller_factory\b", re.M)
    offenders = [str(p.relative_to(REPO)) for p in (REPO / "03_IMPLEMENTATION").rglob("*.py")
                 if pattern.search(p.read_text(encoding="utf-8", errors="replace"))]
    assert offenders == [], offenders
