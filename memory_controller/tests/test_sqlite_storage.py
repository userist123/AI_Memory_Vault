import pytest
import os
import uuid
import tempfile
import threading
import sqlite3
import time
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.controller import MemoryController, Lifecycle
from memory_controller.authorizer import Principal

@pytest.fixture
def temp_db_path():
    fd, path = tempfile.mkstemp(suffix=".sqlite3")
    os.close(fd)
    if os.path.exists(path):
        os.remove(path)
    yield path
    for ext in ["", "-wal", "-shm"]:
        target = path + ext
        if os.path.exists(target):
            try:
                os.remove(target)
            except Exception:
                pass

def make_note(id_val, lifecycle="ACTIVE", verification="unverified", provenance=None, content="test note"):
    if provenance is None:
        provenance = {"source_type": "user", "source_ref": "test-src"}
    return {
        "id": id_val,
        "type": "knowledge",
        "lifecycle": lifecycle,
        "category": "database",
        "tags": ["sqlite", "wal"],
        "created": "2026-08-14",
        "updated": "2026-08-14",
        "provenance": provenance,
        "confidence": "high",
        "verification": verification,
        "relations": [],
        "content": content
    }

def test_sqlite_storage_basic_crud(temp_db_path):
    engine = SQLiteStorageEngine(temp_db_path)
    note_id = str(uuid.uuid4())
    data = make_note(note_id, lifecycle="ACTIVE")

    # Set and Get
    engine.set(note_id, data)
    retrieved = engine.get(note_id)
    assert retrieved is not None
    assert retrieved["id"] == note_id
    assert retrieved["content"] == "test note"
    assert retrieved["lifecycle"] == "ACTIVE"

    # Query excludes RAW by default
    raw_id = str(uuid.uuid4())
    raw_data = make_note(raw_id, lifecycle="RAW")
    engine.set(raw_id, raw_data)

    results = engine.query()
    result_ids = [n["id"] for n in results]
    assert note_id in result_ids
    assert raw_id not in result_ids

    # Query with filters
    filtered = engine.query(types=["knowledge"], lifecycle=["ACTIVE"])
    assert len(filtered) >= 1
    assert filtered[0]["id"] == note_id

    # Delete
    engine.delete(note_id)
    assert engine.get(note_id) is None
    engine.close()

def test_sqlite_wal_pragmas_and_checkpoint(temp_db_path):
    engine = SQLiteStorageEngine(temp_db_path, wal_mode=True)
    conn = engine._get_connection()
    
    # Check journal mode is WAL
    row = conn.execute("PRAGMA journal_mode;").fetchone()
    assert row[0].lower() == "wal"

    # Write notes
    for i in range(10):
        nid = str(uuid.uuid4())
        engine.set(nid, make_note(nid, content=f"note {i}"))

    # Checkpoint
    engine.checkpoint("TRUNCATE")
    engine.close()

def test_sqlite_schema_check_constraints(temp_db_path):
    engine = SQLiteStorageEngine(temp_db_path)
    note_id = str(uuid.uuid4())
    
    # Invalid lifecycle enum
    invalid_data = make_note(note_id)
    invalid_data["lifecycle"] = "INVALID_STAGE"
    with pytest.raises(sqlite3.IntegrityError):
        engine.set(note_id, invalid_data)

    # Invalid source_type enum
    invalid_prov = make_note(note_id)
    invalid_prov["provenance"] = {"source_type": "illegal_source", "source_ref": "none"}
    with pytest.raises(sqlite3.IntegrityError):
        engine.set(note_id, invalid_prov)

    # ID mismatch
    with pytest.raises(ValueError, match="ID mismatch"):
        engine.set(str(uuid.uuid4()), make_note(note_id))

    engine.close()

def test_sqlite_concurrent_readers_and_writers(temp_db_path):
    engine = SQLiteStorageEngine(temp_db_path, wal_mode=True)
    num_writers = 4
    notes_per_writer = 15
    errors = []

    def writer_worker(worker_id):
        try:
            for j in range(notes_per_writer):
                nid = f"writer-{worker_id}-{j}-{uuid.uuid4()}"
                engine.set(nid, make_note(nid, content=f"worker {worker_id} note {j}"))
                time.sleep(0.005)
        except Exception as e:
            errors.append(e)

    def reader_worker():
        try:
            for _ in range(30):
                engine.query()
                time.sleep(0.005)
        except Exception as e:
            errors.append(e)

    threads = []
    for w in range(num_writers):
        t = threading.Thread(target=writer_worker, args=(w,))
        threads.append(t)
    for _ in range(3):
        t = threading.Thread(target=reader_worker)
        threads.append(t)

    for t in threads:
        t.start()
    for t in threads:
        t.join()

    assert len(errors) == 0, f"Encountered concurrency errors: {errors}"
    total_notes = engine.query()
    assert len(total_notes) == num_writers * notes_per_writer
    engine.close()

def test_sqlite_recursive_lineage_resolution(temp_db_path):
    engine = SQLiteStorageEngine(temp_db_path)
    id1 = str(uuid.uuid4())
    id2 = str(uuid.uuid4())
    id3 = str(uuid.uuid4())

    note1 = make_note(id1, lifecycle="SUPERSEDED")
    note1["superseded_by"] = id2
    note2 = make_note(id2, lifecycle="SUPERSEDED")
    note2["superseded_by"] = id3
    note3 = make_note(id3, lifecycle="ACTIVE")

    engine.set(id1, note1)
    engine.set(id2, note2)
    engine.set(id3, note3)

    # Lineage from id1 should resolve to id3
    resolved = engine.resolve_active_lineage(id1)
    assert resolved == id3

    # Lineage from id2 should resolve to id3
    resolved_2 = engine.resolve_active_lineage(id2)
    assert resolved_2 == id3

    # Lineage from active node resolves to itself
    assert engine.resolve_active_lineage(id3) == id3
    engine.close()

def test_sqlite_memory_controller_full_integration(temp_db_path):
    engine = SQLiteStorageEngine(temp_db_path, wal_mode=True)
    controller = MemoryController(engine)

    note_id = str(uuid.uuid4())
    # Propose note (in REVIEW)
    controller.propose(Principal.AI_AGENT, make_note(note_id, lifecycle="REVIEW", verification="unverified", provenance={"source_type": "inference", "source_ref": "test"}))
    assert engine.get(note_id)["verification"] == "unverified"

    # Attest note
    controller.attest(Principal.HUMAN, note_id, "Verified by human operator", "evidence-doc")
    assert engine.get(note_id)["verification"] == "verified"

    # Promote note
    controller.promote(Principal.HUMAN, note_id)
    assert engine.get(note_id)["lifecycle"] == "ACTIVE"

    # Read note via controller
    pack = controller.read(Principal.HUMAN, note_id)
    assert len(pack["results"]) == 1

    engine.close()

def test_sqlite_pragmas_explicit(temp_db_path):
    engine = SQLiteStorageEngine(temp_db_path, wal_mode=True)
    conn = engine._get_connection()
    
    # Check journal_mode = WAL
    cur = conn.execute("PRAGMA journal_mode;")
    assert cur.fetchone()[0].lower() == "wal"
    
    # Check busy_timeout = 5000
    cur = conn.execute("PRAGMA busy_timeout;")
    assert cur.fetchone()[0] == 5000
    
    # Check foreign_keys = ON (1)
    cur = conn.execute("PRAGMA foreign_keys;")
    assert cur.fetchone()[0] == 1
    
    engine.close()

def test_sqlite_recursive_lineage_cycle_and_depth_limit(temp_db_path):
    engine = SQLiteStorageEngine(temp_db_path)
    
    # Test cycle: A -> B -> C -> A
    id_a = str(uuid.uuid4())
    id_b = str(uuid.uuid4())
    id_c = str(uuid.uuid4())
    
    note_a = make_note(id_a, lifecycle="SUPERSEDED")
    note_a["superseded_by"] = id_b
    note_b = make_note(id_b, lifecycle="SUPERSEDED")
    note_b["superseded_by"] = id_c
    note_c = make_note(id_c, lifecycle="SUPERSEDED")
    note_c["superseded_by"] = id_a
    
    engine.set(id_a, note_a)
    engine.set(id_b, note_b)
    engine.set(id_c, note_c)
    
    # Must terminate without infinite loop due to depth < 50
    resolved = engine.resolve_active_lineage(id_a)
    assert resolved in (id_a, id_b, id_c)
    
    # Test deep chain of 60 notes: n0 -> n1 -> ... -> n59
    chain_ids = [str(uuid.uuid4()) for _ in range(60)]
    for idx in range(59):
        n = make_note(chain_ids[idx], lifecycle="SUPERSEDED")
        n["superseded_by"] = chain_ids[idx + 1]
        engine.set(chain_ids[idx], n)
    engine.set(chain_ids[59], make_note(chain_ids[59], lifecycle="ACTIVE"))
    
    # Resolving from chain_ids[0] should halt at depth 50
    resolved_deep = engine.resolve_active_lineage(chain_ids[0])
    assert resolved_deep == chain_ids[50]
    
    # Non-existent ID returns itself
    non_existent = str(uuid.uuid4())
    assert engine.resolve_active_lineage(non_existent) == non_existent
    
    engine.close()

def test_sqlite_atomic_rollback_on_failure(temp_db_path):
    engine = SQLiteStorageEngine(temp_db_path)
    valid_id = str(uuid.uuid4())
    engine.set(valid_id, make_note(valid_id, content="valid record"))
    
    # Attempt to insert an invalid record that violates CHECK constraint
    invalid_id = str(uuid.uuid4())
    invalid_note = make_note(invalid_id)
    invalid_note["confidence"] = "INVALID_CONFIDENCE_LEVEL"
    
    with pytest.raises(sqlite3.IntegrityError):
        engine.set(invalid_id, invalid_note)
        
    # Verify valid note is intact and invalid note was rolled back completely
    assert engine.get(valid_id)["content"] == "valid record"
    assert engine.get(invalid_id) is None
    
    engine.close()

