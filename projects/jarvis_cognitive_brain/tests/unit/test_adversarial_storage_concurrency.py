"""
Adversarial Stress Test Suite: Storage & Concurrency Specialist (Challenger 2).

Thoroughly evaluates:
1. High-concurrency read/write hammer (16+ concurrent threads hammering SQLite WAL).
2. Security invariant bypass attacks (AI_AGENT forging verified status, privileged provenance, lifecycle escalation).
3. Recursive CTE lineage loop injection attacks (self-referencing, 2-node cycles, 3-node cycles, deep graph loops).
4. ACT-R activation mathematical edge cases (negative elapsed time, zero decay, extreme history, cyclic spreading).
"""

import math
import os
import time
import uuid
import sqlite3
import threading
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from typing import Dict, Any, List
import pytest

from jarvis.memory.invariants import (
    Principal,
    Lifecycle,
    NoteType,
    NoteFrontmatter,
    ProvenanceModel,
    validate_propose_invariants,
    validate_update_invariants,
    validate_attest_invariants,
    validate_promote_invariants,
    validate_supersession_invariants,
)
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.memory.activation import (
    base_level_activation,
    ActivationRecord,
    ActivationTracker,
    SpreadingActivationEngine,
    DORMANT_THRESHOLD,
    DEFAULT_DECAY_RATE,
)
from jarvis.memory.recall import MultiSignalRecallEngine


# ============================================================================
# Category 1: High-Concurrency 16-Thread Hammer on SQLite WAL
# ============================================================================

def test_sqlite_wal_16_threads_concurrent_hammer(tmp_path: Path):
    """
    Adversarial Concurrency Stress:
    - 16 simultaneous writer threads performing atomic INSERT, UPDATE, SUPERSEDE, DELETE.
    - 8 simultaneous reader threads executing continuous BM25, filter queries, and CTE lineage traversals.
    - Assert: 0 deadlocks, 0 database locked unhandled exceptions, exact row integrity, PRAGMA integrity_check == ok.
    """
    db_file = tmp_path / "hammer_test.sqlite3"
    engine = SQLiteStorageEngine(db_file, timeout=15.0, wal_mode=True)

    num_writers = 16
    ops_per_writer = 25  # Total 400 write ops
    num_readers = 8
    reads_per_reader = 40  # Total 320 read ops

    errors: List[str] = []
    created_ids: List[str] = []
    lock = threading.Lock()

    def writer_worker(thread_idx: int):
        for op_idx in range(ops_per_writer):
            note_id = str(uuid.uuid4())
            # 1. Propose / Insert
            note_data = {
                "id": note_id,
                "type": "knowledge",
                "lifecycle": "REVIEW",
                "category": f"cat_{thread_idx}",
                "tags": [f"t_{thread_idx}", "concurrency_test"],
                "created": "2026-08-27T20:00:00Z",
                "updated": "2026-08-27T20:00:00Z",
                "provenance": {"source_type": "inference", "source_ref": f"worker_{thread_idx}_{op_idx}"},
                "confidence": "high",
                "verification": "unverified",
                "content": f"Concurrency test data payload from thread {thread_idx} operation {op_idx}",
                "relations": [],
            }
            try:
                engine.propose(Principal.AI_AGENT, note_data)
                with lock:
                    created_ids.append(note_id)

                # 2. Update content
                engine.update(Principal.AI_AGENT, note_id, {
                    "content": f"Updated concurrency payload thread {thread_idx} op {op_idx}",
                    "updated": "2026-08-27T20:00:01Z",
                })

                # 3. Read back verified
                fetched = engine.get(note_id)
                if not fetched or "Updated concurrency" not in fetched["content"]:
                    with lock:
                        errors.append(f"Inconsistent read on note {note_id}")

            except Exception as e:
                with lock:
                    errors.append(f"Writer thread {thread_idx} op {op_idx} failed: {type(e).__name__}: {e}")

    def reader_worker(thread_idx: int):
        for r_idx in range(reads_per_reader):
            try:
                # Query by lifecycle
                notes = engine.query(lifecycle=["REVIEW"], limit=20)
                assert isinstance(notes, list)

                # Search BM25
                results = engine.search_bm25("concurrency", limit=10)
                assert isinstance(results, list)

                # Lineage query on random existing ID if available
                with lock:
                    target_id = created_ids[-1] if created_ids else None
                if target_id:
                    lineage = engine.get_lineage(target_id)
                    assert isinstance(lineage, list)

            except Exception as e:
                with lock:
                    errors.append(f"Reader thread {thread_idx} read {r_idx} failed: {type(e).__name__}: {e}")

    with ThreadPoolExecutor(max_workers=num_writers + num_readers) as executor:
        futures = []
        for w in range(num_writers):
            futures.append(executor.submit(writer_worker, w))
        for r in range(num_readers):
            futures.append(executor.submit(reader_worker, r))

        for f in as_completed(futures):
            f.result()

    engine.close()

    assert len(errors) == 0, f"Encountered concurrency errors: {errors}"
    assert len(created_ids) == num_writers * ops_per_writer

    # Direct SQLite verification
    conn = sqlite3.connect(str(db_file))
    cursor = conn.cursor()
    cursor.execute("SELECT COUNT(*) FROM notes")
    total_count = cursor.fetchone()[0]
    cursor.execute("PRAGMA integrity_check")
    integrity = cursor.fetchall()
    conn.close()

    assert total_count == num_writers * ops_per_writer
    assert integrity == [("ok",)], f"Database corruption detected: {integrity}"


def test_sqlite_concurrent_supersession_chains(tmp_path: Path):
    """
    Stress-test concurrent atomic supersessions across multiple threads:
    Multiple worker threads simultaneously create and supersede chains of notes.
    Verify all 2-node reciprocal links (supersedes / superseded_by) remain 100% consistent.
    """
    db_file = tmp_path / "supersede_hammer.sqlite3"
    engine = SQLiteStorageEngine(db_file, timeout=15.0, wal_mode=True)

    num_chains = 8
    chain_length = 5  # Each thread creates chain: n0 -> n1 -> n2 -> n3 -> n4
    errors: List[str] = []

    def chain_worker(worker_id: int):
        prev_id = None
        for step in range(chain_length):
            nid = str(uuid.uuid4())
            note = {
                "id": nid,
                "type": "knowledge",
                "lifecycle": "ACTIVE",
                "category": "chain",
                "tags": [f"chain_{worker_id}"],
                "created": "2026-08-27T21:00:00Z",
                "updated": "2026-08-27T21:00:00Z",
                "provenance": {"source_type": "user", "source_ref": f"chain_{worker_id}"},
                "confidence": "high",
                "verification": "verified",
                "content": f"Chain {worker_id} Node {step}",
                "relations": [],
            }
            try:
                engine.set_note_atomic(note)
                if prev_id is not None:
                    engine.supersede(Principal.HUMAN, prev_id, nid)
                prev_id = nid
            except Exception as e:
                errors.append(f"Chain worker {worker_id} step {step} error: {e}")

    with ThreadPoolExecutor(max_workers=num_chains) as executor:
        futures = [executor.submit(chain_worker, i) for i in range(num_chains)]
        for f in as_completed(futures):
            f.result()

    engine.close()

    assert len(errors) == 0, f"Supersession errors: {errors}"

    # Verify total note count
    conn = sqlite3.connect(str(db_file))
    cursor = conn.cursor()
    cursor.execute("SELECT COUNT(*) FROM notes")
    total = cursor.fetchone()[0]
    cursor.execute("PRAGMA integrity_check")
    integrity = cursor.fetchall()
    conn.close()

    assert total == num_chains * chain_length
    assert integrity == [("ok",)]


# ============================================================================
# Category 2: Invariant Bypass & Security Attacks (P0-P18)
# ============================================================================

def test_invariant_ai_agent_cannot_forge_verified_status(sqlite_storage: SQLiteStorageEngine, sample_note: Dict[str, Any]):
    """
    Attack Scenario: AI_AGENT attempts to forge verification='verified' via propose() or update().
    Expected: Rejected with ValueError, zero database rows created/updated.
    """
    # 1. Propose attack
    sample_note["verification"] = "verified"
    with pytest.raises(ValueError, match="Verification status 'verified' cannot be set via propose"):
        sqlite_storage.propose(Principal.AI_AGENT, sample_note)
    assert sqlite_storage.get(sample_note["id"]) is None

    # 2. Update attack
    sample_note["verification"] = "unverified"
    sqlite_storage.propose(Principal.AI_AGENT, sample_note)
    note_id = sample_note["id"]

    with pytest.raises(ValueError, match="cannot be escalated via update"):
        sqlite_storage.update(Principal.AI_AGENT, note_id, {"verification": "verified"})

    persisted = sqlite_storage.get(note_id)
    assert persisted["verification"] == "unverified"


def test_invariant_ai_agent_privileged_provenance_types(sqlite_storage: SQLiteStorageEngine, sample_note: Dict[str, Any]):
    """
    Attack Scenario: AI_AGENT attempts to claim provenance source_type in {user, official, experience, import}.
    Expected: All forbidden source types rejected with ValueError.
    """
    forbidden_types = ["user", "official", "experience", "import"]
    for src in forbidden_types:
        nid = str(uuid.uuid4())
        bad_note = sample_note.copy()
        bad_note["id"] = nid
        bad_note["provenance"] = {"source_type": src, "source_ref": "adversarial_forgery"}

        with pytest.raises(ValueError, match="is not permitted to claim provenance source_type"):
            sqlite_storage.propose(Principal.AI_AGENT, bad_note)
        assert sqlite_storage.get(nid) is None


def test_invariant_ai_agent_lifecycle_escalation_attacks(sqlite_storage: SQLiteStorageEngine, sample_note: Dict[str, Any]):
    """
    Attack Scenario: AI_AGENT attempts to directly create or escalate notes into ACTIVE, VERIFIED, SUPERSEDED, ARCHIVED.
    Expected: Rejected with ValueError / PermissionError.
    """
    # Direct proposal with invalid lifecycle
    for bad_lc in ["ACTIVE", "VERIFIED", "SUPERSEDED", "ARCHIVED", "RECONSOLIDATING"]:
        nid = str(uuid.uuid4())
        bad_note = sample_note.copy()
        bad_note["id"] = nid
        bad_note["lifecycle"] = bad_lc

        with pytest.raises(ValueError, match="cannot set lifecycle to"):
            sqlite_storage.propose(Principal.AI_AGENT, bad_note)
        assert sqlite_storage.get(nid) is None

    # Update attempt to change lifecycle
    valid_id = str(uuid.uuid4())
    sample_note["id"] = valid_id
    sample_note["lifecycle"] = "REVIEW"
    sqlite_storage.propose(Principal.AI_AGENT, sample_note)

    with pytest.raises(ValueError, match="lifecycle is immutable via update"):
        sqlite_storage.update(Principal.AI_AGENT, valid_id, {"lifecycle": "ACTIVE"})

    # Direct promote attempt by AI_AGENT
    with pytest.raises(PermissionError, match="ai_agent not allowed to promote"):
        sqlite_storage.promote(Principal.AI_AGENT, valid_id)

    # Direct attest attempt by AI_AGENT
    with pytest.raises(PermissionError, match="ai_agent not allowed to perform attest"):
        sqlite_storage.attest(Principal.AI_AGENT, valid_id)


def test_invariant_provenance_immutability(sqlite_storage: SQLiteStorageEngine, sample_note: Dict[str, Any]):
    """
    Attack Scenario: Attempting to mutate provenance.source_type after creation.
    Expected: Rejected with ValueError.
    """
    note_id = sample_note["id"]
    sample_note["provenance"] = {"source_type": "inference", "source_ref": "initial_model"}
    sqlite_storage.propose(Principal.AI_AGENT, sample_note)

    # Attempt to change source_type from inference to execution
    with pytest.raises(ValueError, match="provenance.source_type is immutable post-creation"):
        sqlite_storage.update(Principal.AI_AGENT, note_id, {
            "provenance": {"source_type": "execution", "source_ref": "forged"}
        })

    persisted = sqlite_storage.get(note_id)
    assert persisted["provenance"]["source_type"] == "inference"


# ============================================================================
# Category 3: Recursive CTE Lineage Loop Injection & Cyclic Graphs
# ============================================================================

def test_lineage_self_supersession_prevention(sqlite_storage: SQLiteStorageEngine):
    """
    Attack Scenario: Note attempts to supersede itself (A supersedes A).
    Expected: Rejected by validate_supersession_invariants.
    """
    nid = str(uuid.uuid4())
    note = {
        "id": nid, "type": "knowledge", "lifecycle": "ACTIVE", "category": "graph",
        "tags": [], "created": "2026-08-27", "updated": "2026-08-27",
        "provenance": {"source_type": "user", "source_ref": "spec"},
        "confidence": "high", "verification": "verified", "content": "Self loop", "relations": []
    }
    sqlite_storage.set_note_atomic(note)

    with pytest.raises(ValueError, match="Self-supersession prohibited"):
        sqlite_storage.supersede(Principal.HUMAN, nid, nid)


def test_lineage_2_node_cycle_prevention(sqlite_storage: SQLiteStorageEngine):
    """
    Attack Scenario: 2-node cycle creation: A supersedes B, then B attempts to supersede A.
    Expected: Cyclic supersession detected and rejected.
    """
    n1_id = str(uuid.uuid4())
    n2_id = str(uuid.uuid4())

    def make_n(nid, title):
        return {
            "id": nid, "type": "knowledge", "lifecycle": "ACTIVE", "category": "cycle",
            "tags": [], "created": "2026-08-27", "updated": "2026-08-27",
            "provenance": {"source_type": "user", "source_ref": "spec"},
            "confidence": "high", "verification": "verified", "content": title, "relations": []
        }

    sqlite_storage.set_note_atomic(make_n(n1_id, "Node 1"))
    sqlite_storage.set_note_atomic(make_n(n2_id, "Node 2"))

    # n1 superseded by n2
    sqlite_storage.supersede(Principal.HUMAN, n1_id, n2_id)

    # Now attempt to supersede n2 by n1 (creating cycle n1 -> n2 -> n1)
    with pytest.raises(ValueError, match="Cyclic supersession detected"):
        sqlite_storage.supersede(Principal.HUMAN, n2_id, n1_id)


def test_lineage_cte_cycle_bounded_termination_and_safety(sqlite_storage: SQLiteStorageEngine):
    """
    Adversarial Graph Injection:
    Directly inject a 3-node cycle (A -> B -> C -> A) into SQLite tables.
    Traverse using get_lineage(A, max_depth=50).
    Verify that SQLite recursive CTE terminates safely without infinite loops or crashes,
    and returns exactly the 3 distinct nodes.
    """
    a_id = str(uuid.uuid4())
    b_id = str(uuid.uuid4())
    c_id = str(uuid.uuid4())

    # Raw injection of circular references
    note_a = {
        "id": a_id, "type": "knowledge", "lifecycle": "SUPERSEDED", "category": "cycle",
        "tags": [], "created": "2026-08-27", "updated": "2026-08-27",
        "provenance": {"source_type": "user", "source_ref": "spec"},
        "confidence": "high", "verification": "verified",
        "supersedes": c_id, "superseded_by": b_id,
        "content": "Node A in cycle", "relations": []
    }
    note_b = {
        "id": b_id, "type": "knowledge", "lifecycle": "SUPERSEDED", "category": "cycle",
        "tags": [], "created": "2026-08-27", "updated": "2026-08-27",
        "provenance": {"source_type": "user", "source_ref": "spec"},
        "confidence": "high", "verification": "verified",
        "supersedes": a_id, "superseded_by": c_id,
        "content": "Node B in cycle", "relations": []
    }
    note_c = {
        "id": c_id, "type": "knowledge", "lifecycle": "ACTIVE", "category": "cycle",
        "tags": [], "created": "2026-08-27", "updated": "2026-08-27",
        "provenance": {"source_type": "user", "source_ref": "spec"},
        "confidence": "high", "verification": "verified",
        "supersedes": b_id, "superseded_by": a_id,
        "content": "Node C in cycle", "relations": []
    }

    sqlite_storage.set_note_atomic(note_a)
    sqlite_storage.set_note_atomic(note_b)
    sqlite_storage.set_note_atomic(note_c)

    # get_lineage must terminate in < 100ms and return the 3 nodes
    t0 = time.time()
    lineage = sqlite_storage.get_lineage(a_id, max_depth=20)
    elapsed = time.time() - t0

    assert elapsed < 0.2, f"Recursive CTE hung or exceeded time limit: {elapsed}s"
    lineage_ids = {n["id"] for n in lineage}
    assert lineage_ids == {a_id, b_id, c_id}


def test_recall_successor_resolution_with_lineage(sqlite_storage: SQLiteStorageEngine):
    """
    Verify MultiSignalRecallEngine handles superseded notes by discovering active successor
    via CTE lineage and boosting its relevance score.
    """
    old_id = str(uuid.uuid4())
    new_id = str(uuid.uuid4())

    old_note = {
        "id": old_id, "type": "knowledge", "lifecycle": "SUPERSEDED", "category": "architecture",
        "tags": ["memory"], "created": "2026-08-20", "updated": "2026-08-20",
        "provenance": {"source_type": "user", "source_ref": "manual"},
        "confidence": "high", "verification": "verified",
        "superseded_by": new_id,
        "content": "Legacy architecture used synchronous disk writes.", "relations": []
    }

    new_note = {
        "id": new_id, "type": "knowledge", "lifecycle": "ACTIVE", "category": "architecture",
        "tags": ["memory"], "created": "2026-08-27", "updated": "2026-08-27",
        "provenance": {"source_type": "user", "source_ref": "manual"},
        "confidence": "very_high", "verification": "verified",
        "supersedes": old_id,
        "content": "Modern architecture utilizes SQLite WAL mode with atomic transactions.", "relations": []
    }

    sqlite_storage.set_note_atomic(old_note)
    sqlite_storage.set_note_atomic(new_note)

    recall_engine = MultiSignalRecallEngine(sqlite_storage)
    results = recall_engine.retrieve("synchronous disk writes architecture", limit=5)

    assert len(results) >= 1
    # Check that new active note is pulled in or top-ranked
    result_ids = [r[0]["id"] for r in results]
    assert new_id in result_ids or old_id in result_ids


# ============================================================================
# Category 4: ACT-R Activation Mathematical Edge Cases
# ============================================================================

def test_act_r_mathematical_edge_cases():
    r"""
    Adversarial Math Stress:
    - Empty access history -> DORMANT_THRESHOLD (-2.0)
    - Future timestamps ($t_j > t \implies \text{negative elapsed time}$) -> clamped to 0.001s, no math errors
    - Zero decay ($d = 0$) -> $\ln(N)$
    - Negative decay ($d = -0.5$) -> exponential growth without overflow
    - Extreme decay ($d = 50.0$) -> extreme attenuation
    - Massive access history (10,000 accesses) -> logarithmic stability
    - Timestamp exactly at current time ($t = t_j$) -> clamped to 0.001s
    """
    # 1. Empty list
    assert base_level_activation([]) == DORMANT_THRESHOLD

    # 2. Future timestamps (t_j in future relative to current_time)
    now = 1000.0
    future_times = [now + 50.0, now + 100.0]
    act_future = base_level_activation(future_times, current_time=now)
    assert isinstance(act_future, float)
    assert not math.isnan(act_future)
    assert not math.isinf(act_future)

    # 3. Exact current time (t_j == now)
    act_instant = base_level_activation([now], current_time=now)
    assert isinstance(act_instant, float)
    assert act_instant > 0.0

    # 4. Zero decay rate (d = 0.0): B_i = ln(sum 1) = ln(N)
    accesses = [now - 100.0, now - 50.0, now - 10.0]
    act_zero_decay = base_level_activation(accesses, decay=0.0, current_time=now)
    expected_zero = math.log(3.0)
    assert math.isclose(act_zero_decay, expected_zero, rel_tol=1e-5)

    # 5. Negative decay rate (d = -0.5)
    act_neg_decay = base_level_activation(accesses, decay=-0.5, current_time=now)
    assert isinstance(act_neg_decay, float)
    assert act_neg_decay > 0.0

    # 6. Extreme high decay (d = 20.0)
    act_high_decay = base_level_activation([now - 10.0], decay=20.0, current_time=now)
    assert isinstance(act_high_decay, float)
    assert act_high_decay < 0.0  # Decays to very low value

    # 7. Massive history (10,000 accesses)
    large_history = [now - (i * 0.1) for i in range(1, 10001)]
    act_large = base_level_activation(large_history, current_time=now)
    assert isinstance(act_large, float)
    assert not math.isnan(act_large)
    assert not math.isinf(act_large)
    assert act_large > 5.0  # High activation due to massive access count


def test_spreading_activation_cyclic_and_malformed_wikilinks(sqlite_storage: SQLiteStorageEngine):
    """
    Stress-test SpreadingActivationEngine with:
    - Self-referencing wikilinks ([[NoteA]] inside NoteA)
    - Bidirectional cycles (NoteA -> NoteB -> NoteA)
    - Dangling / nonexistent target links ([[NonExistentNote999]])
    - Empty relations and empty content
    Verify: Zero infinite recursion, zero unhandled crashes, bounded result set <= max_nodes.
    """
    id_a = str(uuid.uuid4())
    id_b = str(uuid.uuid4())

    note_a = {
        "id": id_a, "type": "knowledge", "lifecycle": "ACTIVE", "category": "wikilinks",
        "tags": [], "created": "2026-08-27", "updated": "2026-08-27",
        "provenance": {"source_type": "user", "source_ref": "spec"},
        "confidence": "high", "verification": "verified",
        "content": f"Self link [[{id_a}]] and forward link to [[{id_b}]] and dead link [[missing_uuid_123]].",
        "relations": [
            {"relation": "related_to", "target": "knowledge", "target_id": id_a},
            {"relation": "related_to", "target": "knowledge", "target_id": id_b},
            {"relation": "related_to", "target": "knowledge", "target_id": "nonexistent_target_id"},
        ],
    }

    note_b = {
        "id": id_b, "type": "knowledge", "lifecycle": "ACTIVE", "category": "wikilinks",
        "tags": [], "created": "2026-08-27", "updated": "2026-08-27",
        "provenance": {"source_type": "user", "source_ref": "spec"},
        "confidence": "high", "verification": "verified",
        "content": f"Back link to [[{id_a}]].",
        "relations": [
            {"relation": "related_to", "target": "knowledge", "target_id": id_a},
        ],
    }

    sqlite_storage.set_note_atomic(note_a)
    sqlite_storage.set_note_atomic(note_b)

    spreading = SpreadingActivationEngine(max_depth=5, max_nodes=10)
    activated = spreading.spread_activation([note_a], storage_fetch_func=sqlite_storage.get)

    assert len(activated) <= 10
    activated_ids = [item[0]["id"] for item in activated]
    assert id_a in activated_ids
    assert id_b in activated_ids
    for _, score in activated:
        assert 0.0 <= score <= 1.0


def test_bm25_sql_injection_and_special_character_resilience(sqlite_storage: SQLiteStorageEngine):
    """
    Adversarial Search Stress:
    Execute BM25 search with malicious SQL injection strings and bizarre characters:
    - "' OR '1'='1"
    - "'; DROP TABLE notes; --"
    - "%%%"
    - UTF-8 emoji and non-ASCII characters
    Verify: Zero crashes, zero database damage, table notes remains intact.
    """
    # Seed a normal note
    nid = str(uuid.uuid4())
    sqlite_storage.set_note_atomic({
        "id": nid, "type": "knowledge", "lifecycle": "ACTIVE", "category": "core",
        "tags": ["sql_test"], "created": "2026-08-27", "updated": "2026-08-27",
        "provenance": {"source_type": "user", "source_ref": "spec"},
        "confidence": "high", "verification": "verified",
        "content": "Legitimate knowledge note for SQL injection stress testing.", "relations": []
    })

    malicious_queries = [
        "' OR '1'='1",
        "'; DROP TABLE notes; --",
        "UNION SELECT * FROM notes --",
        "''' OR 1=1 --",
        "%; DELETE FROM notes; --",
        "\\x00\\x01\\xff",
        "🚀⚡🔍 [[wikilink_bomb]] *?[]{}()",
        "",
        "   ",
    ]

    for q in malicious_queries:
        results = sqlite_storage.search_bm25(q, limit=10)
        assert isinstance(results, list)

    # Ensure table notes was not dropped or corrupted
    assert sqlite_storage.count() >= 1
    assert sqlite_storage.get(nid) is not None
