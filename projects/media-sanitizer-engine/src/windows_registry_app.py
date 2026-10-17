"""
Aplicația Windows de Registratură și Evidență Digitală (Non-TOE).
Arhivează documentele semnate electronic calificat direct în registrul digital,
fără necesitatea tipăririi pe hârtie.
"""

from typing import Dict, Any, List, Optional
import json
import os
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

from .smartcard_auth import SmartcardAuthenticator
from .report_generator import DigitalArchiveDocument


class WindowsRegistryApp:
    """
    Sistemul administrativ digital de birou pentru gestiunea fără hârtie a mediilor clasificate.
    """

    def __init__(
        self,
        registry_file: str = "media_sanitization_registry.json",
        simulation_mode: bool = True,
        custom_pkcs11_lib: Optional[str] = None,
        trusted_tpm_keys: Optional[List[str]] = None,
    ):
        self.registry_file = registry_file
        self.simulation_mode = simulation_mode
        self.trusted_tpm_keys = trusted_tpm_keys
        self.smartcard_auth = SmartcardAuthenticator(
            simulation_mode=simulation_mode,
            custom_pkcs11_lib=custom_pkcs11_lib,
        )
        self.records: List[Dict[str, Any]] = self._load_registry()

    def _load_registry(self) -> List[Dict[str, Any]]:
        if os.path.exists(self.registry_file):
            try:
                with open(self.registry_file, "r", encoding="utf-8") as f:
                    return json.load(f)
            except Exception:
                return []
        return []

    def _save_registry(self) -> None:
        with open(self.registry_file, "w", encoding="utf-8") as f:
            json.dump(self.records, f, indent=2, ensure_ascii=False)

    def import_and_validate_manifest(self, manifest_data: Dict[str, Any]) -> bool:
        """Validează structura și verifică criptografic semnătura asimetrică TPM a manifestului."""
        if "device" not in manifest_data or "integrity" not in manifest_data:
            raise ValueError("Fișierul de manifest este invalid sau corupt.")

        from .tpm_signer import TPMSigner, TPMVerificationError
        try:
            TPMSigner.verify_manifest_signature(
                manifest_data=manifest_data,
                trusted_public_keys=self.trusted_tpm_keys,
            )
        except TPMVerificationError as v_err:
            raise ValueError(f"Validare manifest eșuată: {str(v_err)}")

        return True

    def process_smartcard_dual_signing(
        self,
        manifest_data: Dict[str, Any],
        operator_pin: str,
        witness_pin: str,
    ) -> Dict[str, Any]:
        """
        Aplică semnăturile electronice calificate ale Operatorului și Martorului prin cipul cartelelor.
        """
        term_hash = manifest_data["integrity"]["terminal_event_hash"]

        # 1. Semnare Operator (Slot 0)
        self.smartcard_auth.authenticate_cardholder("SLOT_0", operator_pin)
        op_sig_record = self.smartcard_auth.sign_hash_with_card("SLOT_0", term_hash)

        # 2. Semnare Martor / Responsabil Securitate (Slot 1)
        self.smartcard_auth.authenticate_cardholder("SLOT_1", witness_pin)
        wit_sig_record = self.smartcard_auth.sign_hash_with_card("SLOT_1", term_hash)

        manifest_data["signatures"]["smartcard_operator_qualified_sig"] = op_sig_record["signature_value"]
        manifest_data["signatures"]["smartcard_witness_qualified_sig"] = wit_sig_record["signature_value"]
        manifest_data["signatures"]["smartcard_metadata"] = {
            "operator": {
                "name": op_sig_record["holder_name"],
                "role": op_sig_record["holder_role"],
                "cert_serial": op_sig_record["cert_serial"],
            },
            "witness": {
                "name": wit_sig_record["holder_name"],
                "role": wit_sig_record["holder_role"],
                "cert_serial": wit_sig_record["cert_serial"],
            }
        }
        return manifest_data

    def archive_digital_record(
        self,
        manifest_data: Dict[str, Any],
        sic_inventory_number: str = "INV-SIC-2026-9901",
        archive_dir: str = "electronic_archive",
    ) -> Dict[str, Any]:
        """
        Creează documentul nativ digital semnat, îl salvează în arhiva electronică și îl indexează în registru.
        Fără tipărire fizică pe hârtie.
        """
        os.makedirs(archive_dir, exist_ok=True)

        digital_doc = DigitalArchiveDocument.build_digital_pv_package(
            manifest=manifest_data,
            sic_inventory_number=sic_inventory_number,
        )

        serial = manifest_data["device"]["serial_number"]
        session_id = manifest_data["session_id"]
        doc_filename = f"PV_SANITIZARE_{serial}_{session_id[:8]}.json"
        doc_path = os.path.join(archive_dir, doc_filename)

        with open(doc_path, "w", encoding="utf-8") as f:
            json.dump(digital_doc, f, indent=2, ensure_ascii=False)

        registry_entry = {
            "numar_inregistrare": digital_doc["antet"]["numar_inregistrare_digital"],
            "timestamp_utc": digital_doc["antet"]["data_emiterii_utc"],
            "serie_hardware_disc": serial,
            "numar_inventar_sic": sic_inventory_number,
            "verdict": digital_doc["dispozitie_finala"]["verdict"],
            "semnatura_operator_cert": digital_doc["comisie_semnatari"]["operator"]["certificat"].get("cert_serial", "N/A"),
            "semnatura_martor_cert": digital_doc["comisie_semnatari"]["martor_securitate"]["certificat"].get("cert_serial", "N/A"),
            "sigiliu_document_sha256": digital_doc["sigiliu_arhiva_digitala_sha256"],
            "cale_fisier_arhiva_digitala": doc_path,
            "suport_hartie_utilizat": False,
        }
        self.records.append(registry_entry)
        self._save_registry()

        return digital_doc
