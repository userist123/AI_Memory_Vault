# E2E Test Infra: JARVIS Web Ecosystem

## Test Philosophy
- Opaque-box, requirement-driven testing. Verified against `ORIGINAL_REQUEST.md`.
- Zero-cost test execution using Node.js built-in `node --test` test runner and lightweight browser mocks.
- Methodology: Category-Partition + Boundary Value Analysis + Pairwise Combinations + Real-World Workloads.

## Feature Inventory & Test Coverage Matrix
| # | Feature | Source | Tier 1 (Coverage ≥5) | Tier 2 (Boundaries ≥5) | Tier 3 (Pairwise) | Tier 4 (Real-World) |
|---|---------|--------|:-------------------:|:---------------------:|:----------------:|:------------------:|
| 1 | Continuous Speech Recognition | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ | ✓ |
| 2 | Wake-Word Detection ("Jarvis") | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ | ✓ |
| 3 | Bilingual Autodetection (RO/EN) | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ | ✓ |
| 4 | Neural Speech Synthesis | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ | ✓ |
| 5 | Dynamic Voice Selector | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ | ✓ |
| 6 | Mic Mute / Audio Toggle | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ | ✓ |
| 7 | Holographic Arc-Reactor Three.js | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ | ✓ |
| 8 | Dynamic State Animations (6 states)| ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ | ✓ |
| 9 | Audio Reactive Pulsation | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ | ✓ |
| 10 | Procedural Tactical Audio Engine | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ | ✓ |
| 11 | Thinking Ambient Drone | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ | ✓ |
| 12 | Live Knowledge Search REST | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ | ✓ |
| 13 | Note Inspector & Citations | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ | ✓ |
| 14 | Memory Proposal API | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ | ✓ |
| 15 | Offline Fallback Cache | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ | ✓ |
| 16 | Cyberpunk Glassmorphism HUD | ORIGINAL_REQUEST §R4 | 5 | 5 | ✓ | ✓ |
| 17 | Real-time Conversation Stream | ORIGINAL_REQUEST §R4 | 5 | 5 | ✓ | ✓ |
| 18 | Subagent Council Telemetry | ORIGINAL_REQUEST §R4 | 5 | 5 | ✓ | ✓ |
| 19 | Direct Command / Prompt Input | ORIGINAL_REQUEST §R4 | 5 | 5 | ✓ | ✓ |
| 20 | Central Finite State Controller | ORIGINAL_REQUEST AC | 5 | 5 | ✓ | ✓ |

## Test Architecture
- Test Runner: Node.js native `node --test projects/jarvis_web/test/test_jarvis.js`
- Assertions: `node:assert/strict`
- Browser Mocks: `mock_web_speech.js`, `mock_web_audio.js`, `mock_webgl.js`, `mock_fetch.js`
- Test Directory: `projects/jarvis_web/test/`

## Real-World Application Scenarios (Tier 4)
| # | Scenario | Features Exercised | Complexity |
|---|----------|--------------------|------------|
| 1 | Romanian Voice Search Flow: User says "Hei Jarvis, caută regulile de memorie", system detects RO, transitions Idle->Listening->Thinking->Speaking, searches REST/cache, speaks response in Romanian, displays citation card. | F1, F2, F3, F4, F7, F8, F10, F12, F13, F17, F20 | High |
| 2 | English Voice Search with Interruption: User wakes JARVIS in English, initiates search, and speaks to interrupt mid-response. JARVIS immediately cancels TTS and transitions to Listening. | F1, F2, F3, F4, F6, F8, F17, F20 | High |
| 3 | Backend Offline Resilience: Server is offline, user submits prompt via text input bar, system gracefully falls back to local memory cache, displays citation, and plays success audio without unhandled rejection. | F10, F12, F13, F15, F16, F19, F20 | Medium |
| 4 | WebGL Context Loss & Degradation: Headless or non-WebGL browser environment initialized; system smoothly activates 2D Canvas/CSS visualizer fallback while maintaining 100% voice & UI functionality. | F7, F8, F9, F16, F20 | Medium |
| 5 | Subagent Dispatch & Proposal Flow: User requests memory proposal, client constructs valid proposal JSON, validates schema against P0-P15 invariants, and dispatches to POST /api/v1/propose. | F12, F14, F17, F18, F19, F20 | High |

## Coverage Thresholds
- Tier 1: ≥100 test cases (5 × 20 features)
- Tier 2: ≥100 boundary & corner test cases (5 × 20 features)
- Tier 3: ≥20 cross-feature pairwise integration tests
- Tier 4: ≥5 end-to-end real-world workload scenarios
- Tier 5: Adversarial edge-case & fuzzing stress tests
