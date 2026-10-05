"""Secure local execution bridge for AI Memory Vault."""

from .crypto import Ed25519Signer, X25519Envelope, canonical_json
from .policy import BridgePolicy, CapabilityToken, PolicyError
from .replay import ReplayGuard
from .antigravity import AntigravityExecutionError, AntigravitySession
