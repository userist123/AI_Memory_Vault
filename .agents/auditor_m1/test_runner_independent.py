import os
import sys
import tempfile
import uuid
import asyncio
from pathlib import Path

# Add project root to path
PROJECT_DIR = r"c:\Users\Marius\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_cognitive_brain"
if PROJECT_DIR not in sys.path:
    sys.path.insert(0, PROJECT_DIR)

from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.memory.invariants import Principal, Lifecycle, NoteType
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.memory.markdown_sync import MarkdownSyncEngine
from jarvis.memory.activation import base_level_activation, ActivationTracker, SpreadingActivationEngine
from jarvis.memory.recall import MultiSignalRecallEngine
from jarvis.memory.reflection import ReflexionEngine
from jarvis.memory.consolidation import ConsolidationEngine
from jarvis.core.models import PerceptionEvent, IntentType
from jarvis.core.ooda import OODACognitiveEngine
from jarvis.core.executive import CognitiveExecutive

async def run_independent_checks():
    print("--- RUNNING INDEPENDENT DIRECT EXECUTION CHECKS ---")
    tmp_dir = tempfile.mkdtemp()
    db_path = os.path.join(tmp_dir, "test.sqlite3")
    vault_path = os.path.join(tmp_dir, "vault")
    
    # 1. SQLite Engine Test
    engine = SQLiteStorageEngine(db_path=db_path, timeout=5.0, wal_mode=True)
    sample_note = {
        "id": str(uuid.uuid4()),
        "type": NoteType.KNOWLEDGE.value,
        "lifecycle": Lifecycle.REVIEW.value,
        "category": "test",
        "tags": ["test"],
        "created": "2026-08-27",
        "updated": "2026-08-27",
        "provenance": {"source_type": "inference", "source_ref": "test"},
        "confidence": "high",
        "verification": "unverified",
        "content": "Test content for direct execution",
        "relations": []
    }
    engine.propose(Principal.AI_AGENT, sample_note)
    assert engine.get(sample_note["id"]) is not None
    print("[PASS] SQLite propose & get verified.")
    
    # 2. Markdown Sync Test
    md_sync = MarkdownSyncEngine(vault_root=Path(vault_path))
    written = md_sync.write_note_atomic(sample_note)
    assert os.path.exists(written)
    read_back = md_sync.read_note(Path(written))
    assert read_back["id"] == sample_note["id"]
    print("[PASS] MarkdownSyncEngine atomic write & read verified.")
    
    # 3. OODA Cognitive Engine E2E Test
    mock_llm = MockLLMProvider(default_response="Jarvis autonomous response.")
    ooda = OODACognitiveEngine(llm_provider=mock_llm, storage_engine=engine)
    perception = PerceptionEvent(channel="voice", raw_data="What is the architecture?")
    cycle_res = await ooda.execute_cycle(perception)
    assert cycle_res.intent.intent_type == IntentType.QUERY
    assert cycle_res.active_plan.is_complete()
    print("[PASS] OODA cycle execution verified.")
    
    # 4. Reflexion Engine Test
    refl_engine = ReflexionEngine(storage_engine=engine)
    err_id = refl_engine.reflect_error(Principal.AI_AGENT, "iot_call", "Timeout error")
    err_note = engine.get(err_id)
    assert err_note is not None
    assert err_note["type"] == NoteType.ERROR.value
    print("[PASS] ReflexionEngine error reflection verified.")
    
    # 5. Consolidation Engine Test
    cons_engine = ConsolidationEngine(storage_engine=engine)
    l1 = sample_note.copy()
    l1["id"] = str(uuid.uuid4())
    l1["type"] = NoteType.LESSON.value
    l1["content"] = "Lesson 1: DB timeout handling is essential."
    l2 = sample_note.copy()
    l2["id"] = str(uuid.uuid4())
    l2["type"] = NoteType.LESSON.value
    l2["content"] = "Lesson 2: Use WAL mode for concurrency."
    engine.set_note_atomic(l1)
    engine.set_note_atomic(l2)
    cons_id = cons_engine.consolidate_lessons(Principal.AI_AGENT)
    assert cons_id is not None
    print("[PASS] ConsolidationEngine lesson consolidation verified.")
    
    print("\nALL 5 INDEPENDENT EXECUTION TESTS PASSED.")

if __name__ == "__main__":
    asyncio.run(run_independent_checks())
