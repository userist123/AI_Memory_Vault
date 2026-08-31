"""
Tier 1 Feature Coverage: Dual Persistence Storage Engine & Memory Governance (R1).
Covers SQLite WAL engine, atomic Markdown synchronization, recursive CTE lineage traversal,
ACT-R base-level decay activation, and multi-signal recall.
"""

import os
import uuid
import time
import pytest
from pathlib import Path

from jarvis.memory.invariants import (
    Principal,
    Lifecycle,
    NoteType,
    NoteFrontmatter,
    ProvenanceModel,
    MemoryNote,
)
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.memory.markdown_sync import MarkdownSyncEngine
from jarvis.memory.activation import (
    base_level_activation,
    ActivationRecord,
    ActivationTracker,
    SpreadingActivationEngine,
    DORMANT_THRESHOLD,
)
from jarvis.memory.recall import MultiSignalRecallEngine


def test_sqlite_wal_initialization_and_connection(temp_sqlite_path: Path):
    """Test SQLite initialization creates tables and connects with WAL mode."""
    engine = SQLiteStorageEngine(db_path=temp_sqlite_path, timeout=5.0, wal_mode=True)
    conn = engine._get_conn()
    cursor = conn.execute("PRAGMA journal_mode;")
    row = cursor.fetchone()
    assert row[0].lower() == "wal"


def test_sqlite_propose_and_read_note(sqlite_storage: SQLiteStorageEngine):
    """Test proposing a new note and retrieving it by UUID."""
    note_id = str(uuid.uuid4())
    note_data = {
        "id": note_id,
        "type": NoteType.KNOWLEDGE.value,
        "lifecycle": Lifecycle.REVIEW.value,
        "category": "core_rules",
        "tags": ["governance", "immutable"],
        "created": "2026-08-27T12:00:00Z",
        "updated": "2026-08-27T12:00:00Z",
        "provenance": {
            "source_type": "execution",
            "source_ref": "test_harness",
        },
        "confidence": "high",
        "verification": "unverified",
        "relations": [],
        "content": "# Test Note Content\nVerified system rule for testing.",
    }

    sqlite_storage.propose(Principal.AI_AGENT, note_data)
    stored = sqlite_storage.get(note_id)

    assert stored is not None
    assert stored["id"] == note_id
    assert stored["type"] == "knowledge"
    assert stored["lifecycle"] == "REVIEW"
    assert stored["confidence"] == "high"
    assert "Test Note Content" in stored["content"]


def test_sqlite_update_and_atomic_replace(sqlite_storage: SQLiteStorageEngine):
    """Test updating content and tags while maintaining invariant compliance."""
    note_id = str(uuid.uuid4())
    note_data = {
        "id": note_id,
        "type": NoteType.PROCEDURE.value,
        "lifecycle": Lifecycle.REVIEW.value,
        "category": "ops",
        "tags": ["deploy"],
        "created": "2026-08-27T12:00:00Z",
        "updated": "2026-08-27T12:00:00Z",
        "provenance": {"source_type": "execution", "source_ref": "tests"},
        "confidence": "medium",
        "verification": "unverified",
        "relations": [],
        "content": "Step 1: Build.",
    }
    sqlite_storage.propose(Principal.AI_AGENT, note_data)

    # Perform update
    updates = {
        "content": "Step 1: Build. Step 2: Verify.",
        "tags": ["deploy", "v2"],
        "updated": "2026-08-27T13:00:00Z",
    }
    sqlite_storage.update(Principal.AI_AGENT, note_id, updates)

    updated_note = sqlite_storage.get(note_id)
    assert updated_note is not None
    assert updated_note["content"] == "Step 1: Build. Step 2: Verify."
    assert "v2" in updated_note["tags"]


def test_sqlite_recursive_lineage_supersession(sqlite_storage: SQLiteStorageEngine):
    """Test multi-hop supersession resolution via recursive CTE."""
    id_v1 = str(uuid.uuid4())
    id_v2 = str(uuid.uuid4())
    id_v3 = str(uuid.uuid4())

    # Create V1
    n1 = {
        "id": id_v1,
        "type": "decision",
        "lifecycle": "ACTIVE",
        "category": "arch",
        "tags": ["v1"],
        "created": "2026-08-20T10:00:00Z",
        "updated": "2026-08-20T10:00:00Z",
        "provenance": {"source_type": "execution", "source_ref": "v1"},
        "confidence": "high",
        "verification": "partially_verified",
        "relations": [],
        "content": "Architecture Decision v1",
    }
    sqlite_storage.propose(Principal.HUMAN, n1)

    # Create V2 and supersede V1
    n2 = {
        "id": id_v2,
        "type": "decision",
        "lifecycle": "ACTIVE",
        "category": "arch",
        "tags": ["v2"],
        "created": "2026-08-22T10:00:00Z",
        "updated": "2026-08-22T10:00:00Z",
        "provenance": {"source_type": "execution", "source_ref": "v2"},
        "confidence": "high",
        "verification": "partially_verified",
        "relations": [],
        "content": "Architecture Decision v2",
    }
    sqlite_storage.propose(Principal.HUMAN, n2)
    sqlite_storage.supersede(Principal.HUMAN, old_id=id_v1, new_id=id_v2)

    # Create V3 and supersede V2
    n3 = {
        "id": id_v3,
        "type": "decision",
        "lifecycle": "ACTIVE",
        "category": "arch",
        "tags": ["v3"],
        "created": "2026-08-25T10:00:00Z",
        "updated": "2026-08-25T10:00:00Z",
        "provenance": {"source_type": "execution", "source_ref": "v3"},
        "confidence": "very_high",
        "verification": "partially_verified",
        "relations": [],
        "content": "Architecture Decision v3",
    }
    sqlite_storage.propose(Principal.HUMAN, n3)
    sqlite_storage.supersede(Principal.HUMAN, old_id=id_v2, new_id=id_v3)

    # Lineage traversal from V1 should return all related nodes in the chain
    lineage_nodes = sqlite_storage.get_lineage(id_v1)
    assert len(lineage_nodes) == 3
    node_ids = {n["id"] for n in lineage_nodes}
    assert id_v1 in node_ids
    assert id_v2 in node_ids
    assert id_v3 in node_ids

    # Find the active successor
    active_notes = [n for n in lineage_nodes if n["lifecycle"] == "ACTIVE"]
    assert len(active_notes) == 1
    assert active_notes[0]["id"] == id_v3


def test_markdown_sync_frontmatter_parsing_and_writing(temp_vault_dir: Path):
    """Test Markdown note serialization, deserialization, and atomic saving."""
    sync_engine = MarkdownSyncEngine(vault_root=temp_vault_dir)

    note_dict = {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "system",
        "tags": ["sync", "unit"],
        "created": "2026-08-27T12:00:00Z",
        "updated": "2026-08-27T12:00:00Z",
        "provenance": {"source_type": "execution", "source_ref": "unit_test"},
        "confidence": "high",
        "verification": "unverified",
        "relations": [],
        "content": "# Sync Test Note\nTesting atomic markdown save.",
    }

    saved_path = sync_engine.write_note_atomic(note_dict)
    assert saved_path.exists()

    loaded_note = sync_engine.read_note(saved_path)
    assert loaded_note is not None
    assert loaded_note["id"] == note_dict["id"]
    assert "Sync Test Note" in loaded_note["content"]


def test_memory_activation_actr_decay_calculation():
    """Test ACT-R base-level decay activation mathematical calculation."""
    t0 = 1000.0
    act_single = base_level_activation(access_times=[900.0], decay=0.5, current_time=t0)
    assert -2.4 < act_single < -2.2

    act_frequent = base_level_activation(access_times=[980.0, 990.0, 995.0, 999.0], decay=0.5, current_time=t0)
    assert act_frequent > act_single
    assert act_frequent > 0.0

    assert base_level_activation(access_times=[], current_time=t0) == DORMANT_THRESHOLD


def test_memory_recall_bm25_and_semantic_fusion(sqlite_storage: SQLiteStorageEngine):
    """Test MultiSignalRecallEngine retrieve based on semantic + lexical + activation."""
    id1 = str(uuid.uuid4())
    id2 = str(uuid.uuid4())

    note1 = {
        "id": id1,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "network",
        "tags": ["wifi", "iot"],
        "created": "2026-08-27T12:00:00Z",
        "updated": "2026-08-27T12:00:00Z",
        "provenance": {"source_type": "execution", "source_ref": "test"},
        "confidence": "very_high",
        "verification": "partially_verified",
        "relations": [],
        "content": "The home WiFi network is named JarvisIoT running on 5GHz band.",
    }
    note2 = {
        "id": id2,
        "type": "procedure",
        "lifecycle": "ACTIVE",
        "category": "culinary",
        "tags": ["recipe"],
        "created": "2026-08-27T12:00:00Z",
        "updated": "2026-08-27T12:00:00Z",
        "provenance": {"source_type": "execution", "source_ref": "test"},
        "confidence": "medium",
        "verification": "unverified",
        "relations": [],
        "content": "Brewing dark espresso coffee requires 9 bar pressure.",
    }
    sqlite_storage.propose(Principal.HUMAN, note1)
    sqlite_storage.propose(Principal.HUMAN, note2)

    recall_engine = MultiSignalRecallEngine(storage_engine=sqlite_storage)
    results = recall_engine.retrieve(query="What is the WiFi network SSID?", limit=5)

    assert len(results) >= 1
    top_note, score = results[0]
    assert top_note["id"] == id1
    assert "WiFi" in top_note["content"]
    assert score > 0.0
