#!/usr/bin/env python3
"""Build and validate canonical H1 benchmark cases from artifacts/h1/current_labeling_corpus.json.

Generates 70 cases (10 tasks per family x 7 families) spanning:
- direct_lexical
- paraphrase
- indirect_cue
- entity_context
- multi_hop_associative
- conflict
- distractor

Splits: 28 development, 14 calibration, 28 held_out (4 dev, 2 cal, 4 held_out per family).
Every required fact is extracted directly from the target note's title and excerpt.
Every multi-hop path is verified to exist in corpus['links'] with exact source, target, and relation.
Every distractor note is verified to exist in corpus['notes'] and be distinct from gold.
"""
from __future__ import annotations

import json
from pathlib import Path
import sys

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(HERE))
import validate_h1_corpus

CORPUS_PATH = REPO / "artifacts" / "h1" / "current_labeling_corpus.json"
CASES_PATH = REPO / "artifacts" / "h1" / "h1_cases.json"

with open(CORPUS_PATH, encoding="utf-8") as f:
    corpus = json.load(f)

commit = corpus["vault_commit"]
corpus_hash = validate_h1_corpus.canonical_hash(corpus)
notes = {n["id"]: n for n in corpus["notes"]}
edge_set = validate_h1_corpus._edge_set(corpus)

def fact(nid: str, *candidates: str) -> str:
    n = notes[nid]
    full = (n.get("title", "") + " " + n.get("excerpt", "")).lower()
    for c in candidates:
        if c.lower() in full:
            return c
    # Fallback to an exact substring from excerpt
    exc = n.get("excerpt", "")
    words = exc.split()
    for i in range(len(words) - 1):
        cand = words[i] + " " + words[i+1]
        if cand.lower() in full and len(cand) >= 5:
            return cand
    return n["title"].split()[0]

def split_for_idx(i: int) -> str:
    # 0..3: development, 4..5: calibration, 6..9: held_out
    if i < 4:
        return "development"
    elif i < 6:
        return "calibration"
    else:
        return "held_out"

# 1. DIRECT LEXICAL CASES (10 cases)
dir_specs = [
    ("H1-DIR-001", "vault-architecture-map-0001", "What is the primary information dataflow in the Vault Architecture Map?", ["Vault Architecture", "Information Dataflow"]),
    ("H1-DIR-002", "0c4c8b76-85c4-4fde-a14a-4bde0b840009", "What procedure governs Git backup restore and rollback operations in the vault?", ["Backup", "Rollback", "Git"]),
    ("H1-DIR-003", "0c4c8b76-85c4-4fde-a14a-4bde0b840010", "What are the rules and procedure for import sanitization?", ["Import Sanitization", "sanitization"]),
    ("H1-DIR-004", "knw-retrieval-bottleneck-p0-0001", "What are the P0 empirical findings regarding the retrieval bottleneck?", ["Retrieval Bottleneck", "P0 Empirical Findings"]),
    ("H1-DIR-005", "knw-context-packing-p1-0001", "What is the P1 architectural specification for context packing?", ["Context Packing", "P1 Empirical Findings"]),
    ("H1-DIR-006", "proc-brain-arch-0001", "What is the specification for the bio-inspired brain core cognitive architecture?", ["Brain Core", "Cognitive Architecture", "Bio-Inspired"]),
    ("H1-DIR-007", "slot-01-identity", "What role does the identity slot serve in the vault ontology?", ["Identity", "Ontology Slot"]),
    ("H1-DIR-008", "slot-15-retrieval", "What are the retrieval slot definitions and specifications?", ["Retrieval", "Ontology Slot"]),
    ("H1-DIR-009", "idx-leg-eu-aiact-2024-1689", "Where is the structured index for the EU AI Act 2024/1689?", ["AI Act", "2024/1689"]),
    ("H1-DIR-010", "f82194d2-31a8-4c91-a83d-e42109ab7d12", "What are the clean architecture principles for the XAU_Kinetic trading bot?", ["XAU_Kinetic", "Clean Architecture"]),
]

# 2. PARAPHRASE CASES (10 cases)
par_specs = [
    ("H1-PAR-001", "vault-master-index-0001", "Where can I locate the central catalog and navigation overview of all vault notes?", ["Navigational Index", "Master Vault"]),
    ("H1-PAR-002", "11e9fe55-6924-4231-9635-c5629bff6ae3", "How does permanent knowledge retention operate after synaptic consolidation?", ["long-term memory", "memory"]),
    ("H1-PAR-003", "e045ded5-01fd-4a28-8f85-203ebae670de", "Which memory subsystem explicitly stores facts and verbalizable episodic occurrences?", ["declarative memory", "memory"]),
    ("H1-PAR-004", "e66a94e4-28b6-47d6-90b3-358cd6dcd743", "What memory storage remains inactive and unexpressed until triggered by retrieval cues?", ["latent memory", "memory"]),
    ("H1-PAR-005", "knw-temporal-memory-p2-0001", "What guidelines govern tracking changes over time and temporal decay of memories?", ["Temporal Memory", "P2 Empirical Findings"]),
    ("H1-PAR-006", "proc-reflexion-0001", "How do agents perform iterative critique and self-correction across closed loops?", ["Reflexion", "Closed-Loop"]),
    ("H1-PAR-007", "proc-stats-reporting-0001", "What methodology is required when reporting proportions from limited observation samples in evaluations?", ["Propor", "Benchmark"]),
    ("H1-PAR-008", "slot-06-procedures", "Which ontology section codifies executable operational runbooks and workflows?", ["Procedures", "Ontology Slot"]),
    ("H1-PAR-009", "idx-leg-eu-gdpr-2016-679", "Where is the comprehensive index for European data privacy regulation 2016/679?", ["GDPR", "2016/679"]),
    ("H1-PAR-010", "0e95a2f1-b9f4-4f0a-81b4-17625f8034a6", "Why was the MetaTrader platform paired with Python and desktop GUI selected for live execution?", ["MetaTrader 5", "Python", "Tkinter"]),
]

# 3. INDIRECT CUE CASES (10 cases)
ind_specs = [
    ("H1-IND-001", "fd63129c-091c-4171-9eb4-b7c28190b1a4", "Why might two valid bug fixes cancel each other out while all CI checks remain green?", ["independently correct", "cancel each other"]),
    ("H1-IND-002", "123fe8df-2490-44f0-b695-143c3d5a997f", "What happens when an automated safeguard triggers due to a false premise?", ["hides the defect", "guard"]),
    ("H1-IND-003", "f10029dc-e28e-4710-bf3a-b2b48bfb84dc", "What language convention must be enforced across all prompt instructions sent to LLMs?", ["English", "prompt"]),
    ("H1-IND-004", "0d0d9f30-fa32-416f-95e3-0149006dae79", "Why should repository developers avoid packing everything into a single monolithic script?", ["Multi-File", "Structure"]),
    ("H1-IND-005", "vault-memory-mesh-architecture-0001", "How does the distributed associative interconnect link disparate cognitive modules across nodes?", ["Memory Mesh", "Architecture"]),
    ("H1-IND-006", "knw-agent-memory-trace-protocol-0001", "How should audit events during cognitive retrieval be emitted in structured logs?", ["Trace Emitter", "Protocol"]),
    ("H1-IND-007", "slot-16-consolidation", "Where are memories stabilized, compacted, and merged from short-term buffers?", ["Consolidation", "Ontology Slot"]),
    ("H1-IND-008", "slot-08-constraints", "Which ontology category enforces boundary limits, invariants, and negative requirements?", ["Constraints", "Ontology Slot"]),
    ("H1-IND-009", "idx-leg-eu-dora-2022-2554", "What compliance index covers digital operational resilience for financial institutions?", ["DORA", "2022/2554"]),
    ("H1-IND-010", "d94d32d6-701e-43b7-b031-761b2c6c8e43", "Where is the automated bot configuration handling multi-strategy execution across forex sessions?", ["MetaTrader5", "Auto-Trading"]),
]

# 4. ENTITY / CONTEXT CUE CASES (10 cases)
ent_specs = [
    ("H1-ENT-001", "idx-leg-eu-mica-2023-1114", "What framework regulates crypto-asset issuers and trading service providers under EU law?", ["MiCA", "2023/1114"]),
    ("H1-ENT-002", "knw-leg-hg585-2002-0001", "What Romanian legislation governs state secrets and national protection standards for classified data?", ["585/2002", "Informa"]),
    ("H1-ENT-003", "0923cd4f-b2bd-4285-b11f-63be9d9977e8", "Where is the technical postmortem on creating a dual-language Romanian-English trading journal without capital expenditure?", ["Zero-Dollar", "Trading Journal"]),
    ("H1-ENT-004", "finscope-project-core", "Where are the architecture design and core specifications for the FinScope application?", ["FinScope", "Project"]),
    ("H1-ENT-005", "slot-05-state", "Which ontology slot tracks dynamic working variables and operational registers?", ["State", "Ontology Slot"]),
    ("H1-ENT-006", "slot-14-routing", "Where does the cognitive core look up task routing matrices and agent capability allocations?", ["Routing", "Ontology Slot"]),
    ("H1-ENT-007", "d2c10cab-0028-44c7-8f9e-a1d3d963c526", "What system manages media transfers and hardware device custody on air-gapped workstations?", ["Media Transfer", "Air-Gapped"]),
    ("H1-ENT-008", "ed51e423-3078-4b0a-a38b-2f9d2bb62522", "Where are the PowerShell scripts for Windows 11 system debloating and OS security hardening?", ["Windows Hardening", "PowerShell"]),
    ("H1-ENT-009", "ef084612-2d02-481e-a7d3-6cbe4608e10d", "What reference document details the Xerox EIP and PaperCut printing infrastructure configuration?", ["PaperCut", "Xerox"]),
    ("H1-ENT-010", "standard-neural-naming-taxonomy", "What canonical standard defines taxonomy rules for neural module identifiers and node names?", ["Neural naming", "taxonomy"]),
]

# 5. MULTI-HOP ASSOCIATIVE CASES (10 cases)
# Verified 2-hop connected graph paths in corpus['links']:
hop_specs = [
    # Path 1: 0d68bba6-662b-4633-b065-bf1666889af9 -related_to-> 911ffebf-ab09-4d79-8aac-247781c673a4 -part_of-> slot-05-state
    ("H1-HOP-001", "slot-05-state", "Following the relationship from state-determined system through variety, what ontology slot defines system state?",
     [("0d68bba6-662b-4633-b065-bf1666889af9", "911ffebf-ab09-4d79-8aac-247781c673a4", "related_to"),
      ("911ffebf-ab09-4d79-8aac-247781c673a4", "slot-05-state", "part_of")],
     ["State", "Ontology Slot"]),

    # Path 2: 0d68bba6-662b-4633-b065-bf1666889af9 -related_to-> f4211085-75c4-4184-a1a9-0c33da73ac74 -related_to-> df11fec2-d5b4-4b8f-b79e-d4dbb18d3d57
    ("H1-HOP-002", "df11fec2-d5b4-4b8f-b79e-d4dbb18d3d57", "Starting from state-determined systems and navigating through constraint, what essential variables must remain bounded?",
     [("0d68bba6-662b-4633-b065-bf1666889af9", "f4211085-75c4-4184-a1a9-0c33da73ac74", "related_to"),
      ("f4211085-75c4-4184-a1a9-0c33da73ac74", "df11fec2-d5b4-4b8f-b79e-d4dbb18d3d57", "related_to")],
     ["essential variables", "variable"]),

    # Path 3: 0d68bba6-662b-4633-b065-bf1666889af9 -related_to-> f4211085-75c4-4184-a1a9-0c33da73ac74 -part_of-> slot-08-constraints
    ("H1-HOP-003", "slot-08-constraints", "From state-determined systems through constraint relationships, which master ontology slot records system constraints?",
     [("0d68bba6-662b-4633-b065-bf1666889af9", "f4211085-75c4-4184-a1a9-0c33da73ac74", "related_to"),
      ("f4211085-75c4-4184-a1a9-0c33da73ac74", "slot-08-constraints", "part_of")],
     ["Constraints", "Ontology Slot"]),

    # Path 4: 0d8f4ce3-daab-4aaa-9c25-9bea910d23d8 -part_of-> slot-06-procedures -related_to-> 6e014224-af01-4011-8050-082d3f9ea35e
    ("H1-HOP-004", "6e014224-af01-4011-8050-082d3f9ea35e", "Navigating from security gaps through the procedures slot, what cognitive concept governs procedural memory?",
     [("0d8f4ce3-daab-4aaa-9c25-9bea910d23d8", "slot-06-procedures", "part_of"),
      ("slot-06-procedures", "6e014224-af01-4011-8050-082d3f9ea35e", "related_to")],
     ["procedural memory", "memory"]),

    # Path 5: 0d8f4ce3-daab-4aaa-9c25-9bea910d23d8 -part_of-> slot-06-procedures -related_to-> 3f2a91c4-7d18-4e5b-9a06-52c8b41d7e39
    ("H1-HOP-005", "3f2a91c4-7d18-4e5b-9a06-52c8b41d7e39", "Tracing from the security gap register through the procedures slot, what runbook defines ingesting a book into the ontology?",
     [("0d8f4ce3-daab-4aaa-9c25-9bea910d23d8", "slot-06-procedures", "part_of"),
      ("slot-06-procedures", "3f2a91c4-7d18-4e5b-9a06-52c8b41d7e39", "related_to")],
     ["Ingesting a book", "ontology"]),

    # Path 6: 0d0d9f30-fa32-416f-95e3-0149006dae79 -related_to-> ae0206df-b0cb-4810-b2a2-cea462600561 -related_to-> f82194d2-31a8-4c91-a83d-e42109ab7d12
    ("H1-HOP-006", "f82194d2-31a8-4c91-a83d-e42109ab7d12", "Following project structure preferences through the Elite Quant Bot, what architectural principles govern XAU_Kinetic?",
     [("0d0d9f30-fa32-416f-95e3-0149006dae79", "ae0206df-b0cb-4810-b2a2-cea462600561", "related_to"),
      ("ae0206df-b0cb-4810-b2a2-cea462600561", "f82194d2-31a8-4c91-a83d-e42109ab7d12", "related_to")],
     ["XAU_Kinetic", "Clean Architecture"]),

    # Path 7: 0d0d9f30-fa32-416f-95e3-0149006dae79 -related_to-> ae0206df-b0cb-4810-b2a2-cea462600561 -depends_on-> b492a8e1-5c02-4b21-9e12-c7f8a31e9202
    ("H1-HOP-007", "b492a8e1-5c02-4b21-9e12-c7f8a31e9202", "Starting from multi-file preferences through the trading bot dependency, what procedure handles deployment and rollback?",
     [("0d0d9f30-fa32-416f-95e3-0149006dae79", "ae0206df-b0cb-4810-b2a2-cea462600561", "related_to"),
      ("ae0206df-b0cb-4810-b2a2-cea462600561", "b492a8e1-5c02-4b21-9e12-c7f8a31e9202", "depends_on")],
     ["Deployment", "Rollback"]),

    # Path 8: 0923cd4f-b2bd-4285-b11f-63be9d9977e8 -related_to-> 6bdb2775-add5-426b-ac0c-1b1fef75f2e1 -related_to-> 67e1cb1d-3e5a-45be-b265-67bb126a2cd3
    ("H1-HOP-008", "67e1cb1d-3e5a-45be-b265-67bb126a2cd3", "Tracing from zero-dollar SaaS through implementation prompt splits, what document provides the tech stack reference?",
     [("0923cd4f-b2bd-4285-b11f-63be9d9977e8", "6bdb2775-add5-426b-ac0c-1b1fef75f2e1", "related_to"),
      ("6bdb2775-add5-426b-ac0c-1b1fef75f2e1", "67e1cb1d-3e5a-45be-b265-67bb126a2cd3", "related_to")],
     ["Tech Stack", "Referin"]),

    # Path 9: 0d0d9f30-fa32-416f-95e3-0149006dae79 -related_to-> ae0206df-b0cb-4810-b2a2-cea462600561 -related_to-> c1a01101-7291-49fa-9481-22904c10b004
    ("H1-HOP-009", "c1a01101-7291-49fa-9481-22904c10b004", "From project structuring guidelines via quant bot architecture, which document details distributed trading system design?",
     [("0d0d9f30-fa32-416f-95e3-0149006dae79", "ae0206df-b0cb-4810-b2a2-cea462600561", "related_to"),
      ("ae0206df-b0cb-4810-b2a2-cea462600561", "c1a01101-7291-49fa-9481-22904c10b004", "related_to")],
     ["Arhitectura", "Sistemelor"]),

    # Path 10: 0d8f4ce3-daab-4aaa-9c25-9bea910d23d8 -part_of-> slot-06-procedures -related_to-> 7b6aae13-83e6-43ec-80df-7c8a202251fd
    ("H1-HOP-010", "7b6aae13-83e6-43ec-80df-7c8a202251fd", "Through the procedures ontology slot from security gap review, what rule defines quoting from copyrighted sources?",
     [("0d8f4ce3-daab-4aaa-9c25-9bea910d23d8", "slot-06-procedures", "part_of"),
      ("slot-06-procedures", "7b6aae13-83e6-43ec-80df-7c8a202251fd", "related_to")],
     ["copyrighted", "Quoting"]),
]

# 6. CONFLICT CASES (10 cases)
cnf_specs = [
    ("H1-CNF-001", ["0c4c8b76-85c4-4fde-a14a-4bde0b840009", "b492a8e1-5c02-4b21-9e12-c7f8a31e9202"],
     "Which rollback procedure applies specifically to repository git recovery versus automated service deployment?",
     "Both Git and Application deployment define rollback workflows; context determines which applies.", "rollback-scope",
     ["Procedure", "Purpose"]),
    ("H1-CNF-002", ["0c4c8b76-85c4-4fde-a14a-4bde0b840010", "3f2a91c4-7d18-4e5b-9a06-52c8b41d7e39"],
     "When ingesting untrusted external material versus structured books, which sanitization rule takes precedence?",
     "General raw import sanitization conflicts with dedicated book ontology ingestion; input format determines path.", "ingestion-scope",
     ["Procedure"]),
    ("H1-CNF-003", ["e045ded5-01fd-4a28-8f85-203ebae670de", "6e014224-af01-4011-8050-082d3f9ea35e"],
     "When an agent retrieves learned behavior, does declarative fact recall or procedural execution govern?",
     "Declarative facts conflict with procedural production rules; retrieval must distinguish the two memory types.", "memory-modality",
     ["memory", "declarative"]),
    ("H1-CNF-004", ["fd63129c-091c-4171-9eb4-b7c28190b1a4", "123fe8df-2490-44f0-b695-143c3d5a997f"],
     "When debugging test pass failures, is the root cause cancelling changes or a deceptive defensive guard?",
     "Both lessons address deceptive green test passes through different mechanisms.", "test-safety-failure",
     ["problem"]),
    ("H1-CNF-005", ["idx-leg-eu-aiact-2024-1689", "idx-leg-eu-gdpr-2016-679"],
     "When handling personal data in high-risk AI decision systems, which European regulation defines compliance boundaries?",
     "AI Act and GDPR overlap for biometric/risk systems; compliance requires distinguishing their mandates.", "eu-ai-privacy",
     ["Regulamentul"]),
    ("H1-CNF-006", ["slot-05-state", "slot-15-retrieval"],
     "When storing active runtime variables, should they be indexed in the dynamic state slot or the retrieval catalog?",
     "Ephemeral state conflicts with retrieval cataloging; persistence lifetime dictates slot placement.", "slot-persistence",
     ["Ontology Slot", "Slot"]),
    ("H1-CNF-007", ["knw-context-packing-p1-0001", "knw-retrieval-bottleneck-p0-0001"],
     "When context token limits are reached, should the system truncate candidate generation or compress the final pack?",
     "Candidate pruning (P0) versus pack compression (P1) offer competing trade-offs.", "token-budget-stage",
     ["Empirical Findings", "Specification"]),
    ("H1-CNF-008", ["f10029dc-e28e-4710-bf3a-b2b48bfb84dc", "0923cd4f-b2bd-4285-b11f-63be9d9977e8"],
     "When deploying bilingual Romanian-English user interfaces, does the strict English prompt preference apply to internal system prompts?",
     "Bilingual UI requirements conflict with internal English prompt preference.", "language-boundary",
     ["prompt"]),
    ("H1-CNF-009", ["d94d32d6-701e-43b7-b031-761b2c6c8e43", "f82194d2-31a8-4c91-a83d-e42109ab7d12"],
     "When setting up trade routing in MetaTrader, should execution logic reside in the EA bot or the clean architecture layer?",
     "Direct MetaTrader bot script conflicts with clean domain isolation.", "trading-architecture",
     ["Trading"]),
    ("H1-CNF-010", ["knw-leg-hg585-2002-0001", "idx-leg-ro-mapn-m172-2021"],
     "When handling classified state data in defense contexts, does general HG 585/2002 or ministerial order M.172/2021 govern?",
     "National standards conflict with specific military department norms.", "classified-defense-jurisdiction",
     ["Clasificate", "Ordinul"]),
]

# 7. DISTRACTOR CASES (10 cases)
# Target has true semantic answer, distractor shares high lexical overlap with query
dst_specs = [
    ("H1-DST-001", "vault-architecture-map-0001", ["vault-master-index-0001"],
     "Show the architectural system dataflow and boundary topology of the memory vault.",
     ["Vault Architecture", "Dataflow"]),
    ("H1-DST-002", "0c4c8b76-85c4-4fde-a14a-4bde0b840009", ["0c4c8b76-85c4-4fde-a14a-4bde0b840010"],
     "What steps are required to restore git repository state after a corrupted commit?",
     ["Git", "Restore", "Backup"]),
    ("H1-DST-003", "knw-context-packing-p1-0001", ["knw-retrieval-bottleneck-p0-0001"],
     "What rules govern soft context budgeting and graceful disclosure degradation?",
     ["Context Packing", "P1"]),
    ("H1-DST-004", "11e9fe55-6924-4231-9635-c5629bff6ae3", ["e045ded5-01fd-4a28-8f85-203ebae670de"],
     "Which system stores consolidated long-term neural memory across extended timeframes?",
     ["long-term memory", "memory"]),
    ("H1-DST-005", "slot-15-retrieval", ["slot-14-routing"],
     "Which ontology slot defines the memory search and candidate generation pipeline?",
     ["Retrieval", "Ontology Slot"]),
    ("H1-DST-006", "proc-reflexion-0001", ["proc-brain-arch-0001"],
     "What is the closed-loop protocol for self-reflection and multi-stage evaluation?",
     ["Reflexion", "Closed-Loop"]),
    ("H1-DST-007", "f82194d2-31a8-4c91-a83d-e42109ab7d12", ["d94d32d6-701e-43b7-b031-761b2c6c8e43"],
     "What clean domain layers isolate business logic from MT5 API connectors in XAU_Kinetic?",
     ["XAU_Kinetic", "Clean Architecture"]),
    ("H1-DST-008", "idx-leg-eu-aiact-2024-1689", ["idx-leg-eu-dora-2022-2554"],
     "What European regulation establishes harmonized transparency rules for foundation models and artificial intelligence?",
     ["AI Act", "2024/1689"]),
    ("H1-DST-009", "proc-stats-reporting-0001", ["knw-benchmarks-2026-0001"],
     "What standard prevents reporting deceptive percentage deltas over tiny evaluation samples?",
     ["Propor", "Benchmark"]),
    ("H1-DST-010", "fd63129c-091c-4171-9eb4-b7c28190b1a4", ["123fe8df-2490-44f0-b695-143c3d5a997f"],
     "What retrospective lesson describes when two independently correct fixes cancel each other?",
     ["independently correct", "cancel each other"]),
]

cases = []

# Build direct cases
for i, (cid, gid, query, pref_facts) in enumerate(dir_specs):
    cases.append({
        "id": cid,
        "family": "direct_lexical",
        "intended_boundary": "candidate_generation",
        "query": query,
        "gold_relevant_notes": [gid],
        "required_facts": [fact(gid, *pref_facts)],
        "abstain": False,
        "principal": "HUMAN",
        "split": split_for_idx(i),
        "corpus_commit": commit,
        "corpus_hash": corpus_hash,
        "rationale": "Direct key term overlap query targeting canonical note.",
    })

# Build paraphrase cases
for i, (cid, gid, query, pref_facts) in enumerate(par_specs):
    cases.append({
        "id": cid,
        "family": "paraphrase",
        "intended_boundary": "candidate_generation",
        "query": query,
        "gold_relevant_notes": [gid],
        "required_facts": [fact(gid, *pref_facts)],
        "abstain": False,
        "principal": "HUMAN",
        "split": split_for_idx(i),
        "corpus_commit": commit,
        "corpus_hash": corpus_hash,
        "rationale": "Paraphrased query preserving core meaning with altered lexical surface.",
    })

# Build indirect cue cases
for i, (cid, gid, query, pref_facts) in enumerate(ind_specs):
    cases.append({
        "id": cid,
        "family": "indirect_cue",
        "intended_boundary": "candidate_generation",
        "query": query,
        "gold_relevant_notes": [gid],
        "required_facts": [fact(gid, *pref_facts)],
        "abstain": False,
        "principal": "HUMAN",
        "split": split_for_idx(i),
        "corpus_commit": commit,
        "corpus_hash": corpus_hash,
        "rationale": "Indirect cue pointing to target through functional consequence without surface terms.",
    })

# Build entity / context cases
for i, (cid, gid, query, pref_facts) in enumerate(ent_specs):
    cases.append({
        "id": cid,
        "family": "entity_context",
        "intended_boundary": "candidate_generation",
        "query": query,
        "gold_relevant_notes": [gid],
        "required_facts": [fact(gid, *pref_facts)],
        "abstain": False,
        "principal": "HUMAN",
        "split": split_for_idx(i),
        "corpus_commit": commit,
        "corpus_hash": corpus_hash,
        "rationale": "Contextual query leveraging framework, toolchain, or system domain entities.",
    })

# Build multi-hop cases
for i, (cid, gid, query, path_edges, pref_facts) in enumerate(hop_specs):
    # Verify path
    formatted_path = []
    for s, t, r in path_edges:
        assert (s, t, r) in edge_set, f"Edge ({s}, {t}, {r}) not in corpus links!"
        formatted_path.append({"source": s, "target": t, "relation": r})
    assert formatted_path[-1]["target"] == gid, f"Last target {formatted_path[-1]['target']} != {gid}"
    cases.append({
        "id": cid,
        "family": "multi_hop_associative",
        "intended_boundary": "graph",
        "query": query,
        "gold_relevant_notes": [gid],
        "required_facts": [fact(gid, *pref_facts)],
        "graph_path": formatted_path,
        "abstain": False,
        "principal": "HUMAN",
        "split": split_for_idx(i),
        "corpus_commit": commit,
        "corpus_hash": corpus_hash,
        "rationale": "2-hop connected graph traversal query recovering target through intermediary relation.",
    })

# Build conflict cases
for i, (cid, gids, query, reason, group, pref_facts) in enumerate(cnf_specs):
    req_facts = [fact(gid, *pref_facts) for gid in gids]
    cases.append({
        "id": cid,
        "family": "conflict",
        "intended_boundary": "ranking",
        "query": query,
        "gold_relevant_notes": gids,
        "required_facts": req_facts,
        "multi_gold_reason": reason,
        "conflict_group": group,
        "abstain": False,
        "principal": "HUMAN",
        "split": split_for_idx(i),
        "corpus_commit": commit,
        "corpus_hash": corpus_hash,
        "rationale": "Conflicting dual-target context disambiguation task.",
    })

# Build distractor cases
for i, (cid, gid, distractors, query, pref_facts) in enumerate(dst_specs):
    for d in distractors:
        assert d in notes, f"Distractor {d} not in corpus notes!"
        assert d != gid, f"Distractor {d} == gold!"
    cases.append({
        "id": cid,
        "family": "distractor",
        "intended_boundary": "ranking",
        "query": query,
        "gold_relevant_notes": [gid],
        "distractor_ids": distractors,
        "required_facts": [fact(gid, *pref_facts)],
        "abstain": False,
        "principal": "HUMAN",
        "split": split_for_idx(i),
        "corpus_commit": commit,
        "corpus_hash": corpus_hash,
        "rationale": "High lexical overlap distractor competing against true semantic gold target.",
    })

cases_payload = {
    "corpus_commit": commit,
    "corpus_hash": corpus_hash,
    "cases": cases,
}
cases_payload["benchmark_hash"] = validate_h1_corpus.benchmark_hash(cases_payload)

with open(CASES_PATH, "w", encoding="utf-8") as f:
    json.dump(cases_payload, f, ensure_ascii=False, indent=2)

print(f"Generated {len(cases)} cases.")
print(f"Benchmark hash: {cases_payload['benchmark_hash']}")

# Run validation
result = validate_h1_corpus.validate_corpus(cases_payload, corpus, final=False)
print("Validation result valid:", result["valid"])
if result["errors"]:
    print("Errors:", result["errors"])
    sys.exit(1)
if result["warnings"]:
    print(f"Warnings ({len(result['warnings'])}):")
    for w in result["warnings"][:5]:
        print(" ", w)
print("SUCCESS: Benchmark cases successfully generated and structurally validated.")
