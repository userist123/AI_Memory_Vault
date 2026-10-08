"""
Driver Linux de nivel jos pentru comenzi native de stocare (IOCTL Direct Pass-Through).
Implementat cu ctypes pentru comunicare directă cu subsistemele Linux kernel /dev/nvme* și /dev/sd*.
Nu depinde de utilitare externe din user-space (nvme-cli, hdparm) pentru a menține TOE minimal.
Conform NVM Express Base Specification 2.2 și SCSI Architecture Model (SAM-6).
"""

import os
import ctypes
import struct
from typing import Tuple, Dict, Any, Optional

# Constante Linux IOCTL
NVME_IOCTL_ADMIN_CMD = 0xC0484E41  # _IOWR('N', 0x41, struct nvme_admin_cmd)
SG_IO = 0x2285                     # SCSI Generic pass-through

# Opcodes NVMe
NVME_ADMIN_OP_GET_LOG_PAGE = 0x02
NVME_ADMIN_OP_SANITIZE = 0x84
NVME_LOG_PAGE_SANITIZE_STATUS = 0x81

# Structură C: nvme_admin_cmd (conform linux/nvme_ioctl.h)
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

                # Parsare jurnal conform NVMe Base Spec 2.2:
                # Bytes 0-1: SPROG (Sanitize Progress, 16-bit)
                # Bytes 2-3: SSTAT (Sanitize Status, 16-bit)
                sprog, sstat = struct.unpack_from("<HH", buf.raw, 0)
                
                # SPROG: dacă valoarea este 0xFFFF, progresul este nedisponibil sau 100%
                # Progres procentual: (SPROG / 65535) * 100
                progress_pct = round((sprog / 65535.0) * 100.0, 1) if sprog <= 65535 else 0.0
                
                # SSTAT bits:
                # bit 0-2: Most Recent Sanitize Status
                # 000b = Nicio operație sau finalizată cu succes
                # 001b = În desfășurare
                # 010b = Eșuată
                status_code = sstat & 0x07
                if status_code == 0:
                    return True, 100.0, "COMPLETED_SUCCESS"
                elif status_code == 1:
                    return False, progress_pct, "IN_PROGRESS"
                elif status_code == 2:
                    return False, progress_pct, "FAILED"
                else:
                    return False, progress_pct, f"STATUS_CODE_{status_code}"
            finally:
                os.close(fd)
        except Exception as ex:
            return False, 0.0, f"ERR_{str(ex)}"
