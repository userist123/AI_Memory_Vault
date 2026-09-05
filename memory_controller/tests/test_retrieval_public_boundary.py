from memory_controller.context.retrieval import RetrievalEngine, RetrievalSecurityError


def test_public_retrieval_defaults_to_active_verified_only():
    class Storage:
        def query(self, intent, lifecycle=None, types=None):
            assert lifecycle == ["ACTIVE"]
            return [
                {"id": "active-ok", "lifecycle": "ACTIVE", "verification": "verified"},
                {"id": "active-unverified", "lifecycle": "ACTIVE", "verification": "unverified"},
                {"id": "review-verified", "lifecycle": "REVIEW", "verification": "verified"},
            ]

    results = RetrievalEngine(Storage()).retrieve({"intent": "knowledge"})
    assert [note["id"] for note in results] == ["active-ok"]


def test_public_retrieval_rejects_non_active_filter():
    class Storage:
        def query(self, intent, lifecycle=None, types=None):
            raise AssertionError("storage must not be reached after boundary rejection")

    try:
        RetrievalEngine(Storage()).retrieve({"intent": "knowledge", "lifecycle_filters": ["REVIEW"]})
    except RetrievalSecurityError:
        return
    raise AssertionError("REVIEW filter must be rejected")
