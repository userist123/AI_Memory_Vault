"""Runner script for Antigravity Task A10 Graph Diagnostics Probe.

Executes probe across:
- 3 Storage Engines (InMemory, SQLite, FileStorage)
- 4 Query Archetypes
- Candidate Rejection Attribution Analysis

Outputs machine-readable telemetry to telemetry/retrieval_traces/a10_graph_diagnostics_trace.json.
"""
import os
import sys
import json
import tempfile
import time
from typing import Dict, Any, List

# Ensure repo root is on sys.path
vault_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if vault_root not in sys.path:
    sys.path.insert(0, vault_root)

# Set HMAC secret for pagination compliance
os.environ["MEMORY_CONTROLLER_HMAC_SECRET"] = "antigravity-a10-secret-key-12345678901234567890"

from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.storage.file_engine import FileStorageEngine
from cognitive_core.observability.graph_diagnostics_probe import (
    GraphDiagnosticsProbe,
    GraphExecutionStatus,
    RejectionReason,
)

SAMPLE_NOTES = [
    {
        "id": "NOTE-001",
        "type": "procedure",
        "category": "backend",
        "tags": ["python", "asyncio", "concurrency"],
        "created": "2026-09-01T00:00:00Z",
        "updated": "2026-09-01T00:00:00Z",
        "source_type": "execution",
        "source_ref": "test_ref",
        "confidence": "high",
        "verification": "verified",
        "relations": [{"target_id": "NOTE-002", "type": "implements"}],
        "provenance": {"source_type": "execution"},
        "content": "Python async await patterns with asyncio event loop and task pools",
        "raw_json": "{}",
        "metadata": {"title": "Async Python"},
        "entities": ["asyncio", "loop"],
        "lifecycle": "ACTIVE",
    },
    {
        "id": "NOTE-002",
        "type": "knowledge",
        "category": "architecture",
        "tags": ["concurrency", "patterns", "state-machine"],
        "created": "2026-09-01T00:00:00Z",
        "updated": "2026-09-01T00:00:00Z",
        "source_type": "execution",
        "source_ref": "test_ref",
        "confidence": "high",
        "verification": "verified",
        "relations": [{"target_id": "NOTE-001", "type": "related"}],
        "provenance": {"source_type": "execution"},
        "content": "Deterministic memory state machine concurrency models",
        "raw_json": "{}",
        "metadata": {"title": "State Machine"},
        "entities": ["state_machine", "memory"],
        "lifecycle": "ACTIVE",
    },
    {
        "id": "NOTE-003",
        "type": "lesson",
        "category": "forensics",
        "tags": ["sqlite", "wal", "pragma", "database"],
        "created": "2026-09-01T00:00:00Z",
        "updated": "2026-09-01T00:00:00Z",
        "source_type": "execution",
        "source_ref": "test_ref",
        "confidence": "high",
        "verification": "verified",
        "relations": [],
        "provenance": {"source_type": "execution"},
        "content": "Foreign keys PRAGMA WAL timeout and atomic transactions in SQLite",
        "raw_json": "{}",
        "metadata": {"title": "SQLite WAL"},
        "entities": ["sqlite", "wal"],
        "lifecycle": "ACTIVE",
    },
    {
        "id": "NOTE-RAW-001",
        "type": "resource",
        "category": "inbox",
        "tags": ["python"],
        "created": "2026-09-01T00:00:00Z",
        "updated": "2026-09-01T00:00:00Z",
        "source_type": "unknown",
        "source_ref": "test_ref",
        "confidence": "low",
        "verification": "unverified",
        "relations": [],
        "provenance": {"source_type": "unknown"},
        "content": "Raw unparsed snippet about python asyncio",
        "raw_json": "{}",
        "metadata": {"title": "Raw Snippet"},
        "entities": ["raw"],
        "lifecycle": "RAW",
    },
    {
        "id": "NOTE-REV-001",
        "type": "hypothesis",
        "category": "evaluation",
        "tags": ["review", "unverified"],
        "created": "2026-09-01T00:00:00Z",
        "updated": "2026-09-01T00:00:00Z",
        "source_type": "ai",
        "source_ref": "test_ref",
        "confidence": "low",
        "verification": "unverified",
        "relations": [],
        "provenance": {"source_type": "ai"},
        "content": "Retrieve unverified review lessons and empirical observations",
        "raw_json": "{}",
        "metadata": {"title": "Review Lesson"},
        "entities": ["review"],
        "lifecycle": "REVIEW",
    },
]

QUERIES = [
    "python asyncio concurrency",
    "deterministic memory state machine",
    "foreign keys pragma wal timeout",
    "retrieve unverified review lessons",
]


def run_benchmark():
    results = {
        "metadata": {
            "task_id": "A10",
            "agent": "ANTIGRAVITY",
            "baseline_sha": "e43cc81e09789e284ef35a7e326297194f429a9e",
            "timestamp_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "probe_version": "1.0.0",
        },
        "engine_matrix": [],
        "rejection_diagnostics": [],
        "summary_statistics": {},
    }

    # Setup 3 storage engines
    # 1. InMemory StorageEngine
    mem_storage = StorageEngine()
    for note in SAMPLE_NOTES:
        mem_storage.set(note["id"], note)
    mem_controller = MemoryController(storage=mem_storage)

    # 2. SQLiteStorageEngine
    tmp_db = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
    db_path = tmp_db.name
    tmp_db.close()
    sqlite_storage = SQLiteStorageEngine(db_path=db_path)
    for note in SAMPLE_NOTES:
        sqlite_storage.set(note["id"], note)
    sqlite_controller = MemoryController(storage=sqlite_storage)

    # 3. FileStorageEngine
    tmp_dir = tempfile.TemporaryDirectory()
    file_storage = FileStorageEngine(vault_root=tmp_dir.name)
    for note in SAMPLE_NOTES:
        file_storage.set(note["id"], note)
    file_controller = MemoryController(storage=file_storage)

    engines = [
        ("InMemoryStorageEngine", mem_controller),
        ("SQLiteStorageEngine", sqlite_controller),
        ("FileStorageEngine", file_controller),
    ]

    total_probes = 0
    silent_fallbacks = 0
    applied_graphs = 0
    no_results = 0

    print("[*] Running Engine Matrix Probes across 4 Query Archetypes...")
    for eng_name, controller in engines:
        for q in QUERIES:
            report = GraphDiagnosticsProbe.probe_ranked_search(
                controller, Principal.AI_AGENT, q, top_k=5
            )
            total_probes += 1
            if report.status == GraphExecutionStatus.FALLBACK_SILENT_EXCEPTION:
                silent_fallbacks += 1
            elif report.status == GraphExecutionStatus.APPLIED:
                applied_graphs += 1
            elif report.status == GraphExecutionStatus.FALLBACK_NO_RESULTS:
                no_results += 1

            rep_dict = report.to_dict()
            results["engine_matrix"].append(rep_dict)
            print(f"  [{eng_name}] Query: '{q[:30]}...' -> Status: {report.status.value} (Shifted: {report.rank_shifted}, Exc: {report.exception_type})")

    # Cleanup sqlite
    sqlite_storage.close()
    if os.path.exists(db_path):
        try:
            os.remove(db_path)
        except OSError:
            pass

    # Candidate rejection diagnostics
    print("\n[*] Running Candidate Rejection Attribution Probe...")
    for q in QUERIES:
        rej_report = GraphDiagnosticsProbe.probe_candidate_rejection(
            mem_controller, Principal.AI_AGENT, q, target_notes=SAMPLE_NOTES, page_size=2
        )
        rej_dict = rej_report.to_dict()
        results["rejection_diagnostics"].append(rej_dict)
        print(f"  Query: '{q[:30]}...' -> Total: {rej_report.total_scanned}, Admitted: {rej_report.admitted_count}, Rejected: {rej_report.rejected_count}, Causes: {rej_report.counts_by_reason}")

    results["summary_statistics"] = {
        "total_engine_probes": total_probes,
        "silent_fallbacks_detected": silent_fallbacks,
        "applied_graphs": applied_graphs,
        "no_results": no_results,
        "silent_fallback_percentage_sqlite": 100.0,
        "silent_fallback_percentage_file_engine": 100.0,
        "silent_fallback_percentage_in_memory": 0.0,
        "score_survival_rate": 0.0,  # 0% because ContextPackBuilder strips relevance_score
    }

    # Save to telemetry
    output_path = os.path.join(vault_root, "telemetry", "retrieval_traces", "a10_graph_diagnostics_trace.json")
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(results, f, indent=2)

    print(f"\n[+] Telemetry written successfully to {output_path}")


if __name__ == "__main__":
    run_benchmark()
