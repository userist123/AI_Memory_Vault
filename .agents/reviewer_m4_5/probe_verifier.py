import sys
import os
sys.path.insert(0, os.path.abspath("."))
from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal
from cognitive_core.agents.verifier_agent import VerifierAgent

storage = StorageEngine()
controller = MemoryController(storage)
v = VerifierAgent(controller)

# Probe 1: Non-dict nodes
res1 = v.process_task(Principal.AI_AGENT, {'nodes': [None, 123, 'str_node', ['list']]})
assert res1['status'] == 'success'
assert res1['total_inspected'] == 4
assert res1['unverified_count'] == 4
assert res1['is_clean'] is False
assert len(res1['violations']) == 4

# Probe 2: Malformed provenance types
res2 = v.process_task(Principal.AI_AGENT, {'nodes': [
    {'id': '1', 'provenance': None, 'verification': 'unverified'},
    {'id': '2', 'provenance': 'string_prov', 'verification': 'unverified'},
    {'id': '3', 'provenance': 999, 'verification': 'unverified'},
    {'id': '4', 'provenance': ['list_prov'], 'verification': 'unverified'},
    {'id': '5', 'provenance': True, 'verification': 'unverified'},
]})
assert res2['status'] == 'success'
assert res2['total_inspected'] == 5
assert res2['unverified_count'] == 5
assert len(res2['violations']) == 5
assert res2['is_clean'] is False

# Probe 3: Clean nodes vs Unattested privileged provenance
res3 = v.process_task(Principal.AI_AGENT, {'nodes': [
    {'id': 'c1', 'provenance': {'source_type': 'ai'}, 'verification': 'unverified'},
    {'id': 'c2', 'provenance': {'source_type': 'execution'}, 'verification': 'unverified'},
    {'id': 'v1', 'provenance': {'source_type': 'user'}, 'verification': 'verified'},
    {'id': 'bad_user', 'provenance': {'source_type': 'user'}, 'verification': 'unverified'},
    {'id': 'bad_off', 'provenance': {'source_type': 'official'}, 'verification': 'partially_verified'},
]})
assert res3['status'] == 'success'
assert res3['verified_count'] == 1
assert res3['unverified_count'] == 4
assert len(res3['violations']) == 2
assert res3['violations'] == [
    "Node bad_user claims 'user' without attested verification",
    "Node bad_off claims 'official' without attested verification"
]
assert res3['is_clean'] is False

print('ALL VERIFIER AGENT ADVERSARIAL PROBES PASSED')
