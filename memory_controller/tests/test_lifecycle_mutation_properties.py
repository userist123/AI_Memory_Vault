"""Lifecycle mutation property tests (runtime security front, owner:
claude-code). Companion to LIFECYCLE_MUTATION_INVENTORY.md and
LIFECYCLE_SINGLE_SOURCE_OF_TRUTH.md.

Two properties are checked for every one of the six MemoryController
mutation methods (review, attest, promote, update, archive, supersede):

  P1 (invalid transition): raises -> storage state byte-for-byte unchanged.
  P2 (valid transition):   exactly one intended state change -> an
                            audit_event was recorded -> the cache was
                            invalidated (memory_updated event fired).

A third section checks cross-storage equivalence: the identical operation
sequence against StorageEngine (in-memory), FileStorageEngine, and
SQLiteStorageEngine produces the identical final lifecycle/verification,
under identical authorization outcomes.
"""
from __future__ import annotations

import copy
import uuid

import pytest

from memory_controller.authorizer import Principal
from memory_controller.controller import Lifecycle, MemoryController, StorageEngine
from memory_controller.storage.file_engine import FileStorageEngine
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine


def _note(note_id=None, lifecycle="REVIEW", verification="unverified", **overrides):
    note_id = note_id or str(uuid.uuid4())
    base = {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": "test",
        "tags": [],
        "created": "2026-01-01",
        "updated": "2026-01-01",
        "provenance": {"source_type": "user", "source_ref": "test"},
        "confidence": "high",
        "verification": verification,
        "relations": [],
        "content": "body",
    }
    base.update(overrides)
    return base


class _AuditRecorder:
    """Captures every audit_event call made during a `with` block, by
    monkeypatching the `audit_event` name in memory_controller.controller."""

    def __init__(self, monkeypatch):
        self.events = []
        self._monkeypatch = monkeypatch

    def __enter__(self):
        import memory_controller.controller as controller_module

        def fake_audit_event(operation, principal, target_id, success=True, details=None):
            self.events.append({
                "operation": operation, "principal": principal, "target_id": target_id,
                "success": success, "details": details or {},
            })

        self._monkeypatch.setattr(controller_module, "audit_event", fake_audit_event)
        return self

    def __exit__(self, *exc):
        return False

    def last_success(self, operation):
        matches = [e for e in self.events if e["operation"] == operation and e["success"]]
        return matches[-1] if matches else None


@pytest.fixture
def controller():
    return MemoryController(StorageEngine())


# =====================================================================
# P1: invalid transition -> exception -> storage byte-for-byte unchanged
# =====================================================================

class TestInvalidTransitionLeavesStorageUnchanged:
    def test_review_invalid_lifecycle_state(self, controller):
        nid = str(uuid.uuid4())
        controller.storage.set(nid, _note(nid, lifecycle="ARCHIVED"))
        before = copy.deepcopy(controller.storage.get(nid))
        with pytest.raises(ValueError):
            controller.review(Principal.HUMAN, nid, "approve")
        assert controller.storage.get(nid) == before

    def test_attest_empty_evidence(self, controller):
        nid = str(uuid.uuid4())
        controller.storage.set(nid, _note(nid))
        before = copy.deepcopy(controller.storage.get(nid))
        with pytest.raises(ValueError):
            controller.attest(Principal.HUMAN, nid, "reason", "")
        assert controller.storage.get(nid) == before

    def test_promote_unverified(self, controller):
        nid = str(uuid.uuid4())
        controller.storage.set(nid, _note(nid, lifecycle="REVIEW", verification="unverified"))
        before = copy.deepcopy(controller.storage.get(nid))
        with pytest.raises(ValueError):
            controller.promote(Principal.HUMAN, nid)
        assert controller.storage.get(nid) == before

    def test_update_ai_agent_on_active_note(self, controller):
        nid = str(uuid.uuid4())
        controller.storage.set(nid, _note(nid, lifecycle="ACTIVE", verification="verified"))
        before = copy.deepcopy(controller.storage.get(nid))
        with pytest.raises(ValueError):
            controller.update(Principal.AI_AGENT, nid, {"verification": "verified", "content": "hack"})
        assert controller.storage.get(nid) == before

    def test_archive_pre_review_lifecycle(self, controller):
        nid = str(uuid.uuid4())
        controller.storage.set(nid, _note(nid, lifecycle="RAW"))
        before = copy.deepcopy(controller.storage.get(nid))
        with pytest.raises(ValueError):
            controller.archive(Principal.HUMAN, nid, "reason")
        assert controller.storage.get(nid) == before

    def test_supersede_self(self, controller):
        nid = str(uuid.uuid4())
        controller.storage.set(nid, _note(nid, lifecycle="ACTIVE"))
        before = copy.deepcopy(controller.storage.get(nid))
        with pytest.raises(ValueError):
            controller.supersede(Principal.HUMAN, nid, nid)
        assert controller.storage.get(nid) == before

    def test_supersede_cycle(self, controller):
        a, b = str(uuid.uuid4()), str(uuid.uuid4())
        controller.storage.set(a, _note(a, lifecycle="ACTIVE"))
        controller.storage.set(b, _note(b, lifecycle="ACTIVE"))
        controller.supersede(Principal.HUMAN, a, b)  # a -> superseded by b
        before_a, before_b = copy.deepcopy(controller.storage.get(a)), copy.deepcopy(controller.storage.get(b))
        with pytest.raises(ValueError):
            controller.supersede(Principal.HUMAN, b, a)  # would create a cycle
        assert controller.storage.get(a) == before_a
        assert controller.storage.get(b) == before_b


# =====================================================================
# P2: valid transition -> exactly one state change -> audit -> cache invalidation
# =====================================================================

class TestValidTransitionSideEffects:
    def test_review_valid(self, controller, monkeypatch):
        nid = str(uuid.uuid4())
        controller.storage.set(nid, _note(nid, lifecycle="RAW"))
        controller.cache.set("stale", Principal.HUMAN, "some-query", events=["memory_updated"])
        with _AuditRecorder(monkeypatch) as rec:
            controller.review(Principal.HUMAN, nid, "approve")
        assert controller.storage.get(nid)["lifecycle"] == Lifecycle.REVIEW
        assert rec.last_success("review") is not None
        assert controller.cache.get(Principal.HUMAN, "some-query") is None  # invalidated

    def test_attest_valid(self, controller, monkeypatch):
        nid = str(uuid.uuid4())
        controller.storage.set(nid, _note(nid, verification="unverified"))
        controller.cache.set("stale", Principal.HUMAN, "q2", events=["memory_updated"])
        with _AuditRecorder(monkeypatch) as rec:
            controller.attest(Principal.HUMAN, nid, "reason", "evidence")
        assert controller.storage.get(nid)["verification"] == "verified"
        assert controller.storage.get(nid)["lifecycle"] == "REVIEW"  # unchanged -- exactly one field group changed
        ev = rec.last_success("attest")
        assert ev is not None
        assert ev["details"]["previous_verification_state"] == "unverified"
        assert ev["details"]["new_verification_state"] == "verified"
        assert controller.cache.get(Principal.HUMAN, "q2") is None

    def test_promote_valid(self, controller, monkeypatch):
        nid = str(uuid.uuid4())
        controller.storage.set(nid, _note(nid, lifecycle="REVIEW", verification="verified"))
        controller.cache.set("stale", Principal.HUMAN, "q3", events=["memory_updated"])
        with _AuditRecorder(monkeypatch) as rec:
            controller.promote(Principal.HUMAN, nid)
        assert controller.storage.get(nid)["lifecycle"] == Lifecycle.ACTIVE
        assert rec.last_success("promote") is not None
        assert controller.cache.get(Principal.HUMAN, "q3") is None

    def test_update_valid(self, controller, monkeypatch):
        nid = str(uuid.uuid4())
        controller.storage.set(nid, _note(nid, lifecycle="ACTIVE"))
        controller.cache.set("stale", Principal.HUMAN, "q4", events=["memory_updated"])
        with _AuditRecorder(monkeypatch) as rec:
            controller.update(Principal.HUMAN, nid, {"category": "updated-category"})
        assert controller.storage.get(nid)["category"] == "updated-category"
        assert controller.storage.get(nid)["lifecycle"] == "ACTIVE"  # unchanged
        assert rec.last_success("update") is not None
        assert controller.cache.get(Principal.HUMAN, "q4") is None

    def test_archive_valid(self, controller, monkeypatch):
        nid = str(uuid.uuid4())
        controller.storage.set(nid, _note(nid, lifecycle="ACTIVE"))
        controller.cache.set("stale", Principal.HUMAN, "q5", events=["memory_updated"])
        with _AuditRecorder(monkeypatch) as rec:
            controller.archive(Principal.HUMAN, nid, "reason")
        assert controller.storage.get(nid)["lifecycle"] == Lifecycle.ARCHIVED
        ev = rec.last_success("archive")
        assert ev is not None
        assert ev["details"]["previous_lifecycle"] == "ACTIVE"
        assert ev["details"]["new_lifecycle"] == "ARCHIVED"
        assert controller.cache.get(Principal.HUMAN, "q5") is None

    def test_supersede_valid(self, controller, monkeypatch):
        old_id, new_id = str(uuid.uuid4()), str(uuid.uuid4())
        controller.storage.set(old_id, _note(old_id, lifecycle="ACTIVE"))
        controller.storage.set(new_id, _note(new_id, lifecycle="ACTIVE"))
        controller.cache.set("stale", Principal.HUMAN, "q6", events=["memory_updated"])
        with _AuditRecorder(monkeypatch) as rec:
            controller.supersede(Principal.HUMAN, old_id, new_id)
        assert controller.storage.get(old_id)["lifecycle"] == Lifecycle.SUPERSEDED.value
        assert controller.storage.get(old_id)["superseded_by"] == new_id
        assert controller.storage.get(new_id)["lifecycle"] == "ACTIVE"  # new note's OWN lifecycle unchanged
        assert rec.last_success("supersede") is not None
        assert rec.last_success("archive_superseded") is not None
        assert controller.cache.get(Principal.HUMAN, "q6") is None


# =====================================================================
# Cross-storage equivalence: same operation sequence, same result, on
# StorageEngine, FileStorageEngine, SQLiteStorageEngine.
# =====================================================================

def _make_file_storage(tmp_path):
    for folder in ["00_CORE", "01_KNOWLEDGE", "02_PROJECTS", "03_PROCEDURES",
                   "04_MEMORY", "05_RESOURCES", "99_SYSTEM"]:
        (tmp_path / folder).mkdir(parents=True, exist_ok=True)
    return FileStorageEngine(str(tmp_path))


def _make_sqlite_storage(tmp_path):
    return SQLiteStorageEngine(str(tmp_path / "lifecycle_equiv.sqlite3"), wal_mode=False)


@pytest.fixture(params=["memory", "file", "sqlite"])
def storage_engine(request, tmp_path):
    if request.param == "memory":
        yield StorageEngine()
    elif request.param == "file":
        yield _make_file_storage(tmp_path)
    else:
        engine = _make_sqlite_storage(tmp_path)
        yield engine
        engine.close()


def _run_full_lifecycle_sequence(storage) -> dict:
    """propose (via a raw seed note, since propose() needs a real
    MemoryController) -> review -> attest -> promote -> archive, returning
    the observed lifecycle after each step, plus the authorization outcome
    of one deliberately-denied action (AI_AGENT attempting promote)."""
    controller = MemoryController(storage)
    nid = str(uuid.uuid4())
    controller.propose(Principal.HUMAN, _note(nid, lifecycle="RAW",
                                                provenance={"source_type": "user", "source_ref": "u"}))
    observed = {"after_propose": controller.storage.get(nid)["lifecycle"]}

    controller.review(Principal.HUMAN, nid, "approve")
    observed["after_review"] = controller.storage.get(nid)["lifecycle"]

    denied = False
    try:
        controller.promote(Principal.AI_AGENT, nid)
    except PermissionError:
        denied = True
    observed["ai_promote_denied"] = denied

    controller.attest(Principal.HUMAN, nid, "reason", "evidence")
    observed["after_attest_verification"] = controller.storage.get(nid)["verification"]

    controller.promote(Principal.HUMAN, nid)
    observed["after_promote"] = controller.storage.get(nid)["lifecycle"]

    # Archiving a human-verified ACTIVE note requires ADMIN (this pass's
    # F-02 archive-state-machine rule) -- this note is verified by the
    # attest() call above, so ADMIN is required here, not HUMAN.
    controller.archive(Principal.ADMIN, nid, "cross-storage equivalence probe")
    observed["after_archive"] = controller.storage.get(nid)["lifecycle"]
    return observed


class TestCrossStorageEquivalence:
    def test_identical_sequence_produces_identical_lifecycle_on_every_engine(self, storage_engine):
        result = _run_full_lifecycle_sequence(storage_engine)
        assert result["after_propose"] == Lifecycle.RAW.value
        assert result["after_review"] == Lifecycle.REVIEW.value
        assert result["ai_promote_denied"] is True
        assert result["after_attest_verification"] == "verified"
        assert result["after_promote"] == Lifecycle.ACTIVE.value
        assert result["after_archive"] == Lifecycle.ARCHIVED.value

    def test_all_three_engines_agree_with_each_other_exactly(self, tmp_path):
        results = {
            "memory": _run_full_lifecycle_sequence(StorageEngine()),
            "file": _run_full_lifecycle_sequence(_make_file_storage(tmp_path / "file_backend")),
        }
        sqlite_storage = _make_sqlite_storage(tmp_path / "sqlite_backend")
        try:
            results["sqlite"] = _run_full_lifecycle_sequence(sqlite_storage)
        finally:
            sqlite_storage.close()

        memory_result = results["memory"]
        for engine_name in ("file", "sqlite"):
            assert results[engine_name] == memory_result, (
                f"{engine_name} storage produced a semantically different lifecycle "
                f"outcome than the in-memory reference: {results[engine_name]} != {memory_result}"
            )


# =====================================================================
# F-D from the inventory: demonstrates the Consolidator bypass empirically
# (documentation-as-test -- this is expected to currently FAIL/xfail-pass
# since the bypass exists; it exists to make the finding regression-visible,
# not to fix it).
# =====================================================================

class TestConsolidatorBypassIsRealNotTheoretical:
    """Documents LIFECYCLE_MUTATION_INVENTORY.md finding D empirically: an
    unauthenticated, unvalidated path exists from ACTIVE straight back to
    ACTIVE with arbitrary attacker-supplied content, entirely outside
    MemoryController's authorization/verification/validation/audit gates.
    This test is intentionally a demonstration of the gap, not a security
    control -- it should start failing (in the good sense) once
    Consolidator is fixed in a future pass, at which point it should be
    updated to assert the fix instead of deleted silently."""

    def test_challenge_and_resolve_bypass_all_controller_gates(self, controller):
        from cognitive_core.consolidation import Consolidator
        from cognitive_core.tool_router import ToolRouter

        nid = str(uuid.uuid4())
        controller.storage.set(nid, _note(nid, lifecycle="ACTIVE", verification="verified",
                                           content="original trusted content"))
        consolidator = Consolidator(controller, ToolRouter(controller))

        # No Principal is required, and any Principal value is accepted with
        # no authorization check -- confirmed by omitting it entirely.
        consolidator.challenge(nid, {"reason": "attacker-supplied conflicting evidence"})
        assert controller.storage.get(nid)["lifecycle"] == Lifecycle.RECONSOLIDATING.value

        # resolve_challenge() with attacker-controlled content pushes the
        # note straight back to ACTIVE -- promote()'s verification gate,
        # attest()'s authorization, and propose()'s provenance rules are
        # never consulted.
        consolidator.resolve_challenge(nid, resolved_node={"content": "attacker-controlled content"})
        final = controller.storage.get(nid)
        assert final["lifecycle"] == Lifecycle.ACTIVE.value
        assert final["content"] == "attacker-controlled content"
        # verification was never touched by this path, yet the note is ACTIVE
        # again -- exactly the invariant promote() exists to prevent.
        assert final["verification"] == "verified"  # unchanged from before, but irrelevant: it was never re-checked
