import pytest
import hashlib
from typing import Dict, Any

from memory_controller.controller import controller, Principal, Operation
from memory_controller.security.utils import sanitize_query, check_query_size, check_path_traversal, detect_cache_poisoning

def setup_function():
    controller.storage.store.clear()
    controller.cache.store.clear()
    controller.cache.hit_count = 0
    controller.cache.miss_count = 0
    
    # Insert a dummy note for path traversal tests
    controller.storage.set("valid-id-123", {
        "id": "valid-id-123",
        "lifecycle": "ACTIVE",
        "type": "knowledge",
        "category": "test",
        "confidence": "high",
        "created": "2026-08-09",
        "updated": "2026-08-09",
        "verification": "unverified",
        "tags": [],
        "relations": [],
        "provenance": {"source_type": "user", "source_ref": "test"}
    })

def test_prompt_injection_sanitization():
    # Existing sanitize_query contract
    malicious = "Hello {{prompt}} <script>alert(1)</script> World <html>"
    clean = sanitize_query(malicious)
    assert clean == "Hello   World"
    
    # Test via controller
    pack = controller.search(Principal.HUMAN, malicious)
    # The actual retrieval logic might not return anything, but we ensure no exception
    assert pack is not None

def test_query_size_boundary():
    # 4096 -> accepted
    valid_query = "A" * 4096
    pack = controller.search(Principal.HUMAN, valid_query)
    assert pack is not None
    
    # 4097 -> rejected
    invalid_query = "A" * 4097
    with pytest.raises(ValueError, match="exceeds maximum allowed"):
        controller.search(Principal.HUMAN, invalid_query)

def test_path_traversal_controller_operations():
    bad_paths = [
        "../etc/passwd",
        "../../secrets.txt",
        "..\\windows\\system32",
        "C:\\Windows\\system32\\cmd.exe",
        "/etc/passwd"
    ]
    
    for bad in bad_paths:
        with pytest.raises(ValueError, match="Path traversal detected|Absolute paths not allowed"):
            controller.read(Principal.ADMIN, bad)
        with pytest.raises(ValueError, match="Path traversal detected|Absolute paths not allowed"):
            controller.update(Principal.ADMIN, bad, {"category": "test"})
        with pytest.raises(ValueError, match="Path traversal detected|Absolute paths not allowed"):
            controller.archive(Principal.ADMIN, bad, "reason")
        with pytest.raises(ValueError, match="Path traversal detected|Absolute paths not allowed"):
            controller.review(Principal.ADMIN, bad, "approve")
        with pytest.raises(ValueError, match="Path traversal detected|Absolute paths not allowed"):
            controller.promote(Principal.ADMIN, bad)
            
    # Valid ID remains accepted (should not raise ValueError for path traversal)
    res = controller.read(Principal.ADMIN, "valid-id-123")
    assert res is not None

def test_cache_poisoning_malformed_key():
    with pytest.raises(ValueError, match="Invalid cache key format"):
        detect_cache_poisoning("bad-key", "value")
        
def test_cache_poisoning_oversized_payload():
    valid_key = hashlib.sha256(b"test").hexdigest()
    oversized = "A" * 1_000_001
    with pytest.raises(ValueError, match="exceeds size limit"):
        detect_cache_poisoning(valid_key, oversized)
        
    oversized_list = [{"id": "x" * 500_000}, {"id": "y" * 500_001}]
    with pytest.raises(ValueError, match="exceeds size limit"):
        detect_cache_poisoning(valid_key, oversized_list)

def test_poisoned_cache_entry_invalidation():
    query = "test cache poisoning"
    # First search -> MISS, populates cache
    controller.search(Principal.HUMAN, query)
    assert controller.cache.miss_count == 1
    assert controller.cache.hit_count == 0
    
    # Get the cache key that was stored
    keys = list(controller.cache.store.keys())
    assert len(keys) == 1
    stored_key = keys[0]
    
    # Poison the payload directly in the store
    controller.cache.store[stored_key].value = "A" * 1_000_001
    
    # Second search -> Should detect poisoning, invalidate entry, and treat as MISS
    controller.search(Principal.HUMAN, query)
    
    assert controller.cache.miss_count == 2
    assert controller.cache.hit_count == 0
    # The poisoned entry should have been replaced with the valid fresh data
    assert stored_key in controller.cache.store
    assert controller.cache.store[stored_key].value != "A" * 1_000_001

def test_valid_cache_entry_remains_usable():
    query = "test valid cache"
    controller.search(Principal.HUMAN, query)
    assert controller.cache.miss_count == 1
    assert controller.cache.hit_count == 0
    
    # Second search -> HIT
    controller.search(Principal.HUMAN, query)
    assert controller.cache.miss_count == 1
    assert controller.cache.hit_count == 1

def test_no_cross_principal_leakage():
    query = "test isolation"
    controller.search(Principal.HUMAN, query)
    
    hits_before = controller.cache.hit_count
    misses_before = controller.cache.miss_count
    
    # Different principal, same query
    controller.search(Principal.AI_AGENT, query)
    
    # Must be a MISS
    assert controller.cache.miss_count == misses_before + 1
    assert controller.cache.hit_count == hits_before
