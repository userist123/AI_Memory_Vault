"""
Modul de integrare cu cartele cu cip / smartcards pentru semnătură electronică calificată.
Suportă interfețe PKCS#11 și CryptoAPI / CNG (Windows Smart Card Subsystem).
Permite semnarea în interiorul dispozitivului securizat (QSCD - Qualified Signature Creation Device).
"""

from typing import Dict, Any, Optional, Tuple
import hashlib
import time


class SmartcardError(Exception):
    """Excepție ridicată la erori de citire/autentificare smartcard."""
    pass


class SmartcardAuthenticator:
    """
    Gestionează accesul la cartelele cu cip ale personalului (Operator și Martor/Ofițer Securitate).
    Cheia privată nu părăsește niciodată cartela fizică.
    """

    def __init__(self, simulation_mode: bool = True):
        self.simulation_mode = simulation_mode
        self._connected_cards = {
            "SLOT_0": {
                "holder_name": "Lt. Popescu Ion",
                "holder_role": "OPERATOR_INFOSEC",
                "cert_serial": "RO-CERT-QUALIFIED-77889911",
                "issuer": "Autoritatea de Certificare a Ministerului / Furnizor Calificat",
                "is_pin_verified": False,
                "pin": "1234",
            },
            "SLOT_1": {
                "holder_name": "Cpt. Ionescu Vasile",
                "holder_role": "RESPONSABIL_SECURITATE_INFOSEC",
                "cert_serial": "RO-CERT-QUALIFIED-33445522",
                "issuer": "Autoritatea de Certificare a Ministerului / Furnizor Calificat",
                "is_pin_verified": False,
                "pin": "5678",
            }
        }

    def detect_cards(self) -> Dict[str, str]:
        """Detectează cartelele prezente în cititoarele USB conectate."""
        if self.simulation_mode:
            return {
                slot: f"{data['holder_name']} ({data['holder_role']})"
                for slot, data in self._connected_cards.items()
            }
        raise NotImplementedError("Detectare hardware PC/SC pe stație.")

    def authenticate_cardholder(self, slot: str, pin: str) -> bool:
        """Autentifică deținătorul cartelei prin verificarea PIN-ului pe cip."""
        if self.simulation_mode:
            card = self._connected_cards.get(slot)
            if not card:
                raise SmartcardError(f"Nicio cartelă detectată în slotul {slot}.")
            if card["pin"] != pin:
                card["is_pin_verified"] = False
                raise SmartcardError("PIN incorect! Autentificare refuzată de cartelă.")
            card["is_pin_verified"] = True
            return True
        raise NotImplementedError("Verificare PIN prin middleware PKCS#11.")

    def sign_hash_with_card(self, slot: str, data_hash: str) -> Dict[str, str]:
        """
        Trimite hash-ul către cartelă pentru semnare internă (RSA-PSS sau ECDSA).
        Cheia privată rămâne în siguranță pe cip.
        """
        card = self._connected_cards.get(slot)
        if not card or not card["is_pin_verified"]:
            raise SmartcardError("Cartela nu este autentificată cu PIN pentru semnare!")

        timestamp = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
        # În mod real, aceasta este semnătura criptografică returnată de funcția C_Sign din PKCS#11
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
