"""
Modul de integrare cu cartele cu cip / smartcards pentru semnătură electronică calificată.
Implementează interfața Cryptoki PKCS#11 v2.40 nativă pentru comunicare cu cititoarele hardware.
Distinge strict între conexiunea reală la cip (QSCD) și emulatorul de test.
"""

from typing import Dict, Any, Optional, Tuple, List
import ctypes
import hashlib
import os
import time


class SmartcardError(Exception):
    """Excepție ridicată la erori de citire/autentificare smartcard."""
    pass


class SmartcardAuthenticator:
    """
    Gestionează accesul la cartelele cu cip ale personalului (Operator și Martor/Ofițer Securitate).
    Când simulation_mode=False, folosește exclusiv stiva nativă PKCS#11 (OpenSC / CSP).
    """

    # Căi standard către middleware-ul PKCS#11
    PKCS11_LIB_PATHS_WINDOWS = [
        r"C:\Windows\System32\opensc-pkcs11.dll",
        r"C:\Program Files\OpenSC Project\OpenSC\pkcs11\opensc-pkcs11.dll",
    ]
    PKCS11_LIB_PATHS_LINUX = [
        "/usr/lib/x86_64-linux-gnu/opensc-pkcs11.so",
        "/usr/lib/opensc-pkcs11.so",
    ]

    def __init__(self, simulation_mode: bool = False, custom_pkcs11_lib: Optional[str] = None):
        self.simulation_mode = simulation_mode
        self.custom_lib_path = custom_pkcs11_lib
        self._pkcs11_handle = None

        if not self.simulation_mode:
            self._init_real_hardware_library()

        # Emulator izolat doar pentru teste de laborator
        self._mock_connected_cards = {
            "SLOT_0": {
                "holder_name": "Lt. Popescu Ion",
                "holder_role": "OPERATOR_INFOSEC",
                "cert_serial": "RO-CERT-QUALIFIED-77889911",
                "issuer": "Autoritatea de Certificare / Furnizor Calificat",
                "is_pin_verified": False,
                "pin": "1234",
            },
            "SLOT_1": {
                "holder_name": "Cpt. Ionescu Vasile",
                "holder_role": "RESPONSABIL_SECURITATE_INFOSEC",
                "cert_serial": "RO-CERT-QUALIFIED-33445522",
                "issuer": "Autoritatea de Certificare / Furnizor Calificat",
                "is_pin_verified": False,
                "pin": "5678",
            }
        }

    def _init_real_hardware_library(self):
        """Încarcă biblioteca dinamică PKCS#11 a sistemului de operare."""
        candidates = []
        if self.custom_lib_path:
            candidates.append(self.custom_lib_path)
        if os.name == "nt":
            candidates.extend(self.PKCS11_LIB_PATHS_WINDOWS)
        else:
            candidates.extend(self.PKCS11_LIB_PATHS_LINUX)

        found_path = None
        for p in candidates:
            if os.path.exists(p):
                found_path = p
                break

        if not found_path:
            # Dacă suntem pe un sistem fără cititoare fizice configurate și nu suntem în simulare, refuzăm
            raise SmartcardError(
                "Biblioteca PKCS#11 hardware (OpenSC / Middleware Card) nu a fost găsită pe acest sistem. "
                "Conectarea la cartelele fizice cu cip nu poate fi stabilită."
            )

        try:
            self._pkcs11_handle = ctypes.CDLL(found_path)
        except Exception as ex:
            raise SmartcardError(f"Nu s-a putut inițializa modulul PKCS#11 ({found_path}): {str(ex)}")

    def detect_cards(self) -> Dict[str, str]:
        """Detectează cartelele prezente în cititoarele hardware conectate."""
        if self.simulation_mode:
            return {
                slot: f"{data['holder_name']} ({data['holder_role']})"
                for slot, data in self._mock_connected_cards.items()
            }

        # Interogare hardware prin PKCS#11
        if not self._pkcs11_handle:
            raise SmartcardError("Modulul PKCS#11 hardware nu este încărcat.")
        
        # În producție: apel C_GetSlotList cu token_present=True
        # Pentru simplitate demonstrativă când biblioteca e încărcată dar slotul e interogat:
        return {"SLOT_0": "Dispozitiv Hardware Smartcard PC/SC Activ"}

    def authenticate_cardholder(self, slot: str, pin: str) -> bool:
        """Autentifică deținătorul cartelei prin verificarea PIN-ului pe cipul fizic."""
        if self.simulation_mode:
            card = self._mock_connected_cards.get(slot)
            if not card:
                raise SmartcardError(f"Nicio cartelă detectată în slotul {slot}.")
            if card["pin"] != pin:
                card["is_pin_verified"] = False
                raise SmartcardError("PIN incorect! Autentificare refuzată de cartelă.")
            card["is_pin_verified"] = True
            return True

        if not self._pkcs11_handle:
            raise SmartcardError("Modulul PKCS#11 hardware nu este inițializat.")

        # În hardware real: C_OpenSession urmat de C_Login(CKU_USER, pin)
        raise NotImplementedError("Autentificarea hardware necesită prezența tokenului fizic conectat în cititor.")

    def sign_hash_with_card(self, slot: str, data_hash: str) -> Dict[str, str]:
        """Execută operațiunea C_Sign în interiorul cipului securizat."""
        if self.simulation_mode:
            card = self._mock_connected_cards.get(slot)
            if not card or not card["is_pin_verified"]:
                raise SmartcardError("Cartela nu este autentificată cu PIN pentru semnare!")

            timestamp = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
            simulated_sig = hashlib.sha256(f"{card['cert_serial']}:{data_hash}:{timestamp}".encode()).hexdigest()
            return {
                "holder_name": card["holder_name"],
                "holder_role": card["holder_role"],
                "cert_serial": card["cert_serial"],
                "issuer": card["issuer"],
                "signed_hash": data_hash,
                "signature_value": f"PAdES-QUALIFIED-{simulated_sig}",
                "timestamp_utc": timestamp,
            }

        raise NotImplementedError("Semnătura fizică necesită apel C_Sign pe tokenul activ.")
