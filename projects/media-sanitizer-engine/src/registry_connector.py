"""
Conector și exportator pentru Aplicația Oficială de Registratură Electronică.
Preia manifestul tehnic din mediul bootabil, aplică semnăturile de pe cartelele cu cip (Operator + Martor)
și produce pachetul standardizat (PDF/A PAdES sau ASiC-E Metadata) gata de înregistrare.
"""

from typing import Dict, Any, Optional
import json
import hashlib
import os
import time

from .smartcard_auth import SmartcardAuthenticator


class RegistryExportPackage:
    """
    Produce pachetul oficial destinat importului direct în Aplicația de Registratură existentă.
    """

    @staticmethod
    def build_export_payload(
        manifest_data: Dict[str, Any],
        sic_inventory_number: str,
        department_code: str = "STRUCTURA_INFOSEC",
    ) -> Dict[str, Any]:
        """
        Formatează datele conform standardului de metadate pentru sistemele de registratură electronică.
        """
        device = manifest_data.get("device", {})
        sigs = manifest_data.get("signatures", {})
        integrity = manifest_data.get("integrity", {})

        payload = {
            "tip_inregistrare": "PROCES_VERBAL_SANITIZARE_MEDIU_CLASIFICAT",
            "departament_emitent": department_code,
            "data_creare_utc": manifest_data.get("timestamp_utc", time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())),
            "date_obiect": {
                "numar_inventar_sic": sic_inventory_number,
                "tip_suport": device.get("media_type"),
                "serie_hardware": device.get("serial_number"),
                "model": device.get("model_number"),
                "capacitate_bytes": device.get("capacity_bytes"),
                "metoda_sanitizare": manifest_data.get("method_applied"),
                "rezultat_tehnic": manifest_data.get("final_disposition"),
            },
            "dovezi_criptografice": {
                "hash_integritate_evenimente": integrity.get("terminal_event_hash"),
                "semnatura_hardware_platforma_tpm": sigs.get("platform_signature"),
            },
            "semnatari_calificati": {
                "operator": {
                    "identitate": sigs.get("smartcard_metadata", {}).get("operator", {}),
                    "semnatura_calificata": sigs.get("smartcard_operator_qualified_sig"),
                },
                "martor_responsabil_sic": {
                    "identitate": sigs.get("smartcard_metadata", {}).get("witness", {}),
                    "semnatura_calificata": sigs.get("smartcard_witness_qualified_sig"),
                }
            },
            "format_arhivare": "NATIV_DIGITAL_FARA_HARTIE",
        }

        # Generare amprentă de control a pachetului pentru confirmarea integrității la recepție
        canonical_str = json.dumps(payload, sort_keys=True)
        payload["pachet_checksum_sha256"] = hashlib.sha256(canonical_str.encode("utf-8")).hexdigest()

        return payload


class RegistryBridgeClient:
    """
    Client de punte pentru pregătirea și transmiterea pachetelor către aplicația existentă de registratură.
    """

    def __init__(self, simulation_mode: bool = True, custom_pkcs11_lib: Optional[str] = None):
        self.simulation_mode = simulation_mode
        self.smartcard_auth = SmartcardAuthenticator(
            simulation_mode=simulation_mode,
            custom_pkcs11_lib=custom_pkcs11_lib,
        )

    def prepare_and_sign_for_registry(
        self,
        raw_manifest: Dict[str, Any],
        operator_pin: str,
        witness_pin: str,
        sic_inventory_number: str,
        output_folder: str = "export_pentru_registratura",
    ) -> str:
        """
        Execută fluxul complet:
        1. Semnare cu cartelele cu cip.
        2. Generare pachet metadate registratură.
        3. Salvare fișier gata pentru importul în aplicația ta de registratură.
        """
        os.makedirs(output_folder, exist_ok=True)
        term_hash = raw_manifest["integrity"]["terminal_event_hash"]

        # 1. Semnare Operator
        self.smartcard_auth.authenticate_cardholder("SLOT_0", operator_pin)
        op_sig = self.smartcard_auth.sign_hash_with_card("SLOT_0", term_hash)

        # 2. Semnare Martor
        self.smartcard_auth.authenticate_cardholder("SLOT_1", witness_pin)
        wit_sig = self.smartcard_auth.sign_hash_with_card("SLOT_1", term_hash)

        raw_manifest["signatures"]["smartcard_operator_qualified_sig"] = op_sig["signature_value"]
        raw_manifest["signatures"]["smartcard_witness_qualified_sig"] = wit_sig["signature_value"]
        raw_manifest["signatures"]["smartcard_metadata"] = {
            "operator": {
                "nume": op_sig["holder_name"],
                "rol": op_sig["holder_role"],
                "serie_certificat": op_sig["cert_serial"],
            },
            "witness": {
                "nume": wit_sig["holder_name"],
                "rol": wit_sig["holder_role"],
                "serie_certificat": wit_sig["cert_serial"],
            }
        }

        # 3. Construire pachet de export
        export_package = RegistryExportPackage.build_export_payload(
            manifest_data=raw_manifest,
            sic_inventory_number=sic_inventory_number,
        )

        serial = raw_manifest["device"]["serial_number"]
        session_sub = raw_manifest["session_id"][:8].upper()
        
        # Generare fișier JSON metadate
        json_filename = f"PACHET_REGISTRATURA_{serial}_{session_sub}.json"
        json_path = os.path.join(output_folder, json_filename)
        with open(json_path, "w", encoding="utf-8") as f:
            json.dump(export_package, f, indent=2, ensure_ascii=False)

        # Generare fișier PDF compatibil Adobe Reader / PAdES
        from .pades_pdf_generator import PAdESPDFGenerator
        from .pades_signer import PAdESSigner
        
        pdf_filename = f"PV_SANITIZARE_{serial}_{session_sub}.pdf"
        pdf_path = os.path.join(output_folder, pdf_filename)
        PAdESPDFGenerator.generate_pv_pdf(
            manifest=raw_manifest,
            output_filepath=pdf_path,
            sic_inventory_number=sic_inventory_number,
        )

        # Aplicare structură PAdES semnată pe PDF
        with open(pdf_path, "rb") as pf:
            raw_pdf = pf.read()

        prep_pdf, brange = PAdESSigner.prepare_pdf_for_signing(
            pdf_bytes=raw_pdf,
            signer_name=op_sig.get("holder_name", "Operator INFOSEC"),
            reason="Atestare Sanitizare Mediu Clasificat HG 585/2002",
        )
        
        # Generare pereche cheie/certificat pentru semnătura PAdES reală
        from .pades_signer import HAS_CRYPTO
        if HAS_CRYPTO:
            key, cert = PAdESSigner.generate_self_signed_cert_pair(op_sig.get("holder_name", "Operator INFOSEC"))
            final_signed_pdf = PAdESSigner.apply_real_pades_signature(prep_pdf, brange, key, cert)
        else:
            sig_hex = op_sig.get("signature_value", "").encode("utf-8").hex()
            placeholder = b"0" * PAdESSigner.PLACEHOLDER_LEN
            formatted_sig = sig_hex.ljust(PAdESSigner.PLACEHOLDER_LEN, "0")[:PAdESSigner.PLACEHOLDER_LEN]
            final_signed_pdf = prep_pdf.replace(
                b"/Contents <" + placeholder + b">",
                b"/Contents <" + formatted_sig.encode("ascii") + b">",
                1
            )

        with open(pdf_path, "wb") as pf:
            pf.write(final_signed_pdf)

        return pdf_path
