---
agent: ANTIGRAVITY
last_updated_utc: 2026-09-20T23:30:00Z
repository: userist123/AI_Memory_Vault_CODEX_READY
working_branch: antigravity/tokenizer-and-reranker-envelope
base_branch: origin/main
base_sha: 0685d87b7
project_id: AI_MEMORY_VAULT
application: AI Memory Vault / Memory Engine
current_task: MEASUREMENT_PROGRAM_PART_3_PR_1_TOKENIZER_EXPERIMENT
status: COMPLETE
completed:
  - "Measurement Program Partea 3, PR 1 — Normalizarea Tokenizatorului (EXP-TOKEN-001):
     1. Pre-registration committed alone in commit f3a19bc59 (07_EVALUATION/tokenizer_experiment/PREREGISTRATION.md):
        - H-TOKEN-1: Delta_RO >= +5.00 pp (>= 3 net cases won out of 61).
        - English non-regression: Delta_EN >= -1.45 pp (<= 1 case lost out of 69, p > 0.10).
        - Pre-registered decision rule: adopt new tokenizer iff Delta_RO >= +5.00 pp AND Delta_EN >= -1.45 pp AND Delta_Total >= +1.54 pp. Otherwise maintain baseline.
     2. Implemented evaluation harness 30_SCRIPTS/evaluation/eval_tokenizer_experiment.py testing 3 arms across all 130 non-abstain benchmark v3 cases (Principal.AI_AGENT, page_size=5, Floor: ACTIV):
        - Arm 1 (Baseline): TOKEN_RE = [a-z0-9][a-z0-9_\\-\\.]* (21/130 hits, 16.15%, RO 9/61 14.75%, EN 12/69 17.39%, Top 200 RO: 46/61).
        - Arm 2 (Unicode Preserving): UNICODE_TOKEN_RE (ăâîșț preserved) (21/130 hits, 16.15%, RO 9/61 14.75%, EN 12/69 17.39%, Top 200 RO: 46/61).
        - Arm 3 (Diacritics Stripped): NFKD accent folding (21/130 hits, 16.15%, RO 9/61 14.75%, EN 12/69 17.39%, Top 200 RO: 46/61).
     3. Discordant pair & McNemar exact test:
        - Arm 2 vs Baseline: b = 0, c = 0, discordant = 0, p_mcnemar = 1.000000.
        - Arm 3 vs Baseline: b = 0, c = 0, discordant = 0, p_mcnemar = 1.000000.
        - Zero flipped cases across all 130 benchmark queries.
     4. Empirical findings & pre-registered rule execution:
        - H-TOKEN-1: INFIRMATĂ (Delta_RO = 0.00 pp vs >= +5.00 pp required).
        - Non-Regresie EN: CONFIRMATĂ (Delta_EN = 0.00 pp).
        - Câștig Net Total: INFIRMATĂ (Delta_Total = 0.00 pp).
        - Pre-registered verdict: MENȚINERE BASELINE (RESPINGERE ADOPTARE TOKENIZATOR NOU).
        - Production hybrid_retrieval.py remains unmodified on main, avoiding zero-benefit code churn and index invalidation.
     5. Generated artifacts and test suite:
        - 07_EVALUATION/tokenizer_experiment/tokenizer_experiment_cases.json (24 KB).
        - 07_EVALUATION/tokenizer_experiment/TOKENIZER_EXPERIMENT_REPORT.md (6.2 KB).
        - 20_TESTS/test_tokenizer_experiment_accuracy.py (9/9 PASS) validating bit-for-bit rendering, report & JSON tampering negative controls, operating point headers, McNemar exact mathematics, and frozen benchmark SHA-256."
  - "Measurement Program Partea 2, PR 3 — Diagnosticul Propriu-Zis al Pâlniei Pierderilor:
     1. Evaluated all 130 non-abstain benchmark cases across production and reference operating points:
        - Production: Principal.AI_AGENT, page_size=5, Floor ACTIV (21/130 hits, 16.15%, Wilson CI [10.82%, 23.44%]).
        - Reference: Principal.HUMAN, page_size=10, Floor INACTIV (38/130 hits, 29.23%, Wilson CI [22.10%, 37.56%]).
        - Control: Principal.HUMAN, page_size=5, Floor INACTIV (21/130 hits, 16.15%, Wilson CI [10.82%, 23.44%]).
     2. Forensic resolution of the 21 vs 39 hits gap:
        - 16 cases lost to pagination budget (page_size 5 vs 10 under agent).
        - 0 cases lost to lifecycle floor at page_size=5 (net impact 0; all 15 floor-excluded notes ranked >= 6).
        - 1 case lost to lifecycle floor at page_size=10 (R3-039).
        - 1 case lost to corpus note growth (R3-052 moved from rank 10 to rank 14).
     3. Complete causal loss attribution on 109 misses:
        - PAGINATION_CUT: 78 cases (71.56%, Wilson CI [62.47%, 79.18%]).
        - AGENT_LIFECYCLE_FLOOR_EXCLUDED: 15 cases (13.76%, Wilson CI [8.52%, 21.47%]).
        - NEVER_CANDIDATE: 13 cases (11.93%, Wilson CI [7.10%, 19.34%]).
        - CANDIDATE_LIMIT_CUT: 2 cases (1.83%, Wilson CI [0.50%, 6.44%]).
        - RAW_EXCLUDED: 1 case (0.92%, Wilson CI [0.16%, 5.01%]).
        - UNDETERMINED: 0 cases (0.00%, Wilson CI [0.00%, 3.40%]), strictly meeting the < 10% threshold.
     4. Rank distribution & Oracle ceiling:
        - Median rank on ranked misses: 20.5.
        - Oracle ceiling: Recall@5=33.08%, @10=40.77%, @20=46.92%, @50=65.38%, @100=70.77%, @200=76.15% (+60.00 pp max ceiling over current 16.15%).
     5. NEVER_CANDIDATE breakdown: 9 vocabulary mismatch (69.23%) vs 4 sub-threshold BM25 score (30.77%).
     6. Statistical hypotheses evaluation with Holm-Bonferroni correction:
        - H1 (NEVER_CANDIDATE >= 40%): INFIRMATĂ (11.93%, p_adj = 0.000000).
        - H2 (multi_hop NEVER_CANDIDATE >= 60%): INFIRMATĂ (3.12%, p_adj = 0.000000; dominated by PAGINATION_CUT 75.0%).
        - H3 (Romanian recall < English recall): INFIRMATĂ (EN 17.39% vs RO 14.75%, diff +2.64 pp, p_adj = 1.000000). Tokenizer regex mechanical flaw identified and disclosed. Non-independence of H1/H2 formally declared.
        - H4 (Hits in Top 3 >= 75%): CONFIRMATĂ (16/21 = 76.19%, p_adj = 1.000000).
     7. Pre-registered decision threshold verdict: ADOPTARE RERANKER (73.39% ranking/candidate cuts >= 40% AND median miss rank 20.5 <= 30).
     8. Cost of failure: policy_and_retrieval stage latency identical between hits (350.68 ms) and misses (354.33 ms); failure is not costlier, merely useless.
     9. Artifacts & Tests:
        - Generated 07_EVALUATION/loss_funnel/loss_funnel_cases.json (360 KB).
        - Rendered 07_EVALUATION/loss_funnel/LOSS_FUNNEL_REPORT.md (22 KB).
        - Added 20_TESTS/test_loss_funnel_report_accuracy.py (9/9 PASS) with bit-for-bit rendering verification, report tampering negative control, and contract assertions.
        - All negative controls passing (shuffled labels, gold injection, determinism, synthetic cases)."
  - "Measurement Program Partea 2, PR 2 — Diagnostic Harness & Negative Controls:
     1. Implemented diagnostic harness 30_SCRIPTS/evaluation/retrieval_loss_funnel.py diagnosing all 130 non-abstain benchmark cases into canonical categories: AGENT_LIFECYCLE_FLOOR_EXCLUDED, RAW_EXCLUDED, CANDIDATE_LIMIT_CUT, PAGINATION_CUT, NEVER_CANDIDATE, and UNDETERMINED.
     2. Negative Control A (Shuffled labels in-memory): Mean recall 2.77% across 5 seeds (target: < 5.0%), std dev 0.62%, frozen benchmark SHA-256 bit-for-bit unmodified on disk.
     3. Negative Control B (Gold injection): 130/130 (100.0%) exact detection rate.
     4. Negative Control C (Determinism): 130/130 cases identical across 3 independent runs, 0 varying cases, hit std dev 0.0000.
     5. Synthetic cases negative control: Verified exact classification for rank 7 (PAGINATION_CUT), missing/no-match (NEVER_CANDIDATE), RAW_EXCLUDED, and AGENT_LIFECYCLE_FLOOR_EXCLUDED.
     6. Comprehensive test suite: 20_TESTS/test_retrieval_loss_funnel.py (5/5 PASS).
     7. Runtime performance: 275.29s (4.59 minutes, well within the 20 minute limit).
     8. Blind validation invariant preserved: zero loss funnel aggregate distributions disclosed or committed in PR 2."
  - "Measurement Program Partea 2, PR 1 — Preînregistrare:
     1. Formal pre-registration committed in 07_EVALUATION/loss_funnel/PREREGISTRATION.md prior to any execution.
     2. Pre-registered hypotheses H1–H4, decision thresholds (≥40% PAGINATION/CANDIDATE_LIMIT_CUT with median rank ≤30 vs ≥40% NEVER_CANDIDATE), Holm–Bonferroni correction, and Wilson score intervals.
     3. Merged into main via PR #186 (commit 11ae8cbc9)."
  - "Measurement Program Partea 1 — Închide OBS-001 (Economic RetrievalTrace & Stage Latencies):
     1. Schema version upgraded to '1.1.0' with dual stage latency exposure: both stage_latency_ms and stage_latencies_ms populated across all 6 pipeline stages (query_validation, classification, policy_and_retrieval, scoring, pagination, context_pack).
     2. Economic trace representation implemented: individual detailed DecisionRecords preserved for competitive candidate notes (disclosed pool and final pack), while non-competitive storage policy and mass pagination/candidate cuts are compactly aggregated into aggregated_exclusions by reason_code with counts, criteria, and sample IDs.
     3. Added get_decision() resolving decisions seamlessly from either competitive decisions or aggregated_exclusions.
     4. Empirical trace size benchmark: measured on 10 benchmark v3 queries via 30_SCRIPTS/evaluation/measure_trace_size.py. Average trace size dropped from ~250-255 KB down to 16.69 KB (93.3% net space reduction, well under the 20.0 KB ceiling).
     5. Zero reason codes lost: all failure modes (RAW_EXCLUDED, AGENT_LIFECYCLE_FLOOR_EXCLUDED, CANDIDATE_LIMIT_CUT, PAGINATION_CUT, TYPE_FILTERED) fully preserved and auditable.
     6. Comprehensive test suite: 20_TESTS/test_retrieval_trace_contract.py (9/9 PASS).
     7. Full test suite validation: 2517 passed, 13 skipped, 9 xfailed in 307s with zero regressions across the entire repository."
  - "OBS-001 RetrievalTrace Contract Completed:
     1. Strongly-typed, versioned RetrievalTrace contract (schema_version = '1.0.0') with schema fingerprint validation and drift protection.
     2. Complete explainability: every candidate note carries an explicit machine-readable reason code across all pipeline stages (RAW_EXCLUDED, AGENT_LIFECYCLE_FLOOR_EXCLUDED, LIFECYCLE_FILTERED, TYPE_FILTERED, CANDIDATE_LIMIT_CUT, GRAPH_HUB_SKIPPED, PAGINATION_CUT, BUDGET_EXCEEDED, INCLUDED_IN_FINAL_PACK).
     3. Score reconstructibility and displacement tracking: raw signal scores and fused rank displacement (initial vs final) preserved for auditing.
     4. Data minimization verified: adversarial test confirms high-entropy secrets and raw query strings never appear in serialized traces; only SHA-256 query fingerprints and intent classifications are logged.
     5. Fail-safe telemetry execution: SafeTraceCollectorProxy ensures telemetry faults never disrupt retrieval; degraded status is reported safely.
     6. Latency overhead measured: ~0.90 ms/query total search latency across 50 iterations with negligible trace generation overhead.
     7. Comprehensive test suite: 20_TESTS/test_retrieval_trace_contract.py (7/7 PASS)."
  - "CI-002 Corpus Health Gate Completed:
     1. Built 30_SCRIPTS/verification/corpus_health_gate.py asserting baseline invariants from 00_GOVERNANCE/phase_0/CORPUS_HEALTH_BASELINE.md (edges >= 483, duplicate groups <= 5, dangling edges == 0, fixture notes == 0, active notes verified).
     2. Validated empirical metrics: 483 edges, 0 dangling edges, 0 fixture notes, 5 exact duplicate groups (13 files), 57 active notes (100% valid provenance and verification).
     3. Comprehensive test suite: 20_TESTS/test_corpus_health_gate.py (6/6 PASS).
     4. Full test suite validation: 2,515 passed, 13 skipped, 9 xfailed in 200s with zero regressions."
  - "Exploit & Offensive Skills Audit: Inspected all skills across .agents/skills/ and cataloged all offensive, exploit, payload, bypass, and weaponized pentesting tools."
  - "Safely Purged 40 Exploit Skills: Removed 40 attack/exploit skills."
  - "Preserved Defensive Forensics & Hardening: Kept intact all defensive security, forensic, threat-hunting, and compliance tools."
  - "Verified Kandel 2001 Ingestion: 10 candidates extracted and gated with 0 rejections and 10/10 distinct definition openings."
  - "Verified Newell UTC Ingestion: 34 candidate instances extracted across multiple chunks, 19 canonical concepts kept (15 duplicates merged, 13 recurring with occurrences >= 2), 0 rejections, definition openings 34 distinct (top 5 cover only 15% of rows)."
  - "Audited Newell How Can Human Mind Occur: Parcurs integral 154 chunk-uri (0 low_prose); detectat fișier corupt/scam provenit din ilide.info (conținut filler din manuale de golf, reclame de biciclete și fragmente maghiare de Gutenberg). 0 concepte valide extrase; salvat 0 candidați."
  - "Verified Laird Soar Cognitive Architecture: Parcurs integral 114 prose chunks (din 126, 12 low_prose); extrase 22 concepte autentice acoperind capitolele 1-14 (SMem, EpMem, SVS, RL, PSCM, Chunking, Impasses), 0 rejecții, definition openings 22 distincte (top 5 acoperă doar 23% din rânduri)."
  - "Verified Minsky Society of Mind Ingestion: Parcurs integral 107 prose chunks (din 111, 4 low_prose); extrase 28 submisii pe 10 concepte canonice pe 27 de secțiuni distincte, 0 rejecții, 9 concepte cu occurrences=3, definition openings: 28 distinct; top 5 cover 18% of rows."
  - "Verified Schacter & Tulving Memory Systems 1994: Parcurs integral 74 prose chunks (din 95, 21 low_prose); extrase 121 submisii pe 13 concepte canonice, 0 rejecții, 108 duplicate consolidate pe secțiuni distincte. Distribuție naturală/împrăștiată de occurrences: {3: 1, 4: 1, 5: 1, 7: 1, 8: 3, 9: 1, 10: 1, 11: 1, 13: 1, 15: 1, 20: 1}. Definition openings: 120 distincte; top 5 acoperă doar 5% din rânduri."
  - "Verified Squire & Kandel Mind to Molecules: Parcurs integral 77 prose chunks (din 87, 10 low_prose); extrase 109 submisii pe 12 concepte canonice, 0 rejecții, 97 duplicate consolidate pe secțiuni distincte. Distribuție naturală/împrăștiată de occurrences: {4: 3, 5: 1, 6: 1, 8: 1, 9: 1, 10: 2, 12: 1, 15: 1, 22: 1}. Definition openings: 109 distincte; top 5 acoperă doar 5% din rânduri."
  - "Verified Ashby Design for a Brain: Parcurs integral 90 prose chunks (din 94, 4 low_prose); extrase 265 submisii pe 11 concepte canonice, 0 rejecții, 254 duplicate consolidate pe secțiuni distincte. Distribuție autentică/împrăștiată de occurrences: {5: 1, 6: 1, 7: 1, 16: 1, 21: 1, 26: 1, 31: 2, 34: 1, 41: 1, 47: 1}. Definition openings: 262 distincte; top 5 acoperă doar 3% din rânduri."
  - "Verified Budson & Kensinger Why We Forget: Parcurs integral 83 prose chunks (din 84, 1 low_prose); extrase 100 submisii pe 13 concepte canonice din secțiuni expozitive (chunks 2..75), 0 rejecții, 87 duplicate consolidate pe secțiuni distincte. Distribuție naturală de occurrences: {3: 2, 4: 1, 5: 2, 6: 2, 7: 2, 10: 1, 14: 1, 15: 2}. Definition openings: 97 distincte; top 5 acoperă doar 8% din rânduri."
  - "Verified Ashby Introduction to Cybernetics: Parcurs integral 50 prose chunks (din 53, 1 low_prose [52], 50-51 răspunsuri exerciții/index); extrase 160 submisii pe 14 concepte canonice acoperind toate capitolele expozitive (2..49), 0 rejecții, 146 duplicate consolidate pe secțiuni distincte. Distribuție autentică de occurrences: {5: 2, 6: 3, 8: 1, 9: 1, 10: 2, 11: 2, 15: 1, 26: 1, 32: 1}. Definition openings: 158 distincte; top 5 acoperă doar 4% din rânduri. PASS la verify_agent_submission.py."
  - "Verified Memory in the Age of AI Agents: Parcurs integral 25 prose chunks (din 35, 10 low_prose [5, 26..34]); extrase 131 submisii pe 14 concepte canonice, 0 rejecții, 117 duplicate consolidate pe secțiuni distincte. Distribuție autentică de occurrences: {3: 1, 6: 2, 8: 2, 9: 5, 13: 2, 14: 1, 15: 1}. Definition openings: 131 distincte; top 5 acoperă doar 4% din rânduri. PASS la verify_agent_submission.py (14/14 exact, 100% prose coverage)."
  - "Verified 2601.09113v1 (The AI Hippocampus): Parcurs integral 16 prose chunks (din 22, 6 low_prose [16..21]); extrase 84 submisii pe 16 concepte canonice, 0 rejecții, 68 duplicate consolidate pe 15 secțiuni distincte. Distribuție autentică de occurrences: {3: 7, 4: 3, 5: 1, 7: 1, 8: 1, 9: 2, 13: 1}. PASS la verify_agent_submission.py."
  - "Verified Machine Learning Report (Royal Society 2017): Parcurs integral 19 prose chunks, 0 rejecții."
  - "Verified wcs_1488 (ACT-R Cognitive Architecture): Parcurs integral 14 prose chunks, 0 rejecții."
  - "Verified sarfraz22a (Error Sensitivity in Continual Learning): Parcurs integral 11 prose chunks, 0 rejecții."
  - "Verified 2504.05840v1 (Momentum Boosted Episodic Memory): Parcurs integral 8 prose chunks, 0 rejecții."
  - "Verified quantum_consciousness_framework: Parcurs integral 5 prose chunks, 0 rejecții."
  - "Verified wiener_cybernetics: Parcurs integral 4 prose chunks, 0 rejecții."
  - "Verified 7688_jkt_au: Parcurs integral 1 prose chunk, 0 rejecții."
  - "Verified comparison_cognitive_architectures: Parcurs integral 1 prose chunk, 0 rejecții."
  - "Fixed Laird Soar Recurrence: Reconstruit candidații folosind mention_worksheet.py peste 114 prose chunks."
  - "PASUL 1-4 Complete: Reparat occurrences, rezolvat conflicte sloturi, generat CORPUS_INGESTION_REPORT.md, consolidat 201 concepte noi unice."
  - "Promovare 23 Concepte Nucleu: Generate 23 note atomice canonice Promoted_*.md în 01_ARCHITECTURE/knowledge/ cu ciclu de viață strict REVIEW."
  - "Cablare Relații Sinaptice: Conectate legături conform ALLOWED_RELATIONS."
  - "Curriculum Module Profile Schema & Validator: Implemented schema with automated validation in validate_curriculum_profile.py."
  - "Bibliographic Provenance Architecture: Enriched 16 Psychology notes and 5 Statistics notes with verifiable provenance."
  - "OpenStax Introductory Statistics Chapter 8 Ingestion: Ingested Confidence Intervals module via generic pipeline."
  - "Partea 2 Complete: Length >= 4, word boundaries, duplicate filtering in edge_proposer.py."
  - "Partea 3 Complete (Cognitive Core wiring & empirical evaluation): Pre-registered hypotheses; wired core into MemoryController.search(); dual-arm evaluated on heldout v2."
  - "Partea 3B Complete (5 Cognitive Modules Evaluation): Pre-registered decision rules; wired modules into MemoryController.search() (OFF by default); evaluated on benchmark v3."
  - "Antigravity Program Complete — Graph Purge Audited:
     1. Verified audit_sample_declared_50.json SHA-256 matches verdicts (815d00d131297670533ae2a6ac207b78f45333415c13612909ef271136ba1c1b).
     2. Wired graph/plasticity.py into SynapseStore and MemoryController production interfaces, verified non-empty production consumers via grep.
     3. Produced and committed dry-run report for 29 rejected edges in 07_EVALUATION/edge_audit_v2/audit_purge_dry_run_report.md (commit 610e1bf07).
     4. Purged the 29 rejected relations at the source across all 27 source notes frontmatters, added [[state-determined system]] to Promoted_transformation.md to prevent island notes (87/87 Promoted notes pass in test_promoted_notes_reach_the_graph.py).
     5. Added comprehensive test suite 20_TESTS/test_plasticity_audit_purge_rollback.py with byte-for-byte rollback validation (4/4 PASS).
     6. Updated 00_GOVERNANCE/VAULT_STATE.md measured table (483 edges, 203 declared / 203 inferred / 77 wikilink, 85 typed) and passed test_vault_state_accuracy.py (11/11 PASS).
     7. Created 30_SCRIPTS/evaluation/build_remaining_typed_edge_audit.py and packaged remaining 65 unjudged typed relations into 07_EVALUATION/edge_audit_v2_remaining/ (aggregate SHA-256 06bfd856e4b794bec75bd4a6b9e0e8474e74722e103893cfdeb8638a80d928f5, 7 batches <= 20 KB each, and copy-paste ready LABELING_PROMPT.md). Added test_remaining_typed_edge_audit.py (5/5 PASS).
     8. Full verification: LAYOUT_STATUS=PASS, FAILURES=0, PERSONAL_DATA_STATUS=PASS, pytest 20_TESTS -q passed 2485 items (zero regressions)."
in_progress: []
next_actions:
  - "Submit PR from antigravity/graph-purge-audited to claude/wave-c-edge-proposer"
blockers: []
risks: []
Evidence_refs:
  - 07_EVALUATION/loss_funnel/LOSS_FUNNEL_REPORT.md
  - 07_EVALUATION/loss_funnel/loss_funnel_cases.json
  - 20_TESTS/test_loss_funnel_report_accuracy.py
  - 20_TESTS/test_retrieval_loss_funnel.py
  - 30_SCRIPTS/evaluation/retrieval_loss_funnel.py
  - 20_TESTS/test_retrieval_trace_contract.py
  - 20_TESTS/test_corpus_health_gate.py
  - 03_IMPLEMENTATION/packages/observability/retrieval_trace.py
  - 30_SCRIPTS/verification/corpus_health_gate.py
  - 03_IMPLEMENTATION/packages/retrieval/context/candidate_generation.py
  - 03_IMPLEMENTATION/packages/memory/controller.py
  - 00_GOVERNANCE/phase_0/CORPUS_HEALTH_BASELINE.md
  - 00_GOVERNANCE/VAULT_STATE.md
related_agents: CODEX, CLAUDE_CODE, LUNA
---


## 🔗 Legături Sinaptice
- [[Governance_Repository_Spine_Specification|Governance]]
- [[00 Core Map]]
- [[14 Subagents Council Map]]
- [[Knowledge Graph Home]]
