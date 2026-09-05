import os
import json
import base64
import hmac
import hashlib
from datetime import datetime, timezone


class MissingHMACSecretError(RuntimeError):
    """Raised when the required HMAC secret is not set in the environment."""
    pass


class InvalidPaginationTokenError(RuntimeError):
    """Raised when a pagination token is malformed, tampered or expired."""
    pass


def _validate_payload_shape(payload: dict) -> None:
    """Defense-in-depth schema/bounds check run AFTER HMAC verification.

    The HMAC check already guarantees the payload was produced by this same
    server (nothing forged can pass it), but this still bounds every field's
    type and range explicitly rather than trusting "valid JSON object" alone
    -- protecting against a legacy/mismatched-schema token (e.g. from a
    future or rolled-back server version sharing the same secret) carrying a
    field of an unexpected type or an out-of-range value that a consumer
    might use unsafely (e.g. a negative or absurdly large `offset`).
    """
    offset = payload.get("offset", 0)
    if not isinstance(offset, int) or isinstance(offset, bool) or offset < 0:
        raise InvalidPaginationTokenError("Token offset must be a non-negative integer")
    if offset > 10_000_000:
        raise InvalidPaginationTokenError("Token offset exceeds maximum allowed value")

    page_size = payload.get("page_size")
    if page_size is not None and (
        not isinstance(page_size, int) or isinstance(page_size, bool) or not (0 < page_size <= 1000)
    ):
        raise InvalidPaginationTokenError("Token page_size must be an integer in (0, 1000]")

    for str_field in ("query_fp", "agent_id", "disclosure"):
        val = payload.get(str_field)
        if val is not None and not isinstance(val, str):
            raise InvalidPaginationTokenError(f"Token field '{str_field}' must be a string")

    for list_field in ("lifecycles", "types"):
        val = payload.get(list_field)
        if val is not None and (
            not isinstance(val, list) or not all(isinstance(v, str) for v in val)
        ):
            raise InvalidPaginationTokenError(f"Token field '{list_field}' must be a list of strings")

    exp = payload.get("expiration")
    if exp is not None and (not isinstance(exp, (int, float)) or isinstance(exp, bool)):
        raise InvalidPaginationTokenError("Token expiration must be numeric")


class PaginationToken:
    """Opaque, tamper-evident pagination token."""

    MAX_TOKEN_BYTES = 2048

    def __init__(self, payload: dict, secret: bytes):
        self.payload = payload
        self.secret = secret
        self.signature = hmac.new(
            secret,
            json.dumps(payload, separators=(",", ":"), sort_keys=True).encode(),
            hashlib.sha256,
        ).digest()

    def encode(self) -> str:
        payload_b = base64.urlsafe_b64encode(
            json.dumps(self.payload, separators=(",", ":"), sort_keys=True).encode()
        ).rstrip(b"=")
        sig_b = base64.urlsafe_b64encode(self.signature).rstrip(b"=")
        token = payload_b + b"." + sig_b
        if len(token) > self.MAX_TOKEN_BYTES:
            raise ValueError("Pagination token exceeds maximum size of 2 KB")
        return token.decode()

    @classmethod
    def decode(cls, token: str) -> dict:
        if not isinstance(token, str):
            raise InvalidPaginationTokenError("Pagination token must be a string")
        if len(token.encode("utf-8")) > cls.MAX_TOKEN_BYTES:
            raise InvalidPaginationTokenError("Pagination token exceeds maximum size of 2 KB")

        secret = os.getenv("MEMORY_CONTROLLER_HMAC_SECRET")
        if not secret:
            raise MissingHMACSecretError(
                "HMAC secret not configured in MEMORY_CONTROLLER_HMAC_SECRET"
            )
        secret_b = secret.encode()

        try:
            payload_b, sig_b = token.encode().split(b".")
            payload_json = base64.urlsafe_b64decode(
                payload_b + b"=" * (-len(payload_b) % 4)
            )
            payload = json.loads(payload_json)
            if not isinstance(payload, dict):
                raise ValueError("Token payload must be a JSON object")
            expected_sig = hmac.new(
                secret_b,
                json.dumps(payload, separators=(",", ":"), sort_keys=True).encode(),
                hashlib.sha256,
            ).digest()
            actual_sig = base64.urlsafe_b64decode(
                sig_b + b"=" * (-len(sig_b) % 4)
            )
        except Exception as exc:
            raise InvalidPaginationTokenError(f"Failed to parse token: {exc}") from exc

        if not hmac.compare_digest(expected_sig, actual_sig):
            raise InvalidPaginationTokenError("Token signature mismatch")

        exp_ts = payload.get("expiration")
        if exp_ts is not None:
            try:
                exp = datetime.fromtimestamp(exp_ts, tz=timezone.utc)
            except (TypeError, ValueError, OverflowError, OSError) as exc:
                raise InvalidPaginationTokenError(
                    f"Invalid token expiration: {exc}"
                ) from exc
            if datetime.now(tz=timezone.utc) > exp:
                raise InvalidPaginationTokenError("Token has expired")
        _validate_payload_shape(payload)
        return payload
