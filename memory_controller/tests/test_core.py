import pytest
import os
import time
from memory_controller.context.budget import ContextBudget, ContextBudgetError
from memory_controller.context.query_classifier import QueryClassifier, Intent
from memory_controller.cache.lru_cache import LRUCache
from memory_controller.context.retrieval import RetrievalEngine
from memory_controller.context.progressive_disclosure import ProgressiveDisclosure
from memory_controller.audit.logger import AuditLogger, get_logger
from memory_controller.security import sanitize_query, check_path_traversal, detect_cache_poisoning

# Helper mock storage engine
class MockStorageEngine:
    def __init__(self, notes=None):
        self.notes = notes or []
    def query(self, intent=None, lifecycle=None, types=None):
        # Simple filter: just return all notes (ignore args)
        return self.notes
    def get_provenance(self, note_id):
        return {"source": "mock", "id": note_id}

def test_context_budget_hard_limit():
    cfg = {"soft_context_budget": 5, "hard_context_budget": 10}
    budget = ContextBudget(cfg)
    budget.check_budget(9)  # within hard limit
    with pytest.raises(ContextBudgetError):
        budget.check_budget(11)

def test_query_classifier_defaults():
    classifier = QueryClassifier()
    result = classifier.classify("please read the knowledge notes")
    assert result["intent"] == Intent.READ
    assert "knowledge" in result["target_types"]
    # confidence low for default READ
    assert result["confidence"] == 0.5

def test_lru_cache_basic_eviction_and_ttl():
    cache = LRUCache(max_items=2, default_ttl=1)  # short ttl
    cache.set("value1", "k1")
    assert cache.get("k1") == "value1"
    time.sleep(1.1)  # expire
    assert cache.get("k1") is None
    # Fill beyond max_items to trigger LRU eviction
    cache.set("v2", "k2")
    cache.set("v3", "k3")
    # k2 should still be present (most recent), k3 present, only two items allowed
    assert cache.get("k2") == "v2"
    assert cache.get("k3") == "v3"
    # Adding another forces eviction of oldest (k2)
    cache.set("v4", "k4")
    # Since k2 was older than k3, it may be evicted
    # At least one of k2 or k3 will be missing; check total count
    remaining = [k for k in ["k2", "k3", "k4"] if cache.get(k) is not None]
    assert len(remaining) == 2

def test_retrieval_engine_respects_max_notes():
    notes = [{"id": f"n{i}", "content": f"content {i}"} for i in range(5)]
    storage = MockStorageEngine(notes=notes)
    cache = LRUCache(max_items=10)
    engine = RetrievalEngine(storage, cache=cache)
    classified = {"intent": Intent.READ, "lifecycle_filters": [], "target_types": [], "max_notes": 3}
    result = engine.retrieve(classified)
    assert len(result) == 3
    assert result[0]["id"] == "n0"

def test_progressive_disclosure_limits():
    notes = [{"id": f"n{i}", "type": "knowledge", "lifecycle": "ACTIVE", "confidence": 0.9,
              "content": "a " * 100} for i in range(3)]
    from memory_controller.context.budget import ContextBudget
    budget = ContextBudget({"soft_context_budget": 10, "hard_context_budget": 1000})
    pd = ProgressiveDisclosure(budget)
    meta = pd.metadata_only(notes)
    assert len(meta) == 3
    snippet = pd.snippet(notes, chars=10)
    assert snippet[0]["snippet"] == "a a a a a "[:10]
    full = pd.full_document(notes)
    # usage limited by hard budget (bytes). 1000 bytes allows all three notes (each ~200 bytes)
    assert len(full) == 3


def test_full_document_skips_oversized_note_and_keeps_later_candidate():
    from memory_controller.context.budget import ContextBudget
    budget = ContextBudget({"soft_context_budget": 10, "hard_context_budget": 500})
    pd = ProgressiveDisclosure(budget)
    notes = [
        {"id": "oversized", "content": "x" * 600},
        {"id": "small", "content": "relevant evidence"},
    ]

    full = pd.full_document(notes)

    assert [note["id"] for note in full] == ["small"]


def test_full_document_respects_token_budget():
    from memory_controller.context.budget import ContextBudget
    budget = ContextBudget({"soft_context_budget": 10000, "hard_context_budget": 10000,
                            "hard_limit_tokens": 10, "chars_per_token": 4})
    pd = ProgressiveDisclosure(budget)
    notes = [{"id": "a", "content": "word " * 30}, {"id": "b", "content": "ok"}]

    full = pd.full_document(notes)

    assert [note["id"] for note in full] == ["b"]

def test_audit_logger_writes_and_reads(tmp_path):
    log_file = tmp_path / "audit.log"
    logger = AuditLogger(str(log_file))
    logger.log(actor="human", operation="READ", target_id="note1", outcome="success")
    # read back
    with open(log_file, "r", encoding="utf-8") as f:
        line = f.readline().strip()
    import json
    entry = json.loads(line)
    assert entry["actor"] == "human"
    assert entry["operation"] == "READ"
    assert entry["target_id"] == "note1"

def test_security_sanitize_and_path():
    bad = "{{ secret }} <script>alert(1)</script> normal"
    clean = sanitize_query(bad)
    assert "{{" not in clean and "<script>" not in clean
    # path traversal detection
    with pytest.raises(ValueError):
        check_path_traversal("..\\outside\\file.txt")
    # cache poisoning detection
    detect_cache_poisoning("a"*64, "ok")
    with pytest.raises(ValueError):
        detect_cache_poisoning("invalid_key", "data")
