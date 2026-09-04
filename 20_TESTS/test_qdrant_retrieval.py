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


class _FakeStorage:
    def __init__(self, notes):
        self.store = {n["id"]: n for n in notes}


class _FakeController:
    def __init__(self, notes):
        self.storage = _FakeStorage(notes)


def test_semantic_retrieval_reindex_skips_when_embedder_unavailable():
    controller = _FakeController([
        {"id": "a", "lifecycle": "ACTIVE", "content": "folosim SQLite WAL", "category": "architecture"},
    ])
    embedder = MagicMock()
    embedder.embed.return_value = None
    retrieval = SemanticRetrieval(controller, embedder=embedder, index=QdrantIndex())
    with patch("urllib.request.urlopen", side_effect=OSError("refused")):
        assert retrieval.reindex() == 0


def test_semantic_retrieval_query_returns_empty_without_vector():
    controller = _FakeController([])
    embedder = MagicMock()
    embedder.embed.return_value = None
    retrieval = SemanticRetrieval(controller, embedder=embedder)
    assert retrieval.query("anything") == []
