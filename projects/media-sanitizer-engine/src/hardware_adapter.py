"""
Hardware Abstraction Layer (HAL) pentru comenzi native de stocare.
Oferă interfață pentru trimiterea comenzilor firmware de nivel jos (NVMe Sanitize, ATA Sanitize/Secure Erase, SCSI).
Include un simulator determinist complet pentru validare și teste unitare în medii de laborator.
"""

from typing import Tuple, Dict, Any, List
import time
from .models import MediaType, DeviceTopology, SanitizeMethod, DeviceMetadata


class StorageCommandError(Exception):
    """Excepție ridicată când firmware-ul sau controlerul respinge o comandă critică."""
    pass


class HardwareAdapter:
    """
    Abstracție hardware pentru comenzi de nivel firmware.
    Interzice orice scriere logică mascată prin sistemul de fișiere.
    """

    def __init__(self, simulation_mode: bool = True):
        self.simulation_mode = simulation_mode
        self._mock_progress: Dict[str, float] = {}
        self._mock_power_cut: Dict[str, bool] = {}

    def issue_sanitize_command(self, device: DeviceMetadata, method: SanitizeMethod) -> Dict[str, Any]:
        """
        Emite comanda asincronă firmware corespunzătoare.
        Returnează descriptorul tranzacției inițiate.
        """
        if device.topology == DeviceTopology.BRIDGED_USB:
            raise StorageCommandError("Refuz comandă: puntea USB nu garantează propagarea transparentă a comenzii native Sanitize.")

        if not self.simulation_mode:
            # În mod real de producție (pe Linux UEFI minimal):
            # Se execută ioctl către nodul /dev/nvmeX (NVMe Admin Command 0x84)
            # sau ioctl HDIO_DRIVE_CMD / SG_IO pentru ATA/SCSI.
            raise NotImplementedError("Rularea pe hardware real necesită mediul de producție Linux UEFI cu privilegii directe de I/O.")

        # Mod Simulare Controlată
        self._mock_progress[device.serial_number] = 0.0
        return {
            "status": "COMMAND_ACCEPTED",
            "device_serial": device.serial_number,
            "method": method.value,
            "timestamp_start": time.time(),
        }

    def poll_sanitize_status(self, device: DeviceMetadata) -> Tuple[bool, float, str]:
        """
        Interoghează jurnalul de stare (ex: NVMe Sanitize Status Log 0x81).
        Returnează:
          (is_completed, progress_percentage, status_message)
        """
        if self.simulation_mode:
            if self._mock_power_cut.get(device.serial_number, False):
                return False, self._mock_progress.get(device.serial_number, 0.0), "HARDWARE_RESET_OR_POWER_CUT"

            current_prog = self._mock_progress.get(device.serial_number, 0.0)
            if current_prog < 100.0:
                current_prog += 25.0
                self._mock_progress[device.serial_number] = current_prog

            if current_prog >= 100.0:
                return True, 100.0, "SUCCESSFUL_COMPLETION"
            return False, current_prog, "OPERATION_IN_PROGRESS"

        raise NotImplementedError("Hardware I/O real disponibil doar în kernelul bootabil.")

    def sample_verify_lba(self, device: DeviceMetadata, sample_count: int = 1000) -> bool:
        """
        Citește eșantioane LBA aleatorii pe suprafața discului pentru a verifica
        că datele anterioare nu mai sunt accesibile și că se returnează zerouri sau starea ștearsă/nedefinită deterministă.
        """
        if self.simulation_mode:
            # Dacă operația a fost întreruptă sau discul este defect, verificarea eșuează
            if self._mock_power_cut.get(device.serial_number, False):
                return False
            return True

        raise NotImplementedError("Hardware I/O real.")

    # Utilitare pentru testare adversarială
    def inject_power_cut(self, serial_number: str) -> None:
        """Simulează o cădere accidentală de tensiune în timpul ștergerii."""
        self._mock_power_cut[serial_number] = True
