"""
Modele de date și structuri pentru Secure Sanitization Engine (TOE-SSE-v1).
Respectă terminologia INFOSEC 14, HG 585/2002, NIST SP 800-88r2 și IEEE 2883-2022.
Include distincția strictă între execuția hardware reală și simularea de laborator.
"""

from dataclasses import dataclass, field
from enum import Enum
from typing import Dict, List, Optional
import time
import hashlib


class Jurisdiction(str, Enum):
    RO = "RO"
    NATO = "NATO"
    EU = "EU"
    BILATERAL = "BILATERAL"


class ClassificationLevel(str, Enum):
    UNCLASSIFIED = "NECONFIDENTIAL"
    SECRET_DE_SERVICIU = "SECRET_DE_SERVICIU"
    CONFIDENTIAL = "CONFIDENTIAL"
    SECRET = "SECRET"
    STRICT_SECRET = "STRICT_SECRET"
    
    # NATO
    NATO_CONFIDENTIAL = "NATO_CONFIDENTIAL"
    NATO_SECRET = "NATO_SECRET"
    COSMIC_TOP_SECRET = "COSMIC_TOP_SECRET"
    
    # UE
    EU_CONFIDENTIAL = "EU_CONFIDENTIAL"
    EU_SECRET = "EU_SECRET"
    EU_TOP_SECRET = "EU_TOP_SECRET"


class MediaType(str, Enum):
    NVME_SSD = "NVME_SSD"
    SATA_SSD = "SATA_SSD"
    SATA_HDD = "SATA_HDD"
    SAS_HDD = "SAS_HDD"
    USB_FLASH = "USB_FLASH"
    OPTICAL = "OPTICAL"
    UNKNOWN_OR_DEFECTIVE = "UNKNOWN_OR_DEFECTIVE"


class DeviceTopology(str, Enum):
    NATIVE_PCIE = "NATIVE_PCIE"
    NATIVE_SATA = "NATIVE_SATA"
    NATIVE_SAS = "NATIVE_SAS"
    BRIDGED_USB = "BRIDGED_USB"
    BRIDGED_RAID = "BRIDGED_RAID"


class SanitizeMethod(str, Enum):
    NVME_SANITIZE_BLOCK_ERASE = "NVME_SANITIZE_BLOCK_ERASE"
    NVME_SANITIZE_CRYPTO_ERASE = "NVME_SANITIZE_CRYPTO_ERASE"
    NVME_SANITIZE_OVERWRITE = "NVME_SANITIZE_OVERWRITE"
    ATA_SANITIZE_BLOCK = "ATA_SANITIZE_BLOCK"
    ATA_ENHANCED_SECURE_ERASE = "ATA_ENHANCED_SECURE_ERASE"
    SCSI_SANITIZE_BLOCK = "SCSI_SANITIZE_BLOCK"
    PHYSICAL_DESTRUCTION_REQUIRED = "PHYSICAL_DESTRUCTION_REQUIRED"


class FinalDisposition(str, Enum):
    # Rezultate operaționale reale (obținute exclusiv prin kernel IOCTL pe block device real)
    CONFORM_PURGED = "CONFORM_PURGED"
    CONFORM_CLEARED = "CONFORM_CLEARED"
    NON_CONFORM_REQUIRES_DESTRUCTION = "NON_CONFORM_REQUIRES_DESTRUCTION"
    INCOMPLETE_ABORTED = "INCOMPLETE_ABORTED"
    ABORTED_UNSUPPORTED_ENVIRONMENT = "ABORTED_UNSUPPORTED_ENVIRONMENT"
    
    # Rezultat exclusiv pentru simulări și teste de laborator (FĂRĂ VALOARE DE DECLASIFICARE)
    SIMULATED_NOT_SANITIZED = "SIMULATED_NOT_SANITIZED"


@dataclass(frozen=True)
class DeviceMetadata:
    serial_number: str
    model_number: str
    firmware_revision: str
    capacity_bytes: int
    media_type: MediaType
    topology: DeviceTopology
    bus_path: str
    hpa_detected: bool = False
    dco_detected: bool = False
    is_healthy: bool = True
    crypto_history_certified: bool = False


@dataclass
class DualAuthorization:
    operator_id: str
    operator_token: str
    witness_id: str
    witness_token: str
    timestamp: float = field(default_factory=time.time)
    
    def is_valid(self) -> bool:
        return bool(self.operator_id and self.witness_id and 
                    self.operator_id != self.witness_id and
                    self.operator_token and self.witness_token)


@dataclass
class AuditRecord:
    step_id: int
    event_type: str
    timestamp_utc: str
    details: Dict[str, str]
    previous_hash: str
    record_hash: str = ""

    def calculate_hash(self) -> str:
        payload = f"{self.step_id}:{self.event_type}:{self.timestamp_utc}:{self.previous_hash}:{sorted(self.details.items())}"
        return hashlib.sha256(payload.encode("utf-8")).hexdigest()
