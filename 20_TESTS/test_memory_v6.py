from pathlib import Path

from cognitive_core.extraction import AtomicMemoryExtractor
from cognitive_core.proposal_queue import MemoryProposalQueue
from cognitive_core.sensor_buffer import SensorBuffer
from cognitive_core.spatial_index import SpatialIndex


def test_sensor_buffer_capacity_and_snapshot():
    buffer = SensorBuffer(max_events_per_session=2, ttl_minutes=10)
    buffer.append("s1", "agent", "user", "one")
    buffer.append("s1", "agent", "user", "two")
    buffer.append("s1", "agent", "user", "three")
    assert [item["content"] for item in buffer.snapshot("s1")] == ["two", "three"]


def test_extractor_returns_raw_unverified_atomic_candidates():
    text = "Am decis: folosim SQLite WAL. Todo: adaug\u0103 index spa\u021bial."
    results = AtomicMemoryExtractor().extract(text, "session:test")
    assert {item.type for item in results} == {"decision", "task"}
    assert all(item.lifecycle == "RAW" and item.verification == "unverified" for item in results)


def test_queue_deduplicates_content_hash(tmp_path):
    queue = MemoryProposalQueue(tmp_path / "queue.jsonl")
    results = AtomicMemoryExtractor().extract("Am decis: folosim WAL.", "session:test")
    assert queue.enqueue(results) == 1
    assert queue.enqueue(results) == 0
    assert len(queue.pending()) == 1


def test_spatial_index_detects_python_and_markdown(tmp_path):
    (tmp_path / "module.py").write_text("import json\n", encoding="utf-8")
    (tmp_path / "note.md").write_text("[[module]]\n", encoding="utf-8")
    index = SpatialIndex(tmp_path)
    data = index.rebuild()
    assert "module.py" in data["nodes"]
    assert "note.md" in data["nodes"]
    assert any(edge["relation"] == "imports" for edge in data["edges"])
    assert any(edge["relation"] == "references" for edge in data["edges"])
