"""write_path_audit.classify() must recognise the real canonical boundaries.

It keyed on `memory_controller/controller.py` and `cognitive_core/consolidation.py`, two
paths that are only import shims, so CANONICAL_CONTROLLER never fired on the real source.
"""
from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
_spec = importlib.util.spec_from_file_location("write_path_audit", REPO / "30_SCRIPTS" / "verification" / "write_path_audit.py")
wpa = importlib.util.module_from_spec(_spec)
sys.modules[_spec.name] = wpa  # @dataclass resolves its module
_spec.loader.exec_module(wpa)

CONTROLLER = REPO / "03_IMPLEMENTATION" / "packages" / "memory" / "controller.py"
CONSOLIDATION = REPO / "03_IMPLEMENTATION" / "packages" / "learning" / "consolidation.py"


def test_the_real_canonical_files_exist():
    assert CONTROLLER.is_file() and CONSOLIDATION.is_file()


def test_classify_recognises_the_real_controller_and_consolidation_paths():
    assert wpa.classify(CONTROLLER, "self.storage.set") == "CANONICAL_CONTROLLER"
    assert wpa.classify(CONSOLIDATION, "self.storage.set") == "CANONICAL_RECONSOLIDATION_BOUNDARY"


def test_other_files_are_not_mistaken_for_the_controller():
    other = REPO / "03_IMPLEMENTATION" / "packages" / "memory" / "data_router.py"
    assert wpa.classify(other, "self.storage.set") == "DIRECT_STORAGE_MUTATION"
    assert wpa.classify(Path("x/not_memory/controller.py"), "self.storage.set") == "DIRECT_STORAGE_MUTATION"


def test_the_inventory_reports_canonical_controller_findings_on_the_repository():
    findings = wpa.audit(REPO / "03_IMPLEMENTATION" / "packages" / "memory")
    assert any(f.classification == "CANONICAL_CONTROLLER" and f.path == "controller.py" for f in findings)
