# E2E Test Infra: Jarvis Cognitive Brain

## Test Philosophy
- Opaque-box, requirement-driven. No dependency on implementation design.
- Methodology: Category-Partition + Boundary Value Analysis (BVA) + Pairwise Combinatorial + Real-World Workload Testing + White-box Adversarial Hardening.

## Feature Inventory Mapping
| # | Feature | Source | Tier 1 (Features) | Tier 2 (Boundaries) | Tier 3 (Interactions) | Tier 4 (Real-World) |
|---|---------|--------|:-----------------:|:-------------------:|:---------------------:|:-------------------:|
| 1 | Modular LLM Provider Layer | R1 | ≥5 tests | ≥5 tests | ✓ | ✓ |
| 2 | Complete Stateful OODA Loop | R1 | ≥5 tests | ≥5 tests | ✓ | ✓ |
| 3 | Multi-Layer Associative Recall | R1 | ≥5 tests | ≥5 tests | ✓ | ✓ |
| 4 | Dual Persistence Storage Engine | R1 | ≥5 tests | ≥5 tests | ✓ | ✓ |
| 5 | Trust Boundary Invariants (P0-P18) | R1 | ≥5 tests | ≥5 tests | ✓ | ✓ |
| 6 | Continuous STT with Silero VAD | R2 | ≥5 tests | ≥5 tests | ✓ | ✓ |
| 7 | Streaming TTS Engine (Kokoro-82M) | R2 | ≥5 tests | ≥5 tests | ✓ | ✓ |
| 8 | Sub-50ms Barge-In Interruption | R2 | ≥5 tests | ≥5 tests | ✓ | ✓ |
| 9 | Headless Audio Drivers & Mock Engine | R2 | ≥5 tests | ≥5 tests | ✓ | ✓ |
| 10 | Multi-Agent Supervisor | R3 | ≥5 tests | ≥5 tests | ✓ | ✓ |
| 11 | Least-Privilege Specialized Agents | R3 | ≥5 tests | ≥5 tests | ✓ | ✓ |
| 12 | FastMCP JarvisControls Server | R4 | ≥5 tests | ≥5 tests | ✓ | ✓ |
| 13 | Home Assistant REST Client | R4 | ≥5 tests | ≥5 tests | ✓ | ✓ |
| 14 | Local Home Assistant Simulator | R4 | ≥5 tests | ≥5 tests | ✓ | ✓ |
| 15 | 3D WebGL Holographic Visualizer & HUD | R5 | ≥5 tests | ≥5 tests | ✓ | ✓ |
| 16 | Real-Time OODA Thought Stream | R5 | ≥5 tests | ≥5 tests | ✓ | ✓ |
| 17 | Interactive Memory Graph Visualizer | R5 | ≥5 tests | ≥5 tests | ✓ | ✓ |
| 18 | System Health & Audio Controls | R5 | ≥5 tests | ≥5 tests | ✓ | ✓ |

## Test Architecture
- Test runner: `pytest -v tests/` and dedicated E2E runner script `python -m tests.e2e.test_runner`.
- Pass/fail semantics: Exit code 0, 0 test failures, 100% pass rate.
- Deterministic headless drivers for audio I/O, WebSockets, and mock LLM / HA simulator.

## Coverage Thresholds
- Tier 1: ≥5 per feature (≥90 test cases)
- Tier 2: ≥5 per feature boundary (≥90 test cases)
- Tier 3: Pairwise coverage of major feature combinations (≥20 test cases)
- Tier 4: Realistic end-to-end voice and cognitive workflows (≥10 test cases)
- Tier 5: Adversarial edge-case, race-condition, and fault-injection suites (≥15 test cases)
- **Total Minimum Test Count: ≥225 test cases**
