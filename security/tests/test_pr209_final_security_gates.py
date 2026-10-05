from __future__ import annotations

import inspect
from pathlib import Path

import pytest

from memory_controller.authorizer import Principal
from memory_controller.controller import Lifecycle, MemoryController, StorageEngine
from cognitive_core.proposal_queue import MemoryProposalQueue
from cognitive_core.queue_promoter import QueuePromoter
from security.runtime_enforcer import PersistentNonceStore

REPO = Path(__file__).resolve().parents[2]


def _active_note(note_id: str, content: str = "original") -> dict:
    return {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": Lifecycle.ACTIVE,
        "category": "security-test",
        "tags": ["security"],
        "created": "2026-10-05",
        "updated": "2026-10-05",
        "provenance": {"source_type": "user", "source_ref": "test"},
        "confidence": "high",
        "verification": "unverified",
        "relations": [],
        "content": content,
    }


def test_u01_ai_agent_cannot_mutate_active_note():
    controller = MemoryController(StorageEngine())
    note_id = "11111111-1111-1111-1111-111111111111"
    controller.propose(Principal.HUMAN, _active_note(note_id))

    with pytest.raises(PermissionError, match="AI_AGENT.*ACTIVE"):
        controller.update(Principal.AI_AGENT, note_id, {"content": "attacker mutation"})

    assert controller.storage.get(note_id)["content"] == "original"


def test_gate2_nonce_store_uses_explicit_begin_immediate():
    source = inspect.getsource(PersistentNonceStore.check_and_mark)
    assert "BEGIN IMMEDIATE" in source


def test_u05_queue_promoter_requires_verified_evidence(tmp_path):
    queue_path = tmp_path / "queue.jsonl"
    queue_path.write_text(
        '{"candidate_id":"c1","queue_status":"APPROVED","type":"knowledge",'
        '"category":"test","content":"candidate"}\n',
        encoding="utf-8",
    )
    queue = MemoryProposalQueue(queue_path)

    class Controller:
        storage = type("Storage", (), {"store": {}})()

        def propose(self, principal, note):
            raise AssertionError("promotion must be blocked before controller.propose")

    with pytest.raises(ValueError, match="verified evidence"):
        QueuePromoter(queue, Controller(), Principal.ADMIN).promote_approved()


def test_u08_controller_rejects_oversized_note():
    controller = MemoryController(StorageEngine())
    note = _active_note("22222222-2222-2222-2222-222222222222", "X" * 20001)

    with pytest.raises(ValueError, match="maximum note content"):
        controller.propose(Principal.HUMAN, note)


def test_u06_direct_dependencies_are_exactly_version_pinned():
    required = {
        "pytest": "9.1.1",
        "pytest-asyncio": "1.4.0",
        "pyyaml": "6.0.3",
        "jsonschema": "4.26.0",
        "fastapi": "0.142.2",
        "httpx": "0.28.1",
        "pydantic": "2.13.5",
        "numpy": "2.3.5",
        "pandas": "3.0.6",
        "requests": "2.34.2",
        "mcp": "1.30.0",
    }
    lines = [
        line.strip().lower()
        for line in (REPO / "requirements.txt").read_text(encoding="utf-8-sig").splitlines()
        if line.strip() and not line.lstrip().startswith("#")
    ]
    for package, version in required.items():
        assert f"{package.lower()}=={version}" in lines


def test_mcp_memory_get_does_not_upgrade_agent_to_human():
    source = (
        REPO / "03_IMPLEMENTATION/packages/interfaces/memory_mcp_server.py"
    ).read_text(encoding="utf-8")
    start = source.index("def memory_get(")
    end = source.index("\ndef memory_propose(", start)
    block = source[start:end]
    assert "Principal.HUMAN" not in block
    assert "Principal.AI_AGENT" in block


def test_u04_api_mutation_routes_require_authentication():
    source = (
        REPO / "03_IMPLEMENTATION/packages/interfaces/api_server.py"
    ).read_text(encoding="utf-8")
    assert "AI_MEMORY_VAULT_API_TOKEN" in source
    assert "_require_api_auth" in source
    assert "Authorization" in source
