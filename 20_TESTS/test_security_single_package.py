"""WP-14a: `security` is one regular package (03_IMPLEMENTATION/packages/security).

Before the merge the repository-root `security/` and the implementation package shared the
name; from a wheel (packages/ only) `security.verified_reduction` did not exist, so
`memory_controller.controller` failed to import. These tests pin the single-location layout.
"""
from __future__ import annotations

import importlib
import importlib.util
import subprocess
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
PACKAGES = REPO / "03_IMPLEMENTATION" / "packages"


def test_no_security_directory_in_the_repository_root():
    assert not (REPO / "security").exists()


def test_security_resolves_to_exactly_one_location():
    spec = importlib.util.find_spec("security")
    assert spec is not None and spec.submodule_search_locations is not None
    locations = list(spec.submodule_search_locations)
    assert len(locations) == 1
    assert Path(locations[0]).resolve() == (PACKAGES / "security").resolve()


def test_init_is_a_plain_package_without_path_extension():
    text = (PACKAGES / "security" / "__init__.py").read_text(encoding="utf-8")
    assert "__path__" not in text


@pytest.mark.parametrize("module", ["verified_reduction", "trust_gate", "authorizer", "owner_principal", "tool_response_boundary"])
def test_former_root_and_package_modules_import_from_the_package(module):
    mod = importlib.import_module(f"security.{module}")
    assert Path(mod.__file__).resolve().parent == (PACKAGES / "security").resolve()


def test_probe_with_only_packages_on_the_path_imports_pack_builder_and_controller_chain():
    """The wheel layout: no repository root on sys.path, only the packages directory."""
    code = (
        "import sys; sys.path[:] = [p for p in sys.path if p and p != '']; sys.path.insert(0, sys.argv[1]);"
        "import importlib.util as u;"
        "loc = list(u.find_spec('security').submodule_search_locations); assert len(loc) == 1, loc;"
        "import security.verified_reduction, security.trust_gate, security.authorizer;"
        "from retrieval.context import pack_builder as pb;"
        "assert pb.record_observed_memory_trace.__module__ == 'retrieval.context.pack_builder';"
        "import memory_controller.controller"
    )
    import tempfile
    with tempfile.TemporaryDirectory() as cwd:
        r = subprocess.run([sys.executable, "-c", code, str(PACKAGES)], cwd=cwd, capture_output=True, text=True, timeout=120)
    assert r.returncode == 0, r.stderr[-2000:]


def test_owner_principal_rule_is_shared_and_still_rejects_non_principals():
    from security import owner_principal
    from security.authorizer import Principal
    import cognitive_core.proposal_queue as pq

    assert pq.require_owner_principal is owner_principal.require_owner_principal
    assert pq.owner_principal_values is owner_principal.owner_principal_values
    assert owner_principal.require_owner_principal(Principal.HUMAN) == "human"
    assert owner_principal.require_owner_principal(Principal.ADMIN) == "admin"
    for bad in ("human", None, Principal.AI_AGENT):
        with pytest.raises(PermissionError):
            owner_principal.require_owner_principal(bad)

    class Principal_:  # a look-alike that is not named `Principal`
        value = "human"

    with pytest.raises(PermissionError):
        owner_principal.require_owner_principal(Principal_())


def test_trust_gate_has_no_upward_import_of_the_proposal_queue():
    text = (PACKAGES / "security" / "trust_gate.py").read_text(encoding="utf-8")
    assert "proposal_queue" not in text.replace("the proposal queue", "")
    assert "cognitive_core" not in text
