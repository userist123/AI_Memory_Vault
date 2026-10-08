"""
Driver Linux de nivel jos pentru comenzi native de stocare (IOCTL Direct Pass-Through).
Implementat cu ctypes pentru comunicare directă cu subsistemele Linux kernel /dev/nvme* și /dev/sd*.
Aliniat strict la NVM Express Base Specification 2.2 (Secțiunea 5.24 & Jurnalul de stare 0x81).
"""

import os
import ctypes
import struct
from typing import Tuple, Dict, Any, Optional

# Constante Linux IOCTL
NVME_IOCTL_ADMIN_CMD = 0xC0484E41  # _IOWR('N', 0x41, struct nvme_admin_cmd)
SG_IO = 0x2285                     # SCSI Generic pass-through

# Opcodes NVMe conform Specificației Oficiale
NVME_ADMIN_OP_GET_LOG_PAGE = 0x02
NVME_ADMIN_OP_SANITIZE = 0x84
NVME_LOG_PAGE_SANITIZE_STATUS = 0x81

# Structură C: nvme_admin_cmd (conform linux/nvme_ioctl.h - exact 72 octeți)
class NVMeAdminCmd(ctypes.Structure):
    _fields_ = [
        ("opcode", ctypes.c_uint8),
        ("flags", ctypes.c_uint8),
        ("reserved1", ctypes.c_uint16),
        ("nsid", ctypes.c_uint32),
        ("cdw2", ctypes.c_uint32),
        ("cdw3", ctypes.c_uint32),
        ("metadata", ctypes.c_uint64),
        ("addr", ctypes.c_uint64),
        ("metadata_len", ctypes.c_uint32),
        ("data_len", ctypes.c_uint32),
        ("cdw10", ctypes.c_uint32),
        ("cdw11", ctypes.c_uint32),
        ("cdw12", ctypes.c_uint32),
        ("cdw13", ctypes.c_uint32),
        ("cdw14", ctypes.c_uint32),
        ("cdw15", ctypes.c_uint32),
        ("timeout_ms", ctypes.c_uint32),
        ("result", ctypes.c_uint32),
    ]


# Structură C: sg_io_hdr (conform scsi/sg.h)
class SgIoHdr(ctypes.Structure):
    _fields_ = [
        ("interface_id", ctypes.c_int),
        ("dxfer_direction", ctypes.c_int),
        ("cmd_len", ctypes.c_uint8),
        ("mx_sb_len", ctypes.c_uint8),
        ("iovec_count", ctypes.c_uint16),
        ("dxfer_len", ctypes.c_uint32),
        ("dxferp", ctypes.c_void_p),
        ("cmdp", ctypes.c_char_p),
        ("sbp", ctypes.c_char_p),
        ("timeout", ctypes.c_uint32),
        ("flags", ctypes.c_uint32),
        ("pack_id", ctypes.c_int),
        ("usr_ptr", ctypes.c_void_p),
        ("status", ctypes.c_uint8),
        ("masked_status", ctypes.c_uint8),
        ("msg_status", ctypes.c_uint8),
        ("sb_len_wr", ctypes.c_uint8),
        ("host_status", ctypes.c_uint16),
        ("driver_status", ctypes.c_uint16),
        ("resid", ctypes.c_int),
        ("duration", ctypes.c_uint32),
        ("info", ctypes.c_uint32),
    ]


def parse_nvme_sanitize_status_raw(sstat: int, sprog: int) -> Tuple[bool, float, str]:
    """
    Funcție pură de parsare a registrelor SSTAT și SPROG.
    Permite testarea unitară riguroasă a tuturor stărilor conform NVMe Base Spec 2.2.
    
    NVMe Base Spec 2.2, Table 278 (Sanitize Status Log Page):
      Bits 02:00 Sanitize Status (SSTAT & 0x07):
        000b (0x0): The NVM subsystem has never been sanitized.
        001b (0x1): The most recent sanitize operation completed successfully.
        010b (0x2): A sanitize operation is currently in progress.
        011b (0x3): The most recent sanitize operation failed.
    """
    status_code = sstat & 0x07
    
    # Progres procentual (SPROG: 0xFFFF înseamnă nicio operație activă sau finalizat)
    if sprog == 0xFFFF:
        progress_pct = 100.0 if status_code == 0x01 else 0.0
    elif sprog <= 65535:
        progress_pct = round((sprog / 65535.0) * 100.0, 1)
    else:
        progress_pct = 0.0

    if status_code == 0x01:
        # Singura stare validă de succes
        return True, 100.0, "COMPLETED_SUCCESS"
    elif status_code == 0x02:
        # Operație în curs de desfășurare
        return False, progress_pct, "IN_PROGRESS"
    elif status_code == 0x00:
        # Subsistemul nu a fost niciodată sanitizat! Nu este succes!
        return False, 0.0, "NEVER_SANITIZED"
    elif status_code == 0x03:
        # Operația a eșuat
        return False, progress_pct, "FAILED"
    else:
        return False, progress_pct, f"UNKNOWN_STATUS_CODE_{status_code}"


class LinuxStorageDriver:
    """
    Driver de execuție hardware pentru platforma bootabilă minimală UEFI Linux.
    """

    @staticmethod
    def issue_nvme_sanitize_block_erase(device_path: str) -> bool:
        """
        Emite comanda NVMe Sanitize (Block Erase) asincronă către controller.
        CDW10: SANACT = 0x02 (Block Erase).
        """
        if not os.path.exists(device_path):
            raise FileNotFoundError(f"Nodul de dispozitiv {device_path} nu a fost găsit.")

        cmd = NVMeAdminCmd()
        cmd.opcode = NVME_ADMIN_OP_SANITIZE
        cmd.nsid = 0  # Sanitize se aplică întregului subsistem NVM (NSID 0)
        cmd.cdw10 = 0x02  # SANACT: Block Erase
        cmd.timeout_ms = 60000

        try:
            fd = os.open(device_path, os.O_RDWR)
            try:
                import fcntl
                res = fcntl.ioctl(fd, NVME_IOCTL_ADMIN_CMD, cmd)
                return res == 0
            finally:
                os.close(fd)
        except (ImportError, AttributeError, OSError) as ex:
            raise RuntimeError(f"Eroare execuție ioctl NVMe pe {device_path}: {str(ex)}")

    @staticmethod
    def get_nvme_sanitize_status(device_path: str) -> Tuple[bool, float, str]:
        """
        Citește pagina de jurnal NVMe Sanitize Status (Log ID 0x81).
        Returnează (is_finished, progress_percentage, status_str).
        """
        if not os.path.exists(device_path):
            raise FileNotFoundError(f"Nodul {device_path} nu există.")

        buf = ctypes.create_string_buffer(512)
        cmd = NVMeAdminCmd()
        cmd.opcode = NVME_ADMIN_OP_GET_LOG_PAGE
        cmd.nsid = 0xFFFFFFFF
        cmd.addr = ctypes.addressof(buf)
        cmd.data_len = 512
        cmd.cdw10 = NVME_LOG_PAGE_SANITIZE_STATUS | (127 << 16)  # 512 bytes (128 dwords - 1)
        cmd.timeout_ms = 5000

        try:
            fd = os.open(device_path, os.O_RDWR)
            try:
                import fcntl
                res = fcntl.ioctl(fd, NVME_IOCTL_ADMIN_CMD, cmd)
                if res != 0:
                    return False, 0.0, "IOCTL_FAILED"

                sprog, sstat = struct.unpack_from("<HH", buf.raw, 0)
                return parse_nvme_sanitize_status_raw(sstat=sstat, sprog=sprog)
            finally:
                os.close(fd)
        except Exception as ex:
            return False, 0.0, f"ERR_{str(ex)}"
