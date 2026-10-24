"""
Interfața Terminal Text (TUI) pentru Mediul Bootabil UEFI Offline (TOE-SSE-v1).
Conectată la modulul real de descoperire hardware DeviceDiscoveryManager.
Nu inventează dispozitive fizice; reflectă strict starea magistralelor hardware.
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
from .device_discovery import DeviceDiscoveryManager


class UEFIConsoleTUI:
    """
    Consola interactivă pentru stația de lucru bootabilă dedicată.
    """

    def __init__(self, hardware_adapter: Optional[HardwareAdapter] = None):
        self.adapter = hardware_adapter or HardwareAdapter(simulation_mode=False)
        self.detected_devices: List[DeviceMetadata] = []

    def clear_screen(self):
        os.system("cls" if os.name == "nt" else "clear")

    def print_header(self):
        print("=" * 80)
        print("  SISTEM DE SANITIZARE A MEDIILOR CLASIFICATE - MEDIU BOOTABIL OFFLINE (TOE)")
        print("  Conform: INFOSEC 14 | HG 585/2002 | NATO AC/35-D | NIST SP 800-88r2 | IEEE 2883")
        print("=" * 80)
        mode_str = "SIMULARE LABORATOR" if self.adapter.simulation_mode else "HARDWARE REAL (KERNEL IOCTL)"
        print(f"  REGIM OPERARE: {mode_str}")
        print("-" * 80)

    def scan_devices(self) -> List[DeviceMetadata]:
        """Scanează magistralele hardware reale folosind DeviceDiscoveryManager."""
        self.detected_devices = DeviceDiscoveryManager.scan_physical_devices()
        return self.detected_devices

    def display_device_menu(self) -> Optional[DeviceMetadata]:
        self.clear_screen()
        self.print_header()
        print("\n  [ DISPOZITIVE DE STOCARE FIZICE DETECTATE ]\n")

        if not self.detected_devices:
            print("  [!] NICIUN DISPOZITIV DE STOCARE DETECTAT PE MAGISTRALELE HARDWARE.")
            print("      (Asigurați-vă că discul este conectat nativ și că rulați în mediul Linux UEFI).")
            print("\n  " + "-" * 90)
            print("  [0] Ieșire")
            input("\n  Apăsați Enter pentru a ieși..."); return None

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
            return False

        self.clear_screen()
        self.print_header()
        print(f"\n  [ CONFIRMARE CRITICĂ DISPOZITIV SELECTAT ]")
        print(f"  - Model:      {target.model_number}")
        print(f"  - Serie (SN): {target.serial_number}")
        print(f"  - Capacitate: {target.capacity_bytes / (1024**3):.2f} GB")
        print(f"  - Cale Bus:   {target.bus_path}")
        print(f"  - Topologie:  {target.topology.value}")
        print("\n  " + "!" * 70)
        print("  AVERTISMENT: ACEASTĂ ACȚIUNE ESTE COMPLET IREVERSIBILĂ!")
        print("  Pentru a preveni ștergerea accidentală a altui disc, introduceți")
        print(f"  ultimele 4 caractere din numărul de serie fizic tipărit pe etichetă.")
        print("  " + "!" * 70)

        suffix_input = input("\n  Introduceți ultimele 4 caractere din serie: ").strip()

        session = SanitizationSession(
            device=target,
            classification=ClassificationLevel.STRICT_SECRET,
            jurisdiction=Jurisdiction.RO,
            hardware_adapter=self.adapter,
        )

        if not session.confirm_target_safeguard(suffix_input):
            print("\n  [-] EROARE: Nepotrivire de serie! Sesiune oprită pentru siguranță.")
            input("  Apăsați Enter..."); return False

        print("\n  [ AUTORIZARE DUALĂ OPERAȚIUNE ]")
        op_id = input("  Introduceți Indicativ / ID Operator: ").strip()
        wit_id = input("  Introduceți Indicativ / ID Martor Securitate: ").strip()

        auth = DualAuthorization(
            operator_id=op_id,
            operator_token="TOKEN-AUTH-1",
            witness_id=wit_id,
            witness_token="TOKEN-AUTH-2",
        )

        if not session.evaluate_and_authorize(dual_auth=auth):
            print(f"\n  [-] REFUZAT DE POLITICĂ: {session.rejection_reason}")
            print(f"  DISPOZIȚIE: {session.final_disposition.value}")
            input("  Apăsați Enter..."); return False

        print(f"\n  [+] METODĂ APROBATĂ: {session.authorized_method.value}")
        confirm_run = input("\n  Confirmați începerea ștergerii fizice? (DA/NU): ").strip().upper()
        if confirm_run != "DA":
            print("  Operațiune anulată de operator.")
            return False

        print("\n  [ PROGRES SANITIZARE HARDWARE ]")
        success = session.execute_sanitization()

        if success:
            print("\n  " + "=" * 70)
            if self.adapter.simulation_mode:
                print("  [!] TEST SIMULAT FINALIZAT: SIMULATED_NOT_SANITIZED")
                print("      Atenție: Niciun disc fizic nu a fost șters.")
            else:
                print("  [✓] OPERAȚIUNE HARDWARE FINALIZATĂ CU SUCCES: CONFORM PURGED")
            print("  " + "=" * 70)
        else:
            print("\n  " + "=" * 70)
            print(f"  [-] EȘEC: {session.rejection_reason}")
            print("  " + "=" * 70)

        os.makedirs(output_export_dir, exist_ok=True)
        manifest = session.export_manifest()
        export_file = os.path.join(output_export_dir, f"manifest_{target.serial_number}.json")
        with open(export_file, "w", encoding="utf-8") as f:
            json.dump(manifest, f, indent=2, ensure_ascii=False)

        print(f"\n  [+] Manifest tehnic salvat: {export_file}")
        input("\n  Apăsați Enter pentru finalizare..."); return True
