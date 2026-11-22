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
    assert s["n_scorable"] == 2  # gold visible to storage and agent-eligible
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


def test_miss_classes_with_a_controlled_search(monkeypatch):
    import active_knowledge_engine.retrieval_quality as rq

    monkeypatch.setattr(rq, "agent_policy_eligible", lambda controller, nid: True)
    monkeypatch.setattr(rq, "agent_readable", lambda controller, nid: True)
    case = lambda gold: {"id": gold, "class": "c", "query": "q", "gold_relevant_notes": [gold], "abstain": False}
    ctl = _ScriptedController(served=["a", "b"], fused=["a", "f", "b", "x", "y", "late"], known={"a", "b", "f", "late", "never"})
    rows = {r["id"]: r for r in evaluate(ctl, [case("a"), case("f"), case("late"), case("never"), case("ghost")], k=5)["cases"]}
    assert rows["a"]["hit"] == 1 and rows["a"]["rank_in_page"] == 1
    assert rows["f"]["miss_class"] == "REORDERED_OUT" and rows["f"]["fused_rank"] == 2
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


# --- review round 1 (Milestone 2) -------------------------------------------------------------

def test_controller_settings_are_part_of_the_job(tmp_path, vault_and_ids):
    """A candidate run (other ranking arm, other code) must never get the baseline record back."""
    vault, ids = vault_and_ids
    bench = _freeze(tmp_path / "bench.json", _cases(ids))
    out = vault / "08_OBSERVABILITY" / "ake"
    first = AKEShadowRunner(_controller(vault), output_dir=out, vault_root=vault, benchmark_path=bench).run(trigger="manual")
    other = _controller(vault)
    other.ranking_arm = "some_other_arm"
    second = AKEShadowRunner(other, output_dir=out, vault_root=vault, benchmark_path=bench).run(trigger="manual")
    assert second["job_id"] != first["job_id"] and second["reused_existing_record"] is False
    assert first["config"]["code_fingerprint"]["sha256"]
    assert first["config"]["controller"]["ranking_arm"] != second["config"]["controller"]["ranking_arm"]


def test_code_fingerprint_changes_with_the_controller_source(tmp_path, vault_and_ids, monkeypatch):
    import active_knowledge_engine.runner as runner_mod

    vault, ids = vault_and_ids
    runner = AKEShadowRunner(_controller(vault), output_dir=vault / "08_OBSERVABILITY" / "ake", vault_root=vault)
    a = runner_mod.code_fingerprint(runner.controller)
    fake_src = tmp_path / "patched_controller.py"
    fake_src.write_text("# changed\n", encoding="utf-8")
    monkeypatch.setattr(runner_mod.inspect, "getsourcefile", lambda obj: str(fake_src))
    b = runner_mod.code_fingerprint(runner.controller)
    assert a["sha256"] != b["sha256"]


def test_sidecar_is_part_of_the_job(tmp_path, vault_and_ids):
    vault, ids = vault_and_ids
    bench = _freeze(tmp_path / "bench.json", _cases(ids))
    out = vault / "08_OBSERVABILITY" / "ake"
    first = AKEShadowRunner(_controller(vault), output_dir=out, vault_root=vault, benchmark_path=bench).run(trigger="manual")
    bench.with_suffix(".json.sha256").write_text("0" * 64 + "\n", encoding="utf-8")
    second = AKEShadowRunner(_controller(vault), output_dir=out, vault_root=vault, benchmark_path=bench).run(trigger="manual")
    assert second["job_id"] != first["job_id"]
    assert second["status"] == JobStatus.FAILED.value


def test_k_is_clamped_like_memory_search():
    ctl = _ScriptedController(served=["a"], fused=["a"], known={"a"})
    case = {"id": "a", "class": "c", "query": "q", "gold_relevant_notes": ["a"], "abstain": False}
    assert evaluate(ctl, [case], k=50)["summary"]["k"] == 20
    assert set(ctl.calls) == {("AI_AGENT", 20)}


def test_policy_exclusion_and_candidate_limit_are_their_own_classes(monkeypatch):
    import active_knowledge_engine.retrieval_quality as rq

    case = lambda gold: {"id": gold, "class": "c", "query": "q", "gold_relevant_notes": [gold], "abstain": False}
    ctl = _ScriptedController(served=["a"], fused=["a"], known={"a", "nolife", "cut", "never"})
    ctl.cut = [{"id": "cut"}]
    original = ctl.search

    def search(principal, query, page_size=10, **kw):
        pack = original(principal, query, page_size=page_size)
        pack["candidate_trace"]["cut_candidates"] = ctl.cut
        return pack

    ctl.search = search
    monkeypatch.setattr(rq, "agent_policy_eligible", lambda controller, nid: nid != "nolife")
    monkeypatch.setattr(rq, "agent_readable", lambda controller, nid: nid != "nolife")
    rows = {r["id"]: r for r in evaluate(ctl, [case("nolife"), case("cut"), case("never")], k=5)["cases"]}
    assert rows["nolife"]["miss_class"] == "GOLD_NOT_AGENT_ELIGIBLE"
    assert rows["cut"]["miss_class"] == "CANDIDATE_LIMIT_CUT"
    assert rows["never"]["miss_class"] == "NEVER_CANDIDATE"


def test_real_eligibility_predicate_rejects_a_note_without_lifecycle(tmp_path):
    from memory_vault_fixture import make_vault

    vault = make_vault(tmp_path)
    nid = str(uuid.uuid4())
    (vault / "01_ARCHITECTURE" / "knowledge" / "core.md").write_text(
        f"---\nid: {nid}\ntype: core\ncategory: x\n---\n# core\n\nzebra protocol core body\n", encoding="utf-8")
    ctl = _controller(vault)
    case = {"id": "Z", "class": "c", "query": "zebra protocol core", "gold_relevant_notes": [nid], "abstain": False}
    row = evaluate(ctl, [case], k=5)["cases"][0]
    assert row["hit"] == 0 and row["miss_class"] == "GOLD_NOT_AGENT_ELIGIBLE"


def test_a_ranked_gold_is_classed_by_rank_even_if_memory_get_refuses_it(monkeypatch):
    """Readability is reported per case, it never relabels a candidate as a policy exclusion."""
    import active_knowledge_engine.retrieval_quality as rq

    monkeypatch.setattr(rq, "agent_policy_eligible", lambda controller, nid: True)
    monkeypatch.setattr(rq, "agent_readable", lambda controller, nid: False)
    ctl = _ScriptedController(served=["a"], fused=["a", "b", "c", "d", "e", "late"], known={"a", "late"})
    case = {"id": "late", "class": "c", "query": "q", "gold_relevant_notes": ["late"], "abstain": False}
    result = evaluate(ctl, [case], k=5)
    row = result["cases"][0]
    assert row["miss_class"] == "PAGINATION_CUT" and row["gold_readable"] is False
    assert result["summary"]["gold_unreadable_by_memory_get"] == 1


# --- review round 2 (Milestone 2) -------------------------------------------------------------

def test_fingerprint_covers_the_read_path_and_fails_on_a_missing_source(monkeypatch):
    import active_knowledge_engine.runner as runner_mod

    rel = {p.relative_to(runner_mod.PACKAGES_ROOT).as_posix() for p in runner_mod.fingerprinted_files()}
    for needed in ("security/verified_reduction.py", "security/__init__.py", "memory/storage/file_engine.py",
                   "memory/controller.py", "lifecycle/policy.py", "retrieval/context/candidate_generation.py",
                   "interfaces/memory_access.py", "interfaces/vault_cli.py", "learning/sleep_consolidation.py"):
        assert needed in rel, needed
    monkeypatch.setattr(runner_mod, "FINGERPRINTED_SOURCES", ("does/not/exist.py",))
    with pytest.raises(FileNotFoundError):
        runner_mod.fingerprinted_files()


def test_quarantine_does_not_make_a_note_policy_ineligible():
    """search() applies the lifecycle floor only; quarantine is a disclosure (memory_get) rule."""
    import active_knowledge_engine.retrieval_quality as rq

    note = {"id": "q", "lifecycle": "ACTIVE", "quarantined": True}
    ctl = type("C", (), {"storage": type("S", (), {"get": staticmethod(lambda nid: note)})()})()
    assert rq.agent_policy_eligible(ctl, "q") is True
    note["lifecycle"] = "raw"
    assert rq.agent_policy_eligible(ctl, "q") is False


def _cut_controller(raw_scores):
    ctl = _ScriptedController(served=["a"], fused=["a"], known={"a", "cut"})
    original = ctl.search

    def search(principal, query, page_size=10, **kw):
        pack = original(principal, query, page_size=page_size)
        pack["candidate_trace"]["cut_candidates"] = [{"rank": 201, "id": "cut", "fused_score": 0.001}]
        pack["candidate_trace"]["raw_scores_by_signal"] = {"cut": raw_scores}
        return pack

    ctl.search = search
    return ctl


@pytest.mark.parametrize("raw,expected", [
    ({"bm25": 0.0, "entity": 0.0}, "NO_LEXICAL_SIGNAL"),
    ({"bm25": 1.2, "entity": 0.0}, "CANDIDATE_LIMIT_CUT"),
    (None, "CANDIDATE_LIMIT_CUT"),
])
def test_a_cut_gold_with_no_signal_is_not_blamed_on_the_limit(monkeypatch, raw, expected):
    import active_knowledge_engine.retrieval_quality as rq

    monkeypatch.setattr(rq, "agent_readable", lambda controller, nid: True)
    case = {"id": "cut", "class": "c", "query": "q", "gold_relevant_notes": ["cut"], "abstain": False}
    row = evaluate(_cut_controller(raw), [case], k=5)["cases"][0]
    assert row["miss_class"] == expected
    assert row["cut_rank"] == 201
