"""test_retrieval_foundation.py — P1.0, P1.1, and P1.1-B Regression & Contract Test Suite.

Covers all 17 mandatory verification scenarios:
  1. Canonical index loads only canonical notes by default (RAW/ARCHIVED excluded).
  2. Override flag allows loading RAW/ARCHIVED.
  3. Duplicate note IDs detected and reported by validator.
  4. Duplicate content hashes detected and reported.
  5. Invalid UUIDs detected and reported.
  6. Missing frontmatter detected and reported.
  7. Broken targets in relations detected and reported.
  8. Validator emits valid structured JSON report.
  9. BM25 ranker produces deterministic scores and ranking.
  10. Entity extractor correctly identifies CamelCase, acronyms, version strings, known tags.
  11. Entity extractor explicitly ignores generic decimal literals (e.g. 0.15, 0.6).
  12. Dense provider fails closed with DENSE_PROVIDER_UNAVAILABLE when Ollama is offline.
  13. RRF fuses ranks with k=60 and deterministic tie-breaking by note ID.
  14. Security filters default to ACTIVE + VERIFIED.
  15. Retrieval trace contains query, filters, candidate rankings per arm, fused ranking, timestamps, latencies.
  16. Benchmark runs all arms and query classes, outputting structured JSON stamped CORPUS_MURDAR.
  17. Ablation rules properly flag 'DENSE NOT JUSTIFIED' when conditions met.
"""
from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path
from typing import List

import pytest

from cognitive_core.vault_index import Note, VaultIndex, stats
from cognitive_core.hybrid_retrieval import (
    HybridRetriever,
    OllamaEmbedder,
    DenseProviderUnavailableError,
    BM25,
    entities,
    tokenize,
)
from cognitive_core.benchmarks.retrieval_ab import (
    evaluate_dense_ablation,
    evaluate_graph_ablation,
    _rank_metrics,
    main as benchmark_main,
)

# Load validate_corpus_index module
_VALIDATOR_PATH = Path(__file__).resolve().parents[2] / "30_SCRIPTS" / "verification" / "validate_corpus_index.py"
_spec = importlib.util.spec_from_file_location("validate_corpus_index", _VALIDATOR_PATH)
_validator = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_validator)
validate_corpus_index = _validator.validate_corpus_index


def _write_note(root: Path, rel_path: str, frontmatter: str, body: str) -> Path:
    p = root / rel_path
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(f"---\n{frontmatter}\n---\n{body}", encoding="utf-8")
    return p


# ---------------------------------------------------------------------------
# 1. Canonical index loads only canonical notes by default (RAW/ARCHIVED excluded)
# ---------------------------------------------------------------------------
def test_01_canonical_index_excludes_raw_and_archived_by_default(tmp_path):
    _write_note(tmp_path, "01_ARCHITECTURE/active.md", "id: 11111111-1111-1111-1111-111111111111\ntype: knowledge\nlifecycle: ACTIVE", "Active body")
    _write_note(tmp_path, "01_ARCHITECTURE/archived.md", "id: 22222222-2222-2222-2222-222222222222\ntype: knowledge\nlifecycle: ARCHIVED", "Archived body")
    _write_note(tmp_path, "01_ARCHITECTURE/05_archive/old.md", "id: 33333333-3333-3333-3333-333333333333\ntype: knowledge\nlifecycle: ACTIVE", "Archive path body")
    _write_note(tmp_path, "06_INBOX/raw.md", "id: 44444444-4444-4444-4444-444444444444\ntype: raw\nlifecycle: RAW", "Raw body")

    idx = VaultIndex.load(tmp_path, roots=("01_ARCHITECTURE", "06_INBOX"))
    loaded_ids = {n.id for n in idx.notes}
    assert "11111111-1111-1111-1111-111111111111" in loaded_ids
    assert "22222222-2222-2222-2222-222222222222" not in loaded_ids
    assert "33333333-3333-3333-3333-333333333333" not in loaded_ids
    assert "44444444-4444-4444-4444-444444444444" not in loaded_ids


# ---------------------------------------------------------------------------
# 2. Override flag allows loading RAW/ARCHIVED
# ---------------------------------------------------------------------------
def test_02_override_flag_allows_loading_raw_and_archived(tmp_path):
    _write_note(tmp_path, "01_ARCHITECTURE/active.md", "id: 11111111-1111-1111-1111-111111111111\ntype: knowledge\nlifecycle: ACTIVE", "Active body")
    _write_note(tmp_path, "01_ARCHITECTURE/archived.md", "id: 22222222-2222-2222-2222-222222222222\ntype: knowledge\nlifecycle: ARCHIVED", "Archived body")
    _write_note(tmp_path, "06_INBOX/raw.md", "id: 44444444-4444-4444-4444-444444444444\ntype: raw\nlifecycle: RAW", "Raw body")

    idx = VaultIndex.load(tmp_path, roots=("01_ARCHITECTURE", "06_INBOX"), include_raw=True, include_archived=True)
    loaded_ids = {n.id for n in idx.notes}
    assert "11111111-1111-1111-1111-111111111111" in loaded_ids
    assert "22222222-2222-2222-2222-222222222222" in loaded_ids
    assert "44444444-4444-4444-4444-444444444444" in loaded_ids


# ---------------------------------------------------------------------------
# 3. Duplicate note IDs detected and reported by validator
# ---------------------------------------------------------------------------
def test_03_validator_detects_duplicate_note_ids(tmp_path):
    _write_note(tmp_path, "01_ARCHITECTURE/note1.md", "id: 11111111-1111-1111-1111-111111111111\ntype: knowledge", "Body 1")
    _write_note(tmp_path, "01_ARCHITECTURE/note2.md", "id: 11111111-1111-1111-1111-111111111111\ntype: knowledge", "Body 2")

    report = validate_corpus_index(tmp_path)
    dup_ids = report["issues"]["duplicate_ids"]
    assert len(dup_ids) == 1
    assert dup_ids[0]["id"] == "11111111-1111-1111-1111-111111111111"
    assert dup_ids[0]["count"] == 2
    assert report["is_clean"] is False


# ---------------------------------------------------------------------------
# 4. Duplicate content hashes detected and reported
# ---------------------------------------------------------------------------
def test_04_validator_detects_duplicate_content_hashes(tmp_path):
    _write_note(tmp_path, "01_ARCHITECTURE/note1.md", "id: 11111111-1111-1111-1111-111111111111\ntype: knowledge", "Exact duplicate text content here.")
    _write_note(tmp_path, "01_ARCHITECTURE/note2.md", "id: 22222222-2222-2222-2222-222222222222\ntype: knowledge", "Exact   duplicate   text  CONTENT here.")

    report = validate_corpus_index(tmp_path)
    dup_hashes = report["issues"]["duplicate_content_hashes"]
    assert len(dup_hashes) >= 1
    assert report["is_clean"] is False


# ---------------------------------------------------------------------------
# 5. Invalid UUIDs detected and reported
# ---------------------------------------------------------------------------
def test_05_validator_detects_invalid_uuids(tmp_path):
    _write_note(tmp_path, "01_ARCHITECTURE/invalid.md", "id: not-a-canonical-uuid-12345\ntype: knowledge", "Body")

    report = validate_corpus_index(tmp_path)
    invalid_uuids = report["issues"]["invalid_uuids"]
    assert any(item["id"] == "not-a-canonical-uuid-12345" for item in invalid_uuids)
    assert report["is_clean"] is False


# ---------------------------------------------------------------------------
# 6. Missing frontmatter detected and reported
# ---------------------------------------------------------------------------
def test_06_validator_detects_missing_frontmatter(tmp_path):
    raw_file = tmp_path / "01_ARCHITECTURE" / "missing_fm.md"
    raw_file.parent.mkdir(parents=True, exist_ok=True)
    raw_file.write_text("# Heading Without Frontmatter\nSome content.", encoding="utf-8")

    report = validate_corpus_index(tmp_path)
    missing_fm = report["issues"]["missing_frontmatter"]
    assert len(missing_fm) >= 1
    assert any("missing_fm.md" in item["path"] for item in missing_fm)
    assert report["is_clean"] is False


# ---------------------------------------------------------------------------
# 7. Broken targets in relations detected and reported
# ---------------------------------------------------------------------------
def test_07_validator_detects_broken_relations(tmp_path):
    _write_note(
        tmp_path,
        "01_ARCHITECTURE/note1.md",
        "id: 11111111-1111-1111-1111-111111111111\ntype: knowledge\nrelations:\n  - target_id: 99999999-9999-9999-9999-999999999999",
        "Body content",
    )

    report = validate_corpus_index(tmp_path)
    broken_rel = report["issues"]["broken_relations"]
    assert len(broken_rel) == 1
    assert broken_rel[0]["target_id"] == "99999999-9999-9999-9999-999999999999"
    assert report["is_clean"] is False


# ---------------------------------------------------------------------------
# 8. Validator emits valid structured JSON report
# ---------------------------------------------------------------------------
def test_08_validator_emits_valid_structured_json_report(tmp_path):
    _write_note(tmp_path, "01_ARCHITECTURE/valid.md", "id: 11111111-1111-1111-1111-111111111111\ntype: knowledge\nlifecycle: ACTIVE", "Clean body")

    report = validate_corpus_index(tmp_path)
    assert "timestamp_utc" in report
    assert "vault_root" in report
    assert "total_notes" in report
    assert "issues" in report
    assert "issue_counts" in report
    assert "is_clean" in report
    assert report["is_clean"] is True
    # Ensure serializable to JSON
    json_str = json.dumps(report)
    assert isinstance(json_str, str)


# ---------------------------------------------------------------------------
# 9. BM25 ranker produces deterministic scores and ranking
# ---------------------------------------------------------------------------
def test_09_bm25_ranker_produces_deterministic_scores_and_ranking():
    corpus = [
        ["architecture", "memory", "controller", "sqlite"],
        ["cognitive", "core", "orchestrator", "activation"],
        ["unrelated", "gardening", "plants", "flowers"],
    ]
    bm25 = BM25(corpus)
    query = ["memory", "controller"]
    scores_run1 = bm25.scores(query)
    scores_run2 = bm25.scores(query)
    assert scores_run1 == scores_run2
    assert scores_run1[0] > scores_run1[1]
    assert scores_run1[0] > scores_run1[2]


# ---------------------------------------------------------------------------
# 10. Entity extractor identifies CamelCase, acronyms, version strings, tags
# ---------------------------------------------------------------------------
def test_10_entity_extractor_identifies_canonical_identifiers():
    sample_text = (
        "The MemoryController enforces I-001 and P0-015 in version 6.0.0 "
        "and handles snake_case_event."
    )
    extracted = entities(sample_text)
    assert "memorycontroller" in extracted
    assert "i-001" in extracted
    assert "p0-015" in extracted
    assert "6.0.0" in extracted
    assert "snake_case_event" in extracted


# ---------------------------------------------------------------------------
# 11. Entity extractor explicitly ignores generic decimal literals
# ---------------------------------------------------------------------------
def test_11_entity_extractor_explicitly_ignores_generic_decimals():
    text = "threshold 0.15 with score 0.6 and weight 0.99 with alpha 3.14"
    extracted = entities(text)
    assert "0.15" not in extracted
    assert "0.6" not in extracted
    assert "0.99" not in extracted
    assert "3.14" not in extracted


# ---------------------------------------------------------------------------
# 12. Dense provider fails closed with DENSE_PROVIDER_UNAVAILABLE when Ollama offline
# ---------------------------------------------------------------------------
def test_12_dense_provider_fails_closed_when_offline():
    # Use non-existent port to simulate offline Ollama
    embedder = OllamaEmbedder(host="http://127.0.0.1:59999", timeout=0.1)
    is_avail = embedder.check_availability()
    assert is_avail is False
    assert embedder.status == "DENSE_PROVIDER_UNAVAILABLE"

    notes = [
        Note(id="1", path=Path("1.md"), title="Test", body="Body text", meta={"type": "knowledge"}),
    ]
    retriever = HybridRetriever(VaultIndex(notes), embedder=embedder)

    with pytest.raises(DenseProviderUnavailableError) as exc_info:
        retriever.dense_only("search term", raise_on_unavailable=True)
    assert "DENSE_PROVIDER_UNAVAILABLE" in str(exc_info.value)


# ---------------------------------------------------------------------------
# 13. RRF fuses ranks with k=60 and deterministic tie-breaking by note ID
# ---------------------------------------------------------------------------
def test_13_rrf_fuses_with_k60_and_deterministic_tie_breaking():
    assert HybridRetriever.RRF_K == 60

    # Create two notes with identical content so BM25 and Entity scores tie
    note_b = Note(id="b_note_id", path=Path("b.md"), title="Identical Title", body="Identical text", meta={"type": "knowledge"})
    note_a = Note(id="a_note_id", path=Path("a.md"), title="Identical Title", body="Identical text", meta={"type": "knowledge"})
    retriever = HybridRetriever(VaultIndex([note_b, note_a]))

    hits = retriever.search("Identical Title", top_k=2)
    assert len(hits) == 2
    # Equal score must be broken by note.id lexicographically: "a_note_id" before "b_note_id"
    assert hits[0].note.id == "a_note_id"
    assert hits[1].note.id == "b_note_id"


# ---------------------------------------------------------------------------
# 14. Security filters default to ACTIVE + VERIFIED
# ---------------------------------------------------------------------------
def test_14_security_filters_default_to_active_and_verified():
    n_active_verif = Note(id="1", path=Path("1.md"), title="Note 1", body="Memory controller SQLite", meta={"type": "knowledge", "lifecycle": "ACTIVE", "verification": "verified"})
    n_review_verif = Note(id="2", path=Path("2.md"), title="Note 2", body="Memory controller SQLite", meta={"type": "knowledge", "lifecycle": "REVIEW", "verification": "verified"})
    n_active_unverif = Note(id="3", path=Path("3.md"), title="Note 3", body="Memory controller SQLite", meta={"type": "knowledge", "lifecycle": "ACTIVE", "verification": "unverified"})

    retriever = HybridRetriever(VaultIndex([n_active_verif, n_review_verif, n_active_unverif]))

    # Default secure_search enforces ACTIVE + verified
    hits = retriever.secure_search("Memory controller", top_k=5)
    assert len(hits) == 1
    assert hits[0].note.id == "1"


# ---------------------------------------------------------------------------
# 15. Retrieval trace contains query, filters, candidate rankings, timestamps, latencies
# ---------------------------------------------------------------------------
def test_15_retrieval_trace_contains_all_required_metadata():
    note = Note(id="1", path=Path("1.md"), title="Architecture", body="Detailed architecture of memory vault", meta={"type": "knowledge", "lifecycle": "ACTIVE", "verification": "verified"})
    retriever = HybridRetriever(VaultIndex([note]))

    hits, trace = retriever.search_with_trace("architecture", top_k=3, secure=True)
    assert len(hits) == 1
    assert trace["query"] == "architecture"
    assert trace["top_k"] == 3
    assert "filters" in trace
    assert trace["filters"]["lifecycles"] == ["ACTIVE"]
    assert trace["filters"]["verification"] == ["verified"]
    assert "candidate_rankings" in trace
    assert "bm25" in trace["candidate_rankings"]
    assert "fused_ranking" in trace
    assert "timestamp_utc" in trace
    assert "latency_ms" in trace
    assert isinstance(trace["latency_ms"], float)


# ---------------------------------------------------------------------------
# 16. Benchmark runs all arms and query classes, outputting structured JSON stamped CORPUS_MURDAR
#     Paraphrase is always attempted (never NOT_REQUESTED). With paraphrase-sample=0 no
#     Ollama calls are made: the probe passes immediately since pool[:0] is empty, Ollama
#     liveness check still happens. To avoid Ollama dependency in offline CI, set
#     paraphrase-sample to 0 which triggers the provider probe but generates 0 queries.
# ---------------------------------------------------------------------------
def test_16_benchmark_runs_all_arms_and_stamped_corpus_murdar(tmp_path):
    _write_note(tmp_path, "01_ARCHITECTURE/n1.md", "id: 11111111-1111-1111-1111-111111111111\ntype: knowledge\nlifecycle: ACTIVE", "# Note One\n" + "This is a substantial body paragraph detailing the architecture. " * 15)
    _write_note(tmp_path, "01_ARCHITECTURE/n2.md", "id: 22222222-2222-2222-2222-222222222222\ntype: knowledge\nlifecycle: ACTIVE", "# Note Two\n" + "Another substantial body paragraph detailing cognitive processing. " * 15)

    synapses_path = tmp_path / "05_DATA" / "synapses.json"
    synapses_path.parent.mkdir(parents=True, exist_ok=True)
    synapses_path.write_text('{"synapses": []}', encoding="utf-8")

    out_file = tmp_path / "retrieval_ab_report.json"
    argv = [
        "--vault", str(tmp_path),
        "--sample", "2",
        "--synapses", "05_DATA/synapses.json",
        "--out", str(out_file.relative_to(tmp_path)),
        "--no-multi-hop",
        "--paraphrase-sample", "0",   # no Ollama calls; still probes provider, may succeed or fail
    ]

    ret = benchmark_main(argv)
    assert ret == 0
    assert out_file.exists()

    report = json.loads(out_file.read_text(encoding="utf-8"))
    assert report["corpus_label"] == "CORPUS_MURDAR"
    assert "corpus_label_note" in report
    assert "known_item" in report
    assert "entity_heavy" in report
    assert "dense_ablation" in report
    assert "graph_ablation" in report
    # Check that arms are present
    assert "jaccard" in report["known_item"]
    assert "bm25" in report["known_item"]
    assert "entity" in report["known_item"]
    assert "lexical_rrf" in report["known_item"]
    assert "dense" in report["known_item"]
    assert "graph" in report["known_item"]
    # Paraphrase must be present and never NOT_REQUESTED
    assert "paraphrase" in report
    para = report["paraphrase"]
    assert para.get("status") in ("OK", "PARAPHRASE_PROVIDER_UNAVAILABLE"), (
        f"paraphrase.status must be OK or PARAPHRASE_PROVIDER_UNAVAILABLE, got: {para.get('status')!r}"
    )



# ---------------------------------------------------------------------------
# 17. Ablation rules properly flag 'DENSE NOT JUSTIFIED' when conditions met
# ---------------------------------------------------------------------------
def test_17_ablation_rules_properly_flag_dense_not_justified():
    lexical = {"mrr": 0.80, "recall@10": 0.90}

    # Case 1: Dense provider unavailable
    res_unavail = evaluate_dense_ablation(lexical, {"status": "DENSE_PROVIDER_UNAVAILABLE"})
    assert res_unavail["verdict"] == "DENSE NOT JUSTIFIED"
    assert "fail-closed" in res_unavail["reason"]

    # Case 2: Gain below threshold (< 0.05)
    res_low_gain = evaluate_dense_ablation(lexical, {"mrr": 0.82, "recall@10": 0.91})
    assert res_low_gain["verdict"] == "DENSE NOT JUSTIFIED"
    assert res_low_gain["delta_mrr"] == 0.02

    # Case 3: Gain meets threshold (>= 0.05)
    res_justified = evaluate_dense_ablation(lexical, {"mrr": 0.87, "recall@10": 0.95})
    assert res_justified["verdict"] == "DENSE JUSTIFIED"
    assert res_justified["delta_mrr"] == 0.07
