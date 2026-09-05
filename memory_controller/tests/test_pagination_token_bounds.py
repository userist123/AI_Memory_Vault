import os

import pytest

from memory_controller.security.pagination_token import (
    InvalidPaginationTokenError,
    MissingHMACSecretError,
    PaginationToken,
)


def test_decode_rejects_oversized_token_before_parsing(monkeypatch):
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "test_secret_for_pagination")
    oversized = "A" * (PaginationToken.MAX_TOKEN_BYTES + 1)

    with pytest.raises(InvalidPaginationTokenError, match="exceeds maximum size"):
        PaginationToken.decode(oversized)


def test_decode_requires_string_token(monkeypatch):
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "test_secret_for_pagination")

    with pytest.raises(InvalidPaginationTokenError, match="must be a string"):
        PaginationToken.decode(None)


def test_decode_still_requires_hmac_secret(monkeypatch):
    monkeypatch.delenv("MEMORY_CONTROLLER_HMAC_SECRET", raising=False)

    with pytest.raises(MissingHMACSecretError):
        PaginationToken.decode("abc.def")
