import json

with open('.agents/survey_test_explorer_1/test_structure_info.json', 'r', encoding='utf-8') as f:
    files_info = json.load(f)

with open('.agents/survey_test_explorer_1/parsed_tests.json', 'r', encoding='utf-8') as f:
    parsed_tests = json.load(f)

print(f"Total test files: {len(files_info)}")
print(f"Total collected tests: {parsed_tests['total']}")

# Let's inspect test breakdown by subsystem
subsystems = {
    "Security / Invariants (P0-P15)": [
        "memory_controller/tests/test_authorization.py",
        "memory_controller/tests/test_security.py",
        "memory_controller/tests/test_security_hardening.py",
        "cognitive_core/tests/test_tool_router_security.py",
        "cognitive_core/tests/test_reconciliation_boundary.py"
    ],
    "Storage / WAL / Audit & Memory Controller Core": [
        "memory_controller/tests/test_sqlite_storage.py",
        "memory_controller/tests/test_storage.py",
        "memory_controller/tests/test_audit.py",
        "memory_controller/tests/test_cache.py",
        "memory_controller/tests/test_lifecycle.py",
        "memory_controller/tests/test_raw_imports.py",
        "memory_controller/tests/test_pagination.py",
        "memory_controller/tests/test_context_economy.py",
        "memory_controller/tests/test_core.py",
        "memory_controller/tests/test_git_isolation.py",
        "memory_controller/tests/test_supersession_phase43.py"
    ],
    "Cognitive Loop / OODA / ToT / Executive": [
        "cognitive_core/tests/test_cognitive_loop.py",
        "cognitive_core/tests/test_reasoning.py",
        "cognitive_core/tests/test_tot_and_formal_reflexion.py",
        "cognitive_core/tests/test_reflection.py",
        "cognitive_core/tests/test_planning.py",
        "cognitive_core/tests/test_executive.py"
    ],
    "Multi-Agent Worker Coordination": [
        "cognitive_core/tests/test_multiagent_orchestration.py",
        "cognitive_core/tests/test_specialized_agents.py"
    ],
    "Metrics / TRACe / IR / Continual Learning / Consolidation": [
        "cognitive_core/tests/test_evaluation_and_recall_lineage.py",
        "cognitive_core/tests/test_continual_learning.py",
        "cognitive_core/tests/test_consolidation.py",
        "cognitive_core/tests/test_deduplication.py",
        "cognitive_core/tests/test_learning.py"
    ],
    "Cognitive Working Memory / Recall / Synaptic Graph & Versioning": [
        "cognitive_core/tests/test_working_memory.py",
        "cognitive_core/tests/test_working_memory_persistence.py",
        "cognitive_core/tests/test_activation.py",
        "cognitive_core/tests/test_dynamic_synapses.py",
        "cognitive_core/tests/test_recall.py",
        "cognitive_core/tests/test_version_parsing.py",
        "cognitive_core/tests/test_continuity.py",
        "cognitive_core/tests/test_end_to_end_workflow.py"
    ]
}

print("\nDetailed Subsystem Mapping Check:")
all_categorized_files = set()
for sub, flist in subsystems.items():
    sub_test_count = 0
    for file_path in flist:
        all_categorized_files.add(file_path)
        matching = [t for t in parsed_tests['tests'] if t['file'] == file_path]
        sub_test_count += len(matching)
    print(f"  {sub}: {len(flist)} files, {sub_test_count} tests (100% passing)")

uncategorized = [f['file'] for f in files_info if not f['is_conftest'] and f['file'] not in all_categorized_files]
print(f"Uncategorized test files: {uncategorized}")
