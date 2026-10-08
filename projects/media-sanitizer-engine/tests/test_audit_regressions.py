"""
Suita de teste de regresie dedicată constatărilor din auditul tehnic static.
Demonstrează eliminarea succesului fals, parsarea conformă NVMe Base Spec 2.2
și blocarea riguroasă a componentelor nevalidate hardware.
"""

import os
import sys
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
from src.hardware_adapter import HardwareAdapter, StorageCommandError
from src.state_machine import SanitizationSession, EngineState
from src.linux_ioctl_driver import parse_nvme_sanitize_status_raw
from src.pades_pdf_generator import PAdESPDFGenerator


@pytest.fixture
def dummy_device():
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
def valid_dual_auth():
    return DualAuthorization(
        operator_id="OP-ION",
        operator_token="TOKEN-1",
        witness_id="WIT-VASILE",
        witness_token="TOKEN-2",
    )


def test_regression_simulation_never_emits_conform_purged(dummy_device, valid_dual_auth):
    """
    AUDIT CONSTATARE 1: Simulatorul nu are voie să emită CONFORM_PURGED.
    Trebuie să emită strict SIMULATED_NOT_SANITIZED.
    """
    sim_adapter = HardwareAdapter(simulation_mode=True)
    session = SanitizationSession(
        device=dummy_device,
        classification=ClassificationLevel.STRICT_SECRET,
        hardware_adapter=sim_adapter,
    )
    session.confirm_target_safeguard("3456")
    session.evaluate_and_authorize(dual_auth=valid_dual_auth)
    session.execute_sanitization()

    assert session.final_disposition == FinalDisposition.SIMULATED_NOT_SANITIZED
    assert session.final_disposition != FinalDisposition.CONFORM_PURGED

    manifest = session.export_manifest()
    assert manifest["execution_mode"] == "SIMULATION_LABORATORY_TEST"
    assert manifest["operational_validity"] == "INVALID_FOR_OFFICIAL_DECLASSIFICATION_SIMULATION_ONLY"


def test_regression_nvme_sstat_parser_all_states():
    """
    AUDIT CONSTATARE 4: Parserul SSTAT & 0x07 conform NVMe Base Spec 2.2.
    - 0x0 = NEVER_SANITIZED (Nu este succes!)
    - 0x1 = COMPLETED_SUCCESS (Singurul succes)
    - 0x2 = IN_PROGRESS (În derulare)
    - 0x3 = FAILED (Eșec)
    """
    # 0x0: Never sanitized
    is_done, prog, msg = parse_nvme_sanitize_status_raw(sstat=0x00, sprog=0xFFFF)
    assert is_done is False
    assert msg == "NEVER_SANITIZED"

    # 0x1: Completed successfully
    is_done, prog, msg = parse_nvme_sanitize_status_raw(sstat=0x01, sprog=0xFFFF)
    assert is_done is True
    assert prog == 100.0
    assert msg == "COMPLETED_SUCCESS"

    # 0x2: In progress (sprog = 32767 -> ~50%)
    is_done, prog, msg = parse_nvme_sanitize_status_raw(sstat=0x02, sprog=32767)
    assert is_done is False
    assert 49.0 <= prog <= 51.0
    assert msg == "IN_PROGRESS"

    # 0x3: Failed
    is_done, prog, msg = parse_nvme_sanitize_status_raw(sstat=0x03, sprog=0)
    assert is_done is False
    assert msg == "FAILED"


def test_regression_hardware_mode_fails_safe_on_missing_device(valid_dual_auth):
    """
    AUDIT CONSTATARE 3: HardwareAdapter(simulation_mode=False) pe un nod inexistent
    blochează sesiunea imediat ca ABORTED_UNSUPPORTED_ENVIRONMENT, fără succes fals.
    """
    nonexistent_dev = DeviceMetadata(
        serial_number="FAKE-DEV-9999",
        model_number="NONEXISTENT SSD",
        firmware_revision="0.0",
        capacity_bytes=100000000,
        media_type=MediaType.NVME_SSD,
        topology=DeviceTopology.NATIVE_PCIE,
        bus_path="/dev/nonexistent_nvme_test_path_1234",
        is_healthy=True,
    )
    real_adapter = HardwareAdapter(simulation_mode=False)
    session = SanitizationSession(
        device=nonexistent_dev,
        classification=ClassificationLevel.SECRET,
        hardware_adapter=real_adapter,
    )
    session.confirm_target_safeguard("9999")
    session.evaluate_and_authorize(dual_auth=valid_dual_auth)

    success = session.execute_sanitization()
    assert success is False
    assert session.state == EngineState.FAILED_REJECTED
    assert session.final_disposition == FinalDisposition.ABORTED_UNSUPPORTED_ENVIRONMENT
    assert "nu a fost găsit" in session.rejection_reason


def test_regression_simulated_pdf_clearly_marked(dummy_device, valid_dual_auth, tmp_path):
    """
    AUDIT CONSTATARE 7: Documentul PDF emis dintr-o sesiune simulată conține avertismentul DEMO
    și nu pretinde fals că LBA au fost verificate sau datele purjate.
    """
    sim_adapter = HardwareAdapter(simulation_mode=True)
    session = SanitizationSession(
        device=dummy_device,
        classification=ClassificationLevel.SECRET,
        hardware_adapter=sim_adapter,
    )
    session.confirm_target_safeguard("3456")
    session.evaluate_and_authorize(dual_auth=valid_dual_auth)
    session.execute_sanitization()
    manifest = session.export_manifest()

    pdf_path = str(tmp_path / "test_sim.pdf")
    PAdESPDFGenerator.generate_pv_pdf(manifest=manifest, output_filepath=pdf_path)

    with open(pdf_path, "rb") as f:
        pdf_bytes = f.read()

    assert b"DOCUMENT SIMULAT DE LABORATOR - FARA VALOARE DE DECLASIFICARE" in pdf_bytes
    assert b"SIMULAT - NESANITIZAT HARDWARE" in pdf_bytes
    assert b"Niciun bloc de pe disc nu a fost citit fizic" in pdf_bytes
