from pathlib import Path

from importlib.util import module_from_spec, spec_from_file_location


SCRIPT = Path(__file__).resolve().parents[2] / "30_SCRIPTS" / "verification" / "lifecycle_policy_consistency.py"
CONTROLLER = Path(__file__).resolve().parents[2] / "memory_controller" / "controller.py"


def _load_module():
    spec = spec_from_file_location("lifecycle_policy_consistency", SCRIPT)
    assert spec and spec.loader
    module = module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_controller_contains_no_unaccounted_lifecycle_pairs():
    module = _load_module()
    _, missing = module.audit(CONTROLLER)
    assert not (missing - module.COMPATIBILITY_TRANSITIONS)


def test_current_legacy_pairs_are_explicitly_classified():
    module = _load_module()
    _, missing = module.audit(CONTROLLER)
    assert missing <= module.COMPATIBILITY_TRANSITIONS
    assert missing
