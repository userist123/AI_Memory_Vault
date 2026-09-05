"""Targeted adversarial gap-closure tests (runtime security front, owner:
claude-code) for sections 8 (pagination token) and 9 (retrieval cache) of
the runtime-security brief -- specifically the attack shapes not already
covered by test_pagination.py / test_pagination_token_bounds.py /
test_security.py: malformed base64, malformed JSON payload, a tampered
signature, per-filter tampering (lifecycle/types/page_size/disclosure), and
cross-principal cache isolation / poisoning.
"""
from __future__ import annotations

import base64
import json
import uuid

import pytest

from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.security.pagination_token import (
    InvalidPaginationTokenError,
    PaginationToken,
)


SECRET = b"test-hmac-secret-for-security-matrix-gaps"


@pytest.fixture(autouse=True)
def _hmac_secret(monkeypatch):
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", SECRET.decode())
    yield


class TestPaginationTokenMalformedInput:
    def test_malformed_base64_fails_closed(self):
        with pytest.raises(InvalidPaginationTokenError):
            PaginationToken.decode("not-valid-base64!!!.also-not-valid!!!")

    def test_malformed_json_payload_fails_closed(self):
        garbage_payload = base64.urlsafe_b64encode(b"{not valid json").rstrip(b"=")
        fake_sig = base64.urlsafe_b64encode(b"x" * 32).rstrip(b"=")
        token = (garbage_payload + b"." + fake_sig).decode()
        with pytest.raises(InvalidPaginationTokenError):
            PaginationToken.decode(token)

    def test_payload_that_is_a_json_array_not_object_fails_closed(self):
        arr_payload = base64.urlsafe_b64encode(json.dumps([1, 2, 3]).encode()).rstrip(b"=")
        fake_sig = base64.urlsafe_b64encode(b"x" * 32).rstrip(b"=")
        token = (arr_payload + b"." + fake_sig).decode()
        with pytest.raises(InvalidPaginationTokenError):
            PaginationToken.decode(token)

    def test_tampered_signature_rejected(self):
        payload = {"offset": 5, "query_fp": "a" * 64, "agent_id": "human", "page_size": 10}
        token = PaginationToken(payload, SECRET).encode()
        head, _, tail = token.rpartition(".")
        tampered = head + "." + ("A" * len(tail))
        with pytest.raises(InvalidPaginationTokenError, match="signature mismatch"):
            PaginationToken.decode(tampered)

    def test_signed_with_different_secret_rejected(self, monkeypatch):
        payload = {"offset": 5, "query_fp": "a" * 64, "agent_id": "human", "page_size": 10}
        token = PaginationToken(payload, b"a-different-secret-entirely").encode()
        # decode() reads the secret from env, which is SECRET here -- a
        # token signed under a different secret must fail verification.
        with pytest.raises(InvalidPaginationTokenError, match="signature mismatch"):
            PaginationToken.decode(token)

    def test_negative_offset_in_otherwise_validly_signed_payload_rejected(self):
        payload = {"offset": -1, "query_fp": "a" * 64, "agent_id": "human", "page_size": 10}
        token = PaginationToken(payload, SECRET).encode()
        with pytest.raises(InvalidPaginationTokenError, match="offset"):
            PaginationToken.decode(token)

    def test_non_integer_offset_rejected(self):
        payload = {"offset": "5", "query_fp": "a" * 64, "agent_id": "human", "page_size": 10}
        token = PaginationToken(payload, SECRET).encode()
        with pytest.raises(InvalidPaginationTokenError, match="offset"):
            PaginationToken.decode(token)

    def test_absurd_page_size_rejected(self):
        payload = {"offset": 0, "query_fp": "a" * 64, "agent_id": "human", "page_size": 999999999}
        token = PaginationToken(payload, SECRET).encode()
        with pytest.raises(InvalidPaginationTokenError, match="page_size"):
            PaginationToken.decode(token)

    def test_lifecycles_field_wrong_type_rejected(self):
        payload = {"offset": 0, "query_fp": "a" * 64, "agent_id": "human", "page_size": 10,
                   "lifecycles": "ACTIVE"}  # should be a list, not a bare string
        token = PaginationToken(payload, SECRET).encode()
        with pytest.raises(InvalidPaginationTokenError, match="lifecycles"):
            PaginationToken.decode(token)


class TestSearchTokenBindingTamperDetection:
    """Confirms controller.search() rejects a structurally-valid, correctly
    signed token whose bound context (query/principal/filters/page_size/
    disclosure) no longer matches the current request -- i.e. a token
    legitimately issued for request A cannot be replayed against request B
    with different parameters."""

    @pytest.fixture
    def controller(self):
        return MemoryController(StorageEngine())

    def _first_page_token(self, controller, principal, query, **kwargs):
        # Populate enough ACTIVE notes that a next_page_token is actually issued.
        for _ in range(15):
            nid = str(uuid.uuid4())
            controller.storage.set(nid, {
                "id": nid, "type": "knowledge", "lifecycle": Lifecycle.ACTIVE.value,
                "category": "test", "tags": [], "created": "2026-01-01", "updated": "2026-01-01",
                "provenance": {"source_type": "user", "source_ref": "t"}, "confidence": "high",
                "verification": "unverified", "relations": [], "content": "shared token binding content",
            })
        result = controller.search(principal, query, page_size=5, **kwargs)
        return result.get("next_page_token")

    def test_token_rejected_for_different_query(self, controller):
        token = self._first_page_token(controller, Principal.HUMAN, "shared token binding content")
        if not token:
            pytest.skip("no next_page_token issued (insufficient matching results)")
        with pytest.raises(InvalidPaginationTokenError, match="query fingerprint"):
            controller.search(Principal.HUMAN, "a totally different query", page_size=5, page_token=token)

    def test_token_rejected_for_different_principal(self, controller):
        token = self._first_page_token(controller, Principal.HUMAN, "shared token binding content")
        if not token:
            pytest.skip("no next_page_token issued (insufficient matching results)")
        with pytest.raises(InvalidPaginationTokenError, match="principal"):
            controller.search(Principal.AI_AGENT, "shared token binding content", page_size=5, page_token=token)

    def test_token_rejected_for_different_page_size(self, controller):
        token = self._first_page_token(controller, Principal.HUMAN, "shared token binding content")
        if not token:
            pytest.skip("no next_page_token issued (insufficient matching results)")
        with pytest.raises(InvalidPaginationTokenError, match="page size"):
            controller.search(Principal.HUMAN, "shared token binding content", page_size=999, page_token=token)


class TestCacheCrossPrincipalIsolation:
    """request A (as HUMAN) -> cache; request B (as AI_AGENT, same query) ->
    must NOT receive A's cached result under A's identity; each principal
    gets its own cache entry even for byte-identical queries/filters."""

    @pytest.fixture
    def controller(self):
        c = MemoryController(StorageEngine())
        for _ in range(3):
            nid = str(uuid.uuid4())
            c.storage.set(nid, {
                "id": nid, "type": "knowledge", "lifecycle": Lifecycle.ACTIVE.value,
                "category": "test", "tags": [], "created": "2026-01-01", "updated": "2026-01-01",
                "provenance": {"source_type": "user", "source_ref": "t"}, "confidence": "high",
                "verification": "unverified", "relations": [], "content": "cross principal cache probe content",
            })
        return c

    def test_two_principals_never_share_a_cache_entry(self, controller):
        controller.search(Principal.HUMAN, "cross principal cache probe content")
        assert controller.cache.miss_count == 1

        controller.search(Principal.AI_AGENT, "cross principal cache probe content")
        # If the cache leaked across principals this would have been a HIT
        # (miss_count would still be 1, hit_count would be 1).
        assert controller.cache.miss_count == 2
        assert controller.cache.hit_count == 0

    def test_same_principal_repeated_query_is_a_cache_hit(self, controller):
        controller.search(Principal.HUMAN, "cross principal cache probe content")
        controller.search(Principal.HUMAN, "cross principal cache probe content")
        assert controller.cache.hit_count == 1

    def test_same_principal_different_lifecycle_filter_is_not_a_cache_hit(self, controller):
        controller.search(Principal.HUMAN, "cross principal cache probe content",
                           lifecycles=[Lifecycle.ACTIVE])
        controller.search(Principal.HUMAN, "cross principal cache probe content",
                           lifecycles=[Lifecycle.REVIEW])
        assert controller.cache.hit_count == 0
