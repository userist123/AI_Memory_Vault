"""
Hardware Abstraction Layer (HAL) pentru comenzi native de stocare.
Conectează mașina de stări direct la apelurile de sistem Linux IOCTL (NVMe Admin și SCSI Generic).
Interzice emiterea verdictului CONFORM_PURGED dacă rularea are loc în afara unui block device real,
cu excepția cazului în care modul de testare de laborator este marcat explicit și izolat în manifest.
"""

from typing import Tuple, Dict, Any, List, Optional
import os
import random
import time
from .models import MediaType, DeviceTopology, SanitizeMethod, DeviceMetadata
from .linux_ioctl_driver import LinuxStorageDriver


class StorageCommandError(Exception):
    """Excepție ridicată când firmware-ul sau controlerul respinge o comandă critică."""
    pass


class HardwareAdapter:
    """
    Abstracție hardware de nivel jos.
    Când simulation_mode=False, execută exclusiv ioctl-uri directe pe nodurile /dev/nvme* sau /dev/sd*.
    """

    def __init__(self, simulation_mode: bool = False):
        self.simulation_mode = simulation_mode
        self._mock_progress: Dict[str, float] = {}
        self._mock_power_cut: Dict[str, bool] = {}

    def issue_sanitize_command(self, device: DeviceMetadata, method: SanitizeMethod) -> Dict[str, Any]:
        """
        Emite comanda asincronă firmware corespunzătoare.
        """
        if device.topology == DeviceTopology.BRIDGED_USB:
            raise StorageCommandError("Refuz comandă: puntea USB nu garantează propagarea transparentă a comenzii native Sanitize.")

        # EXECUȚIE REALĂ PE HARDWARE (KERNEL LINUX OFFLINE)
        if not self.simulation_mode:
            if not os.path.exists(device.bus_path):
                raise StorageCommandError(f"Dispozitivul fizic {device.bus_path} nu a fost găsit în sistem.")

            if method == SanitizeMethod.NVME_SANITIZE_BLOCK_ERASE:
                success = LinuxStorageDriver.issue_nvme_sanitize_block_erase(device.bus_path)
                if not success:
                    raise StorageCommandError("Apelul IOCTL NVMe Sanitize a returnat cod de eroare hardware.")
                return {
                    "status": "COMMAND_ACCEPTED_HARDWARE",
                    "device_serial": device.serial_number,
                    "bus_path": device.bus_path,
                    "method": method.value,
                    "timestamp_start": time.time(),
                }
            else:
                raise StorageCommandError(f"Metoda hardware {method.value} nu este încă suportată nativ de driverul IOCTL curent.")

        # MOD SIMULARE (DOAR PENTRU TESTE UNITARE DE LABORATOR)
        self._mock_progress[device.serial_number] = 0.0
        return {
            "status": "COMMAND_ACCEPTED_SIMULATED",
            "device_serial": device.serial_number,
            "method": method.value,
            "timestamp_start": time.time(),
        }

    def poll_sanitize_status(self, device: DeviceMetadata) -> Tuple[bool, float, str]:
        """
        Interoghează jurnalul de stare firmware (NVMe Sanitize Status Log 0x81).
        """
        if not self.simulation_mode:
            if not os.path.exists(device.bus_path):
                return False, 0.0, "DEVICE_DISCONNECTED"
            return LinuxStorageDriver.get_nvme_sanitize_status(device.bus_path)

        # Mod Simulare
        if self._mock_power_cut.get(device.serial_number, False):
            return False, self._mock_progress.get(device.serial_number, 0.0), "HARDWARE_RESET_OR_POWER_CUT"

        current_prog = self._mock_progress.get(device.serial_number, 0.0)
        if current_prog < 100.0:
            current_prog += 25.0
            self._mock_progress[device.serial_number] = current_prog

        if current_prog >= 100.0:
            return True, 100.0, "SUCCESSFUL_COMPLETION"
        return False, current_prog, "OPERATION_IN_PROGRESS"

    def sample_verify_lba(self, device: DeviceMetadata, sample_count: int = 1000) -> bool:
        """
        Citește fizic eșantioane LBA de pe suprafața discului pentru a verifica
        că toate datele returnate sunt octeți purjați (0x00) și nu există reziduuri.
        """
        if not self.simulation_mode:
            if not os.path.exists(device.bus_path):
                return False

            try:
                # Deschidere directă a nodului de bloc
                fd = os.open(device.bus_path, os.O_RDONLY)
                try:
                    sector_size = 512
                    total_sectors = device.capacity_bytes // sector_size
                    if total_sectors <= 0:
                        return False

                    # Verificăm LBA 0 (Master Boot Record / GPT)
                    buf = os.pread(fd, sector_size, 0)
                    if any(b != 0 for b in buf):
                        return False

                    # Verificăm un eșantion aleatoriu pe tot cuprinsul discului
                    random.seed(42)  # Deterministic pentru audit
                    for _ in range(min(sample_count, 500)):
                        rand_sector = random.randint(1, total_sectors - 1)
                        offset = rand_sector * sector_size
                        buf = os.pread(fd, sector_size, offset)
                        if any(b != 0 for b in buf):
                            return False

                    return True
                finally:
                    os.close(fd)
            except Exception:
                return False

        # Mod Simulare
        if self._mock_power_cut.get(device.serial_number, False):
            return False
        return True

    def inject_power_cut(self, serial_number: str) -> None:
        """Simulează o cădere de tensiune (doar în mod simulare)."""
        self._mock_power_cut[serial_number] = True
