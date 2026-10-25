"""
Trading Bot — Secure Credential Storage
Encrypts API keys with a master password using Fernet (AES-128-CBC).

The Fernet key is derived from the master password with PBKDF2-HMAC-SHA256
(600 000 iterations) and a random 16-byte salt generated for every save. The
salt and iteration count are stored in a header in front of the Fernet token:

    tbkdf2$<iterations>$<urlsafe-b64 salt>$<fernet token>

Files written by older versions (a bare Fernet token whose key was an unsalted
SHA-256 of the password) are still readable; after a successful legacy decrypt
the file is re-encrypted in the new format.
"""
import json
import logging
import base64
import hashlib
import os
from pathlib import Path
from cryptography.fernet import Fernet, InvalidToken
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.kdf.pbkdf2 import PBKDF2HMAC
from core.config import CREDENTIALS_FILE

KDF_MAGIC = b"tbkdf2"
KDF_ITERATIONS = 600_000
SALT_BYTES = 16
# Accepted iteration counts when reading a file; bounds a tampered header.
KDF_MIN_ITERATIONS = 100_000
KDF_MAX_ITERATIONS = 10_000_000


def _derive_key(master_password: str, salt: bytes, iterations: int = KDF_ITERATIONS) -> bytes:
    """Derive a Fernet key from a master password with salted PBKDF2-HMAC-SHA256."""
    kdf = PBKDF2HMAC(algorithm=hashes.SHA256(), length=32, salt=salt, iterations=iterations)
    return base64.urlsafe_b64encode(kdf.derive(master_password.encode("utf-8")))


def _legacy_derive_key(master_password: str) -> bytes:
    """Key derivation used by files written before the salted KDF (read-only migration path)."""
    digest = hashlib.sha256(master_password.encode("utf-8")).digest()
    return base64.urlsafe_b64encode(digest)


def _encrypt(data: dict, master_password: str) -> bytes:
    salt = os.urandom(SALT_BYTES)
    fernet = Fernet(_derive_key(master_password, salt))
    payload = json.dumps(data, ensure_ascii=False).encode("utf-8")
    token = fernet.encrypt(payload)
    return b"$".join([KDF_MAGIC, str(KDF_ITERATIONS).encode("ascii"),
                      base64.urlsafe_b64encode(salt), token])


def _write_atomic(path: Path, blob: bytes):
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_bytes(blob)
    os.replace(tmp, path)


def save_credentials(data: dict, master_password: str):
    """Encrypt and save broker credentials to disk."""
    _write_atomic(CREDENTIALS_FILE, _encrypt(data, master_password))


def load_credentials(master_password: str) -> dict:
    """Load and decrypt broker credentials. Returns {} on failure."""
    if not CREDENTIALS_FILE.exists():
        return {}
    blob = CREDENTIALS_FILE.read_bytes()
    try:
        if blob.startswith(KDF_MAGIC + b"$"):
            _, iterations, salt_b64, token = blob.split(b"$", 3)
            salt = base64.urlsafe_b64decode(salt_b64)
            rounds = int(iterations)
            if not KDF_MIN_ITERATIONS <= rounds <= KDF_MAX_ITERATIONS:
                return {}
            fernet = Fernet(_derive_key(master_password, salt, rounds))
            return json.loads(fernet.decrypt(token).decode("utf-8"))
        # Legacy format: bare Fernet token keyed by unsalted SHA-256.
        fernet = Fernet(_legacy_derive_key(master_password))
        data = json.loads(fernet.decrypt(blob).decode("utf-8"))
    except (InvalidToken, ValueError):
        # ValueError covers json.JSONDecodeError and a malformed header.
        return {}
    # Successful legacy decrypt: migrate to the salted KDF. If the rewrite fails
    # the original file is left untouched, so nothing is lost.
    try:
        save_credentials(data, master_password)
    except OSError as exc:
        logging.getLogger(__name__).warning(
            "Credentials stay in the legacy format; migration failed: %s", exc
        )
    return data


def has_credentials() -> bool:
    return CREDENTIALS_FILE.exists()


def delete_credentials():
    if CREDENTIALS_FILE.exists():
        CREDENTIALS_FILE.unlink()
