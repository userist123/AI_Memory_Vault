"""AKE Milestone 2, Loop B: retrieval quality through the production agent path.

Contract:
- the benchmark is frozen: a canonical-LF SHA-256 sidecar must match before any case runs;
- every case goes through MemoryController.search() as AI_AGENT with the agent page size;
- a miss is classified (PAGINATION_CUT / FILTERED / NEVER_CANDIDATE / GOLD_NOT_IN_STORAGE), so a
  structural zero (gold the storage cannot see) is visible instead of counted as a retrieval miss;
- the record holds case ids, classes and ranks only: no query text, no note content;
- two records compare only when benchmark digest, k and principal match, with an exact paired test.
"""
from __future__ import annotations

import hashlib
import json
import uuid
from pathlib import Path

import pytest

from active_knowledge_engine.compare import compare_records
from active_knowledge_engine.retrieval_quality import (
    BenchmarkIntegrityError,
    evaluate,
    load_frozen_benchmark,
    wilson_interval,
)
from active_knowledge_engine.runner import AKEShadowRunner, JobStatus

REPO = Path(__file__).resolve().parents[1]


@pytest.fixture(autouse=True)
def _hmac_secret(monkeypatch):
    # search() signs pagination tokens; production loads the per-user secret (vault_runtime)
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "a" * 64)


def _digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes().replace(b"\r\n", b"\n").replace(b"\r", b"\n")).hexdigest()


def _freeze(path: Path, cases: list) -> Path:
    path.write_text(json.dumps({"schema_version": 2, "set_name": "fixture", "cases": cases}), encoding="utf-8")
    path.with_suffix(path.suffix + ".sha256").write_text(_digest(path) + "\n", encoding="utf-8")
    return path


@pytest.fixture
def vault_and_ids(tmp_path):
    from memory_vault_fixture import make_vault, note_text

    vault = make_vault(tmp_path)
    k = vault / "01_ARCHITECTURE" / "knowledge"
    ids = {name: str(uuid.uuid4()) for name in ("alpha", "beta", "gamma")}
    (k / "alpha.md").write_text(note_text("zebra protocol", "The zebra protocol rotates keys every night.", ids["alpha"]), encoding="utf-8")
    (k / "beta.md").write_text(note_text("zebra fallback", "The zebra fallback keeps the old keys for a day.", ids["beta"]), encoding="utf-8")
    (k / "gamma.md").write_text(note_text("orchid budget", "Orchid budget caps the graph at twenty notes.", ids["gamma"]), encoding="utf-8")
    return vault, ids


def _controller(vault):
    from memory_controller.controller import MemoryController
    from memory_controller.storage.file_engine import FileStorageEngine

    return MemoryController(storage=FileStorageEngine(str(vault)))


def _cases(ids):
    return [
        {"id": "C1", "class": "exact_identifier_lookup", "query": "orchid budget graph",
         "gold_relevant_notes": [ids["gamma"]], "abstain": False},
        {"id": "C2", "class": "paraphrase", "query": "quantum spaghetti",
         "gold_relevant_notes": [ids["alpha"]], "abstain": False},
        {"id": "C3", "class": "unanswerable", "query": "what is the moon made of",
         "gold_relevant_notes": [], "abstain": True},
        {"id": "C4", "class": "paraphrase", "query": "orchid budget",
         "gold_relevant_notes": ["knw-does-not-exist-0001"], "abstain": False},
    ]


def test_wilson_interval_known_values():
    lo, hi = wilson_interval(5, 10)
    assert round(lo, 4) == 0.2366 and round(hi, 4) == 0.7634
    assert wilson_interval(0, 0) == (None, None)


def test_frozen_benchmark_loads_and_detects_tampering(tmp_path, vault_and_ids):
    _, ids = vault_and_ids
    path = _freeze(tmp_path / "bench.json", _cases(ids))
    bench = load_frozen_benchmark(path)
    assert bench["digest"] == _digest(path) and len(bench["cases"]) == 4
    # CRLF checkout is not tampering
    path.write_bytes(path.read_bytes().replace(b"\n", b"\r\n"))
    assert load_frozen_benchmark(path)["digest"] == bench["digest"]
    path.write_text(path.read_text(encoding="utf-8").replace("orchid", "lotus"), encoding="utf-8")
    with pytest.raises(BenchmarkIntegrityError):
        load_frozen_benchmark(path)
    path.with_suffix(".json.sha256").unlink()
    with pytest.raises(BenchmarkIntegrityError):
        load_frozen_benchmark(path)


def test_the_real_v2_sets_verify():
    for name in ("heldout.json", "dev.json"):
        assert load_frozen_benchmark(REPO / "07_EVALUATION" / "heldout_retrieval_benchmark_v2" / name)["cases"]


def test_evaluate_classifies_hits_and_misses(vault_and_ids):
    vault, ids = vault_and_ids
    result = evaluate(_controller(vault), _cases(ids), k=5)
    rows = {r["id"]: r for r in result["cases"]}
    assert rows["C1"]["hit"] == 1 and rows["C1"]["rank_in_page"] >= 1 and rows["C1"]["miss_class"] is None
    # C2: a tiny vault returns every note for any query, so C2 is not asserted here
    # (NEVER_CANDIDATE is covered with a controlled search in test_miss_classes_with_a_controlled_search)
    assert rows["C3"]["hit"] is None and rows["C3"]["miss_class"] == "ABSTAIN_UNMEASURABLE"
    assert rows["C4"]["hit"] == 0 and rows["C4"]["miss_class"] == "GOLD_NOT_IN_STORAGE"
    s = result["summary"]
    assert s["n"] == 4 and s["n_measurable"] == 3 and s["hits"] == 1 + rows["C2"]["hit"]
    assert s["principal"] == "AI_AGENT" and s["k"] == 5
    assert s["miss_classes"]["GOLD_NOT_IN_STORAGE"] == 1
    assert s["n_scorable"] == 2  # gold visible to storage
    assert s["hit_rate_ci95"][0] is not None


class _ScriptedController:
    """search() returns a fixed page and fused ranking; storage knows a fixed set of ids."""

    def __init__(self, served, fused, known):
        self._served, self._fused = served, fused
        self.storage = type("S", (), {"id_to_path": {i: f"{i}.md" for i in known}})()
        self.calls = []

    def search(self, principal, query, page_size=10, **kw):
        self.calls.append((principal.name, page_size))
        return {"results": [{"id": i} for i in self._served][:page_size],
                "candidate_trace": {"fused_ranking": [{"rank": n, "id": i} for n, i in enumerate(self._fused, 1)]}}


def test_miss_classes_with_a_controlled_search():
    case = lambda gold: {"id": gold, "class": "c", "query": "q", "gold_relevant_notes": [gold], "abstain": False}
    ctl = _ScriptedController(served=["a", "b"], fused=["a", "f", "b", "x", "y", "late"], known={"a", "b", "f", "late", "never"})
    rows = {r["id"]: r for r in evaluate(ctl, [case("a"), case("f"), case("late"), case("never"), case("ghost")], k=5)["cases"]}
    assert rows["a"]["hit"] == 1 and rows["a"]["rank_in_page"] == 1
    assert rows["f"]["miss_class"] == "FILTERED" and rows["f"]["fused_rank"] == 2
    assert rows["late"]["miss_class"] == "PAGINATION_CUT" and rows["late"]["fused_rank"] == 6
    assert rows["never"]["miss_class"] == "NEVER_CANDIDATE"
    assert rows["ghost"]["miss_class"] == "GOLD_NOT_IN_STORAGE"
    assert set(ctl.calls) == {("AI_AGENT", 5)}


def test_pagination_cut_is_detected(vault_and_ids):
    vault, ids = vault_and_ids
    cases = [
        {"id": "PA", "class": "paraphrase", "query": "zebra keys", "gold_relevant_notes": [ids["alpha"]], "abstain": False},
        {"id": "PB", "class": "paraphrase", "query": "zebra keys", "gold_relevant_notes": [ids["beta"]], "abstain": False},
    ]
    rows = evaluate(_controller(vault), cases, k=1)["cases"]
    # two different golds for one query with k=1: at most one can be served
    misses = [r for r in rows if r["hit"] == 0]
    assert misses
    for miss in misses:
        assert miss["miss_class"] == "PAGINATION_CUT" and miss["fused_rank"] > 1


def test_record_holds_no_query_text_or_content(tmp_path, vault_and_ids):
    vault, ids = vault_and_ids
    bench = _freeze(tmp_path / "bench.json", _cases(ids))
    out = vault / "08_OBSERVABILITY" / "active_knowledge_engine"
    record = AKEShadowRunner(_controller(vault), output_dir=out, vault_root=vault,
                             benchmark_path=bench).run(trigger="manual")
    assert record["status"] == JobStatus.COMPLETED.value
    stage = next(s for s in record["stages"] if s["name"] == "retrieval_quality")
    assert stage["status"] == "COMPLETED"
    assert stage["output"]["benchmark"]["digest"] == _digest(bench)
    raw = (out / f"{record['job_id']}.json").read_text(encoding="utf-8")
    for leaked in ("orchid budget graph", "quantum spaghetti", "rotates keys", "moon made of"):
        assert leaked not in raw
    assert record["config"]["benchmark_sha256"] == _digest(bench)


def test_tampered_benchmark_fails_the_job(tmp_path, vault_and_ids):
    vault, ids = vault_and_ids
    bench = _freeze(tmp_path / "bench.json", _cases(ids))
    bench.write_text(bench.read_text(encoding="utf-8").replace("orchid", "lotus"), encoding="utf-8")
    out = vault / "08_OBSERVABILITY" / "active_knowledge_engine"
    record = AKEShadowRunner(_controller(vault), output_dir=out, vault_root=vault,
                             benchmark_path=bench).run(trigger="manual")
    assert record["status"] == JobStatus.FAILED.value
    stage = next(s for s in record["stages"] if s["name"] == "retrieval_quality")
    assert stage["status"] == "FAILED" and stage["error"].startswith("BenchmarkIntegrityError")


def test_retrieval_stage_never_writes_a_note(tmp_path, vault_and_ids):
    vault, ids = vault_and_ids
    bench = _freeze(tmp_path / "bench.json", _cases(ids))

    def tree():
        return {p.relative_to(vault).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(vault.rglob("*.md"))}

    before = tree()
    AKEShadowRunner(_controller(vault), output_dir=vault / "08_OBSERVABILITY" / "ake", vault_root=vault,
                    benchmark_path=bench).run(trigger="manual")
    assert tree() == before


# --- compare -------------------------------------------------------------------------------

def _record(hits: dict, digest="d" * 64, k=5, principal="AI_AGENT", job="ake-x"):
    cases = [{"id": cid, "class": "c", "hit": h} for cid, h in hits.items()]
    return {"job_id": job, "status": "COMPLETED", "stages": [{
        "name": "retrieval_quality", "status": "COMPLETED",
        "output": {"benchmark": {"digest": digest}, "cases": cases,
                   "summary": {"k": k, "principal": principal}}}]}


def test_compare_refuses_different_benchmarks_or_operating_points():
    a = _record({"1": 1})
    assert compare_records(a, _record({"1": 1}, digest="e" * 64))["verdict"] == "NOT_COMPARABLE"
    assert compare_records(a, _record({"1": 1}, k=10))["verdict"] == "NOT_COMPARABLE"
    assert compare_records(a, _record({"1": 1}, principal="HUMAN"))["verdict"] == "NOT_COMPARABLE"
    failed = _record({"1": 1})
    failed["status"] = "FAILED"
    assert compare_records(a, failed)["verdict"] == "NOT_COMPARABLE"


def test_compare_paired_exact_test():
    same = compare_records(_record({str(i): 1 for i in range(10)}), _record({str(i): 1 for i in range(10)}))
    assert same["verdict"] == "NO_CHANGE" and same["discordant"] == 0
    base = {str(i): 0 for i in range(12)}
    better = {str(i): 1 for i in range(12)}
    r = compare_records(_record(base), _record(better))
    assert r["candidate_only"] == 12 and r["baseline_only"] == 0
    assert r["verdict"] == "IMPROVED" and r["p_value"] < 0.001
    worse = compare_records(_record(better), _record(base))
    assert worse["verdict"] == "REGRESSED"
    small = compare_records(_record({"1": 0, "2": 1}), _record({"1": 1, "2": 1}))
    assert small["verdict"] == "INCONCLUSIVE" and small["p_value"] == 1.0
    # abstain / unmeasurable cases (hit None) are skipped, not coerced
    r = compare_records(_record({"1": None, "2": 1}), _record({"1": 1, "2": 1}))
    assert r["skipped_unmeasurable"] == 1 and r["n_paired"] == 1


def test_cli_compare_exit_codes(tmp_path):
    import os
    import subprocess
    import sys

    base, worse = tmp_path / "b.json", tmp_path / "c.json"
    base.write_text(json.dumps(_record({str(i): 1 for i in range(12)})), encoding="utf-8")
    worse.write_text(json.dumps(_record({str(i): 0 for i in range(12)})), encoding="utf-8")
    env = {**os.environ, "PYTHONPATH": str(REPO / "03_IMPLEMENTATION" / "packages")}

    def run(a, b):
        return subprocess.run([sys.executable, "-m", "active_knowledge_engine", "compare", str(a), str(b)],
                              capture_output=True, text=True, env=env, timeout=120)

    same = run(base, base)
    assert same.returncode == 0 and json.loads(same.stdout)["verdict"] == "NO_CHANGE"
    reg = run(base, worse)
    assert reg.returncode == 4 and json.loads(reg.stdout)["verdict"] == "REGRESSED"
    other = tmp_path / "o.json"
    other.write_text(json.dumps(_record({"1": 1}, k=10)), encoding="utf-8")
    assert run(base, other).returncode == 2
