import pytest
from cognitive_core.agents import (
    RouterAgent,
    RetrievalAgent,
    VerifierAgent,
    ConsolidatorAgent,
    CriticAgent
)
from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal

def test_router_agent():
    storage = StorageEngine()
    controller = MemoryController(storage)
    router = RouterAgent(controller)

    # Simple task
    res = router.process_task(Principal.AI_AGENT, {"query": "find active postgresql configuration"})
    assert res["status"] == "success"
    assert "retrieval" in res["target_agents"]

    # Complex multi-step task
    res_complex = router.process_task(Principal.AI_AGENT, {"query": "verify and consolidate error reflections for postgresql database"})
    assert res_complex["status"] == "success"
    assert "verifier" in res_complex["target_agents"] or "consolidator" in res_complex["target_agents"]

def test_retrieval_agent():
    storage = StorageEngine()
    controller = MemoryController(storage)
    agent = RetrievalAgent(controller)

    storage.set("k1", {
        "id": "k1",
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "content": "PostgreSQL replication configuration guide",
        "confidence": "high",
        "verification": "verified"
    })

    res = agent.process_task(Principal.AI_AGENT, {"query": "PostgreSQL replication"})
    assert res["status"] == "success"
    assert res["total_found"] >= 1

def test_verifier_agent():
    storage = StorageEngine()
    controller = MemoryController(storage)
    agent = VerifierAgent(controller)

    nodes = [
        {"id": "n1", "verification": "verified", "provenance": {"source_type": "user"}},
        {"id": "n2", "verification": "unverified", "provenance": {"source_type": "user"}} # violation
    ]

    res = agent.process_task(Principal.AI_AGENT, {"nodes": nodes})
    assert res["status"] == "success"
    assert res["verified_count"] == 1
    assert res["unverified_count"] == 1
    assert len(res["violations"]) == 1
    assert res["is_clean"] is False

def test_consolidator_agent():
    storage = StorageEngine()
    controller = MemoryController(storage)
    agent = ConsolidatorAgent(controller)

    # Empty run
    res = agent.process_task(Principal.AI_AGENT, {"type": "all"})
    assert res["status"] == "success"
    assert "duplicates_flagged" in res["results"]
    assert "consolidated_id" in res["results"]

def test_critic_agent():
    storage = StorageEngine()
    controller = MemoryController(storage)
    agent = CriticAgent(controller)

    # Self-refine test
    res = agent.process_task(Principal.AI_AGENT, {
        "type": "self_refine",
        "candidate": {"content": "Comprehensive analysis of distributed locking."}
    })
    assert res["status"] == "success"
    assert res["passed_filter"] is True
