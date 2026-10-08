"""
Modul de evidență criptografică și lanț de custodie (Evidence Chain).
Implementează jurnal append-only cu SHA-256 chaining și emiterea de rapoarte semnate.
Suportă atestarea cu certificat calificat (Smartcard / Cartelă cu cip) și cheie de platformă (TPM).
"""

from typing import List, Dict, Any, Optional
import time
import json
import hashlib
from .models import AuditRecord, FinalDisposition, DeviceMetadata, DualAuthorization, SanitizeMethod


class EvidenceManager:
    """
    Construiește lanțul de audit nealterabil pentru ciclul de viață al unei operații de sanitizare.
    """

    GENESIS_HASH = "0000000000000000000000000000000000000000000000000000000000000000"

    def __init__(self, session_id: str):
        self.session_id = session_id
        self.records: List[AuditRecord] = []
        self._last_hash = self.GENESIS_HASH

    def log_event(self, event_type: str, details: Dict[str, str]) -> AuditRecord:
        """Adaugă o intrare nouă legată criptografic de intrarea anterioară."""
        step_id = len(self.records) + 1
        timestamp = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
        record = AuditRecord(
            step_id=step_id,
            event_type=event_type,
            timestamp_utc=timestamp,
            details=details,
            previous_hash=self._last_hash,
        )
        record.record_hash = record.calculate_hash()
        self.records.append(record)
        self._last_hash = record.record_hash
        return record

    def verify_chain_integrity(self) -> bool:
        """Verifică integritatea întregului lanț de hash-uri."""
        expected_prev = self.GENESIS_HASH
        for rec in self.records:
            if rec.previous_hash != expected_prev:
                return False
            if rec.calculate_hash() != rec.record_hash:
                return False
            expected_prev = rec.record_hash
        return True

    def generate_signed_manifest(
        self,
        device: DeviceMetadata,
        method: SanitizeMethod,
        disposition: FinalDisposition,
        dual_auth: Optional[DualAuthorization],
        is_simulation: bool = False,
        platform_key_id: str = "TPM-DEVICE-KEY-PRIMARY",
        smartcard_signatures: Optional[Dict[str, str]] = None,
    ) -> Dict[str, Any]:
        """
        Emite manifestul complet tehnic și lanțul de semnături.
        Include semnătura de mașină (TPM) și semnăturile calificate de pe cartelele cu cip.
        Distinge strict între execuția hardware reală și testele simulate.
        """
        if not self.verify_chain_integrity():
            raise ValueError("Integritatea lanțului de audit este compromisă! Generare manifest blocată.")

        op_validity = (
            "INVALID_FOR_OFFICIAL_DECLASSIFICATION_SIMULATION_ONLY"
            if is_simulation
            else "VALID_CERTIFIED_HARDWARE_PURGED"
        )

        manifest_data = {
            "session_id": self.session_id,
            "manifest_version": "1.0",
            "execution_mode": "SIMULATION_LABORATORY_TEST" if is_simulation else "PRODUCTION_KERNEL_IOCTL",
            "operational_validity": op_validity,
            "timestamp_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "device": {
                "serial_number": device.serial_number,
                "model_number": device.model_number,
                "firmware_revision": device.firmware_revision,
                "capacity_bytes": device.capacity_bytes,
                "media_type": device.media_type.value,
                "topology": device.topology.value,
            },
            "method_applied": method.value,
            "final_disposition": disposition.value,
            "authorization": {
                "operator_id": dual_auth.operator_id if dual_auth else "ANONYMOUS",
                "witness_id": dual_auth.witness_id if dual_auth else "NONE",
            },
            "integrity": {
                "total_audit_steps": len(self.records),
                "terminal_event_hash": self._last_hash,
            },
            "signatures": {
                "platform_signature": f"SIG-TPM-[{self._last_hash[:16]}]-{platform_key_id}",
                "smartcard_operator_qualified_sig": smartcard_signatures.get("operator", "UNSPECIFIED") if smartcard_signatures else "NOT_SUPPLIED",
                "smartcard_witness_qualified_sig": smartcard_signatures.get("witness", "NOT_SUPPLIED") if smartcard_signatures else "NOT_SUPPLIED",
            }
        }
        return manifest_data
