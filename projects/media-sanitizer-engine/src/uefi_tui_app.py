"""
Interfața Terminal Text (TUI) pentru Mediul Bootabil UEFI Offline (TOE-SSE-v1).
Rulează direct în consola text / framebuffer fără server grafic X11/Wayland.
Oferă operatorului fluxul ghidat pas-cu-pas cu toate protecțiile de securitate active.
"""

import sys
import os
import time
import json
from typing import List, Optional

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

from .models import (
    Jurisdiction,
    ClassificationLevel,
    MediaType,
    DeviceTopology,
    DeviceMetadata,
    DualAuthorization,
    FinalDisposition,
)
from .state_machine import SanitizationSession, EngineState
from .hardware_adapter import HardwareAdapter


class UEFIConsoleTUI:
    """
    Consola interactivă pentru stația de lucru bootabilă dedicată.
    """

    def __init__(self, hardware_adapter: Optional[HardwareAdapter] = None):
        self.adapter = hardware_adapter or HardwareAdapter(simulation_mode=True)
        self.detected_devices: List[DeviceMetadata] = []

    def clear_screen(self):
        os.system("cls" if os.name == "nt" else "clear")

    def print_header(self):
        print("=" * 80)
        print("  SISTEM DE SANITIZARE A MEDIILOR CLASIFICATE - MEDIU BOOTABIL OFFLINE (TOE)")
        print("  Conform: INFOSEC 14 | HG 585/2002 | NATO AC/35-D | NIST SP 800-88r2 | IEEE 2883")
        print("=" * 80)
        print("  STARE SISTEM: AIR-GAPPED (FĂRĂ REȚEA) | KERNEL IMUTABIL | CEAS LOCAL SIGILAT")
        print("-" * 80)

    def scan_devices(self) -> List[DeviceMetadata]:
        """Scanează magistralele hardware locale (excluzând discul de boot al sistemului)."""
        # În simulator / demo, detectăm două suporturi: unul nativ și unul prin punte USB
        self.detected_devices = [
            DeviceMetadata(
                serial_number="S676NF0R994123",
                model_number="SAMSUNG MZVL21T0HCLR (NVMe 1TB)",
                firmware_revision="HPS8101Q",
                capacity_bytes=1024209543168,
                media_type=MediaType.NVME_SSD,
                topology=DeviceTopology.NATIVE_PCIE,
                bus_path="/pci0000:00/0000:00:0e.0/nvme0n1",
                is_healthy=True,
            ),
            DeviceMetadata(
                serial_number="WD-WCC4M7812904",
                model_number="WD BLUE SATA SSD (500GB)",
                firmware_revision="415000RL",
                capacity_bytes=500107862016,
                media_type=MediaType.SATA_SSD,
                topology=DeviceTopology.NATIVE_SATA,
                bus_path="/ata1/target0/0:0:0:0/sda",
                is_healthy=True,
            ),
            DeviceMetadata(
                serial_number="KINGSTON-DT50-4821",
                model_number="KINGSTON DATATRAVELER (USB)",
                firmware_revision="1.00",
                capacity_bytes=64120888320,
                media_type=MediaType.USB_FLASH,
                topology=DeviceTopology.BRIDGED_USB,
                bus_path="/usb/bus1/dev3/sdb",
                is_healthy=True,
            )
        ]
        return self.detected_devices

    def display_device_menu(self) -> Optional[DeviceMetadata]:
        self.clear_screen()
        self.print_header()
        print("\n  [ DISPOZITIVE DE STOCARE CONECTATE ]\n")
        print(f"  {'Nr.':<4} {'Tip':<10} {'Capacitate':<12} {'Model':<30} {'Topologie':<14} {'Serie (SN)'}")
        print("  " + "-" * 90)

        for idx, dev in enumerate(self.detected_devices, 1):
            cap_gb = f"{dev.capacity_bytes / (1024**3):.1f} GB"
            print(f"  {idx:<4} {dev.media_type.value:<10} {cap_gb:<12} {dev.model_number:<30} {dev.topology.value:<14} {dev.serial_number}")

        print("  " + "-" * 90)
        print("  [0] Ieșire / Repornire stație")

        try:
            choice = input("\n  Selectați numărul suportului de sanitizat: ").strip()
            if choice == "0":
                return None
            idx = int(choice) - 1
            if 0 <= idx < len(self.detected_devices):
                return self.detected_devices[idx]
        except ValueError:
            pass

        print("  [!] Selecție invalidă.")
        time.sleep(1.5)
        return self.display_device_menu()

    def run_interactive_session(self, output_export_dir: str = "export_stick") -> bool:
        devs = self.scan_devices()
        target = self.display_device_menu()
        if not target:
            print("\n  Oprire la cererea operatorului.")
            return False

        self.clear_screen()
        self.print_header()
        print(f"\n  [ CONFIRMARE CRITICĂ DISPOZITIV SELECTAT ]")
        print(f"  - Model:      {target.model_number}")
        print(f"  - Serie (SN): {target.serial_number}")
        print(f"  - Capacitate: {target.capacity_bytes / (1024**3):.2f} GB")
        print(f"  - Cale Bus:   {target.bus_path}")
        print("\n  " + "!" * 70)
        print("  AVERTISMENT: ACEASTĂ ACȚIUNE ESTE COMPLET IREVERSIBILĂ!")
        print("  Pentru a preveni ștergerea accidentală a altui disc, introduceți")
        print(f"  ultimele 4 caractere din numărul de serie fizic tipărit pe etichetă.")
        print("  " + "!" * 70)

        suffix_input = input("\n  Introduceți ultimele 4 caractere din serie: ").strip()

        # Inițiere sesiune
        session = SanitizationSession(
            device=target,
            classification=ClassificationLevel.STRICT_SECRET,
            jurisdiction=Jurisdiction.RO,
            hardware_adapter=self.adapter,
        )

        if not session.confirm_target_safeguard(suffix_input):
            print("\n  [-] EROARE CRITICĂ: Nepotrivire de serie! Sesiune blocată pentru siguranță.")
            input("  Apăsați Enter pentru a reveni..."); return False

        # Autentificare pe stație
        print("\n  [ AUTORIZARE DUALĂ OPERAȚIUNE ]")
        op_id = input("  Introduceți Indicativ / ID Operator (ex: OP-POPESCU): ").strip() or "OP-POPESCU-ION"
        wit_id = input("  Introduceți Indicativ / ID Martor Securitate (ex: SEC-IONESCU): ").strip() or "SEC-IONESCU-VASILE"

        auth = DualAuthorization(
            operator_id=op_id,
            operator_token="TOKEN-PREAUTH-1",
            witness_id=wit_id,
            witness_token="TOKEN-PREAUTH-2",
        )

        if not session.evaluate_and_authorize(dual_auth=auth):
            print(f"\n  [-] REF皰 DE POLITICĂ: {session.rejection_reason}")
            print(f"  DISPOZIȚIE: {session.final_disposition.value}")
            input("  Apăsați Enter..."); return False

        print(f"\n  [+] METODĂ APROBATĂ: {session.authorized_method.value}")
        confirm_run = input("\n  Confirmați începerea ștergerii fizice? (DA/NU): ").strip().upper()
        if confirm_run != "DA":
            print("  Operațiune anulată.")
            return False

        # Rulare cu bară de progres vizuală
        print("\n  [ PROGRES SANITIZARE HARDWARE ]")
        success = session.execute_sanitization()

        if success:
            print("\n  " + "=" * 70)
            print("  [✓] OPERAȚIUNE FINALIZATĂ CU SUCCES: CONFORM PURGED")
            print("  Toate blocurile fizice și celulele de rezervă au fost alterate.")
            print("  " + "=" * 70)
        else:
            print("\n  " + "=" * 70)
            print(f"  [-] EȘEC: {session.rejection_reason}")
            print("  " + "=" * 70)

        # Export manifest pe stick USB
        os.makedirs(output_export_dir, exist_ok=True)
        manifest = session.export_manifest()
        export_file = os.path.join(output_export_dir, f"manifest_{target.serial_number}.json")
        with open(export_file, "w", encoding="utf-8") as f:
            json.dump(manifest, f, indent=2, ensure_ascii=False)

        print(f"\n  [+] Manifest tehnic semnat TPM salvat pe mediul dedicat:")
        print(f"      {export_file}")
        print("  Acest fișier poate fi preluat acum în aplicația Windows pentru")
        print("  semnarea calificată pe cartele cu cip și transmiterea la registratură.")

        input("\n  Apăsați Enter pentru finalizare..."); return True
