import re
from typing import Tuple

class Validators:
    @staticmethod
    def validate_hash_sha256(hash_str: str) -> Tuple[bool, str]:
        """Validează hash SHA256 (64 caractere hex)."""
        if not hash_str:
            return True, ""
        
        if len(hash_str) != 64:
            return False, "Hash SHA256 trebuie să aibă exact 64 caractere"
        
        if not re.match(r'^[a-fA-F0-9]{64}$', hash_str):
            return False, "Hash SHA256 trebuie să conțină doar caractere hex (0-9, a-f)"
        
        return True, ""

    @staticmethod
    def validate_required_field(value: str, field_name: str) -> Tuple[bool, str]:
        """Validează câmp obligatoriu."""
        if not value or not value.strip():
            return False, f"{field_name} este obligatoriu"
        
        return True, ""

    @staticmethod
    def validate_storage_capacity(used: float, total: float) -> Tuple[bool, str]:
        """Validează capacitate stocare."""
        if used < 0 or total < 0:
            return False, "Capacitățile nu pot fi negative"
        
        if used > total:
            return False, "Spațiul folosit nu poate fi mai mare decât capacitatea totală"
        
        return True, ""

    @staticmethod
    def validate_serial_number(sn: str) -> Tuple[bool, str]:
        """Validează serial number (opțional, dar dacă există să fie valid)."""
        if not sn:
            return True, ""
        
        if len(sn) < 4:
            return False, "Serial number trebuie să aibă minim 4 caractere"
        
        if len(sn) > 50:
            return False, "Serial number prea lung (max 50 caractere)"
        
        return True, ""
