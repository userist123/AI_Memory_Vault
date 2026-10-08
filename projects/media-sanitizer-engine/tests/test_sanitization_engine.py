"""
Suita de teste de securitate și conformitate pentru Secure Sanitization Engine (TOE-SSE-v1).
Aliniată la cerințele din Etapa 1: Distincție clară între Simulator (SIMULATED_NOT_SANITIZED)
și Hardware (ABORTED_UNSUPPORTED_ENVIRONMENT când dispozitivul fizic nu este prezent).
"""

import sys
import os
import pytest

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from src.models import (
    Jurisdiction,
    ClassificationLevel,
    MediaType,
    DeviceTopology,
    SanitizeMethod,
    FinalDisposition,
    DeviceMetadata,
    DualAuthorization,
)
from src.hardware_adapter import HardwareAdapter
from src.state_machine import SanitizationSession, EngineState
from src.smartcard_auth import SmartcardAuthenticator, SmartcardError
from src.windows_registry_app import WindowsRegistryApp
from src.report_generator import OfficialReportGenerator


@pytest.fixture
def sample_nvme_device():
    return DeviceMetadata(
        serial_number="S676NF0R123456",
        model_number="SAMSUNG MZVL2512HCJQ",
        firmware_revision="HPS8101Q",
        capacity_bytes=512110190592,
        media_type=MediaType.NVME_SSD,
        topology=DeviceTopology.NATIVE_PCIE,
        bus_path="/dev/nvme0n1",
        is_healthy=True,
    )


@pytest.fixture
def dual_auth_credentials():
    return DualAuthorization(
        operator_id="OP-ION-POPESCU",
        operator_token="TOKEN-CERT-QUALIFIED-9812",
        witness_id="SEC-VASILE-IONESCU",
        witness_token="TOKEN-CERT-QUALIFIED-4411",
    )


def test_nominal_nvme_sanitization_flow_simulated(sample_nvme_device, dual_auth_credentials):
    """
    Test flux de laborator: Simulatorul parcurge pașii dar emite strict SIMULATED_NOT_SANITIZED.
    """
    adapter = HardwareAdapter(simulation_mode=True)
    session = SanitizationSession(
        device=sample_nvme_device,
        classification=ClassificationLevel.STRICT_SECRET,
        jurisdiction=Jurisdiction.RO,
        hardware_adapter=adapter,
    )
    assert session.state == EngineState.DEVICE_IDENTIFIED

    confirmed = session.confirm_target_safeguard("3456")
    assert confirmed is True
    assert session.state == EngineState.TARGET_VERIFIED

    authorized = session.evaluate_and_authorize(dual_auth=dual_auth_credentials)
    assert authorized is True
    assert session.state == EngineState.AUTHORIZED
    assert session.authorized_method == SanitizeMethod.NVME_SANITIZE_BLOCK_ERASE

    success = session.execute_sanitization()
    assert success is True
    assert session.state == EngineState.COMPLETED
    assert session.final_disposition == FinalDisposition.SIMULATED_NOT_SANITIZED

    smartcard_sigs = {
        "operator": "PAdES-QUALIFIED-CERT-SIGNATURE-OP-ION-POPESCU",
        "witness": "PAdES-QUALIFIED-CERT-SIGNATURE-SEC-VASILE-IONESCU",
    }
    manifest = session.export_manifest(smartcard_signatures=smartcard_sigs)
    assert manifest["final_disposition"] == "SIMULATED_NOT_SANITIZED"
    assert manifest["execution_mode"] == "SIMULATION_LABORATORY_TEST"
    assert manifest["operational_validity"] == "INVALID_FOR_OFFICIAL_DECLASSIFICATION_SIMULATION_ONLY"


def test_target_mismatch_safeguard(sample_nvme_device):
    """Test adversarial: Operatorul greșește serialul fizic -> sistemul blochează sesiunea."""
    session = SanitizationSession(
        device=sample_nvme_device,
        classification=ClassificationLevel.SECRET,
        hardware_adapter=HardwareAdapter(simulation_mode=True),
    )
    confirmed = session.confirm_target_safeguard("9999")
    assert confirmed is False
    assert session.state == EngineState.FAILED_REJECTED
    assert "nu corespunde" in session.rejection_reason


def test_usb_bridged_device_rejected(sample_nvme_device, dual_auth_credentials):
    """Test conformitate: Punte USB (BRIDGED_USB) -> refuz software, decizie: distrugere mecanică."""
    bridged_device = DeviceMetadata(
        serial_number="S676NF0R123456",
        model_number="SAMSUNG MZVL2512HCJQ",
        firmware_revision="HPS8101Q",
        capacity_bytes=512110190592,
        media_type=MediaType.NVME_SSD,
        topology=DeviceTopology.BRIDGED_USB,
        bus_path="/dev/sdb",
        is_healthy=True,
    )
    session = SanitizationSession(
        device=bridged_device,
        classification=ClassificationLevel.SECRET,
        hardware_adapter=HardwareAdapter(simulation_mode=True),
    )
    session.confirm_target_safeguard("3456")
    authorized = session.evaluate_and_authorize(dual_auth=dual_auth_credentials)
    
    assert authorized is False
    assert session.state == EngineState.FAILED_REJECTED
    assert session.final_disposition == FinalDisposition.NON_CONFORM_REQUIRES_DESTRUCTION
    assert "punte USB" in session.rejection_reason


def test_high_security_requires_dual_control(sample_nvme_device):
    """Test securitate: Nivel COSMIC_TOP_SECRET fără martor autorizat -> refuz autorizare."""
    session = SanitizationSession(
        device=sample_nvme_device,
        classification=ClassificationLevel.COSMIC_TOP_SECRET,
        jurisdiction=Jurisdiction.NATO,
        hardware_adapter=HardwareAdapter(simulation_mode=True),
    )
    session.confirm_target_safeguard("3456")
    
    single_user = DualAuthorization(
        operator_id="OP-SINGLE",
        operator_token="TOKEN-1",
        witness_id="",
        witness_token="",
    )
    authorized = session.evaluate_and_authorize(dual_auth=single_user)
    assert authorized is False
    assert session.state == EngineState.FAILED_REJECTED
    assert "Lipsă autorizare duală" in session.rejection_reason


def test_power_cut_in_progress_fails_safe(sample_nvme_device, dual_auth_credentials):
    """Test reziliență: Căderea de tensiune în timpul ștergerii marchează discul ca INCOMPLETE_ABORTED."""
    adapter = HardwareAdapter(simulation_mode=True)
    session = SanitizationSession(
        device=sample_nvme_device,
        classification=ClassificationLevel.SECRET,
        hardware_adapter=adapter,
    )
    session.confirm_target_safeguard("3456")
    session.evaluate_and_authorize(dual_auth=dual_auth_credentials)

    adapter.inject_power_cut(sample_nvme_device.serial_number)

    success = session.execute_sanitization()
    assert success is False
    assert session.state == EngineState.FAILED_REJECTED
    assert session.final_disposition == FinalDisposition.INCOMPLETE_ABORTED
    assert "HARDWARE_RESET_OR_POWER_CUT" in session.rejection_reason


def test_smartcard_authentication_and_wrong_pin():
    """Test Smartcard: PIN corect permite semnarea; PIN greșit este respins."""
    auth = SmartcardAuthenticator(simulation_mode=True)
    cards = auth.detect_cards()
    assert "SLOT_0" in cards
    assert "SLOT_1" in cards

    with pytest.raises(SmartcardError, match="PIN incorect"):
        auth.authenticate_cardholder("SLOT_0", "0000")

    assert auth.authenticate_cardholder("SLOT_0", "1234") is True
    sig_info = auth.sign_hash_with_card("SLOT_0", "f94c2491d2bce079...")
    assert "PAdES-QUALIFIED" in sig_info["signature_value"]
    assert sig_info["cert_serial"] == "RO-CERT-QUALIFIED-77889911"


def test_end_to_end_windows_registry_and_official_pv(sample_nvme_device, dual_auth_credentials, tmp_path):
    """
    Test E2E Simulator: Manifest simulat exportat și marcat corespunzător în registrul Windows.
    """
    adapter = HardwareAdapter(simulation_mode=True)
    session = SanitizationSession(
        device=sample_nvme_device,
        classification=ClassificationLevel.STRICT_SECRET,
        jurisdiction=Jurisdiction.RO,
        hardware_adapter=adapter,
    )
    session.confirm_target_safeguard("3456")
    session.evaluate_and_authorize(dual_auth=dual_auth_credentials)
    session.execute_sanitization()
    raw_manifest = session.export_manifest()

    test_reg_file = str(tmp_path / "test_registry.json")
    app = WindowsRegistryApp(registry_file=test_reg_file)
    assert app.import_and_validate_manifest(raw_manifest) is True

    signed_manifest = app.process_smartcard_dual_signing(
        manifest_data=raw_manifest,
        operator_pin="1234",
        witness_pin="5678",
    )
    assert "smartcard_operator_qualified_sig" in signed_manifest["signatures"]
    assert "smartcard_witness_qualified_sig" in signed_manifest["signatures"]

    archive_dir = str(tmp_path / "archive")
    digital_doc = app.archive_digital_record(
        signed_manifest,
        sic_inventory_number="INV-SIC-TEST-001",
        archive_dir=archive_dir,
    )
    assert digital_doc["document_type"] == "PROCES_VERBAL_SANITIZARE_ELECTRONIC"
    assert digital_doc["dispozitie_finala"]["verdict"] == "SIMULATED_NOT_SANITIZED"
    assert len(app.records) == 1
    assert app.records[0]["verdict"] == "SIMULATED_NOT_SANITIZED"


def test_registry_bridge_export_package(sample_nvme_device, dual_auth_credentials, tmp_path):
    """
    Test Integrare: Export pachet destinat aplicației de registratură electronică.
    """
    from src.registry_connector import RegistryBridgeClient

    adapter = HardwareAdapter(simulation_mode=True)
    session = SanitizationSession(
        device=sample_nvme_device,
        classification=ClassificationLevel.STRICT_SECRET,
        jurisdiction=Jurisdiction.RO,
        hardware_adapter=adapter,
    )
    session.confirm_target_safeguard("3456")
    session.evaluate_and_authorize(dual_auth=dual_auth_credentials)
    session.execute_sanitization()
    raw_manifest = session.export_manifest()

    bridge = RegistryBridgeClient()
    export_dir = str(tmp_path / "export_reg")
    output_file = bridge.prepare_and_sign_for_registry(
        raw_manifest=raw_manifest,
        operator_pin="1234",
        witness_pin="5678",
        sic_inventory_number="INV-SIC-2026-M881",
        output_folder=export_dir,
    )

    assert os.path.exists(output_file)
    assert output_file.endswith(".pdf")
    with open(output_file, "rb") as pf:
        content = pf.read()
    assert content.startswith(b"%PDF-1.4")
    assert b"/Type /Sig" in content
    assert b"/ByteRange" in content
    assert b"/Contents <" in content


def test_linux_ioctl_driver_struct_sizes():
    """Test ctypes ABI kernel Linux."""
    import ctypes
    from src.linux_ioctl_driver import NVMeAdminCmd, SgIoHdr

    assert ctypes.sizeof(NVMeAdminCmd) == 72
    assert ctypes.sizeof(SgIoHdr) > 0
