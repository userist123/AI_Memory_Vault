"""
Unit & Concurrency Tests for Memory Persistence, SQLite WAL, Markdown Sync, and Invariants P0-P18.
"""

import os
import uuid
import threading
import sqlite3
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from typing import Dict, Any
import pytest

from jarvis.memory.invariants import (
    Principal,
    Lifecycle,
    NoteType,
    NoteFrontmatter,
    ProvenanceModel,
)
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.memory.markdown_sync import MarkdownSyncEngine
from jarvis.memory.activation import (
    base_level_activation,
    ActivationTracker,
    SpreadingActivationEngine,
    DORMANT_THRESHOLD,
)
from jarvis.memory.recall import MultiSignalRecallEngine


# ============================================================================
# 1. SQLite Pragmas & WAL Verification
# ============================================================================

def test_sqlite_pragmas_and_wal_mode(temp_db_path: Path):
    """Verify SQLite storage engine initializes in WAL mode with correct PRAGMAs."""
    engine = SQLiteStorageEngine(temp_db_path, wal_mode=True)
    conn = engine._get_conn()
    cursor = conn.cursor()

    cursor.execute("PRAGMA journal_mode;")
    journal_mode = cursor.fetchone()[0].lower()
    assert journal_mode == "wal", f"Expected WAL mode, got {journal_mode}"

    cursor.execute("PRAGMA foreign_keys;")
    fk = cursor.fetchone()[0]
    assert fk == 1, "Foreign keys must be ON"

    engine.close()


# ============================================================================
# 2. Invariants P0-P18 Validation Tests
# ============================================================================

def test_ai_agent_cannot_propose_verified(sqlite_engine: SQLiteStorageEngine, sample_note: Dict[str, Any]):
    """Invariant P0-001/P0-005: AI_AGENT cannot propose notes with verification='verified'."""
    sample_note["verification"] = "verified"
    with pytest.raises(ValueError, match="Verification status 'verified' cannot be set via propose"):
        sqlite_engine.propose(Principal.AI_AGENT, sample_note)

    assert sqlite_engine.get(sample_note["id"]) is None


def test_ai_agent_cannot_forge_privileged_provenance(sqlite_engine: SQLiteStorageEngine, sample_note: Dict[str, Any]):
    """Invariant P0-002: AI_AGENT cannot claim privileged provenance types (user, official, experience, import)."""
    for forbidden in ["user", "official", "experience", "import"]:
        sample_note["id"] = str(uuid.uuid4())
        sample_note["provenance"] = {"source_type": forbidden, "source_ref": "attack"}
        with pytest.raises(ValueError, match="is not permitted to claim provenance source_type"):
            sqlite_engine.propose(Principal.AI_AGENT, sample_note)
        assert sqlite_engine.get(sample_note["id"]) is None


def test_ai_agent_cannot_propose_active_lifecycle(sqlite_engine: SQLiteStorageEngine, sample_note: Dict[str, Any]):
    """Invariant P0-004: AI_AGENT cannot propose notes directly into ACTIVE, VERIFIED, SUPERSEDED, ARCHIVED."""
    for bad_lc in ["ACTIVE", "VERIFIED", "SUPERSEDED", "ARCHIVED"]:
        sample_note["id"] = str(uuid.uuid4())
        sample_note["lifecycle"] = bad_lc
        with pytest.raises(ValueError, match="cannot set lifecycle to"):
            sqlite_engine.propose(Principal.AI_AGENT, sample_note)
        assert sqlite_engine.get(sample_note["id"]) is None


def test_provenance_and_lifecycle_immutability_on_update(sqlite_engine: SQLiteStorageEngine, sample_note: Dict[str, Any]):
    """Invariant P0-003/P0-007: provenance.source_type and lifecycle are immutable via update()."""
    note_id = sample_note["id"]
    sqlite_engine.propose(Principal.AI_AGENT, sample_note)

    # Attempt to change source_type
    with pytest.raises(ValueError, match="provenance.source_type is immutable"):
        sqlite_engine.update(Principal.AI_AGENT, note_id, {"provenance": {"source_type": "execution"}})

    # Attempt to change lifecycle
    with pytest.raises(ValueError, match="lifecycle is immutable via update"):
        sqlite_engine.update(Principal.AI_AGENT, note_id, {"lifecycle": "ACTIVE"})

    # Attempt to escalate verification status
    with pytest.raises(ValueError, match="cannot be escalated via update"):
        sqlite_engine.update(Principal.AI_AGENT, note_id, {"verification": "verified"})


def test_human_attestation_and_promotion(sqlite_engine: SQLiteStorageEngine, sample_note: Dict[str, Any]):
    """Invariant P0-005/P0-008: Human operator can attest and promote draft notes to ACTIVE."""
    note_id = sample_note["id"]
    sqlite_engine.propose(Principal.AI_AGENT, sample_note)

    # AI agent attest -> Rejected
    with pytest.raises(PermissionError, match="ai_agent not allowed to perform attest"):
        sqlite_engine.attest(Principal.AI_AGENT, note_id)

    # AI agent promote -> Rejected
    with pytest.raises(PermissionError, match="ai_agent not allowed to promote"):
        sqlite_engine.promote(Principal.AI_AGENT, note_id)

    # Human attest & promote -> Succeeded
    attested = sqlite_engine.attest(Principal.HUMAN, note_id, reason="Verified against specification")
    assert attested["verification"] == "verified"

    promoted = sqlite_engine.promote(Principal.HUMAN, note_id)
    assert promoted["lifecycle"] == "ACTIVE"


# ============================================================================
# 3. Supersession & Recursive CTE Lineage Traversal
# ============================================================================

def test_atomic_supersession_and_cte_lineage(sqlite_engine: SQLiteStorageEngine):
    """Verify atomic supersession updates and recursive CTE lineage traversal."""
    n1_id = str(uuid.uuid4())
    n2_id = str(uuid.uuid4())
    n3_id = str(uuid.uuid4())

    def make_entry(nid, title):
        return {
            "id": nid,
            "type": NoteType.KNOWLEDGE.value,
            "lifecycle": Lifecycle.ACTIVE.value,
            "category": "lineage",
            "tags": ["lineage"],
            "created": "2026-08-27",
            "updated": "2026-08-27",
            "provenance": {"source_type": "user", "source_ref": "spec"},
            "confidence": "high",
            "verification": "verified",
            "content": title,
            "relations": [],
        }

    sqlite_engine.set_note_atomic(make_entry(n1_id, "Version 1.0 Note"))
    sqlite_engine.set_note_atomic(make_entry(n2_id, "Version 2.0 Note"))
    sqlite_engine.set_note_atomic(make_entry(n3_id, "Version 3.0 Note"))

    # Supersede n1 -> n2
    sqlite_engine.supersede(Principal.HUMAN, n1_id, n2_id)
    # Supersede n2 -> n3
    sqlite_engine.supersede(Principal.HUMAN, n2_id, n3_id)

    # Check forward and backward lineage from n1
    lineage = sqlite_engine.get_lineage(n1_id)
    lineage_ids = {n["id"] for n in lineage}
    assert lineage_ids == {n1_id, n2_id, n3_id}

    # Verify n1 is SUPERSEDED by n2, and n2 is SUPERSEDED by n3
    assert sqlite_engine.get(n1_id)["superseded_by"] == n2_id
    assert sqlite_engine.get(n2_id)["supersedes"] == n1_id
    assert sqlite_engine.get(n2_id)["superseded_by"] == n3_id
    assert sqlite_engine.get(n3_id)["supersedes"] == n2_id


# ============================================================================
# 4. MarkdownSyncEngine Atomic File Persistence Tests
# ============================================================================

def test_markdown_atomic_write_and_sync(markdown_sync: MarkdownSyncEngine, sqlite_engine: SQLiteStorageEngine, sample_note: Dict[str, Any]):
    """Verify atomic Markdown writing and bidirectional vault sync."""
    written_path = markdown_sync.write_note_atomic(sample_note)
    assert written_path.exists()
    assert written_path.stat().st_size > 0

    # Read back and validate frontmatter
    read_note = markdown_sync.read_note(written_path)
    assert read_note["id"] == sample_note["id"]
    assert read_note["category"] == sample_note["category"]
    assert read_note["content"] == sample_note["content"]

    # Sync vault folder to clean SQLite DB
    synced = markdown_sync.sync_vault_to_sqlite(sqlite_engine)
    assert synced >= 1
    assert sqlite_engine.get(sample_note["id"]) is not None


# ============================================================================
# 5. ACT-R Base-Level Activation & Recall Tests
# ============================================================================

def test_act_r_base_level_activation():
    """Verify ACT-R base-level decay mathematical properties."""
    # Empty history returns DORMANT_THRESHOLD
    assert base_level_activation([]) == DORMANT_THRESHOLD

    # Single recent access vs multiple accesses
    now = 1000.0
    act_single = base_level_activation([now - 10.0], current_time=now)
    act_frequent = base_level_activation([now - 100.0, now - 50.0, now - 10.0], current_time=now)

    assert act_frequent > act_single, "Higher frequency must yield higher base-level activation"

    # Distant single access decays
    act_old = base_level_activation([now - 5000.0], current_time=now)
    assert act_old < act_single


def test_spreading_activation_across_wikilinks(sqlite_engine: SQLiteStorageEngine):
    """Verify spreading activation traverses relations and wikilinks."""
    id1 = str(uuid.uuid4())
    id2 = str(uuid.uuid4())

    note1 = {
        "id": id1,
        "type": NoteType.KNOWLEDGE.value,
        "lifecycle": Lifecycle.ACTIVE.value,
        "category": "ai",
        "tags": ["core"],
        "created": "2026-08-27",
        "updated": "2026-08-27",
        "provenance": {"source_type": "user", "source_ref": "manual"},
        "confidence": "high",
        "verification": "verified",
        "content": f"OODA loop connects with [[{id2}]].",
        "relations": [{"relation": "related_to", "target": "knowledge", "target_id": id2}],
    }

    note2 = {
        "id": id2,
        "type": NoteType.KNOWLEDGE.value,
        "lifecycle": Lifecycle.ACTIVE.value,
        "category": "ai",
        "tags": ["core"],
        "created": "2026-08-27",
        "updated": "2026-08-27",
        "provenance": {"source_type": "user", "source_ref": "manual"},
        "confidence": "high",
        "verification": "verified",
        "content": "Working memory holds active cognitive items.",
        "relations": [],
    }

    sqlite_engine.set_note_atomic(note1)
    sqlite_engine.set_note_atomic(note2)

    spreading = SpreadingActivationEngine(max_depth=2, max_nodes=5)
    activated = spreading.spread_activation([note1], storage_fetch_func=sqlite_engine.get)

    act_ids = [item[0]["id"] for item in activated]
    assert id1 in act_ids
    assert id2 in act_ids


# ============================================================================
# 6. Multi-Threaded Adversarial Barrage & 0 Corruptions
# ============================================================================

def test_multi_threaded_adversarial_barrage_zero_corruptions(temp_db_path: Path):
    """
    Stress-test SQLite WAL concurrency:
    - 8 attacker threads flood controller with forbidden proposals.
    - 4 legit writer threads create valid notes.
    - 4 reader threads run queries.
    Assert 0 security leaks, exact row counts, and PRAGMA integrity_check == ok.
    """
    engine = SQLiteStorageEngine(temp_db_path, wal_mode=True, timeout=15.0)

    num_attackers = 8
    attacks_per_thread = 15
    num_legit_writers = 4
    legit_per_thread = 15
    num_readers = 4

    security_breaches = []
    legit_ids_created = []
    lock = threading.Lock()

    def attacker():
        for j in range(attacks_per_thread):
            nid = str(uuid.uuid4())
            mode = j % 3
            if mode == 0:
                payload = {
                    "id": nid, "type": "knowledge", "lifecycle": "REVIEW", "category": "sec",
                    "tags": [], "created": "2026-08-27", "updated": "2026-08-27",
                    "provenance": {"source_type": "inference", "source_ref": "m"},
                    "confidence": "high", "verification": "verified", "content": "Attack verified", "relations": []
                }
            elif mode == 1:
                payload = {
                    "id": nid, "type": "knowledge", "lifecycle": "REVIEW", "category": "sec",
                    "tags": [], "created": "2026-08-27", "updated": "2026-08-27",
                    "provenance": {"source_type": "user", "source_ref": "forged"},
                    "confidence": "high", "verification": "unverified", "content": "Attack user prov", "relations": []
                }
            else:
                payload = {
                    "id": nid, "type": "knowledge", "lifecycle": "ACTIVE", "category": "sec",
                    "tags": [], "created": "2026-08-27", "updated": "2026-08-27",
                    "provenance": {"source_type": "inference", "source_ref": "m"},
                    "confidence": "high", "verification": "unverified", "content": "Attack active", "relations": []
                }

            try:
                engine.propose(Principal.AI_AGENT, payload)
                with lock:
                    security_breaches.append(f"Breach on mode {mode} for note {nid}")
            except (ValueError, PermissionError):
                pass
            except Exception as e:
                with lock:
                    security_breaches.append(f"Unexpected error: {e}")

    def legit_writer(w_idx):
        for _ in range(legit_per_thread):
            nid = str(uuid.uuid4())
            payload = {
                "id": nid, "type": "knowledge", "lifecycle": "REVIEW", "category": "legit",
                "tags": ["valid"], "created": "2026-08-27", "updated": "2026-08-27",
                "provenance": {"source_type": "inference", "source_ref": f"writer-{w_idx}"},
                "confidence": "medium", "verification": "unverified",
                "content": f"Legitimate content from writer {w_idx}", "relations": []
            }
            try:
                engine.propose(Principal.AI_AGENT, payload)
                with lock:
                    legit_ids_created.append(nid)
            except Exception as e:
                with lock:
                    security_breaches.append(f"Legit writer error: {e}")

    def reader():
        for _ in range(20):
            try:
                res = engine.query(lifecycle=["REVIEW"], limit=50)
                assert isinstance(res, list)
            except Exception as e:
                with lock:
                    security_breaches.append(f"Reader error: {e}")

    with ThreadPoolExecutor(max_workers=num_attackers + num_legit_writers + num_readers) as executor:
        futures = []
        for _ in range(num_attackers):
            futures.append(executor.submit(attacker))
        for w in range(num_legit_writers):
            futures.append(executor.submit(legit_writer, w))
        for _ in range(num_readers):
            futures.append(executor.submit(reader))

        for f in as_completed(futures):
            f.result()

    assert len(security_breaches) == 0, f"Security breaches or errors: {security_breaches}"
    expected_total = num_legit_writers * legit_per_thread
    assert len(legit_ids_created) == expected_total

    conn = sqlite3.connect(temp_db_path)
    cursor = conn.cursor()
    cursor.execute("SELECT COUNT(*) FROM notes")
    total_db_rows = cursor.fetchone()[0]
    cursor.execute("PRAGMA integrity_check")
    integrity = cursor.fetchall()
    conn.close()
    engine.close()

    assert total_db_rows == expected_total
    assert integrity == [("ok",)]
