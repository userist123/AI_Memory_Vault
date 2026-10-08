"""
Motorul de politici (Policy Engine) pentru Secure Sanitization Engine.
Mapează atributele suportului și regimul de clasificare către o metodă aprobată sau decizia de distrugere fizică.
"""

from typing import Tuple, Dict, Any
from .models import (
    Jurisdiction,
    ClassificationLevel,
    MediaType,
    DeviceTopology,
    SanitizeMethod,
    FinalDisposition,
    DeviceMetadata,
)


class PolicyEngine:
    """
    Evaluează dacă un mediu poate fi sanitizat prin software sau necesită distrugere mecanică.
    Fără moduri generice 'force' sau excepții nesancționate de autoritate.
    """

    HIGH_SECURITY_LEVELS = {
        ClassificationLevel.SECRET,
        ClassificationLevel.STRICT_SECRET,
        ClassificationLevel.NATO_SECRET,
        ClassificationLevel.COSMIC_TOP_SECRET,
        ClassificationLevel.EU_SECRET,
        ClassificationLevel.EU_TOP_SECRET,
    }

    def requires_dual_control(self, level: ClassificationLevel) -> bool:
        """Determină dacă operația impune autentificare duală (operator + martor)."""
        return level in self.HIGH_SECURITY_LEVELS

    def evaluate_policy(
        self,
        jurisdiction: Jurisdiction,
        classification: ClassificationLevel,
        device: DeviceMetadata,
        target_disposition: str = "REUSE_OR_RELEASE",
    ) -> Tuple[SanitizeMethod, str]:
        """
        Returnează:
          (Metodă aprobată, Rationale / Justificare decizie)
        În caz de dubiu sau neconformitate hardware, decizia este FAIL-SAFE: PHYSICAL_DESTRUCTION_REQUIRED.
        """
        # 1. Verificare stare de sănătate hardware
        if not device.is_healthy or device.media_type == MediaType.UNKNOWN_OR_DEFECTIVE:
            return (
                SanitizeMethod.PHYSICAL_DESTRUCTION_REQUIRED,
                "Mediu marcat ca defect sau cu integritate hardware compromisă. Comandă software nefezabilă.",
            )

        # 2. Verificare topologie: punțile USB sau controlerele RAID opace blochează comenzile directe
        if device.topology == DeviceTopology.BRIDGED_USB:
            return (
                SanitizeMethod.PHYSICAL_DESTRUCTION_REQUIRED,
                "Mediu conectat prin punte USB nevalidată. Comenzile native Sanitize pot fi traduse incorect sau blocate.",
            )

        if device.topology == DeviceTopology.BRIDGED_RAID:
            return (
                SanitizeMethod.PHYSICAL_DESTRUCTION_REQUIRED,
                "Mediu în spatele unui controller RAID virtualizat. Necesită decuplare și sanitizare la nivel de membru fizic.",
            )

        # 3. Reguli specifice pentru NVMe SSD
        if device.media_type == MediaType.NVME_SSD:
            if classification in self.HIGH_SECURITY_LEVELS:
                if device.crypto_history_certified:
                    return (
                        SanitizeMethod.NVME_SANITIZE_CRYPTO_ERASE,
                        "NVMe Sanitize Crypto Erase autorizat: suport cu istoric criptografic integral certificat.",
                    )
                return (
                    SanitizeMethod.NVME_SANITIZE_BLOCK_ERASE,
                    "NVMe Sanitize Block Erase autorizat: alterare hardware completă a celulelor NAND (inclusiv spare/over-provisioning).",
                )
            else:
                return (
                    SanitizeMethod.NVME_SANITIZE_BLOCK_ERASE,
                    "NVMe Sanitize Block Erase standard aplicat pentru nivel scăzut/mediu.",
                )

        # 4. Reguli specifice pentru SATA SSD
        if device.media_type == MediaType.SATA_SSD:
            return (
                SanitizeMethod.ATA_SANITIZE_BLOCK,
                "ATA Sanitize Block Erase autorizat pe controler nativ SATA.",
            )

        # 5. Reguli specifice pentru HDD Magnetic (SATA / SAS)
        if device.media_type in (MediaType.SATA_HDD, MediaType.SAS_HDD):
            if device.hpa_detected or device.dco_detected:
                return (
                    SanitizeMethod.PHYSICAL_DESTRUCTION_REQUIRED,
                    "Zone ascunse HPA/DCO detectate și nerezolvate. Risc de date reziduale ascunse.",
                )
            if device.media_type == MediaType.SAS_HDD:
                return (
                    SanitizeMethod.SCSI_SANITIZE_BLOCK,
                    "SCSI Sanitize Block Erase autorizat pentru HDD SAS.",
                )
            return (
                SanitizeMethod.ATA_ENHANCED_SECURE_ERASE,
                "ATA Enhanced Secure Erase autorizat pe HDD rotativ.",
            )

        # 6. Flash USB standalone / carduri SD
        if device.media_type == MediaType.USB_FLASH:
            if classification in self.HIGH_SECURITY_LEVELS:
                return (
                    SanitizeMethod.PHYSICAL_DESTRUCTION_REQUIRED,
                    "USB Flash/SD clasificate la nivel SECRET/STRICT SECRET nu permit certitudine software. Distrugere fizică obligatorie.",
                )
            return (
                SanitizeMethod.PHYSICAL_DESTRUCTION_REQUIRED,
                "Lipsa comenzii firmware Sanitize pe stick USB generic. Distrugere fizică recomandată.",
            )

        # 7. Medii optice (CD/DVD/BD)
        if device.media_type == MediaType.OPTICAL:
            return (
                SanitizeMethod.PHYSICAL_DESTRUCTION_REQUIRED,
                "Mediile optice nu pot fi sanitizate prin software. Distrugere fizică obligatorie.",
            )

        # Fallback de siguranță
        return (
            SanitizeMethod.PHYSICAL_DESTRUCTION_REQUIRED,
            "Nicio metodă software aprobată pentru combinația de parametri declarată.",
        )
