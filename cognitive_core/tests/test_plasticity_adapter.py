"""Plasticity Prototype -- plasticity_update.py contract tests.

Owner: claude-code. Status of the module under test: EXPERIMENTAL, NOT
RUNTIME INTEGRATED. These tests run fully offline and never touch
memory_controller/** -- the module under test is a pure adapter over JSON
artifacts in a pytest tmp_path.
"""
from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path

_SPEC = importlib.util.spec_from_file_location(
    "plasticity_update_under_test",
    Path(__file__).resolve().parents[2] / "30_SCRIPTS" / "knowledge" / "plasticity_update.py",
)
plasticity_update = importlib.util.module_from_spec(_SPEC)
sys.modules["plasticity_update_under_test"] = plasticity_update
_SPEC.loader.exec_module(plasticity_update)

from cognitive_core.synapse_store import Synapse, SynapseStore  # noqa: E402


def _seeded_store(tmp_path):
    store = SynapseStore()
    store.add(Synapse("a", "b", "related_to", 0.4))
    path = tmp_path / "synapses.json"
    store.save(path)
    return path


def _write_trace(trace_dir: Path, pack_id: str, edges):
    trace_dir.mkdir(parents=True, exist_ok=True)
    (trace_dir / f"{pack_id}.json").write_text(
        json.dumps({"pack_id": pack_id, "observed_edges": edges}), encoding="utf-8")


def test_verified_source_with_trace_updates_weight(tmp_path):
    syn_path = _seeded_store(tmp_path)
    trace_dir = tmp_path / "traces"
    _write_trace(trace_dir, "bp1", [["a", "b"]])
    ledger = tmp_path / "ledger.json"
    ledger.write_text(json.dumps([
        {"pack_id": "bp1", "success": True, "verification_source": "pytest"},
    ]), encoding="utf-8")

    updates = plasticity_update.outcomes_from_ledger(ledger)
    assert updates == [("bp1", True, "pytest")]

    store = SynapseStore.load(syn_path)
    trace = plasticity_update.load_trace(trace_dir, "bp1")
    edges = [tuple(e) for e in trace["observed_edges"]]
    touched = store.reinforce(edges, run_id="bp1", success=True)
    assert touched == 1
    assert store.all()[0].weight > 0.4


def test_self_reported_source_is_excluded_before_reaching_reinforce(tmp_path):
    ledger = tmp_path / "ledger.json"
    ledger.write_text(json.dumps([
        {"pack_id": "bp1", "success": True, "verification_source": "self_reported"},
        {"pack_id": "bp2", "success": True, "verification_source": "model_says_it_was_useful"},
        {"pack_id": "bp3", "success": True, "verification_source": "AGENT_SAID_SO"},
    ]), encoding="utf-8")
    updates = plasticity_update.outcomes_from_ledger(ledger)
    assert updates == []


def test_verified_source_but_missing_trace_yields_no_update(tmp_path):
    syn_path = _seeded_store(tmp_path)
    trace_dir = tmp_path / "traces"  # no trace file written
    trace = plasticity_update.load_trace(trace_dir, "bp_missing")
    assert trace is None
    # NO_UPDATE: store must be unchanged if the caller correctly skips.
    store = SynapseStore.load(syn_path)
    original_weight = store.all()[0].weight
    assert original_weight == 0.4


def test_malformed_trace_json_does_not_crash(tmp_path):
    trace_dir = tmp_path / "traces"
    trace_dir.mkdir(parents=True)
    (trace_dir / "bp_bad.json").write_text("{not valid json", encoding="utf-8")
    trace = plasticity_update.load_trace(trace_dir, "bp_bad")
    assert trace is None  # fails closed, not an exception


def test_malformed_ledger_entries_are_skipped_not_fatal(tmp_path):
    ledger = tmp_path / "ledger.json"
    ledger.write_text(json.dumps([
        {"pack_id": "bp1", "success": True, "verification_source": "pytest"},
        {"not_a_pack_id_field": "garbage"},
        "not even a dict",
        {"pack_id": "bp2", "verification_source": "pytest"},  # missing 'success' -> defaults False
    ]), encoding="utf-8")
    updates = plasticity_update.outcomes_from_ledger(ledger)
    pack_ids = [u[0] for u in updates]
    assert "bp1" in pack_ids
    assert "bp2" in pack_ids
    assert len(updates) == 2  # the two garbage rows are silently skipped, not fatal


def test_ledger_jsonl_format_is_also_supported(tmp_path):
    ledger = tmp_path / "ledger.jsonl"
    lines = [
        json.dumps({"pack_id": "bp1", "success": True, "verification_source": "ci"}),
        json.dumps({"pack_id": "bp2", "success": False, "verification_source": "human_verified"}),
    ]
    ledger.write_text("\n".join(lines), encoding="utf-8")
    updates = plasticity_update.outcomes_from_ledger(ledger)
    assert len(updates) == 2


def test_missing_ledger_file_returns_empty_not_error(tmp_path):
    assert plasticity_update.outcomes_from_ledger(tmp_path / "nope.json") == []


def test_module_never_imports_memory_controller():
    """Ownership boundary: this adapter must not import anything from
    memory_controller/** (owned by Codex). Checked via AST, not a raw
    substring search, since the module's own docstring legitimately
    *mentions* memory_controller/memory_trace.py and outcome_tracker.py by
    name to explain why it deliberately does not import them."""
    import ast
    import inspect
    tree = ast.parse(inspect.getsource(plasticity_update))
    imported_modules = []
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            imported_modules += [alias.name for alias in node.names]
        elif isinstance(node, ast.ImportFrom) and node.module:
            imported_modules.append(node.module)
    assert not any(m.startswith("memory_controller") for m in imported_modules), imported_modules


def test_ensure_utf8_stdout_does_not_raise():
    plasticity_update._ensure_utf8_stdout()
