"""Secure local execution bridge for AI Memory Vault."""

from .crypto import Ed25519Signer, X25519Envelope, canonical_json
from .policy import BridgePolicy, CapabilityToken, PolicyError
from .replay import ReplayGuard
from .antigravity import AntigravityExecutionError, AntigravitySession

from .bridge import BridgeRequestError, SecureBridge

from .client import SecureBridgeClient, SecureBridgeTransportError

from .crypto import packet_bytes, packet_sha256
from .config import BridgeConfig, BridgeConfigError, load_bridge_config
