from __future__ import annotations

import base64
import json
import os
from dataclasses import dataclass

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey, Ed25519PublicKey
from cryptography.hazmat.primitives.asymmetric.x25519 import X25519PrivateKey, X25519PublicKey
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.primitives.kdf.hkdf import HKDF


def canonical_json(value: object) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


def _b64(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).decode("ascii").rstrip("=")


def _unb64(value: str) -> bytes:
    return base64.urlsafe_b64decode(value + "=" * (-len(value) % 4))


@dataclass(frozen=True)
class Ed25519Signer:
    private_key: Ed25519PrivateKey
    public_key: Ed25519PublicKey

    @classmethod
    def generate(cls) -> "Ed25519Signer":
        private = Ed25519PrivateKey.generate()
        return cls(private, private.public_key())

    def sign(self, payload: bytes) -> str:
        return _b64(self.private_key.sign(payload))

    @staticmethod
    def verify(public_key: Ed25519PublicKey, payload: bytes, signature: str) -> None:
        public_key.verify(_unb64(signature), payload)


@dataclass(frozen=True)
class X25519Envelope:
    private_key: X25519PrivateKey
    public_key: X25519PublicKey

    @classmethod
    def generate(cls) -> "X25519Envelope":
        private = X25519PrivateKey.generate()
        return cls(private, private.public_key())

    @staticmethod
    def encrypt_for_public_key(public_key: X25519PublicKey, plaintext: bytes, aad: bytes = b"") -> dict[str, str]:
        ephemeral = X25519PrivateKey.generate()
        shared = ephemeral.exchange(public_key)
        key = HKDF(algorithm=hashes.SHA256(), length=32, salt=None, info=b"amv-bridge-v1").derive(shared)
        nonce = os.urandom(12)
        ciphertext = AESGCM(key).encrypt(nonce, plaintext, aad)
        epk = ephemeral.public_key().public_bytes(
            serialization.Encoding.Raw, serialization.PublicFormat.Raw
        )
        return {
            "version": "amv-envelope-v1",
            "alg": "X25519+HKDF-SHA256+AES-256-GCM",
            "ephemeral_public_key": _b64(epk),
            "nonce": _b64(nonce),
            "ciphertext": _b64(ciphertext),
        }

    def encrypt(self, plaintext: bytes, aad: bytes = b"") -> dict[str, str]:
        return self.encrypt_for_public_key(self.public_key, plaintext, aad)

    def decrypt(self, envelope: dict[str, str], aad: bytes = b"") -> bytes:
        if envelope.get("version") != "amv-envelope-v1":
            raise ValueError("unsupported envelope version")
        if envelope.get("alg") != "X25519+HKDF-SHA256+AES-256-GCM":
            raise ValueError("unsupported envelope algorithm")
        ephemeral = X25519PublicKey.from_public_bytes(_unb64(envelope["ephemeral_public_key"]))
        shared = self.private_key.exchange(ephemeral)
        key = HKDF(algorithm=hashes.SHA256(), length=32, salt=None, info=b"amv-bridge-v1").derive(shared)
        return AESGCM(key).decrypt(_unb64(envelope["nonce"]), _unb64(envelope["ciphertext"]), aad)
