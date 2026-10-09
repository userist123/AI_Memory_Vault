from unittest.mock import MagicMock, patch

from cognitive_core.qdrant_retrieval import OllamaEmbedder, QdrantIndex, SemanticRetrieval


def test_embedder_returns_none_on_connection_error():
    embedder = OllamaEmbedder()
    with patch("urllib.request.urlopen", side_effect=OSError("refused")):
        assert embedder.embed("text") is None


def test_qdrant_index_search_returns_empty_on_failure():
    index = QdrantIndex()
    with patch("urllib.request.urlopen", side_effect=OSError("refused")):
        assert index.search([0.1, 0.2]) == []


def test_qdrant_point_id_is_stable_and_process_independent():
    index = QdrantIndex()
    first = index._stable_point_id("note-a")
    second = index._stable_point_id("note-a")
    assert first == second
    assert first != index._stable_point_id("note-b")


def test_qdrant_upsert_requires_success_status():
    index = QdrantIndex()
    with patch.object(index, "_request", return_value={"status": "error"}):
        assert not index.upsert([("note-a", [0.1, 0.2], {})])
    with patch.object(index, "_request", return_value={"status": "ok"}):
        assert index.upsert([("note-a", [0.1, 0.2], {})])


def test_qdrant_existing_collection_must_match_vector_contract():
    index = QdrantIndex(vector_size=2)
    matching = {"result": {"config": {"params": {
        "vectors": {"size": 2, "distance": "Cosine"}}}}}
    with patch.object(index, "_request", return_value=matching) as request:
        assert index.ensure_collection()
        request.assert_called_once_with("GET", "/collections/vault_memory")


class _FakeStorage:
    def __init__(self, notes):
        self.store = {n["id"]: n for n in notes}


class _FakeController:
    def __init__(self, notes):
        self.storage = _FakeStorage(notes)


class _FakeIndex:
    def __init__(self, existing):
        self.existing = existing
        self.deleted = []
        self.upsert_calls = 0
    def ensure_collection(self): return True
    def scroll_points(self): return list(self.existing)
    def upsert(self, points): self.upsert_calls += 1; return True
    def delete_points(self, ids): self.deleted.extend(ids); return True
    def search(self, vector, top_k=10): return ["active", "review"]
    @staticmethod
    def _stable_point_id(value): return QdrantIndex._stable_point_id(value)


def test_semantic_retrieval_reindex_skips_when_embedder_unavailable():
    controller = _FakeController([
        {"id": "a", "lifecycle": "ACTIVE", "content": "folosim SQLite WAL", "category": "architecture"},
    ])
    embedder = MagicMock()
    embedder.embed.return_value = None
    retrieval = SemanticRetrieval(controller, embedder=embedder, index=QdrantIndex())
    with patch("urllib.request.urlopen", side_effect=OSError("refused")):
        assert retrieval.reindex()["ok"] is False


def test_semantic_retrieval_query_returns_empty_without_vector():
    controller = _FakeController([])
    embedder = MagicMock()
    embedder.embed.return_value = None
    retrieval = SemanticRetrieval(controller, embedder=embedder)
    assert retrieval.query("anything") == []


def test_reindex_refuses_empty_store_before_delete():
    controller = _FakeController([])
    index = _FakeIndex([{"id": 1, "payload": {"note_id": "old"}}])
    retrieval = SemanticRetrieval(controller, embedder=MagicMock(), index=index)
    result = retrieval.reindex()
    assert result == {"ok": False, "reason": "empty_store_refuses_mass_delete", "upserted": 0, "deleted": 0}
    assert index.deleted == []


def test_query_rechecks_lifecycle_policy():
    controller = _FakeController([
        {"id": "active", "lifecycle": "ACTIVE"},
        {"id": "review", "lifecycle": "REVIEW"},
    ])
    embedder = MagicMock(); embedder.embed.return_value = [0.1]
    retrieval = SemanticRetrieval(controller, embedder=embedder, index=_FakeIndex([]))
    assert retrieval.query("x") == ["active"]


def test_reindex_returns_structured_delete_failure():
    controller = _FakeController([{"id": "new", "lifecycle": "ACTIVE", "content": "x"}])
    index = _FakeIndex([{"id": 1, "payload": {"note_id": "old"}}])
    index.delete_points = lambda ids: False
    embedder = MagicMock(); embedder.embed.return_value = [0.1]
    retrieval = SemanticRetrieval(controller, embedder=embedder, index=index, max_delete_fraction=1.0)
    result = retrieval.reindex()
    assert result["ok"] is False and result["reason"] == "delete_failed" and result["upserted"] == 1


def test_reindex_refuses_malformed_scroll_before_upsert():
    controller = _FakeController([{"id": "new", "lifecycle": "ACTIVE", "content": "x"}])
    index = _FakeIndex([]); index.scroll_points = lambda: [{"payload": "bad"}]
    embedder = MagicMock(); embedder.embed.return_value = [0.1]
    result = SemanticRetrieval(controller, embedder=embedder, index=index).reindex()
    assert result["reason"] == "malformed_existing_point" and index.upsert_calls == 0


def test_reindex_refuses_delete_fraction_over_threshold():
    controller = _FakeController([{"id": "new", "lifecycle": "ACTIVE", "content": "x"}])
    existing = [{"id": i, "payload": {"note_id": f"old-{i}"}} for i in range(4)]
    index = _FakeIndex(existing)
    embedder = MagicMock(); embedder.embed.return_value = [0.1]
    result = SemanticRetrieval(controller, embedder=embedder, index=index, max_delete_fraction=0.25).reindex()
    assert result["reason"] == "delete_fraction_exceeds_threshold" and index.deleted == []


def test_reindex_fails_closed_on_embedding_failure_before_delete():
    controller = _FakeController([{"id": "new", "lifecycle": "ACTIVE", "content": "x"}])
    index = _FakeIndex([{"id": 1, "payload": {"note_id": "old"}}])
    embedder = MagicMock(); embedder.embed.return_value = None
    result = SemanticRetrieval(controller, embedder=embedder, index=index).reindex()
    assert result["reason"] == "embedding_failed" and index.deleted == []
