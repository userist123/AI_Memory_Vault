from cognitive_core.ranked_search import build_multi_graph, ranked_search
from memory_controller.storage.file_engine import FileStorageEngine
from memory_controller.storage.sqlite_engine import SQLiteStorageEngine


class _FakeStorage:
    def __init__(self, notes):
        self.store = {n["id"]: n for n in notes}

    def all_notes(self):
        return list(self.store.values())


class _FakeController:
    def __init__(self, notes):
        self.storage = _FakeStorage(notes)

    def search(self, principal, query, page_size=10):
        matches = [n for n in self.storage.store.values() if query.lower() in n.get("content", "").lower()]
        return {"results": [{"id": n["id"]} for n in matches]}


def test_build_multi_graph_from_controller():
    controller = _FakeController([
        {"id": "a", "category": "x", "tags": ["wal"], "content": "wal storage"},
        {"id": "b", "category": "x", "tags": ["wal"], "content": "wal engine"},
    ])
    graph_memory = build_multi_graph(controller)
    assert "a" in graph_memory.semantic.nodes and "b" in graph_memory.semantic.nodes


def test_ranked_search_falls_back_gracefully_on_empty_results():
    controller = _FakeController([{"id": "a", "category": "x", "tags": [], "content": "unrelated"}])
    results = ranked_search(controller, None, "nomatch", top_k=5)
    assert results == []


def test_ranked_search_returns_reranked_results():
    controller = _FakeController([
        {"id": "a", "category": "x", "tags": ["wal"], "content": "wal storage decision"},
        {"id": "b", "category": "x", "tags": ["wal"], "content": "wal storage details"},
    ])
    results = ranked_search(controller, None, "wal", top_k=5)
    ids = [r["id"] for r in results]
    assert set(ids) == {"a", "b"}


def test_graph_index_contract_works_with_sqlite_and_file_storage(tmp_path):
    note = {
        "id": "note-1", "type": "knowledge", "lifecycle": "ACTIVE",
        "category": "architecture", "tags": ["wal"], "created": "2026-01-01",
        "updated": "2026-01-01", "content": "SQLite WAL", "provenance": {},
    }
    sqlite = SQLiteStorageEngine(str(tmp_path / "vault.db"))
    sqlite.set(note["id"], note)
    assert [n["id"] for n in sqlite.all_notes()] == ["note-1"]

    for folder in ("00_CORE", "01_KNOWLEDGE", "02_PROJECTS", "03_PROCEDURES",
                   "04_MEMORY", "05_RESOURCES", "99_SYSTEM"):
        (tmp_path / folder).mkdir()
    file_storage = FileStorageEngine(str(tmp_path))
    file_storage.set(note["id"], note)
    assert [n["id"] for n in file_storage.all_notes()] == ["note-1"]


def test_ranked_search_reports_graph_failure_and_preserves_relevance_score():
    class StorageWithoutGraphContract:
        pass

    class Controller:
        storage = StorageWithoutGraphContract()

        def search(self, principal, query, page_size=10):
            return {"results": [{"id": "a", "relevance_score": 0.91}]}

    diagnostics = {}
    results = ranked_search(Controller(), None, "query", diagnostics=diagnostics)
    assert results == [{"id": "a", "relevance_score": 0.91}]
    assert diagnostics["graph_status"] == "FAILED"
    assert "all_notes" in diagnostics["graph_reason"]


def test_ranked_search_marks_available_for_storage_contract():
    class Storage:
        def all_notes(self):
            return [{"id": "a", "category": "architecture", "tags": [], "content": "query"}]

    class Controller:
        storage = Storage()

        def search(self, principal, query, page_size=10):
            return {"results": [{"id": "a", "relevance_score": 0.91}]}

    diagnostics = {}
    assert ranked_search(Controller(), None, "query", diagnostics=diagnostics)
    assert diagnostics == {"graph_status": "AVAILABLE", "graph_reason": "graph rerank completed"}
