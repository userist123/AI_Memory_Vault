"""AKE Milestone 1: the shadow runner is read-only, idempotent and fully recorded.

Contract (07_EVALUATION/active_knowledge_engine/REPOSITORY_REALITY_AUDIT.md, alternative A):
- one job = one immutable snapshot of the eligible notes + one config; its id is derived from both;
- stages reuse what is in production (SleepConsolidator, ConflictDetector) and wire
  ConflictReviewWorkflow; nothing is proposed, promoted, attested or written to a note;
- every run leaves a JSON job record outside the canonical content tree, with hashes;
- a second run on the same snapshot/config/day returns the existing record untouched;
- a failing stage or a snapshot that changes during the run is recorded as such, never COMPLETED.
"""
from __future__ import annotations

import ast
import hashlib
import json
import subprocess
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest

from active_knowledge_engine.runner import AKEShadowRunner, JobStatus, OutputLocationError

REPO = Path(__file__).resolve().parents[1]
PKG = REPO / "03_IMPLEMENTATION" / "packages" / "active_knowledge_engine"


def _days_ago(days: int) -> str:
    return (datetime.now(timezone.utc) - timedelta(days=days)).date().isoformat()


class _Storage:
    def __init__(self, notes):
        self.store = {n["id"]: n for n in notes}


class _ReadOnlyController:
    """Any mutating call is a test failure: the runner must never reach the write boundary."""

    def __init__(self, notes):
        self.storage = _Storage(notes)

    def __getattr__(self, name):
        if name in {"propose", "update", "archive", "supersede", "attest", "review", "promote", "delete"}:
            raise AssertionError(f"shadow runner called controller.{name}()")
        raise AttributeError(name)


def _notes():
    return [
        {"id": "a", "lifecycle": "ACTIVE", "category": "architecture",
         "content": "folosim SQLite WAL pentru index", "updated": _days_ago(1)},
        {"id": "b", "lifecycle": "ACTIVE", "category": "architecture",
         "content": "nu folosim SQLite WAL pentru index", "updated": _days_ago(1)},
        {"id": "old", "lifecycle": "ACTIVE", "category": "x", "content": "foo", "updated": _days_ago(200)},
        {"id": "rev", "lifecycle": "REVIEW", "category": "x", "content": "bar", "updated": _days_ago(40)},
        {"id": "raw", "lifecycle": "RAW", "category": "x", "content": "ignored", "updated": _days_ago(1)},
    ]


@pytest.fixture
def out_dir(tmp_path):
    return tmp_path / "08_OBSERVABILITY" / "active_knowledge_engine"


def _runner(notes, out_dir, **kw):
    return AKEShadowRunner(_ReadOnlyController(notes), output_dir=out_dir, vault_root=out_dir.parents[1], **kw)


def test_job_record_is_complete_and_hashes_its_artifact(out_dir):
    record = _runner(_notes(), out_dir).run(trigger="manual")
    assert record["status"] == JobStatus.COMPLETED.value
    assert record["schema_version"] == "ake.job/1"
    for key in ("job_id", "idempotency_key", "trigger", "as_of_utc_date", "created_at_utc",
                "source_snapshot_sha256", "agent_config_sha256", "config", "stages", "budget", "limitations"):
        assert key in record, key
    assert record["mode"] == "SHADOW_READ_ONLY"
    assert record["canonical_writes"] == 0 and record["proposals_created"] == 0
    stages = {s["name"]: s for s in record["stages"]}
    assert set(stages) == {"integrity", "conflict_review"}
    assert all(s["status"] == "COMPLETED" for s in stages.values())
    path = out_dir / f"{record['job_id']}.json"
    assert path.is_file()
    on_disk = json.loads(path.read_text(encoding="utf-8"))
    body = dict(on_disk)
    digest = body.pop("record_sha256")
    canonical = json.dumps(body, sort_keys=True, ensure_ascii=False, separators=(",", ":")).encode()
    assert digest == hashlib.sha256(canonical).hexdigest()


def test_snapshot_covers_eligible_notes_only_and_tracks_content(out_dir):
    r1 = _runner(_notes(), out_dir).run(trigger="manual")
    changed_raw = _notes()
    changed_raw[4]["content"] = "RAW notes are not in scope"
    r2 = _runner(changed_raw, out_dir).run(trigger="manual")
    assert r2["source_snapshot_sha256"] == r1["source_snapshot_sha256"]  # RAW is out of scope
    changed_active = _notes()
    changed_active[0]["content"] = "folosim Postgres"
    r3 = _runner(changed_active, out_dir).run(trigger="manual")
    assert r3["source_snapshot_sha256"] != r1["source_snapshot_sha256"]
    assert r3["job_id"] != r1["job_id"]
    assert r1["snapshot"]["note_count"] == 4


def test_same_inputs_reuse_the_existing_record_without_rewriting(out_dir):
    first = _runner(_notes(), out_dir).run(trigger="manual")
    path = out_dir / f"{first['job_id']}.json"
    before = path.read_bytes()
    second = _runner(_notes(), out_dir).run(trigger="manual")
    assert second["job_id"] == first["job_id"]
    assert second["reused_existing_record"] is True
    assert path.read_bytes() == before
    assert len(list(out_dir.glob("*.json"))) == 1


def test_config_change_is_a_different_job(out_dir):
    a = _runner(_notes(), out_dir).run(trigger="manual")
    b = _runner(_notes(), out_dir, dormant_days=30).run(trigger="manual")
    assert a["agent_config_sha256"] != b["agent_config_sha256"]
    assert a["job_id"] != b["job_id"]


def test_contradictions_open_review_cases_and_resolve_nothing(out_dir):
    record = _runner(_notes(), out_dir).run(trigger="manual")
    stage = next(s for s in record["stages"] if s["name"] == "conflict_review")
    cases = stage["output"]["cases"]
    assert cases, "the planted contradiction must open a case"
    case = cases[0]
    assert case["case_id"].startswith("CR-")
    assert case["memory_ids"] == ["a", "b"]
    assert case["status"] == "OPEN"
    assert case["recommendation"] == "VERIFY_WITH_EVIDENCE"
    assert case["conflict_type"] == "contradiction"


def test_integrity_stage_reports_consolidation_findings(out_dir):
    record = _runner(_notes(), out_dir).run(trigger="manual")
    out = next(s for s in record["stages"] if s["name"] == "integrity")["output"]
    assert {c["id"] for c in out["dormant_candidates"]} == {"old"}
    assert {c["id"] for c in out["stale_review_candidates"]} == {"rev"}


def test_budget_cap_is_recorded(out_dir):
    record = _runner(_notes(), out_dir, max_items=2).run(trigger="manual")
    assert record["budget"]["max_items"] == 2
    out = next(s for s in record["stages"] if s["name"] == "integrity")["output"]
    assert out["stats"]["processed_notes"] == 2
    assert out["stats"]["budget_exhausted"] is True


def test_failing_stage_is_recorded_as_failed(out_dir, monkeypatch):
    import active_knowledge_engine.runner as runner_mod

    def boom(self):
        raise RuntimeError("detector exploded")

    monkeypatch.setattr(runner_mod.SleepConsolidator, "run", boom)
    record = _runner(_notes(), out_dir).run(trigger="manual")
    assert record["status"] == JobStatus.FAILED.value
    integrity = next(s for s in record["stages"] if s["name"] == "integrity")
    assert integrity["status"] == "FAILED"
    assert "RuntimeError" in integrity["error"]
    assert (out_dir / f"{record['job_id']}.json").is_file()
    # a failed job is not reused: the next run tries again
    monkeypatch.undo()
    again = _runner(_notes(), out_dir).run(trigger="manual")
    assert again["status"] == JobStatus.COMPLETED.value


def test_snapshot_changing_during_the_run_fails_closed(out_dir, monkeypatch):
    import active_knowledge_engine.runner as runner_mod

    notes = _notes()
    controller = _ReadOnlyController(notes)
    original = runner_mod.SleepConsolidator.run

    def mutate_then_run(self):
        controller.storage.store["a"]["content"] = "changed under our feet"
        return original(self)

    monkeypatch.setattr(runner_mod.SleepConsolidator, "run", mutate_then_run)
    runner = AKEShadowRunner(controller, output_dir=out_dir, vault_root=out_dir.parents[1])
    record = runner.run(trigger="manual")
    assert record["status"] == JobStatus.STALE_SNAPSHOT.value


@pytest.mark.parametrize("bad", ["01_ARCHITECTURE/knowledge", "00_GOVERNANCE", "."])
def test_output_inside_the_canonical_tree_is_refused(tmp_path, bad):
    vault = tmp_path / "vault"
    vault.mkdir()
    with pytest.raises(OutputLocationError):
        AKEShadowRunner(_ReadOnlyController(_notes()), output_dir=vault / bad, vault_root=vault)


def test_unknown_trigger_is_refused(out_dir):
    with pytest.raises(ValueError):
        _runner(_notes(), out_dir).run(trigger="auto_promote")


def test_runner_on_a_real_file_vault_changes_no_note(tmp_path):
    from memory_vault_fixture import make_vault, note_text
    from memory_controller.controller import MemoryController
    from memory_controller.storage.file_engine import FileStorageEngine

    vault = make_vault(tmp_path)
    k = vault / "01_ARCHITECTURE" / "knowledge"
    (k / "c1.md").write_text(note_text("cache", "folosim cache local pentru index", "11111111-1111-4111-8111-111111111111"), encoding="utf-8")
    (k / "c2.md").write_text(note_text("cache", "nu folosim cache local pentru index", "22222222-2222-4222-8222-222222222222"), encoding="utf-8")

    def tree():
        return {p.relative_to(vault).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in sorted(vault.rglob("*.md"))}

    before = tree()
    controller = MemoryController(storage=FileStorageEngine(str(vault)))
    out = vault / "08_OBSERVABILITY" / "active_knowledge_engine"
    record = AKEShadowRunner(controller, output_dir=out, vault_root=vault).run(trigger="manual")
    assert record["status"] == JobStatus.COMPLETED.value
    assert tree() == before
    cases = next(s for s in record["stages"] if s["name"] == "conflict_review")["output"]["cases"]
    assert any(set(c["memory_ids"]) == {"11111111-1111-4111-8111-111111111111",
                                         "22222222-2222-4222-8222-222222222222"} for c in cases)


_FORBIDDEN_IMPORTS = {"socket", "requests", "httpx", "urllib", "aiohttp", "http",
                      "proposal_queue", "queue_promoter", "closed_loop_learning", "learning_promotion_gate"}
_FORBIDDEN_CALLS = {"propose", "update", "archive", "supersede", "attest", "promote", "enqueue", "mark", "set"}


def test_package_has_no_network_and_no_write_path():
    for path in PKG.glob("*.py"):
        tree = ast.parse(path.read_text(encoding="utf-8"))
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                names = {a.name.split(".")[0] for a in node.names} | {a.name.split(".")[-1] for a in node.names}
                assert not names & _FORBIDDEN_IMPORTS, (path.name, names)
            elif isinstance(node, ast.ImportFrom):
                mod = node.module or ""
                parts = set(mod.split("."))
                assert not parts & _FORBIDDEN_IMPORTS, (path.name, mod)
            elif isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute):
                owner = node.func.value
                if isinstance(owner, ast.Attribute) and owner.attr in {"controller", "storage"} or \
                        isinstance(owner, ast.Name) and owner.id in {"controller", "storage"}:
                    assert node.func.attr not in _FORBIDDEN_CALLS, (path.name, node.func.attr)


def test_cli_dry_run_writes_a_record(tmp_path):
    from memory_vault_fixture import make_vault

    vault = make_vault(tmp_path)
    proc = subprocess.run(
        [sys.executable, "-m", "active_knowledge_engine", "run", "--dry-run", "--vault-root", str(vault)],
        cwd=REPO, capture_output=True, text=True, timeout=300,
        env={**__import__("os").environ, "PYTHONPATH": str(REPO / "03_IMPLEMENTATION" / "packages")},
    )
    assert proc.returncode == 0, proc.stderr
    line = [l for l in proc.stdout.splitlines() if l.startswith("record=")]
    assert line, proc.stdout
    record_path = Path(line[0].split("=", 1)[1])
    assert record_path.is_file()
    assert record_path.parent == vault / "08_OBSERVABILITY" / "active_knowledge_engine"
    assert json.loads(record_path.read_text(encoding="utf-8"))["status"] == "COMPLETED"


def test_cli_refuses_without_dry_run(tmp_path):
    from memory_vault_fixture import make_vault

    vault = make_vault(tmp_path)
    proc = subprocess.run(
        [sys.executable, "-m", "active_knowledge_engine", "run", "--vault-root", str(vault)],
        cwd=REPO, capture_output=True, text=True, timeout=300,
        env={**__import__("os").environ, "PYTHONPATH": str(REPO / "03_IMPLEMENTATION" / "packages")},
    )
    assert proc.returncode == 2
    assert "dry-run" in proc.stderr


# --- review round 1 (vault-reviewer): snapshot scope, record validation, skips, side effects ---

def test_any_field_the_stages_read_changes_the_job(out_dir):
    first = _runner(_notes(), out_dir).run(trigger="manual")
    recategorised = _notes()
    recategorised[1]["category"] = "elsewhere"  # the contradiction no longer pairs
    second = _runner(recategorised, out_dir).run(trigger="manual")
    assert second["job_id"] != first["job_id"]
    assert second["reused_existing_record"] is False
    cases = next(s for s in second["stages"] if s["name"] == "conflict_review")["output"]["cases"]
    assert not any(c["memory_ids"] == ["a", "b"] for c in cases)


@pytest.mark.parametrize("payload", ["{not json", "[1, 2]", '{"status": "COMPLETED"}'])
def test_corrupt_or_unverifiable_record_is_kept_aside_and_the_job_reruns(out_dir, payload):
    first = _runner(_notes(), out_dir).run(trigger="manual")
    path = out_dir / f"{first['job_id']}.json"
    path.write_text(payload, encoding="utf-8")
    again = _runner(_notes(), out_dir).run(trigger="manual")
    assert again["reused_existing_record"] is False
    assert again["status"] == JobStatus.COMPLETED.value
    kept = list(out_dir.glob(f"{first['job_id']}.invalid*.json"))
    assert len(kept) == 1 and kept[0].read_text(encoding="utf-8") == payload


def test_tampered_completed_record_is_not_reused(out_dir):
    first = _runner(_notes(), out_dir).run(trigger="manual")
    path = out_dir / f"{first['job_id']}.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    data["stages"][1]["output"]["cases"] = []
    path.write_text(json.dumps(data), encoding="utf-8")
    again = _runner(_notes(), out_dir).run(trigger="manual")
    assert again["reused_existing_record"] is False


def test_previous_attempts_are_never_overwritten(out_dir, monkeypatch):
    import active_knowledge_engine.runner as runner_mod

    monkeypatch.setattr(runner_mod.SleepConsolidator, "run", lambda self: (_ for _ in ()).throw(RuntimeError("x")))
    first = _runner(_notes(), out_dir).run(trigger="manual")
    stray = out_dir / f"{first['job_id']}.attempt1.json"
    stray.write_text("pre-existing", encoding="utf-8")
    _runner(_notes(), out_dir).run(trigger="manual")
    _runner(_notes(), out_dir).run(trigger="manual")
    assert stray.read_text(encoding="utf-8") == "pre-existing"
    assert len(list(out_dir.glob(f"{first['job_id']}.attempt*.json"))) == 3


def test_stage_errors_carry_no_message_text(out_dir, monkeypatch):
    import active_knowledge_engine.runner as runner_mod

    def leak(self):
        raise RuntimeError("SECRET-NOTE-BODY")

    monkeypatch.setattr(runner_mod.SleepConsolidator, "run", leak)
    record = _runner(_notes(), out_dir).run(trigger="manual")
    raw = (out_dir / f"{record['job_id']}.json").read_text(encoding="utf-8")
    assert "SECRET-NOTE-BODY" not in raw
    assert record["stages"][0]["error"].startswith("RuntimeError")


def test_skipped_note_files_degrade_the_run_and_are_listed(tmp_path):
    from memory_vault_fixture import make_vault
    from memory_controller.controller import MemoryController
    from memory_controller.storage.file_engine import FileStorageEngine

    vault = make_vault(tmp_path)
    (vault / "01_ARCHITECTURE" / "knowledge" / "bad.md").write_text(
        "---\nid: [unclosed\nSECRET-BODY\n---\nx\n", encoding="utf-8")
    controller = MemoryController(storage=FileStorageEngine(str(vault)))
    out = vault / "08_OBSERVABILITY" / "active_knowledge_engine"
    record = AKEShadowRunner(controller, output_dir=out, vault_root=vault).run(trigger="manual")
    assert record["status"] == JobStatus.COMPLETED_WITH_SKIPS.value
    assert record["storage"]["skipped_count"] == 1
    assert record["storage"]["skipped_paths"] == ["01_ARCHITECTURE/knowledge/bad.md"]
    assert "SECRET-BODY" not in (out / f"{record['job_id']}.json").read_text(encoding="utf-8")


def test_cli_leaves_no_audit_log_in_the_working_directory(tmp_path):
    import os
    from memory_vault_fixture import make_vault

    vault = make_vault(tmp_path)
    # a skipped file makes the storage engine emit an audit event, which is what lands in the CWD
    (vault / "01_ARCHITECTURE" / "knowledge" / "bad.md").write_text("---\nid: [unclosed\n---\nx\n", encoding="utf-8")
    cwd = tmp_path / "elsewhere"
    cwd.mkdir()
    env = {**os.environ, "PYTHONPATH": str(REPO / "03_IMPLEMENTATION" / "packages"),
           "AI_MEMORY_VAULT_HOME": str(tmp_path / "state")}
    env.pop("ANTIGRAVITY_ARTIFACT_DIR", None)
    env.pop("ANTIGRAVITY_TELEMETRY_DIR", None)
    proc = subprocess.run([sys.executable, "-m", "active_knowledge_engine", "run", "--dry-run",
                           "--vault-root", str(vault)], cwd=cwd, capture_output=True, text=True, timeout=300, env=env)
    assert proc.returncode == 3, (proc.returncode, proc.stderr)  # 3 = COMPLETED_WITH_SKIPS
    assert "skipped=1" in proc.stdout
    assert list(cwd.iterdir()) == []


def test_attempt_numbers_never_repeat_across_invalid_records(out_dir, monkeypatch):
    import active_knowledge_engine.runner as runner_mod

    monkeypatch.setattr(runner_mod.SleepConsolidator, "run", lambda self: (_ for _ in ()).throw(RuntimeError("x")))
    seen = []
    for i in range(4):
        record = _runner(_notes(), out_dir).run(trigger="manual")
        seen.append(record["attempt"])
        if i == 2:
            (out_dir / f"{record['job_id']}.json").write_text("{corrupt", encoding="utf-8")
    assert seen == [1, 2, 3, 4]
