import os
import pytest
from uuid import uuid4
import time
from datetime import datetime, timezone, timedelta
import hashlib
import base64
import json
from memory_controller.controller import MemoryController, StorageEngine, Lifecycle, Principal, MissingHMACSecretError, InvalidPaginationTokenError
from memory_controller.security.pagination_token import PaginationToken

# Helper to set secret for tests
SECRET_ENV = 'MEMORY_CONTROLLER_HMAC_SECRET'
TEST_SECRET = 'test_secret_123'

@pytest.fixture(autouse=True)
def set_secret(monkeypatch):
    # Ensure secret is set for each test unless overridden
    monkeypatch.setenv(SECRET_ENV, TEST_SECRET)
    yield
    # cleanup not needed as monkeypatch resets

def make_controller():
    storage = StorageEngine()
    return MemoryController(storage)

def test_token_encode_decode_basic():
    query_fp = hashlib.sha256('test'.encode()).hexdigest()
    payload = {
        'offset': 0,
        'query_fp': query_fp,
        'agent_id': Principal.AI_AGENT.value,
        'page_size': 5,
        'expiration': int((datetime.now(timezone.utc) + timedelta(minutes=5)).timestamp())
    }
    token = PaginationToken(payload, TEST_SECRET.encode()).encode()
    decoded = PaginationToken.decode(token)
    assert decoded == payload

def test_token_tamper_detection():
    query_fp = hashlib.sha256('test'.encode()).hexdigest()
    payload = {
        'offset': 0,
        'query_fp': query_fp,
        'agent_id': Principal.AI_AGENT.value,
        'page_size': 5,
        'expiration': int((datetime.now(timezone.utc) + timedelta(minutes=5)).timestamp())
    }
    token = PaginationToken(payload, TEST_SECRET.encode()).encode()
    # Tamper by changing a character in the payload part (offset field)
    parts = token.split('.')
    tampered_payload = parts[0][:-1] + ('A' if parts[0][-1] != 'A' else 'B')
    tampered_token = tampered_payload + '.' + parts[1]
    with pytest.raises(InvalidPaginationTokenError):
        PaginationToken.decode(tampered_token)

def test_token_expiration_detection(monkeypatch):
    past = datetime.now(timezone.utc) - timedelta(minutes=1)
    query_fp = hashlib.sha256('test'.encode()).hexdigest()
    payload = {
        'offset': 0,
        'query_fp': query_fp,
        'agent_id': Principal.AI_AGENT.value,
        'page_size': 5,
        'expiration': int(past.timestamp())
    }
    token = PaginationToken(payload, TEST_SECRET.encode()).encode()
    with pytest.raises(InvalidPaginationTokenError):
        PaginationToken.decode(token)

def test_missing_secret_raises(monkeypatch):
    monkeypatch.delenv(SECRET_ENV, raising=False)
    query_fp = hashlib.sha256('test'.encode()).hexdigest()
    payload = {
        'offset': 0,
        'query_fp': query_fp,
        'agent_id': Principal.AI_AGENT.value,
        'page_size': 5,
        'expiration': int((datetime.now(timezone.utc) + timedelta(minutes=5)).timestamp())
    }
    token = PaginationToken(payload, TEST_SECRET.encode()).encode()
    with pytest.raises(MissingHMACSecretError):
        PaginationToken.decode(token)

def test_token_size_limit(monkeypatch):
    large_str = 'x' * 5000  # large enough to exceed 2KB after encoding
    query_fp = large_str
    payload = {
        'offset': 0,
        'query_fp': query_fp,
        'agent_id': Principal.AI_AGENT.value,
        'page_size': 5,
        'expiration': int((datetime.now(timezone.utc) + timedelta(minutes=5)).timestamp())
    }
    with pytest.raises(ValueError):
        PaginationToken(payload, TEST_SECRET.encode()).encode()

def test_search_pagination_success_and_validation(monkeypatch):
    ctrl = make_controller()
    # Populate storage with 15 dummy notes (ACTIVE lifecycle)
    for i in range(15):
        note = {
            'id': str(uuid4()),
            'type': 'knowledge',
            'lifecycle': Lifecycle.ACTIVE.value,
            'category': 'test',
            'tags': [],
            'created': '2023-01-01',
            'updated': '2023-01-01',
            'provenance': {'source_type': 'user', 'source_ref': 'unit'},
            'confidence': 'high',
            'verification': 'unverified',
            'relations': []
        }
        ctrl.storage.set(note['id'], note)
    # First page request
    result1 = ctrl.search(Principal.AI_AGENT, 'dummy', page_size=5)
    assert len(result1['results']) == 5
    token = result1.get('next_page_token')
    assert token is not None
    # Second page with same parameters should succeed
    result2 = ctrl.search(Principal.AI_AGENT, 'dummy', page_size=5, page_token=token)
    assert len(result2['results']) == 5
    # Mismatch query should raise
    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.AI_AGENT, 'other', page_size=5, page_token=token)
    # Mismatch principal should raise
    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.HUMAN, 'dummy', page_size=5, page_token=token)
    # Mismatch page_size should raise
    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.AI_AGENT, 'dummy', page_size=10, page_token=token)
    # Mismatch lifecycles filter should raise
    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.AI_AGENT, 'dummy', page_size=5, page_token=token, lifecycles=[Lifecycle.RAW])
    # Mismatch types filter should raise
    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.AI_AGENT, 'dummy', page_size=5, page_token=token, types=['other'])
    # Mismatch disclosure level (default is metadata) – simulate by changing controller attribute
    ctrl.default_disclosure = 'full'
    # Need a fresh token with new disclosure bound
    result3 = ctrl.search(Principal.AI_AGENT, 'dummy', page_size=5)
    new_token = result3.get('next_page_token')
    # Now use old token with new disclosure – should raise
    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.AI_AGENT, 'dummy', page_size=5, page_token=token)

    # Offset manipulation test – tamper offset in token
    token_parts = token.split('.')
    payload_bytes = base64.urlsafe_b64decode(token_parts[0] + '==')
    payload_dict = json.loads(payload_bytes)
    payload_dict['offset'] = 9999  # unrealistic offset
    tampered_payload = base64.urlsafe_b64encode(json.dumps(payload_dict, separators=(',', ':'), sort_keys=True).encode()).decode().rstrip('=')
    tampered_token = tampered_payload + '.' + token_parts[1]
    with pytest.raises(InvalidPaginationTokenError):
        ctrl.search(Principal.AI_AGENT, 'dummy', page_size=5, page_token=tampered_token)
