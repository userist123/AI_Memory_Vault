from cognitive_core.recall import RecallEngine


class FakeStorage:
    def __init__(self, notes):
        self.notes = notes

    def query(self, lifecycle=None):
        if lifecycle == ["REVIEW"]:
            return [n.copy() for n in self.notes if n.get("lifecycle") == "REVIEW"]
        return [n.copy() for n in self.notes]

    def get(self, node_id):
        for note in self.notes:
            if note.get("id") == node_id:
                return note.copy()
        return None


class FakeController:
    def __init__(self, notes):
        self.storage = FakeStorage(notes)


class FakeSemanticProvider:
    def compute_similarity(self, query, content):
        if "distributed" in query.lower() and "distributed" in content.lower():
            return 1.0
        return 0.0


class FakeWorkingMemory:
    def get_active_context(self):
        return []


def test_review_candidate_is_retrievable_but_unverified_and_not_mutated():
    review_note = {
        "id": "M-DISTRIBUTED-001",
        "content": "Distributed operation introduces timing and partial failure concerns.",
        "lifecycle": "REVIEW",
    }
    engine = RecallEngine(FakeController([review_note]), FakeSemanticProvider())

    result = engine.recall(None, "distributed systems partial failure", [], FakeWorkingMemory())

    assert len(result) == 1
    returned, score = result[0]
    assert returned["id"] == "M-DISTRIBUTED-001"
    assert returned["lifecycle"] == "REVIEW"
    assert returned["_cognitive_unverified"] is True
    assert score >= engine.abstention_threshold
    assert "_cognitive_unverified" not in review_note


def test_irrelevant_query_abstains_instead_of_returning_review_candidates():
    review_note = {
        "id": "M-DISTRIBUTED-001",
        "content": "Distributed operation introduces timing and partial failure concerns.",
        "lifecycle": "REVIEW",
    }
    engine = RecallEngine(FakeController([review_note]), FakeSemanticProvider())

    result = engine.recall(None, "unrelated gardening question", [], FakeWorkingMemory())

    assert result == []
    assert review_note["lifecycle"] == "REVIEW"
    assert "_cognitive_unverified" not in review_note


def test_abstention_threshold_is_configurable_and_validated():
    engine = RecallEngine(
        FakeController([]),
        FakeSemanticProvider(),
        abstention_threshold=0.35,
    )
    assert engine.abstention_threshold == 0.35

    try:
        RecallEngine(FakeController([]), FakeSemanticProvider(), abstention_threshold=1.1)
    except ValueError:
        pass
    else:
        raise AssertionError("out-of-range abstention threshold must fail")
