"""evaluation/tests/test_vault_mesh.py — Structural Validation Suite for Cognitive Memory Mesh."""
from pathlib import Path
import pytest
from evaluation.vault_mesh.mesh_validator import (
    MeshValidator,
    ALLOWED_OBJECT_TYPES,
    ALLOWED_LIFECYCLES,
    ALLOWED_VERIFICATIONS,
    ALLOWED_RELATIONS,
)

VAULT_ROOT = Path(__file__).resolve().parent.parent.parent
INVENTORY_PATH = VAULT_ROOT / "07_EVALUATION" / "vault_mesh" / "vault_inventory.yaml"
GRAPH_PATH = VAULT_ROOT / "07_EVALUATION" / "vault_mesh" / "vault_graph.yaml"


@pytest.fixture
def validator():
    v = MeshValidator(inventory_path=INVENTORY_PATH, graph_path=GRAPH_PATH)
    v.load()
    return v


def test_mesh_files_exist():
    assert INVENTORY_PATH.exists(), "vault_inventory.yaml must exist"
    assert GRAPH_PATH.exists(), "vault_graph.yaml must exist"


def test_inventory_validation(validator):
    res = validator.validate_inventory()
    assert res["valid"], f"Inventory validation failed with errors: {res['errors'][:5]}"
    assert res["total_objects"] > 100, f"Expected >100 objects, found {res['total_objects']}"


def test_graph_validation(validator):
    validator.validate_inventory()
    res = validator.validate_graph()
    assert res["valid"], f"Graph validation failed with errors: {res['errors'][:5]}"
    assert res["total_edges"] > 500, f"Expected >500 edges, found {res['total_edges']}"


def test_no_dangling_edge_references(validator):
    res = validator.validate_all()
    assert res["valid"], f"Dangling references or validation errors found: {res['errors'][:10]}"


def test_canonical_taxonomy_coverage(validator):
    validator.load()
    types_present = {obj.get("type") for obj in validator.inventory}
    assert "KNOWLEDGE" in types_present
    assert "PROCEDURE" in types_present
    assert "EXPERIMENT" in types_present
    assert "EVIDENCE" in types_present
    assert "AGENT" in types_present
    assert "SKILL" in types_present
    assert "TRACE" in types_present
    assert "AUDIT" in types_present


def test_experiment_to_evidence_edges(validator):
    validator.load()
    exp_edges = [e for e in validator.graph_edges if e.get("source", "").startswith("EXP-")]
    assert len(exp_edges) >= 4
    targets = {e["target"] for e in exp_edges}
    assert "EVID-P0-RETRIEVAL-REPORT" in targets
    assert "EVID-P1-PACKING-REPORT" in targets
    assert "EVID-P2-TEMPORAL-REPORT" in targets
    assert "EVID-WOB-ART-AUDIT" in targets


def test_agent_to_skill_edges(validator):
    validator.load()
    ag_edges = [e for e in validator.graph_edges if e.get("source", "").startswith("AGENT-")]
    assert len(ag_edges) >= 6
    relations = {e["relation"] for e in ag_edges}
    assert "uses" in relations
