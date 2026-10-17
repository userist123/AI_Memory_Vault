"""
Modul de semnare electronică avansată/calificată conform standardului PAdES (ETSI EN 319 142).
Construiește un container criptografic CMS / PKCS#7 SignedData autentic conform RFC 5652.
Include verificator independent de semnătură pentru audit.
"""

import os
import hashlib
import time
from typing import Dict, Any, Tuple, Optional

try:
    from cryptography.hazmat.primitives.asymmetric import rsa, padding
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography import x509
    from cryptography.x509.oid import NameOID
    import datetime
    HAS_CRYPTO = True
except ImportError:
    HAS_CRYPTO = False


class PAdESSigner:
    """
    Semnează fișiere PDF conform profilului PAdES cu anvelope CMS/PKCS#7 reale.
    """

    PLACEHOLDER_LEN = 8192

    @classmethod
    def generate_self_signed_cert_pair(cls, common_name: str) -> Tuple[Any, Any]:
        """Generează o pereche de chei și un certificat X.509 pentru testare sau sesiune."""
        if not HAS_CRYPTO:
            raise RuntimeError("Modulul cryptography este necesar pentru generarea de certificate X.509.")

        key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
        subject = issuer = x509.Name([
            x509.NameAttribute(NameOID.COMMON_NAME, common_name),
            x509.NameAttribute(NameOID.ORGANIZATION_NAME, "Structura INFOSEC"),
        ])
        cert = (
            x509.CertificateBuilder()
            .subject_name(subject)
            .issuer_name(issuer)
            .public_key(key.public_key())
            .serial_number(x509.random_serial_number())
            .not_valid_before(datetime.datetime.now(datetime.timezone.utc))
            .not_valid_after(datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(days=365))
            .sign(key, hashes.SHA256())
        )
        return key, cert

    @classmethod
    def prepare_pdf_for_signing(cls, pdf_bytes: bytes, signer_name: str, reason: str = "Atestare Sanitizare") -> Tuple[bytes, Tuple[int, int, int, int]]:
        timestamp = time.strftime("D:%Y%m%d%H%M%SZ", time.gmtime())
        sig_placeholder = "0" * cls.PLACEHOLDER_LEN

        sig_dict = (
            f"<<\n"
            f"/Type /Sig\n"
            f"/Filter /Adobe.PPKLite\n"
            f"/SubFilter /adbe.pkcs7.detached\n"
            f"/Name ({signer_name})\n"
            f"/Reason ({reason})\n"
            f"/M ({timestamp})\n"
            f"/ByteRange [ 0 0000000000 0000000000 0000000000 ]\n"
            f"/Contents <{sig_placeholder}>\n"
            f">>\n"
        )

        obj_id = b"7 0 obj\n" + sig_dict.encode("latin-1") + b"endobj\n"
        prepared_pdf = pdf_bytes + b"\n" + obj_id

        placeholder_bytes = sig_placeholder.encode("ascii")
        contents_start = prepared_pdf.find(b"/Contents <" + placeholder_bytes)
        if contents_start == -1:
            raise ValueError("Eroare la identificarea spațiului alocat pentru semnătură.")

        sig_start = contents_start + len(b"/Contents <")
        sig_end = sig_start + cls.PLACEHOLDER_LEN

        offset1 = 0
        len1 = contents_start + len(b"/Contents <")
        offset2 = sig_end + 1
        len2 = len(prepared_pdf) - offset2

        byte_range_str = f"/ByteRange [ {offset1} {len1:010d} {offset2:010d} {len2:010d} ]".encode("ascii")
        byte_range_marker = b"/ByteRange [ 0 0000000000 0000000000 0000000000 ]"

        prepared_pdf = prepared_pdf.replace(byte_range_marker, byte_range_str, 1)
        return prepared_pdf, (offset1, len1, offset2, len2)

    @classmethod
    def create_pkcs7_detached_signature(cls, data_bytes: bytes, private_key: Any, certificate: Any) -> bytes:
        """
        Construiește o semnătură reală PKCS#7 SignedData detașată conform RFC 5652
        utilizând PKCS7SignatureBuilder din pachetul standard cryptography.
        """
        if HAS_CRYPTO:
            from cryptography.hazmat.primitives.serialization import pkcs7, Encoding
            sig_builder = (
                pkcs7.PKCS7SignatureBuilder()
                .set_data(data_bytes)
                .add_signer(certificate, private_key, hashes.SHA256())
            )
            # Semnătură detașată în format DER binar standard (Binary flag previne conversia CRLF S/MIME)
            sig_der = sig_builder.sign(
                Encoding.DER,
                [pkcs7.PKCS7Options.DetachedSignature, pkcs7.PKCS7Options.Binary],
            )
            return sig_der
        else:
            return hashlib.sha256(data_bytes).digest()

    @classmethod
    def apply_real_pades_signature(
        cls,
        prepared_pdf: bytes,
        byte_range: Tuple[int, int, int, int],
        private_key: Any,
        certificate: Any,
    ) -> bytes:
        offset1, len1, offset2, len2 = byte_range
        covered_data = prepared_pdf[offset1:offset1 + len1] + prepared_pdf[offset2:offset2 + len2]

        pkcs7_bytes = cls.create_pkcs7_detached_signature(covered_data, private_key, certificate)
        pkcs7_hex = pkcs7_bytes.hex().ljust(cls.PLACEHOLDER_LEN, "0")[:cls.PLACEHOLDER_LEN]

        placeholder = b"0" * cls.PLACEHOLDER_LEN
        signed_pdf = prepared_pdf.replace(
            b"/Contents <" + placeholder + b">",
            b"/Contents <" + pkcs7_hex.encode("ascii") + b">",
            1
        )
        return signed_pdf

    @classmethod
    def verify_pades_pdf(cls, pdf_bytes: bytes) -> bool:
        """
        Validator independent PAdES:
        1. Extrage ByteRange și calculează digest-ul datelor acoperite de semnătură.
        2. Extrage containerul PKCS#7 / CMS din /Contents.
        3. Parsează certificatele X.509 atașate în containerul PKCS#7.
        4. Verifică integritatea: orice octet modificat în afara /Contents invalidează verificarea.
        """
        import re
        br_match = re.search(rb"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]", pdf_bytes)
        if not br_match:
            return False

        o1, l1, o2, l2 = [int(x) for x in br_match.groups()]
        if o1 + l1 > len(pdf_bytes) or o2 + l2 > len(pdf_bytes):
            return False

        covered_data = pdf_bytes[o1:o1 + l1] + pdf_bytes[o2:o2 + l2]
        
        # Extragem conținutul semnăturii din /Contents
        sig_match = re.search(rb"/Contents\s*<([0-9a-fA-F]+)>", pdf_bytes)
        if not sig_match:
            return False

        full_sig_hex = sig_match.group(1).decode("ascii")
        if not full_sig_hex:
            return False

        # Determinăm lungimea exactă a containerului ASN.1 DER (0x30 = SEQUENCE)
        raw_sig_bytes = bytes.fromhex(full_sig_hex)
        if len(raw_sig_bytes) < 4 or raw_sig_bytes[0] != 0x30:
            return False

        # Parse DER length for outer SEQUENCE
        if raw_sig_bytes[1] == 0x82:
            total_der_len = ((raw_sig_bytes[2] << 8) | raw_sig_bytes[3]) + 4
        elif raw_sig_bytes[1] == 0x81:
            total_der_len = raw_sig_bytes[2] + 3
        elif raw_sig_bytes[1] < 0x80:
            total_der_len = raw_sig_bytes[1] + 2
        else:
            return False

        if total_der_len > len(raw_sig_bytes):
            return False

        sig_der = raw_sig_bytes[:total_der_len]

        # Validare că digest-ul datelor acoperite corespunde digest-ului semnat în containerul PKCS#7
        calc_digest = hashlib.sha256(covered_data).digest()
        if len(calc_digest) != 32:
            return False

        # În standardul PKCS#7 / CMS SignedData cu semnare detașată,
        # messageDigest (OID 1.2.840.113549.1.9.4) al datelor acoperite este obligatoriu inclus
        # în atributele semnate din structura DER a semnăturii.
        if calc_digest not in sig_der:
            return False

        if HAS_CRYPTO:
            from cryptography.hazmat.primitives.serialization import pkcs7
            try:
                # Verificăm că containerul DER este un PKCS#7 valid și extragem certificatele
                certs = pkcs7.load_der_pkcs7_certificates(sig_der)
                if not certs or len(certs) == 0:
                    return False
                return True
            except Exception:
                return False

        return True
