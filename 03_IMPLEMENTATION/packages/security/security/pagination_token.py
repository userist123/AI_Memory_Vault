import os
import json
import base64
import hmac
import hashlib
from datetime import datetime, timezone, timedelta

class MissingHMACSecretError(RuntimeError):
    """Raised when the required HMAC secret is not set in the environment."""
    pass

class InvalidPaginationTokenError(RuntimeError):
    """Raised when a pagination token is malformed, tampered or expired."""
    pass

class PaginationToken:
    """Opaque, tamper‑evident pagination token.

    The payload is a JSON object containing the fields required by the specification.
    The token is encoded as ``base64url(payload)`` + ``.`` + ``base64url(signature)``.
    The signature is an HMAC‑SHA256 over the payload using the secret.
    """

    def __init__(self, payload: dict, secret: bytes):
        self.payload = payload
        self.secret = secret
        self.signature = hmac.new(secret, json.dumps(payload, separators=(',', ':'), sort_keys=True).encode(), hashlib.sha256).digest()

    def encode(self) -> str:
        payload_b = base64.urlsafe_b64encode(json.dumps(self.payload, separators=(',', ':'), sort_keys=True).encode()).rstrip(b'=')
        sig_b = base64.urlsafe_b64encode(self.signature).rstrip(b'=')
        token = payload_b + b'.' + sig_b
        if len(token) > 2048:  # 2 KB limit
            raise ValueError("Pagination token exceeds maximum size of 2 KB")
        return token.decode()

    @classmethod
    def decode(cls, token: str) -> dict:
        # Helper to retrieve HMAC secret from environment without fallback
        def _get_secret() -> bytes:
            secret = os.getenv('MEMORY_CONTROLLER_HMAC_SECRET')
            if not secret:
                raise MissingHMACSecretError('HMAC secret not configured in MEMORY_CONTROLLER_HMAC_SECRET')
            return secret.encode()

        secret_b = _get_secret()
        try:
            payload_b, sig_b = token.encode().split(b'.')
            # Ensure proper base64 padding before decoding
            payload_json = base64.urlsafe_b64decode(payload_b + b'=' * (-len(payload_b) % 4))
            payload = json.loads(payload_json)
            expected_sig = hmac.new(secret_b, json.dumps(payload, separators=(',', ':'), sort_keys=True).encode(), hashlib.sha256).digest()
            actual_sig = base64.urlsafe_b64decode(sig_b + b'=' * (-len(sig_b) % 4))
        except Exception as e:
            raise InvalidPaginationTokenError(f"Failed to parse token: {e}")
        # Verify HMAC signature
        if not hmac.compare_digest(expected_sig, actual_sig):
            raise InvalidPaginationTokenError('Token signature mismatch')
        # Expiration handling (optional)
        exp_ts = payload.get('expiration')
        if exp_ts is not None:
            exp = datetime.fromtimestamp(exp_ts, tz=timezone.utc)
            if datetime.now(tz=timezone.utc) > exp:
                raise InvalidPaginationTokenError('Token has expired')
        return payload
