import json
from pathlib import Path

import pytest

from scripts.book_knowledge_consolidation import (
    RELATION_TYPES,
    build,
    classify,
    load_candidates,
)


ROOT = Path(__file__).parents[1] / "06_INBOX" / "DERIVED" / "BOOKS" / "2026-09-04"


def candidates_by_id():
    return {c["candidate_id"]: c for c in load_candidates(ROOT)[0]}


def test_candidate_loading_and_schema_source_hashes():
    candidates, manifest = load_candidates(ROOT)
    assert len(candidates) == 54
    assert len({c["candidate_id"] for c in candidates}) == 54
    for candidate in candidates:
        assert candidate["source_path"] in manifest
        assert candidate["source_sha256"] == manifest[candidate["source_path"]]["sha256"]
        assert candidate["locator"]


def test_required_relation_types_and_deliberate_classifications():
    by_id = candidates_by_id()
    assert {"DUPLICATE", "NEAR_DUPLICATE", "COMPLEMENTARY", "CONFLICT", "UNRELATED", "SUPERSEDES"} <= RELATION_TYPES
    assert classify(by_id["book-agents-2026-c003"], by_id["book-llm-apps-c007"]) == "NEAR_DUPLICATE"
    assert classify(by_id["book-ddia-2017-c006"], by_id["book-ddia-2017-c009"]) == "COMPLEMENTARY"
    assert classify(by_id["book-agents-2026-c003"], by_id["book-agents-2026-c005"]) == "UNRELATED"
    a = {"candidate_id": "fixture-a", "statement": "A reliable system requires monitoring and failure handling", "type": "PRINCIPLE"}
    b = {"candidate_id": "fixture-b", "statement": "A reliable system does not require monitoring and failure handling", "type": "PRINCIPLE"}
    assert classify(a, b) == "CONFLICT"
    assert classify({**a, "candidate_id": "fixture-c"}, {**a, "candidate_id": "fixture-d"}) == "DUPLICATE"


def test_build_is_lossless_and_human_gated(tmp_path):
    output = tmp_path / "consolidated"
    stats = build(ROOT, output)
    assert stats["candidates"] == 54
    clusters = json.loads((output / "candidate_clusters.json").read_text())
    mapped = clusters["candidate_to_cluster"]
    assert len(mapped) == 54

    atoms = [json.loads(line) for line in (output / "knowledge_atoms.jsonl").read_text().splitlines()]
    evidence = {x["knowledge_id"]: x for x in (json.loads(line) for line in (output / "evidence_bundles.jsonl").read_text().splitlines())}
    mapped_from_atoms = {cid for atom in atoms for cid in atom["supporting_candidates"]}
    assert mapped_from_atoms == set(mapped)
    for atom in atoms:
        assert atom["verification_required"] is True
        assert atom["status"] == "READY_FOR_HUMAN_REVIEW"
        assert atom["id"] in evidence
        assert atom["sources"]
        for source in atom["sources"]:
            assert source["source_sha256"]
            assert source["locator"]
    queue = [json.loads(line) for line in (output / "verification_queue.jsonl").read_text().splitlines()]
    assert queue and all(item["promotion_allowed"] is False for item in queue)


def test_manifest_mismatch_is_rejected(tmp_path):
    copied = tmp_path / "input"
    copied.mkdir()
    for path in ROOT.glob("*.md"):
        (copied / path.name).write_text(path.read_text(encoding="utf-8"), encoding="utf-8")
    manifest = json.loads((ROOT / "source_manifest.json").read_text())
    manifest[0]["sha256"] = "0" * 64
    (copied / "source_manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    with pytest.raises(ValueError, match="manifest mismatch"):
        load_candidates(copied)
