"""
CLI Demonstrativ Cap-Coadă pentru Secure Sanitization Engine & Windows Registry App.
Simulează întregul ciclu de viață: de la bootarea offline până la semnarea cu cartela cu cip și tipărirea PV-ului.
"""

import sys
import os
import json

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

from .models import (
    Jurisdiction,
    ClassificationLevel,
    MediaType,
    DeviceTopology,
    DeviceMetadata,
    DualAuthorization,
)
from .state_machine import SanitizationSession
from .windows_registry_app import WindowsRegistryApp


def run_full_pipeline_demo():
    print("=" * 100)
    print("PLATFORMĂ DE SANITIZARE A MEDIILOR CU INFORMAȚII CLASIFICATE (TOE-SSE-v1)".center(100))
    print("Conform INFOSEC 14 | HG 585/2002 | Directiva NATO AC/35-D | NIST SP 800-88r2 | IEEE 2883-2022".center(100))
    print("=" * 100)

    # -------------------------------------------------------------
    # ETAPA 1: MEDIUL UEFI BOOTABIL OFFLINE (TOE)
    # -------------------------------------------------------------
    print("\n[FAZA 1: MEDIU BOOTABIL OFFLINE - EXECUTIE CRITICĂ]")
    target_disk = DeviceMetadata(
        serial_number="S676NF0R994123",
        model_number="SAMSUNG MZVL21T0HCLR (NVMe SSD 1TB)",
        firmware_revision="HPS8101Q",
        capacity_bytes=1024209543168,
        media_type=MediaType.NVME_SSD,
        topology=DeviceTopology.NATIVE_PCIE,
        bus_path="/pci0000:00/0000:00:0e.0/nvme0n1",
        is_healthy=True,
    )

    print(f"  [+] Dispozitiv detectat: {target_disk.model_number} (SN: {target_disk.serial_number})")
    print(f"  [+] Topologie: {target_disk.topology.value} | Nivel clasificat: STRICT SECRET")

    session = SanitizationSession(
        device=target_disk,
        classification=ClassificationLevel.STRICT_SECRET,
        jurisdiction=Jurisdiction.RO,
    )

    # Confirmare sufix serial (T.TARGET_MISMATCH safeguard)
    suffix = "4123"
    print(f"  [>] Operatorul tastează sufixul seriei fizice: '{suffix}'")
    session.confirm_target_safeguard(suffix)

    # Autorizare preliminară pe stație
    auth = DualAuthorization(
        operator_id="OP-LT-POPESCU-ION",
        operator_token="TOKEN-PREAUTH-OP",
        witness_id="SEC-CPT-IONESCU-VASILE",
        witness_token="TOKEN-PREAUTH-WIT",
    )
    session.evaluate_and_authorize(dual_auth=auth)
    print(f"  [+] Comandă hardware aprobată: {session.authorized_method.value}")

    print("  [>] Execuție NVMe Sanitize (Block Erase) + Verificare eșantioane LBA...")
    session.execute_sanitization()
    raw_manifest = session.export_manifest()
    print("  [+] Sanitizare încheiată cu succes! Manifest tehnic generat și sigilat cu cheia TPM.")

    # -------------------------------------------------------------
    # ETAPA 2: APLICAȚIA WINDOWS DE BIROU / REGISTRATURĂ (NON-TOE)
    # -------------------------------------------------------------
    print("\n[FAZA 2: APLICAȚIA WINDOWS DE BIROU - SEMNARE CALIFICATĂ & PROCES-VERBAL]")
    app = WindowsRegistryApp(registry_file="demo_registry.json")
    app.import_and_validate_manifest(raw_manifest)
    print("  [+] Manifest importat cu succes. Semnătura TPM a platformei offline este validă.")

    print("\n  [>] Conectare legitimații cu cip (Smartcards / Semnătură Calificată):")
    cards = app.smartcard_auth.detect_cards()
    for slot, desc in cards.items():
        print(f"      - {slot}: {desc}")

    print("  [>] Operatorul (Lt. Popescu Ion) introduce PIN-ul pe cartela din Slot 0...")
    print("  [>] Martorul / Ofițerul Securitate (Cpt. Ionescu Vasile) introduce PIN-ul pe cartela din Slot 1...")

    signed_manifest = app.process_smartcard_dual_signing(
        manifest_data=raw_manifest,
        operator_pin="1234",
        witness_pin="5678",
    )
    print("  [+] Ambele semnături calificate au fost generate în interiorul cipurilor și aplicate.")

    # Arhivare nativ digitală
    digital_doc = app.archive_digital_record(signed_manifest, sic_inventory_number="INV-SIC-2026-B992")
    
    # Generare pachet PDF + JSON oficial pentru registratura ta
    from .registry_connector import RegistryBridgeClient
    bridge = RegistryBridgeClient()
    pdf_out = bridge.prepare_and_sign_for_registry(
        raw_manifest=signed_manifest,
        operator_pin="1234",
        witness_pin="5678",
        sic_inventory_number="INV-SIC-2026-B992",
        output_folder="projects/media-sanitizer-engine/export_pentru_registratura",
    )

    print("\n" + "=" * 100)
    print("PACHET PAdES GENERAT PENTRU REGISTRATURA TA (GATA DE DESCHIS ÎN ADOBE READER):")
    print("=" * 100)
    print(f"  [✓] Fișier PDF Semnat Digital Calificat: {pdf_out}")
    print(f"  [✓] Număr Înregistrare Digital: {digital_doc['antet']['numar_inregistrare_digital']}")
    print(f"  [✓] Sigiliu Criptografic SHA-256: {digital_doc['sigiliu_arhiva_digitala_sha256']}")
    print("  [✓] Statut Juridic: DOCUMENT NATIV DIGITAL (Valoare de înscris autentic conform eIDAS).")
    print("  [✓] Hârtie utilizată: 0 pagini (Complet fără hârtie conform HG 585/2002).")


if __name__ == "__main__":
    run_full_pipeline_demo()
