import sys
from pathlib import Path
sys.path.insert(0, r"C:\Users\Marius\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_cognitive_brain")

import json
import asyncio
from jarvis.iot.fastmcp_server import FastMCPIoTServer
from jarvis.iot.ha_client import HomeAssistantClient
from jarvis.iot.ha_simulator import HomeAssistantSimulator

def run_stress_checks():
    print("--- AUDIT INDEPENDENT STRESS TEST START ---")

    # Test 1: JSON-RPC 2.0 Edge Cases
    server = FastMCPIoTServer()
    edge_cases = [
        12345, 3.14159, True, False, None,
        "12345", "true", "false", "null", "[1, 2, 3]", "\"string_only\"",
        "", "   ", "{", "{\"jsonrpc\": \"2.0\"}", "{\"jsonrpc\": \"1.0\", \"method\": \"test\"}"
    ]
    for ec in edge_cases:
        res = server.handle_jsonrpc(ec)
        assert isinstance(res, dict), f"Failed on {ec}: returned {type(res)}"
        assert res.get("jsonrpc") == "2.0", f"Failed on {ec}: missing jsonrpc 2.0"
        assert "error" in res, f"Failed on {ec}: missing error in {res}"
    print("Test 1: JSON-RPC 2.0 edge cases PASSED")

    # Test 2: Multi-entity list & tuple in safe_call_service
    sim = HomeAssistantSimulator(auth_token="secret_token")
    client = HomeAssistantClient(simulator=sim, token="secret_token")

    # Valid list
    res_list = client.safe_call_service("light", "turn_on", {"entity_id": ["light.living_room_ceiling", "light.kitchen_strip"]})
    assert res_list["status"] == "success"
    assert len(res_list["affected"]) == 2
    print("Test 2A: Multi-entity list PASSED")

    # Valid tuple
    res_tuple = client.safe_call_service("light", "turn_off", {"entity_id": ("light.living_room_ceiling", "light.kitchen_strip")})
    assert res_tuple["status"] == "success"
    assert len(res_tuple["affected"]) == 2
    print("Test 2B: Multi-entity tuple PASSED")

    # Invalid entity inside list
    res_bad_list = client.safe_call_service("light", "turn_on", {"entity_id": ["light.living_room_ceiling", 12345]})
    assert res_bad_list["status"] == "error"
    assert "must be a string" in res_bad_list["error"]
    print("Test 2C: Invalid entity type inside list PASSED")

    # Unknown entity inside list
    res_unk_list = client.safe_call_service("light", "turn_on", {"entity_id": ["light.living_room_ceiling", "light.does_not_exist"]})
    assert res_unk_list["status"] == "error"
    assert "EntityNotFound" in res_unk_list["error"]
    print("Test 2D: Unknown entity inside list PASSED")

    # Test 3: Unauthorized token in safe_call_service
    unauth_client = HomeAssistantClient(simulator=sim, token="wrong_token")
    res_unauth = unauth_client.safe_call_service("light", "turn_on", {"entity_id": "light.living_room_ceiling"})
    assert res_unauth["status"] == "error"
    assert "401" in res_unauth["error"] or "Unauthorized" in res_unauth["error"]
    print("Test 3: Unauthorized safe_call_service PASSED")

    # Test 4: Async safe_call_service
    async def run_async():
        res_async = await client.async_safe_call_service("light", "turn_on", {"entity_id": ["light.living_room_ceiling", "light.kitchen_strip"]})
        assert res_async["status"] == "success"
        assert len(res_async["affected"]) == 2

        res_async_unauth = await unauth_client.async_safe_call_service("light", "turn_on", {"entity_id": "light.living_room_ceiling"})
        assert res_async_unauth["status"] == "error"
        assert "401" in res_async_unauth["error"] or "Unauthorized" in res_async_unauth["error"]
        print("Test 4: Async safe_call_service PASSED")

    asyncio.run(run_async())

    print("--- ALL INDEPENDENT FORENSIC STRESS TESTS PASSED ---")

if __name__ == "__main__":
    run_stress_checks()
