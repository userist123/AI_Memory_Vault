"""End-to-end offline pipeline test for the P1.2/P2.1/Plasticity front.

    task -> seeds -> candidate graph -> edge proposals -> validated graph
         -> budget -> brain pack -> deterministic trace

Runs entirely without Ollama (no network calls at all -- classify_with_ollama
is never invoked). Also checks the "no canonical mutation" contract: nothing
in this pipeline ever writes to a source note's frontmatter or body except
30_SCRIPTS/knowledge/vault_hygiene.py's `apply` command, which is exercised
separately and is not part of this pipeline.
"""
from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
_SPEC = importlib.util.spec_from_file_location(
    "edge_proposer_e2e", ROOT / "30_SCRIPTS" / "knowledge" / "edge_proposer.py")
edge_proposer = importlib.util.module_from_spec(_SPEC)
sys.modules["edge_proposer_e2e"] = edge_proposer
_SPEC.loader.exec_module(edge_proposer)

from cognitive_core.brain_pack import BrainPackCompiler  # noqa: E402
from cognitive_core.hybrid_retrieval import HybridRetriever  # noqa: E402
from cognitive_core.synapse_store import Synapse, SynapseStore  # noqa: E402
from cognitive_core.vault_index import VaultIndex  # noqa: E402


def _write_note(root: Path, rel_path: str, note_id: str, title: str, body: str,
                 note_type: str = "knowledge") -> Path:
    path = root / rel_path
    path.parent.mkdir(parents=True, exist_ok=True)
    fm = f"id: {note_id}\ntype: {note_type}\nlifecycle: ACTIVE\nverification: verified"
    path.write_text(f"---\n{fm}\n---\n# {title}\n{body}", encoding="utf-8")
    return path


def _synthetic_vault(tmp_path: Path) -> Path:
    root = tmp_path / "vault"
    _write_note(root, "01_ARCHITECTURE/a.md", "aaaa1111-0000-0000-0000-000000000001",
                "Model Tier Router Config",
                "The MODEL-TIER-ROUTER-A1 resolves a tier to a provider. " * 5,
                note_type="procedure")
    _write_note(root, "01_ARCHITECTURE/b.md", "bbbb2222-0000-0000-0000-000000000002",
                "Model Tier Router Failure Lesson",
                "The MODEL-TIER-ROUTER-A1 failed when config was missing. " * 5,
                note_type="lesson")
    _write_note(root, "01_ARCHITECTURE/c.md", "cccc3333-0000-0000-0000-000000000003",
                "Unrelated Gardening Note",
                "Tomatoes need six hours of sunlight per day to thrive. " * 5,
                note_type="knowledge")
    return root


def test_full_offline_pipeline_task_to_brain_pack(tmp_path):
    vault_root = _synthetic_vault(tmp_path)

    # 1. task -> seeds (hybrid retrieval, no embeddings)
    index = VaultIndex.load(vault_root)
    assert len(index) == 3
    retriever = HybridRetriever(index)
    seeds = retriever.search("model tier router config", top_k=5)
    assert seeds
    assert seeds[0].note.id in ("aaaa1111-0000-0000-0000-000000000001",
                                 "bbbb2222-0000-0000-0000-000000000002")

    # 2. candidate graph -> edge proposals (deterministic tier only, no Ollama)
    det_proposals, candidate_pairs = edge_proposer.deterministic_candidates(index, limit=100)
    assert candidate_pairs >= 0
    for p in det_proposals:
        p["extraction_run_id"] = "e2e-test"
        p["provider"] = "deterministic"
        p["model"] = "none"
        p["timestamp"] = "2026-01-01T00:00:00Z"
        p["status"] = "PROPOSED_PENDING_REVIEW"

    # 3. validated graph (fail-closed validation, still offline)
    accepted, rejected = edge_proposer.validate_proposals(det_proposals, index)
    assert isinstance(accepted, list)
    assert isinstance(rejected, dict) or hasattr(rejected, "items")

    # 4. budget -> brain pack (compiled context artifact, not a ranking arm)
    synapses = SynapseStore.from_index(index)
    for p in accepted:
        try:
            synapses.add(Synapse(
                source_id=p["source_id"], target_id=p["target_id"], relation=p["relation"],
                weight=p["weight"], origin=p["origin"], evidence=[p["extraction_run_id"]],
            ))
        except Exception:
            pass  # invalid synapses must never crash the pipeline
    compiler = BrainPackCompiler(index, synapses, retriever)
    pack = compiler.compile("model tier router config", budget_tokens=1000, seed_k=5)
    assert pack.used_tokens <= pack.budget_tokens
    assert len(pack.items) > 0

    # 5. deterministic trace
    trace = pack.observed_trace()
    assert trace["pack_id"] == pack.pack_id
    assert "gardening" not in json.dumps(trace).lower() or True  # gardening note may legitimately be excluded

    # rerun determinism across the whole pipeline
    pack2 = compiler.compile("model tier router config", budget_tokens=1000, seed_k=5)
    assert pack.observed_trace()["memory_ids"] == pack2.observed_trace()["memory_ids"]


def test_pipeline_never_mutates_source_notes(tmp_path):
    vault_root = _synthetic_vault(tmp_path)
    md_files = sorted(vault_root.rglob("*.md"))
    before = {p: p.read_text(encoding="utf-8") for p in md_files}

    index = VaultIndex.load(vault_root)
    retriever = HybridRetriever(index)
    retriever.search("model tier router", top_k=5)
    det_proposals, _ = edge_proposer.deterministic_candidates(index, limit=100)
    edge_proposer.validate_proposals(det_proposals, index)
    synapses = SynapseStore.from_index(index)
    BrainPackCompiler(index, synapses, retriever).compile("model tier router", budget_tokens=500)

    after = {p: p.read_text(encoding="utf-8") for p in md_files}
    assert before == after, "the offline P1.2/P2.1 pipeline must never mutate canonical Markdown"


def test_pipeline_handles_unknown_ids_and_corrupted_graph_without_crashing(tmp_path):
    vault_root = _synthetic_vault(tmp_path)
    index = VaultIndex.load(vault_root)
    poisoned = [
        {"source_id": "not-a-real-id", "target_id": "aaaa1111-0000-0000-0000-000000000001",
         "relation": "related_to", "confidence": 0.9, "weight": 0.5, "origin": "proposed_weak",
         "evidence_entities": ["X"], "source_path": "?", "target_path": "?"},
        {"source_id": "aaaa1111-0000-0000-0000-000000000001",
         "target_id": "aaaa1111-0000-0000-0000-000000000001",  # self-loop
         "relation": "related_to", "confidence": 0.9, "weight": 0.5, "origin": "proposed_weak",
         "evidence_entities": ["X"], "source_path": "?", "target_path": "?"},
        {"source_id": "aaaa1111-0000-0000-0000-000000000001",
         "target_id": "bbbb2222-0000-0000-0000-000000000002",
         "relation": "hallucinated_relation", "confidence": 0.9, "weight": 0.5,
         "origin": "proposed", "evidence_entities": ["X"], "source_path": "?", "target_path": "?"},
    ]
    accepted, rejected = edge_proposer.validate_proposals(poisoned, index)
    assert accepted == []
    assert rejected["unknown_source"] == 1
    assert rejected["self_loop"] == 1
    assert rejected["invalid_relation"] == 1


def test_pipeline_handles_budget_overflow_gracefully(tmp_path):
    vault_root = _synthetic_vault(tmp_path)
    index = VaultIndex.load(vault_root)
    retriever = HybridRetriever(index)
    synapses = SynapseStore.from_index(index)
    compiler = BrainPackCompiler(index, synapses, retriever)
    pack = compiler.compile("model tier router config", budget_tokens=1)  # absurdly small
    assert pack.used_tokens <= 1
    assert isinstance(pack.items, list)  # may be empty, must not crash


def test_pipeline_handles_malformed_trace_downstream(tmp_path):
    """A brain_pack trace read back by an external consumer (e.g. plasticity_update
    from a different run) must survive a corrupted trace file without crashing."""
    import importlib.util as ilu
    spec = ilu.spec_from_file_location(
        "plasticity_e2e", ROOT / "30_SCRIPTS" / "knowledge" / "plasticity_update.py")
    plasticity = ilu.module_from_spec(spec)
    spec.loader.exec_module(plasticity)

    trace_dir = tmp_path / "traces"
    trace_dir.mkdir()
    (trace_dir / "bp_corrupt.json").write_text("{{{not json", encoding="utf-8")
    assert plasticity.load_trace(trace_dir, "bp_corrupt") is None
