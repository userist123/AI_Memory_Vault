import json
import os
import urllib.error
import urllib.request
import uuid
from contextlib import suppress

import pytest

from retrieval.qdrant_retrieval import OllamaEmbedder, QdrantIndex, SemanticRetrieval


class _Storage:
    def __init__(self, notes):
        self.store = {note["id"]: note for note in notes}


class _Controller:
    def __init__(self, notes):
        self.storage = _Storage(notes)


def _delete_collection(host: str, collection: str) -> None:
    request = urllib.request.Request(
        f"{host.rstrip('/')}/collections/{collection}",
        method="DELETE",
    )
    with urllib.request.urlopen(request, timeout=10) as response:
        payload = json.loads(response.read().decode("utf-8"))
    assert payload.get("status") == "ok", payload


def _collection_points(index: QdrantIndex) -> int:
    details = index._request("GET", f"/collections/{index.collection}")
    assert details is not None, "Could not read Qdrant collection details"
    return details.get("result", {}).get("points_count", -1)


def test_live_ollama_qdrant_semantic_retrieval_round_trip():
    if os.getenv("RUN_LIVE_QDRANT_TESTS") != "1":
        pytest.skip("Set RUN_LIVE_QDRANT_TESTS=1 to run against real Ollama and Qdrant services")

    ollama_host = os.getenv("OLLAMA_HOST", "http://127.0.0.1:11434")
    embedding_model = os.getenv("OLLAMA_EMBEDDING_MODEL", "nomic-embed-text:latest")
    qdrant_host = os.getenv("QDRANT_URL", "http://127.0.0.1:6333")
    collection = f"vault_test_{uuid.uuid4().hex[:16]}"
    note_id = "live-integration-note-" + uuid.uuid4().hex
    embedder = OllamaEmbedder(model=embedding_model, host=ollama_host, timeout_seconds=30)
    index = None

    try:
        probe_vector = embedder.embed("Memory Vault integration test: verified notes preserve provenance.")
        assert probe_vector, "Ollama embedding request failed or returned no vector"
        assert all(isinstance(value, (int, float)) for value in probe_vector)
        index = QdrantIndex(
            collection=collection,
            host=qdrant_host,
            vector_size=len(probe_vector),
            timeout_seconds=15,
        )
        active_note = {
            "id": note_id,
            "lifecycle": "ACTIVE",
            "content": "Verified memory must preserve provenance and evidence.",
            "category": "integration-test",
        }
        review_note = {
            "id": note_id + "-review",
            "lifecycle": "REVIEW",
            "content": "Unverified review note must not be indexed.",
            "category": "integration-test",
        }
        controller = _Controller([active_note, review_note])
        retrieval = SemanticRetrieval(controller, embedder=embedder, index=index)

        # Seed a legacy point ID to verify migration to the deterministic ID.
        assert index.ensure_collection()
        legacy_result = index._request("PUT", f"/collections/{collection}/points", {
            "points": [{
                "id": 987654321,
                "vector": probe_vector,
                "payload": {"note_id": note_id, "category": "legacy-test"},
            }],
        })
        assert legacy_result is not None, "Could not seed legacy point for migration test"

        indexed_count = retrieval.reindex()
        assert indexed_count == 1, f"Expected only the ACTIVE note to be indexed, got {indexed_count}"
        results = retrieval.query("Which verified memory preserves provenance and evidence?", top_k=5)
        assert note_id in results, f"Expected note {note_id!r} in semantic results, got {results!r}"
        assert note_id + "-review" not in results
        assert _collection_points(index) == 1

        reindexed_count = retrieval.reindex()
        assert reindexed_count == 1, f"Expected one note on repeat reindex, got {reindexed_count}"
        assert _collection_points(index) == 1, "Repeated reindex must upsert the stable point without duplicates"

        # Lifecycle transition: an indexed note moved to REVIEW must be removed from Qdrant.
        active_note["lifecycle"] = "REVIEW"
        assert retrieval.reindex() == 0
        assert _collection_points(index) == 0
        assert note_id not in retrieval.query("Which verified memory preserves provenance and evidence?", top_k=5)

        # Deletion from canonical storage must also remove the orphaned vector.
        active_note["lifecycle"] = "ACTIVE"
        assert retrieval.reindex() == 1
        assert _collection_points(index) == 1
        controller.storage.store.pop(note_id)
        assert retrieval.reindex() == 0
        assert _collection_points(index) == 0
    finally:
        if index is not None:
            with suppress(urllib.error.URLError, OSError, ValueError, AssertionError):
                _delete_collection(qdrant_host, collection)
