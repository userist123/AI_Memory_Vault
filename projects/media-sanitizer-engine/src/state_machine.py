"""
Mașina de stări a sanitizării (Sanitization State Machine).
Orchestrează fluxul de lucru tranzacțional, verificările anti-eroare și controlul dual.
Distinge categoric între execuția hardware reală și simularea de test.
"""

from enum import Enum
from typing import Optional, Dict, Any
import uuid
import time

from .models import (
    Jurisdiction,
    ClassificationLevel,
    DeviceMetadata,
    DualAuthorization,
    SanitizeMethod,
    FinalDisposition,
)
from .policy_engine import PolicyEngine
from .hardware_adapter import HardwareAdapter, StorageCommandError
from .evidence_chain import EvidenceManager


class EngineState(str, Enum):
    IDLE = "IDLE"
    DEVICE_IDENTIFIED = "DEVICE_IDENTIFIED"
    TARGET_VERIFIED = "TARGET_VERIFIED"
    POLICY_EVALUATED = "POLICY_EVALUATED"
    AUTHORIZED = "AUTHORIZED"
    SANITIZING = "SANITIZING"
    VERIFYING = "VERIFYING"
    COMPLETED = "COMPLETED"
    FAILED_REJECTED = "FAILED_REJECTED"


class SanitizationSession:
    """
    Sesiune de sanitizare tranzacțională pentru un dispozitiv specific.
    """

    def __init__(
        self,
        device: DeviceMetadata,
        classification: ClassificationLevel,
        jurisdiction: Jurisdiction = Jurisdiction.RO,
        hardware_adapter: Optional[HardwareAdapter] = None,
    ):
        self.session_id = str(uuid.uuid4())
        self.device = device
        self.classification = classification
        self.jurisdiction = jurisdiction
        self.state = EngineState.IDLE
        
        self.policy_engine = PolicyEngine()
        # Implicit: dacă nu se specifică explicit, hardware_adapter pornește în mod REAL (fără simulare ascunsă)
        self.adapter = hardware_adapter or HardwareAdapter(simulation_mode=False)
        self.evidence = EvidenceManager(session_id=self.session_id)
        
        self.authorized_method: Optional[SanitizeMethod] = None
        self.dual_auth: Optional[DualAuthorization] = None
        self.final_disposition: Optional[FinalDisposition] = None
        self.rejection_reason: str = ""

        # Înregistrare inițială a dispozitivului
        self.evidence.log_event("DEVICE_DISCOVERED", {
            "serial": self.device.serial_number,
            "model": self.device.model_number,
            "capacity": str(self.device.capacity_bytes),
            "media_type": self.device.media_type.value,
            "topology": self.device.topology.value,
            "simulation_mode": str(self.adapter.simulation_mode),
        })
        self.state = EngineState.DEVICE_IDENTIFIED

    def confirm_target_safeguard(self, serial_suffix_confirmation: str) -> bool:
        """
        Contramăsură T.TARGET_MISMATCH:
        Operatorul trebuie să introducă manual ultimele 4 caractere din numărul serial fizic tipărit pe carcasă.
        """
        if self.state != EngineState.DEVICE_IDENTIFIED:
            raise RuntimeError(f"Stare invalidă pentru confirmare țintă: {self.state}")

        expected_suffix = self.device.serial_number[-4:].upper()
        if serial_suffix_confirmation.strip().upper() != expected_suffix:
            self.evidence.log_event("TARGET_MISMATCH_DETECTED", {
                "provided_suffix": serial_suffix_confirmation,
                "expected_suffix": expected_suffix,
            })
            self.state = EngineState.FAILED_REJECTED
            self.final_disposition = FinalDisposition.NON_CONFORM_REQUIRES_DESTRUCTION
            self.rejection_reason = "Eroare operator: sufixul numărului serial nu corespunde discului fizic selectat."
            return False

        self.evidence.log_event("TARGET_CONFIRMED", {"serial": self.device.serial_number})
        self.state = EngineState.TARGET_VERIFIED
        return True

    def evaluate_and_authorize(self, dual_auth: Optional[DualAuthorization] = None) -> bool:
        """
        Evaluează politica de securitate și aplică controlul dual dacă este necesar.
        """
        if self.state != EngineState.TARGET_VERIFIED:
            raise RuntimeError(f"Stare invalidă pentru evaluare politică: {self.state}")

        method, rationale = self.policy_engine.evaluate_policy(
            jurisdiction=self.jurisdiction,
            classification=self.classification,
            device=self.device,
        )
        self.authorized_method = method
        self.evidence.log_event("POLICY_EVALUATION", {
            "method": method.value,
            "rationale": rationale,
            "classification": self.classification.value,
        })

        if method == SanitizeMethod.PHYSICAL_DESTRUCTION_REQUIRED:
            self.state = EngineState.FAILED_REJECTED
            self.final_disposition = FinalDisposition.NON_CONFORM_REQUIRES_DESTRUCTION
            self.rejection_reason = rationale
            return False

        # Verificare control dual
        needs_dual = self.policy_engine.requires_dual_control(self.classification)
        if needs_dual:
            if not dual_auth or not dual_auth.is_valid():
                self.state = EngineState.FAILED_REJECTED
                self.final_disposition = FinalDisposition.NON_CONFORM_REQUIRES_DESTRUCTION
                self.rejection_reason = "Lipsă autorizare duală validă (Operator + Martor) pentru clasificare înaltă."
                self.evidence.log_event("DUAL_AUTH_FAILED", {"reason": self.rejection_reason})
                return False

        self.dual_auth = dual_auth
        self.state = EngineState.AUTHORIZED
        return True

    def execute_sanitization(self, max_poll_seconds: int = 1800) -> bool:
        """
        Execută ciclul de ștergere, polling și verificare finală LBA.
        Dacă este în mod simulare, rezultatul final este STRICT SIMULATED_NOT_SANITIZED.
        """
        if self.state != EngineState.AUTHORIZED or not self.authorized_method:
            raise RuntimeError(f"Sesiunea nu este autorizată pentru execuție: {self.state}")

        self.state = EngineState.SANITIZING
        self.evidence.log_event("COMMAND_DISPATCHED", {
            "method": self.authorized_method.value,
            "is_simulation": str(self.adapter.simulation_mode),
        })

        try:
            self.adapter.issue_sanitize_command(self.device, self.authorized_method)
        except StorageCommandError as err:
            self.state = EngineState.FAILED_REJECTED
            # Dacă dispozitivul nu este găsit sau mediul e nesuportat:
            if "nu a fost găsit" in str(err) or "nu este încă suportată" in str(err):
                self.final_disposition = FinalDisposition.ABORTED_UNSUPPORTED_ENVIRONMENT
            else:
                self.final_disposition = FinalDisposition.NON_CONFORM_REQUIRES_DESTRUCTION
            self.rejection_reason = f"Eroare lansare comandă: {str(err)}"
            self.evidence.log_event("COMMAND_ERROR", {"error": str(err)})
            return False

        # Buclă de polling de stare cu timeout
        completed = False
        start_time = time.time()
        
        while not completed:
            if time.time() - start_time > max_poll_seconds:
                self.state = EngineState.FAILED_REJECTED
                self.final_disposition = FinalDisposition.INCOMPLETE_ABORTED
                self.rejection_reason = f"Timeout depășit ({max_poll_seconds} secunde) în timpul operației de sanitizare."
                self.evidence.log_event("TIMEOUT_EXCEEDED", {"elapsed": str(time.time() - start_time)})
                return False

            done, prog, msg = self.adapter.poll_sanitize_status(self.device)
            self.evidence.log_event("POLL_STATUS", {"progress": str(prog), "status": msg})

            if msg in ("HARDWARE_RESET_OR_POWER_CUT", "DEVICE_DISCONNECTED"):
                self.state = EngineState.FAILED_REJECTED
                self.final_disposition = FinalDisposition.INCOMPLETE_ABORTED
                self.rejection_reason = f"Dispozitiv deconectat sau reset hardware: {msg}"
                return False

            if msg in ("FAILED", "NEVER_SANITIZED", "IOCTL_FAILED"):
                self.state = EngineState.FAILED_REJECTED
                self.final_disposition = FinalDisposition.NON_CONFORM_REQUIRES_DESTRUCTION
                self.rejection_reason = f"Firmware-ul raportează eroare sau lipsă sanitizare: {msg}"
                return False

            if done:
                completed = True
            else:
                # În mod real așteptăm un interval înainte de următorul poll
                if not self.adapter.simulation_mode:
                    time.sleep(1.0)

        # Faza de verificare (LBA Read Sampling)
        self.state = EngineState.VERIFYING
        verified = self.adapter.sample_verify_lba(self.device)
        if not verified:
            self.state = EngineState.FAILED_REJECTED
            self.final_disposition = FinalDisposition.NON_CONFORM_REQUIRES_DESTRUCTION
            self.rejection_reason = "Eșec verificare LBA: model rezidual detectat pe eșantioane."
            self.evidence.log_event("VERIFICATION_FAILED", {})
            return False

        self.evidence.log_event("VERIFICATION_SUCCESS", {})
        self.state = EngineState.COMPLETED

        # REGULĂ CRITICĂ DE INTEGRITATE:
        # Dacă s-a rulat în simulator, NICIODATĂ nu se emite CONFORM_PURGED!
        if self.adapter.simulation_mode:
            self.final_disposition = FinalDisposition.SIMULATED_NOT_SANITIZED
            self.rejection_reason = "Rulare în mod simulator de laborator. Discul fizic NU a fost atins sau purjat."
        else:
            self.final_disposition = FinalDisposition.CONFORM_PURGED

        return True

    def export_manifest(self, smartcard_signatures: Optional[Dict[str, str]] = None) -> Dict[str, Any]:
        """Generează manifestul tehnic semnat."""
        return self.evidence.generate_signed_manifest(
            device=self.device,
            method=self.authorized_method or SanitizeMethod.PHYSICAL_DESTRUCTION_REQUIRED,
            disposition=self.final_disposition or FinalDisposition.INCOMPLETE_ABORTED,
            dual_auth=self.dual_auth,
            is_simulation=self.adapter.simulation_mode,
            smartcard_signatures=smartcard_signatures,
        )
