"""
Trading Bot — Secure Credential Storage
Encrypts API keys with a master password using Fernet (AES-128-CBC).
"""
import json
import base64
import hashlib
from pathlib import Path
from cryptography.fernet import Fernet, InvalidToken
from core.config import CREDENTIALS_FILE


def _derive_key(master_password: str) -> bytes:
    """Derive a Fernet key from a master password using SHA-256."""
    digest = hashlib.sha256(master_password.encode("utf-8")).digest()
    return base64.urlsafe_b64encode(digest)


def save_credentials(data: dict, master_password: str):
    """Encrypt and save broker credentials to disk."""
    key = _derive_key(master_password)
    fernet = Fernet(key)
    payload = json.dumps(data, ensure_ascii=False).encode("utf-8")
    encrypted = fernet.encrypt(payload)
    CREDENTIALS_FILE.write_bytes(encrypted)


def load_credentials(master_password: str) -> dict:
    """Load and decrypt broker credentials. Returns {} on failure."""
    if not CREDENTIALS_FILE.exists():
        return {}
    try:
        key = _derive_key(master_password)
        fernet = Fernet(key)
        encrypted = CREDENTIALS_FILE.read_bytes()
        payload = fernet.decrypt(encrypted)
        return json.loads(payload.decode("utf-8"))
    except (InvalidToken, json.JSONDecodeError):
        return {}


def has_credentials() -> bool:
    return CREDENTIALS_FILE.exists()


def delete_credentials():
    if CREDENTIALS_FILE.exists():
        CREDENTIALS_FILE.unlink()
