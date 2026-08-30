import importlib.util
from pathlib import Path

EXAMPLE_PATH = (
    Path(__file__).resolve().parents[2]
    / "05_RESOURCES"
    / "Examples"
    / "enterprise_memory_client.py"
)

spec = importlib.util.spec_from_file_location("enterprise_memory_client", EXAMPLE_PATH)
if spec is None or spec.loader is None:
    raise ImportError(f"Could not load enterprise memory client from {EXAMPLE_PATH}")

enterprise_module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(enterprise_module)
EnterpriseMemoryClient = enterprise_module.EnterpriseMemoryClient


def test_enterprise_memory_client_instantiation():
    client = EnterpriseMemoryClient()
    assert client.storage is not None
    assert client.controller is not None
    assert client.sensor_buffer is not None
    assert client.extractor is not None
    assert client.proposal_queue is not None


def test_enterprise_memory_client_telemetry_and_extraction():
    client = EnterpriseMemoryClient()
    event = client.capture_session_telemetry(
        session_id="sess_123",
        agent_id="agent_alpha",
        role="tester",
        content="Testing enterprise telemetry",
    )
    assert event.session_id == "sess_123"

    count = client.extract_and_enqueue_facts(
        raw_text="Am decis: folosim SQLite WAL pentru index local.",
        source_ref="unit_test",
    )
    assert isinstance(count, int)
