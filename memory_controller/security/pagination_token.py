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
        return payload
