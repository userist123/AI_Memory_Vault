"""
Modul de descoperire hardware reală a dispozitivelor de stocare (Device Discovery).
Interoghează subsistemele Linux sysfs (/sys/block/) și udev pentru identificarea discurilor fizice.
Exclude automat mediul de pe care rulează sistemul de operare pentru prevenirea auto-ștergerii.
"""

import os
import glob
from typing import List, Optional
from .models import DeviceMetadata, MediaType, DeviceTopology


class DeviceDiscoveryManager:
    """
    Scanează magistralele hardware reale pe un sistem Linux.
    """

    @staticmethod
    def get_boot_device_name() -> Optional[str]:
        """Identifică discul pe care este montat rootfs-ul curent (ex: sda, nvme0n1)."""
        if not os.path.exists("/proc/mounts"):
            return None
        try:
            with open("/proc/mounts", "r") as f:
                for line in f:
                    parts = line.strip().split()
                    if len(parts) >= 2 and parts[1] == "/":
                        root_dev = parts[0]  # ex: /dev/sda2 sau /dev/nvme0n1p2
                        dev_name = os.path.basename(root_dev)
                        # Eliminare prefix partiție (p1, 1 etc.)
                        if dev_name.startswith("nvme"):
                            return dev_name.split("p")[0]
                        else:
                            return "".join([c for c in dev_name if not c.isdigit()])
        except Exception:
            return None
        return None

    @classmethod
    def scan_physical_devices(cls) -> List[DeviceMetadata]:
        """
        Scanează /sys/block pentru a găsi toate dispozitivele fizice conectate.
        Exclude loopback, ramdisk-uri și discul de boot.
        """
        devices: List[DeviceMetadata] = []
        if not os.path.exists("/sys/block"):
            # Pe sisteme non-Linux (sau în mediu de testare fără sysfs), returnează listă goală
            return devices

        boot_dev = cls.get_boot_device_name()

        for bdev in os.listdir("/sys/block"):
            # Excludere dispozitive virtuale
            if bdev.startswith(("loop", "ram", "zram", "dm-", "sr")):
                continue
            # Excludere discul de boot al sistemului
            if boot_dev and bdev.startswith(boot_dev):
                continue

            sys_path = os.path.join("/sys/block", bdev)
            dev_node = f"/dev/{bdev}"

            # Citire atribute din sysfs
            model = cls._read_sysfs_attr(sys_path, "device/model") or bdev.upper()
            serial = cls._read_sysfs_attr(sys_path, "device/serial") or "UNKNOWN_SERIAL"
            firmware = cls._read_sysfs_attr(sys_path, "device/firmware_rev") or cls._read_sysfs_attr(sys_path, "device/rev") or "N/A"
            
            # Citire capacitate în sectoare (1 sector = 512 bytes în sysfs size)
            size_sectors_str = cls._read_sysfs_attr(sys_path, "size") or "0"
            try:
                capacity_bytes = int(size_sectors_str) * 512
            except ValueError:
                capacity_bytes = 0

            # Detecție topologie: verificăm calea fizică a dispozitivului
            real_path = os.path.realpath(sys_path)
            if "/usb" in real_path:
                topology = DeviceTopology.BRIDGED_USB
                media_type = MediaType.USB_FLASH
            elif bdev.startswith("nvme"):
                topology = DeviceTopology.NATIVE_PCIE
                media_type = MediaType.NVME_SSD
            else:
                topology = DeviceTopology.NATIVE_SATA
                rotational = cls._read_sysfs_attr(sys_path, "queue/rotational")
                if rotational == "0":
                    media_type = MediaType.SATA_SSD
                else:
                    media_type = MediaType.SATA_HDD

            devices.append(
                DeviceMetadata(
                    serial_number=serial.strip(),
                    model_number=model.strip(),
                    firmware_revision=firmware.strip(),
                    capacity_bytes=capacity_bytes,
                    media_type=media_type,
                    topology=topology,
                    bus_path=dev_node,
                    is_healthy=True,
                )
            )

        return devices

    @staticmethod
    def _read_sysfs_attr(sys_path: str, attr_rel: str) -> Optional[str]:
        target = os.path.join(sys_path, attr_rel)
        if os.path.exists(target):
            try:
                with open(target, "r", encoding="utf-8", errors="replace") as f:
                    return f.read().strip()
            except Exception:
                return None
        return None
