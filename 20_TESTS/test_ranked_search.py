from cognitive_core.ranked_search import build_multi_graph, ranked_search


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
