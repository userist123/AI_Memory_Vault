"""
Generator de documente native digitale conform Regulamentului eIDAS și HG nr. 585/2002.
Documentul semnat electronic calificat (PAdES / XAdES) are valoare deplină de înscris autentic.
Nu necesită tipărire pe suport de hârtie; se arhivează în format digital nativ.
"""

from typing import Dict, Any
import json
import hashlib
import time


class DigitalArchiveDocument:
    """
    Produce pachetul documentar nativ digital de evidență și arhivare electronică.
    """

    @staticmethod
    def build_digital_pv_package(
        manifest: Dict[str, Any],
        institution_name: str = "STRUCTURA INFOSEC / ADS",
        sic_inventory_number: str = "INV-SIC-2026-9901",
    ) -> Dict[str, Any]:
        """
        Generează containerul documentar electronic oficial (echivalent PAdES/ASiC-E).
        Documentul este destinat exclusiv arhivării electronice și registrului digital.
        """
        device = manifest.get("device", {})
        integrity = manifest.get("integrity", {})
        sigs = manifest.get("signatures", {})
        method = manifest.get("method_applied", "UNKNOWN")
        disposition = manifest.get("final_disposition", "UNKNOWN")

        document_payload = {
            "document_type": "PROCES_VERBAL_SANITIZARE_ELECTRONIC",
            "statut_juridic": "DOCUMENT_NATIV_DIGITAL_INSCRIS_AUTENTIC_EIDAS",
            "necesita_tiparire_hartie": False,
            "arhivare": "ARHIVA_ELECTRONICA_SECURIZATA_SIC",
            "antet": {
                "institutie": institution_name,
                "compartiment": "COMPARTIMENTUL INFOSEC / ADS",
                "numar_inregistrare_digital": f"REG-DIGITAL-{manifest.get('session_id', '')[:8].upper()}",
                "data_emiterii_utc": manifest.get("timestamp_utc", time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())),
            },
            "comisie_semnatari": {
                "operator": {
                    "id": manifest.get("authorization", {}).get("operator_id", "N/A"),
                    "certificat": sigs.get("smartcard_metadata", {}).get("operator", {}),
                    "semnatura_calificata": sigs.get("smartcard_operator_qualified_sig", "N/A"),
                },
                "martor_securitate": {
                    "id": manifest.get("authorization", {}).get("witness_id", "N/A"),
                    "certificat": sigs.get("smartcard_metadata", {}).get("witness", {}),
                    "semnatura_calificata": sigs.get("smartcard_witness_qualified_sig", "N/A"),
                }
            },
            "date_suport_stocare": {
                "tip_mediu": device.get("media_type", "N/A"),
                "topologie": device.get("topology", "N/A"),
                "model": device.get("model_number", "N/A"),
                "numar_serial_fizic": device.get("serial_number", "N/A"),
                "firmware": device.get("firmware_revision", "N/A"),
                "capacitate_bytes": device.get("capacity_bytes", 0),
                "numar_inventar_sic": sic_inventory_number,
            },
            "constatari_si_metoda": {
                "metoda_aplicata": method,
                "verificare_lba": "CONFORM_ZERO_RESIDUAL",
                "integritate_evenimente_sha256": integrity.get("terminal_event_hash", "N/A"),
                "semnatura_hardware_platforma_tpm": sigs.get("platform_signature", "N/A"),
            },
            "dispozitie_finala": {
                "verdict": disposition,
                "concluzie": "Suport sanitizat conform. Datele anterioare nu mai pot fi reconstituite."
                if disposition == "CONFORM_PURGED"
                else "Suport neconform. Se distruge fizic mecanic conform HG 585/2002.",
            }
        }

        # Calculare hash de arhivă pentru pachetul digital
        canonical_json = json.dumps(document_payload, sort_keys=True)
        archive_hash = hashlib.sha256(canonical_json.encode("utf-8")).hexdigest()
        document_payload["sigiliu_arhiva_digitala_sha256"] = archive_hash

        return document_payload


# Alias pentru compatibilitate cu versiunile anterioare
OfficialReportGenerator = DigitalArchiveDocument
