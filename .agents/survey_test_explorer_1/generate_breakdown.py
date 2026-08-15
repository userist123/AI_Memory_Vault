import json
import os

with open('.agents/survey_test_explorer_1/test_structure_info.json', 'r', encoding='utf-8') as f:
    files_info = {fi['file']: fi for fi in json.load(f)}

with open('.agents/survey_test_explorer_1/parsed_tests.json', 'r', encoding='utf-8') as f:
    parsed_tests = json.load(f)

subsystem_mapping = {
    "Security & Invariants (P0-P15)": [
        ("memory_controller/tests/test_authorization.py", "Authorization & Principal Permissions (Human, Admin, AI Agent least privilege)"),
        ("memory_controller/tests/test_security.py", "Security Sanitization, Query Boundaries, Path Traversal, Cache Invalidation"),
        ("memory_controller/tests/test_security_hardening.py", "Hardened Invariants P0-001 through P0-015 (Attestation gates, non-persistence on rejection)"),
        ("cognitive_core/tests/test_tool_router_security.py", "Tool Router principal permission enforcement and command blacklisting"),
        ("cognitive_core/tests/test_reconciliation_boundary.py", "Reconciliation boundary checking against unauthorized mutation")
    ],
    "Storage, WAL & Audit Core": [
        ("memory_controller/tests/test_sqlite_storage.py", "SQLite Storage Engine (WAL mode, busy_timeout, BEGIN IMMEDIATE, Check constraints, recursion)"),
        ("memory_controller/tests/test_storage.py", "File Storage Engine (Frontmatter roundtrip, atomic writes, YAML invariants, template exclusions)"),
        ("memory_controller/tests/test_audit.py", "SHA-256 Audit Log Hash Chaining, verification, and tamper detection"),
        ("memory_controller/tests/test_cache.py", "LRU Cache with TTL, Principal Isolation, Query Fingerprinting, Mutation Invalidation"),
        ("memory_controller/tests/test_lifecycle.py", "Memory Lifecycle State Machine (RAW -> CLASSIFIED -> NORMALIZED -> REVIEW -> VERIFIED -> ACTIVE -> SUPERSEDED/ARCHIVED)"),
        ("memory_controller/tests/test_raw_imports.py", "RAW Inbox Exclusion from Search and Direct Read"),
        ("memory_controller/tests/test_pagination.py", "HMAC-SHA256 Encoded Pagination Tokens, Tamper Detection, TTL Expiry"),
        ("memory_controller/tests/test_context_economy.py", "Zlib Compression, Soft Degradation, and Hard Context Budget Limits"),
        ("memory_controller/tests/test_core.py", "Core Controller Integration (Query Classifier, Progressive Disclosure, Security Sanitization)"),
        ("memory_controller/tests/test_git_isolation.py", "Git Branch & Working Tree Isolation"),
        ("memory_controller/tests/test_supersession_phase43.py", "Supersession Lineage, Self/Cycle Rejection, Human Verified Protection, Audit Trails")
    ],
    "Cognitive Loop, OODA, ToT & Executive": [
        ("cognitive_core/tests/test_cognitive_loop.py", "Autonomous OODA Loop Execution (Observe -> Retrieve -> Reason -> Plan -> Act -> Reflect -> Consolidate)"),
        ("cognitive_core/tests/test_reasoning.py", "Reasoning Core & Strategy Selection"),
        ("cognitive_core/tests/test_tot_and_formal_reflexion.py", "Tree-of-Thought (ToT) Branch Exploration, ThoughtValidator, 6-Stage Formal Reflexion"),
        ("cognitive_core/tests/test_reflection.py", "SelfRefine Reflection, Critique Filters, Ephemeral Error Analysis"),
        ("cognitive_core/tests/test_planning.py", "Hierarchical Task Decomposition & Multi-Step Execution Planning"),
        ("cognitive_core/tests/test_executive.py", "Executive Goal Management, Context Switching & Termination Criteria")
    ],
    "Multi-Agent Worker Coordination": [
        ("cognitive_core/tests/test_multiagent_orchestration.py", "Multi-Agent Orchestration, Inter-Agent Messaging, Distributed Task Execution"),
        ("cognitive_core/tests/test_specialized_agents.py", "Specialized Worker Agents (Router, Retrieval, Verifier, Consolidator, Critic)")
    ],
    "Metrics, TRACe, IR & Continual Learning": [
        ("cognitive_core/tests/test_evaluation_and_recall_lineage.py", "TRACe Metrics (Utilization, Relevance, Adherence, Completeness) & IR Benchmarks (MRR, NDCG@K, Precision@K)"),
        ("cognitive_core/tests/test_continual_learning.py", "ContinualLearningGuard, Catastrophic Forgetting Prevention & Anchor Memory Anchors"),
        ("cognitive_core/tests/test_consolidation.py", "Review Memory Consolidation, Confidence Promotion via Execution Evidence"),
        ("cognitive_core/tests/test_deduplication.py", "Semantic Similarity Deduplication, Entity Alignment, Claim Diffing"),
        ("cognitive_core/tests/test_learning.py", "Episodic to Semantic Knowledge Extraction & Adaptive Rule Learning")
    ],
    "Cognitive Working Memory, Synapses & Recall": [
        ("cognitive_core/tests/test_working_memory.py", "In-Memory Working Memory Buffer, Capacity Limits, Eviction Policies"),
        ("cognitive_core/tests/test_working_memory_persistence.py", "Atomic Checkpoint Persistence (wm.json, plan.json) via os.replace"),
        ("cognitive_core/tests/test_activation.py", "Spreading Activation across Dynamic Synaptic Graph"),
        ("cognitive_core/tests/test_dynamic_synapses.py", "Dynamic Synaptic Weighting, Decay & Reinforcement"),
        ("cognitive_core/tests/test_recall.py", "Associative & Hybrid Semantic Retrieval"),
        ("cognitive_core/tests/test_version_parsing.py", "SemVer & Temporal Version Resolution"),
        ("cognitive_core/tests/test_continuity.py", "Cross-Session Context Continuity & Working Memory Resumption"),
        ("cognitive_core/tests/test_end_to_end_workflow.py", "Full End-to-End Cognitive Brain Pipeline Integration")
    ]
}

report_lines = []

for sub_name, files in subsystem_mapping.items():
    report_lines.append(f"### {sub_name}")
    report_lines.append("")
    for file_path, desc in files:
        f_info = files_info.get(file_path, {})
        tests_in_file = [t for t in parsed_tests['tests'] if t['file'] == file_path]
        fixtures = f_info.get('fixtures', [])
        report_lines.append(f"#### `{file_path}`")
        report_lines.append(f"- **Description**: {desc}")
        report_lines.append(f"- **Lines of Code**: {f_info.get('num_lines', 'N/A')}")
        report_lines.append(f"- **Test Count**: {len(tests_in_file)}")
        report_lines.append(f"- **Fixtures Used / Defined**: {fixtures if fixtures else 'None (uses module/conftest)'}")
        report_lines.append("- **Test Cases**:")
        for t in tests_in_file:
            report_lines.append(f"  - `{t['name']}` ({t['time']:.3f}s) — **{t['status'].upper()}**")
        report_lines.append("")

with open('.agents/survey_test_explorer_1/test_breakdown_detail.md', 'w', encoding='utf-8') as out:
    out.write("\n".join(report_lines))

print("Generated test_breakdown_detail.md")
