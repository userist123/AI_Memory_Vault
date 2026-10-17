"""
Secure Sanitization Engine (TOE-SSE-v1) Package.
Include motorul bootabil (TOE) și conectorul de export către Registratura Electronică (Non-TOE).
"""
from .models import (
    Jurisdiction,
    ClassificationLevel,
    MediaType,
    DeviceTopology,
    SanitizeMethod,
    FinalDisposition,
    DeviceMetadata,
    DualAuthorization,
    AuditRecord,
)
from .policy_engine import PolicyEngine
from .hardware_adapter import HardwareAdapter, StorageCommandError
from .evidence_chain import EvidenceManager
from .state_machine import SanitizationSession, EngineState
from .smartcard_auth import SmartcardAuthenticator, SmartcardError
from .report_generator import DigitalArchiveDocument
from .registry_connector import RegistryBridgeClient, RegistryExportPackage

__all__ = [
    "Jurisdiction",
    "ClassificationLevel",
    "MediaType",
    "DeviceTopology",
    "SanitizeMethod",
    "FinalDisposition",
    "DeviceMetadata",
    "DualAuthorization",
    "AuditRecord",
    "PolicyEngine",
    "HardwareAdapter",
    "StorageCommandError",
    "EvidenceManager",
    "SanitizationSession",
    "EngineState",
    "SmartcardAuthenticator",
    "SmartcardError",
    "DigitalArchiveDocument",
    "RegistryBridgeClient",
    "RegistryExportPackage",
]
