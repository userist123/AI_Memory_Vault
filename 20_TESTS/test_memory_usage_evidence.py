"""The evidence of real use in 07_EVALUATION/memory_usage/ must hold together.

The 20 questions come from open coordination tasks; the results, the judgements, the usage-log
snapshot, the reports and the proposal proof all refer to each other. These tests recompute what can
be recomputed and fail if a file is edited out of step with the others.
"""
from __future__ import annotations

import importlib.util
import json
import os
import re
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
DIR = REPO / "07_EVALUATION" / "memory_usage"
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)


def _load(name: str, rel: str):
    spec = importlib.util.spec_from_file_location(name, REPO / rel)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


usage = _load("memory_usage_report", "30_SCRIPTS/evaluation/memory_usage_report.py")
summ = _load("summarize_worklist", "30_SCRIPTS/evaluation/summarize_worklist.py")
worklist = _load("run_memory_worklist", "30_SCRIPTS/evaluation/run_memory_worklist.py")
from interfaces import vault_runtime  # noqa: E402


def load(name):
    return json.loads((DIR / name).read_text(encoding="utf-8"))


QUERIES = load("work_queries.json")["queries"]
RESULTS = load("work_session_results.json")
JUDGEMENTS = load("work_session_judgements.json")["judgements"]


def test_twenty_different_questions_each_taken_from_a_real_passage():
    assert len(QUERIES) >= 20 and len({q["question"] for q in QUERIES}) == len(QUERIES)
    assert worklist.load_queries() == QUERIES  # also verifies that every passage is in its file
    for q in QUERIES:
        assert q["source_file"].startswith("00_GOVERNANCE/coordination/")


def test_negative_control_a_question_whose_passage_is_not_in_the_file_is_refused(tmp_path, monkeypatch):
    bad = {"queries": [{"id": 1, "source_file": QUERIES[0]["source_file"], "source_quote": "text that is not in the file",
                        "question": "q"}]}
    fake = tmp_path / "work_queries.json"
    fake.write_text(json.dumps(bad), encoding="utf-8")
    monkeypatch.setattr(worklist, "QUERIES", fake)
    with pytest.raises(SystemExit, match="passage is not in"):
        worklist.load_queries()


def test_results_answer_exactly_these_questions():
    assert [r["question"] for r in RESULTS["results"]] == [q["question"] for q in QUERIES]
    assert RESULTS["client"] == "claude-sonnet-mcp-sdk-driver"  # honest about who asked


def test_judgements_only_refer_to_returned_notes():
    by_question = {r["id"]: r["returned"] for r in RESULTS["results"]}
    for entry in JUDGEMENTS:
        returned_ids = [row["id"] for row in by_question[entry["question_id"]][:3]]
        assert [m["id"] for m in entry["judged"]] == returned_ids
        assert all(m["reason"] for m in entry["judged"])


def test_the_usage_log_snapshot_holds_one_line_per_question_and_no_query_text():
    rows = vault_runtime.read_usage_log(DIR / "usage_log_snapshot.jsonl")
    hashes = {r["query_sha256"] for r in rows if r["tool"] == "memory_search"}
    for q in QUERIES:
        assert vault_runtime.query_digest(q["question"]) in hashes, q["id"]
    text = (DIR / "usage_log_snapshot.jsonl").read_text(encoding="utf-8")
    for q in QUERIES:
        assert q["question"] not in text
    assert all(r["client"] == "claude-sonnet-mcp-sdk-driver" and r["outcome"] == "ok" for r in rows)
    assert len({r["query_sha256"] for r in rows if r["tool"] == "memory_search"}) >= 20


def test_negative_control_a_log_without_the_hash_of_a_question_would_be_caught():
    rows = vault_runtime.read_usage_log(DIR / "usage_log_snapshot.jsonl")
    hashes = {r["query_sha256"] for r in rows}
    assert vault_runtime.query_digest("o întrebare pusă de altcineva") not in hashes


def test_the_usage_report_is_exactly_what_the_script_computes_from_the_snapshot():
    rows = vault_runtime.read_usage_log(DIR / "usage_log_snapshot.jsonl")
    fresh = usage.render(usage.build_report(rows)).splitlines()   # attestation depends on the vault today: excluded
    committed = (DIR / "MEMORY_USAGE_REPORT.md").read_text(encoding="utf-8").splitlines()
    strip = lambda lines: [l for l in lines if not l.startswith("- Proposals attested")]  # noqa: E731
    assert strip(fresh) == strip(committed)


def test_the_relevance_summary_is_exactly_what_the_script_computes():
    sources = load("work_session_sources_indexed.json")
    fresh = summ.render(summ.summarise(RESULTS["results"], JUDGEMENTS, sources))
    assert fresh.strip() == (DIR / "WORKLIST_RELEVANCE.md").read_text(encoding="utf-8").strip()


def test_negative_control_editing_a_judgement_changes_the_summary():
    edited = json.loads(json.dumps(JUDGEMENTS))
    edited[0]["judged"][0]["relevant"] = True
    base = summ.summarise(RESULTS["results"], JUDGEMENTS, load("work_session_sources_indexed.json"))
    assert summ.summarise(RESULTS["results"], edited, load("work_session_sources_indexed.json"))["relevant_results"] == base["relevant_results"] + 1


def test_the_real_proposal_exists_is_a_review_candidate_and_was_found_by_search():
    proof = load("proposal_proof.json")
    path = REPO / proof["proposal"]["path"]
    assert path.is_file() and proof["proposal"]["path"].startswith("01_ARCHITECTURE/knowledge/")
    text = path.read_text(encoding="utf-8")
    assert f"id: {proof['proposal']['id']}" in text
    assert re.search(r"^lifecycle: REVIEW$", text, re.M) and re.search(r"^verification: unverified$", text, re.M)
    assert "candidate" in text and "ontology/slots" not in proof["proposal"]["path"]
    assert proof["found_by_memory_search"] is True and proof["search_rank"] == 1
    assert proof["memory_get"]["lifecycle"] == "REVIEW" and proof["memory_get"]["unverified"] is True


def test_the_proposed_note_states_the_computed_numbers():
    summary = summ.summarise(RESULTS["results"], JUDGEMENTS, load("work_session_sources_indexed.json"))
    proof = load("proposal_proof.json")
    text = (REPO / proof["proposal"]["path"]).read_text(encoding="utf-8")
    assert f"{summary['relevant_results']} din {summary['judged_results']}" in text
    assert f"{summary['sources_indexed']} din {summary['sources_total']}" in text


def test_claude_code_loaded_the_registration_and_connected():
    evidence = load("claude_code_mcp_connect.json")
    assert evidence["mcp_servers"][0]["name"] == "vault-memory" and evidence["mcp_servers"][0]["status"] == "connected"
    assert set(evidence["vault_memory_tools"]) == {"mcp__vault-memory__memory_search", "mcp__vault-memory__memory_get",
                                                    "mcp__vault-memory__memory_propose"}
