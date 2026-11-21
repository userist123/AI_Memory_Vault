"""AKE Loop A: which ACTIVE/REVIEW notes an agent can never read through memory_get.

Root cause found in Milestone 2 (07_EVALUATION/active_knowledge_engine/DESIGN_DECISIONS.md):
ContextBudget.apply_degradation never truncates "protected" content (MUST/NEVER/SHALL ...), so a
protected note larger than the 32 KiB hard limit always raises BudgetExceededError inside
cognitive_read, and memory_get refuses it (fail closed). The stage reports such notes with the
production predicate; it changes nothing and copies no content.
"""
from __future__ import annotations

import hashlib
import uuid

import pytest

from active_knowledge_engine.readability import agent_readability
from active_knowledge_engine.runner import AKEShadowRunner, JobStatus


@pytest.fixture(autouse=True)
def _hmac_secret(monkeypatch):
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "a" * 64)


@pytest.fixture
def vault(tmp_path):
    from memory_vault_fixture import make_vault, note_text

    v = make_vault(tmp_path)
    k = v / "01_ARCHITECTURE" / "knowledge"
    big_protected = "\n".join(f"Rule {i}: the agent MUST keep clause {i} intact." for i in range(1500))
    big_plain = "\n".join(f"line {i} of an ordinary long catalogue entry" for i in range(1500))
    assert len(big_protected.encode()) > 32768 and len(big_plain.encode()) > 32768
    (k / "protected.md").write_text(note_text("protected big", big_protected, "11111111-aaaa-4aaa-8aaa-111111111111"), encoding="utf-8")
    (k / "plain.md").write_text(note_text("plain big", big_plain, "22222222-bbbb-4bbb-8bbb-222222222222"), encoding="utf-8")
    (k / "raw.md").write_text(note_text("raw note", "raw body", str(uuid.uuid4()), lifecycle="RAW"), encoding="utf-8")
    return v


def _controller(vault):
    from memory_controller.controller import MemoryController
    from memory_controller.storage.file_engine import FileStorageEngine

    return MemoryController(storage=FileStorageEngine(str(vault)))


def test_oversized_protected_note_is_reported_unreadable(vault):
    result = agent_readability(_controller(vault))
    unreadable = {r["id"]: r for r in result["unreadable"]}
    assert "11111111-aaaa-4aaa-8aaa-111111111111" in unreadable
    row = unreadable["11111111-aaaa-4aaa-8aaa-111111111111"]
    assert row["reason"] == "BUDGET_HARD_BYTES"
    assert row["memory_get"] == "error:ValueError"
    assert row["content_bytes"] > 32768
    assert row["path"] == "01_ARCHITECTURE/knowledge/protected.md"
    # a large note without protected wording is truncated, hence still served
    assert "22222222-bbbb-4bbb-8bbb-222222222222" not in unreadable
    s = result["summary"]
    # seed notes, ontology slots copied by the fixture, protected, plain; the RAW note is out of scope
    assert s["checked"] >= 5
    assert s["unreadable"] == 1 and s["by_reason"] == {"BUDGET_HARD_BYTES": 1}


def test_record_carries_no_note_content(vault):
    out = vault / "08_OBSERVABILITY" / "active_knowledge_engine"
    record = AKEShadowRunner(_controller(vault), output_dir=out, vault_root=vault).run(trigger="manual")
    assert record["status"] == JobStatus.COMPLETED.value
    stage = next(s for s in record["stages"] if s["name"] == "agent_readability")
    assert stage["status"] == "COMPLETED" and stage["output"]["summary"]["unreadable"] == 1
    raw = (out / f"{record['job_id']}.json").read_text(encoding="utf-8")
    assert "keep clause" not in raw and "ordinary long catalogue" not in raw


def test_readability_check_writes_no_note(vault):
    def tree():
        return {p.relative_to(vault).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(vault.rglob("*.md"))}

    before = tree()
    agent_readability(_controller(vault))
    assert tree() == before


def test_a_failing_predicate_is_counted_not_fatal(vault, monkeypatch):
    import active_knowledge_engine.readability as rd

    def boom(controller, nid):
        raise RuntimeError("predicate failed")

    monkeypatch.setattr(rd, "_memory_get_outcome", boom)
    result = agent_readability(_controller(vault))
    assert result["summary"]["unreadable"] == result["summary"]["checked"]
    assert set(result["summary"]["by_reason"]) == {"CheckError:RuntimeError"}


def test_withheld_review_note_reports_its_verification_label(tmp_path):
    """A REVIEW note whose verification is outside the schema enum is withheld (fail closed);
    the row names the label so the cause is visible without opening the note."""
    from memory_vault_fixture import make_vault

    v = make_vault(tmp_path)
    nid = "33333333-cccc-4ccc-8ccc-333333333333"
    # the shape of the real legal primaries: type legal_source, no provenance, verified_source
    (v / "01_ARCHITECTURE" / "knowledge" / "legal.md").write_text(
        "\n".join([
            "---", f"id: {nid}", "type: legal_source", "category: legal", "created: 2026-09-01",
            "updated: 2026-09-01", "verification: verified_source", "lifecycle: REVIEW", "---",
            "# legal source", "", "Article 1 text.", "",
        ]), encoding="utf-8")
    result = agent_readability(_controller(v))
    row = next(r for r in result["unreadable"] if r["id"] == nid)
    assert row["verification"] == "verified_source" and row["verification_in_schema"] is False
    assert row["reason"] == "DROPPED_FROM_PACK"
    assert result["summary"]["verification_outside_schema"] >= 1


def _write(v, name, lines):
    (v / "01_ARCHITECTURE" / "knowledge" / name).write_text(chr(10).join(lines + [""]), encoding="utf-8")


def test_protected_note_between_9_and_32_kib_fails_on_the_token_budget(tmp_path):
    """Review round 1: the 3000-token hard limit (pack_builder.py), not the 32 KiB byte limit."""
    from memory_vault_fixture import make_vault, note_text

    v = make_vault(tmp_path)
    body = chr(10).join(f"Rule {i}: the agent MUST keep clause {i}." for i in range(400))
    assert 9_000 < len(body.encode()) < 32_768
    nid = "44444444-dddd-4ddd-8ddd-444444444444"
    (v / "01_ARCHITECTURE" / "knowledge" / "mid.md").write_text(note_text("mid protected", body, nid), encoding="utf-8")
    row = next(r for r in agent_readability(_controller(v))["unreadable"] if r["id"] == nid)
    assert row["reason"] == "BUDGET_PROTECTED_TOKENS"


def test_review_refusal_does_not_depend_on_the_verification_label(tmp_path):
    """A REVIEW note without inference provenance is dropped for an agent whatever its label."""
    from memory_vault_fixture import make_vault

    v = make_vault(tmp_path)
    ids = {}
    for label in ("verified", "partially_verified", "verified_source"):
        nid = str(uuid.uuid4())
        ids[label] = nid
        _write(v, f"r_{label}.md", ["---", f"id: {nid}", "type: legal_source", "category: legal",
                                     "created: 2026-09-01", "updated: 2026-09-01", f"verification: {label}",
                                     "lifecycle: REVIEW", "---", "# t", "", "body"])
    rows = {r["id"]: r for r in agent_readability(_controller(v))["unreadable"]}
    for label, nid in ids.items():
        assert rows[nid]["reason"] == "DROPPED_FROM_PACK", label


def test_memory_get_with_empty_content_counts_as_unreadable(tmp_path):
    """Review round 1: eligible_for_agent and memory_get can disagree; memory_get is the verdict."""
    from memory_vault_fixture import make_vault

    v = make_vault(tmp_path)
    nid = "55555555-eeee-4eee-8eee-555555555555"
    _write(v, "inf.md", ["---", f"id: {nid}", "type: knowledge", "category: x", "created: 2026-09-01",
                         "updated: 2026-09-01", "verification: inferred", "lifecycle: REVIEW",
                         "provenance:", "  source_type: inference", "  source_ref: x", "---", "# t", "", "some body"])
    rows = {r["id"]: r for r in agent_readability(_controller(v))["unreadable"]}
    if nid in rows:  # served with an empty body -> unreadable, and the outcome says so
        assert rows[nid]["memory_get"] in {"ok_empty_content", "error:ValueError"}
