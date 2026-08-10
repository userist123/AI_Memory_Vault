import pytest
import time
from uuid import uuid4
from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.authorizer import Principal
from memory_controller.cache.lru_cache import LRUCache
from memory_controller.context.budget import ContextBudget

@pytest.fixture
def controller():
    storage = StorageEngine()
    ctrl = MemoryController(storage)
    dummy_id = str(uuid4())
    ctrl.storage.set(dummy_id, {
        "id": dummy_id,
        "type": "knowledge",
        "lifecycle": Lifecycle.ACTIVE.value,
        "category": "test",
        "confidence": "high",
        "created": "2026-08-09",
        "updated": "2026-08-09",
        "verification": "unverified",
        "tags": [],
        "relations": [],
        "provenance": {"source_type": "user", "source_ref": "unit"}
    })
    ctrl.dummy_id = dummy_id
    return ctrl

def test_hit_miss_accounting(controller):
    cache = controller.cache
    cache.store.clear()
    cache.hit_count = 0
    cache.miss_count = 0
    
    # First identical request = MISS
    res1 = controller.search(Principal.HUMAN, "test query")
    assert cache.miss_count == 1
    assert cache.hit_count == 0
    
    # Second identical request = HIT
    res2 = controller.search(Principal.HUMAN, "test query")
    assert cache.miss_count == 1
    assert cache.hit_count == 1
    
    assert res1['results'][0]['id'] == res2['results'][0]['id']

def test_ttl_expiration(controller):
    cache = controller.cache
    cache.store.clear()
    cache.miss_count = 0
    
    controller.search(Principal.HUMAN, "ttl query")
    assert cache.miss_count == 1
    
    # Force TTL expiration by setting it to 0 and manipulating expiry
    key = list(cache.store.keys())[0]
    cache.store[key].expiry = 0
    
    # Second request immediately expired -> MISS
    controller.search(Principal.HUMAN, "ttl query")
    assert cache.miss_count == 2
    # Store gets repopulated with a new valid entry, so we do not assert it's missing.

def test_principal_isolation(controller):
    cache = controller.cache
    cache.miss_count = 0
    
    # HUMAN vs AI_AGENT
    controller.search(Principal.HUMAN, "isolation query")
    controller.search(Principal.AI_AGENT, "isolation query")
    assert cache.miss_count == 2
    
    # HUMAN vs ADMIN
    controller.search(Principal.ADMIN, "isolation query")
    assert cache.miss_count == 3
    
    # AI_AGENT vs ADMIN
    controller.search(Principal.ADMIN, "another")
    controller.search(Principal.AI_AGENT, "another")
    assert cache.miss_count == 5

def test_canonical_query_fingerprint_isolation(controller):
    cache = controller.cache
    cache.miss_count = 0
    cache.hit_count = 0
    
    # "same query  " and "same query" should sanitize to the same string
    controller.search(Principal.HUMAN, "same query  ")
    assert cache.miss_count == 1
    controller.search(Principal.HUMAN, "  same query")
    assert cache.hit_count == 1
    
    # different query = different identity
    controller.search(Principal.HUMAN, "different query")
    assert cache.miss_count == 2

def test_filter_isolation(controller):
    cache = controller.cache
    cache.miss_count = 0
    
    # Baseline
    controller.search(Principal.HUMAN, "q")
    assert cache.miss_count == 1
    
    # Lifecycle filter changed
    controller.search(Principal.HUMAN, "q", lifecycles=[Lifecycle.ACTIVE])
    # Ensure it's treated as a miss due to the filter change
    assert cache.miss_count == 2
    
    # Target type changed
    controller.search(Principal.HUMAN, "q", types=["knowledge"])
    assert cache.miss_count == 3
    
    # Disclosure level changed
    controller.default_disclosure = 'snippet'
    controller.search(Principal.HUMAN, "q")
    assert cache.miss_count == 4

def test_cache_poisoning(controller):
    cache = controller.cache
    cache.miss_count = 0
    
    # Populate under A
    controller.search(Principal.HUMAN, "poison query")
    assert cache.miss_count == 1
    
    # Request under B
    controller.search(Principal.AI_AGENT, "poison query")
    # B missed, did not get A's cache
    assert cache.miss_count == 2

def test_budget_mismatch(monkeypatch, controller):
    cache = controller.cache
    
    # Mock budget loader to return large budget
    def mock_large(*args):
        return ContextBudget({"soft_context_budget": 10000, "hard_context_budget": 10000})
    monkeypatch.setattr('memory_controller.controller.load_agent_budget', mock_large)
    
    # Cache result under larger budget
    controller.search(Principal.HUMAN, "budget query")
    
    # Now tighter budget
    def mock_tight(*args):
        return ContextBudget({"soft_context_budget": 10, "hard_context_budget": 10})
    monkeypatch.setattr('memory_controller.controller.load_agent_budget', mock_tight)
    
    # We want to verify that when it falls through, it queries storage again.
    # The cache.get will actually hit (incrementing hit_count), but RetrievalEngine will reject it.
    original_query = controller.storage.query
    query_calls = 0
    
    def mock_query(*args, **kwargs):
        nonlocal query_calls
        query_calls += 1
        return original_query(*args, **kwargs)
        
    monkeypatch.setattr(controller.storage, 'query', mock_query)
    
    # Tight budget will reject cached result because size > 10 bytes
    controller.search(Principal.HUMAN, "budget query")
    assert query_calls == 1

def test_mutation_invalidation_propose(controller):
    cache = controller.cache
    controller.search(Principal.HUMAN, "propose query")
    hits = cache.hit_count
    
    # Mutation (using valid UUID)
    controller.propose(Principal.HUMAN, {"id": str(uuid4()), "content": "c"})
    
    controller.search(Principal.HUMAN, "propose query")
    # Should miss because cache was invalidated
    assert cache.hit_count == hits

def test_mutation_invalidation_update(controller):
    cache = controller.cache
    controller.search(Principal.HUMAN, "update query")
    hits = cache.hit_count
    
    # Mutation
    controller.update(Principal.ADMIN, controller.dummy_id, {"category": "new_cat"})
    
    controller.search(Principal.HUMAN, "update query")
    assert cache.hit_count == hits

def test_mutation_invalidation_archive(controller):
    cache = controller.cache
    controller.search(Principal.HUMAN, "archive query")
    hits = cache.hit_count
    
    # Mutation
    controller.archive(Principal.ADMIN, controller.dummy_id, "reason")
    
    controller.search(Principal.HUMAN, "archive query")
    assert cache.hit_count == hits

def test_mutation_invalidation_review_promote(controller):
    cache = controller.cache
    
    # Create raw (using valid UUID)
    nid = controller.propose(Principal.HUMAN, {"id": str(uuid4()), "content": "c"})
    
    # Review
    controller.search(Principal.HUMAN, "review query")
    hits = cache.hit_count
    controller.review(Principal.ADMIN, nid, "approve", "ok")
    controller.search(Principal.HUMAN, "review query")
    assert cache.hit_count == hits
    
    # Promote
    controller.search(Principal.HUMAN, "promote query")
    hits = cache.hit_count
    controller.promote(Principal.ADMIN, nid)
    controller.search(Principal.HUMAN, "promote query")
    assert cache.hit_count == hits
