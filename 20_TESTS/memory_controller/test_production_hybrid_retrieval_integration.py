from types import SimpleNamespace
from unittest.mock import patch

from memory_controller.context.retrieval import RetrievalEngine, RetrievalSecurityError


class ProductionStorage:
    vault_root = "/synthetic-vault-root"

    def query(self, *args, **kwargs):
        raise AssertionError("production retrieval must not fall back to storage.query")


def test_production_retrieval_delegates_to_hybrid_retriever():
    fake_note = SimpleNamespace(
        id="note-1",
        title="Lifecycle policy",
        body="ACTIVE memories require verification.",
        meta={"id": "note-1", "lifecycle": "ACTIVE", "verification": "verified"},
    )
    fake_hit = SimpleNamespace(note=fake_note, score=0.123456, signals={"bm25": 1})
    fake_retriever = SimpleNamespace()

    def fake_hybrid_init(index):
        fake_retriever.index = index
        return fake_retriever

    def fake_secure_search(**kwargs):
        fake_retriever.calls = kwargs
        return [fake_hit]

    fake_retriever.secure_search = fake_secure_search

    with patch("cognitive_core.vault_index.VaultIndex.load", return_value=object()) as load, patch(
        "cognitive_core.hybrid_retrieval.HybridRetriever", side_effect=fake_hybrid_init
    ):
        engine = RetrievalEngine(ProductionStorage())
        results = engine.retrieve(
            {
                "query": "lifecycle policy",
                "intent": "read",
                "target_types": [],
                "lifecycle_filters": [],
                "candidate_limit": 5,
            }
        )

    load.assert_called_once()
    assert fake_retriever.calls["query"] == "lifecycle policy"
    assert fake_retriever.calls["top_k"] == 5
    assert fake_retriever.calls["allowed_lifecycles"] == ["ACTIVE"]
    assert fake_retriever.calls["allowed_types"] is None
    assert results[0]["id"] == "note-1"
    assert results[0]["content"] == "ACTIVE memories require verification."
    assert results[0]["_retrieval_score"] == 0.123456


def test_production_retrieval_never_widens_lifecycle_boundary():
    with patch("cognitive_core.vault_index.VaultIndex.load") as load, patch(
        "cognitive_core.hybrid_retrieval.HybridRetriever"
    ) as hybrid:
        engine = RetrievalEngine(ProductionStorage())
        try:
            engine.retrieve(
                {"query": "review memories", "intent": "read", "lifecycle_filters": ["REVIEW"]}
            )
        except RetrievalSecurityError:
            pass
        else:
            raise AssertionError("REVIEW must be rejected before production retrieval")

    load.assert_not_called()
    hybrid.assert_not_called()
