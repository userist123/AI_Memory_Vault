---
id: jarvis-cognitive-fortress-pattern
type: knowledge
category: ai-architecture
tags: [agent-architecture, anti-hallucination, mcp, memory-tiers, confidence-calibration]
created: '2026-08-15'
updated: '2026-08-15'
provenance:
  sourcetype: import
  sourceref: 'JARVIS-SISTEM-3.pdf, Arhitectura-Cognitiva-J.A.R.V.I.S-2.pdf, Prompt-JARVIS-4.pdf'
confidence: high
verification: unverified
relations:
  - relation: relates_to
    target: 01_ARCHITECTURE/System_Architecture
  - relation: relates_to
    target: 01_ARCHITECTURE/knowledge/LLM_Antihallucination_Techniques_Research
lifecycle: raw
---

# JARVIS Cognitive Fortress — Autonomous Agent Prompt Pattern

**Graph links:** [[LLM_Antihallucination_Techniques_Research]] · [[Romania_Classified_Information_Digital_Security_Reform]] · [[01 Cognitive System Map]] · [[04 Security Integrity Map]] · [[07 Knowledge Domains Map]]

Design pattern for a personal autonomous agent, synthesized from academic research (ReAct, Metacognitive Prompting, Chain-of-Verification, Anthropic Constitutional AI) into a concrete, reusable prompt architecture.

## 5-layer mandatory processing pipeline
Every input passes through all 5 layers in order; no bypass; if any layer blocks, no output is delivered.

1. **Triage** (instant classification) — detect modality, real intent, temporal volatility of the topic (volatile info -> mandatory active research, never answer from training memory), user expertise level.
2. **Deep processing** — activate the right specialized module (analytical / pattern-recognition / execution / pedagogical) rather than a generic response. Single-agent systems hit an operational ceiling around 15 concurrent tools — exceeding it collapses attention.
3. **Verification (Amygdala layer)** — run the 12 anti-hallucination antibodies (see below) before any claim survives to output.
4. **Calibration** — label each critical claim's confidence objectively: CONFIRMED (2+ independent recent sources), PROBABLE (1 good source + solid deduction), UNCERTAIN (contradictory/insufficient data), UNKNOWN (zero verifiable data). Never rely on self-reported confidence.
5. **Final gate (Prefrontal Cortex)** — factored Chain-of-Verification pass: list 3-5 key claims, verify each in isolation, revise. Binary PASS/BLOCK checklist before delivery.

## The 12 anti-hallucination antibodies (trigger -> test -> action)
1. Anti data fabrication — no unverifiable number/stat/date.
2. Anti source fabrication — only cite sources actually accessed this session.
3. Anti confidence-extrapolation — distinguish "I know" from "this sounds right" (the latter = STOP signal).
4. Anti pattern-completion — don't fill knowledge gaps with plausible deductions; leave gaps marked.
5. Anti user-pressure — user insistence never lowers the accuracy bar.
6. Anti temporal confusion — don't state training-derived facts about "now" without dating them.
7. Anti cascade hallucination — if an earlier claim is found wrong, reset and rebuild from that point, don't patch.
8. Anti false authority — expert tone only when backed by verified data.
9. Anti exploitable ambiguity — don't default to the easiest interpretation of a vague request; ask or present alternatives.
10. Anti fabricated lists — don't pad "top 10" lists to a round number with invented entries.
11. Anti false consensus — don't say "studies show" without naming the studies.
12. Anti ignored contradiction — surface contradictions in user input instead of smoothing over them.

## Graceful degradation protocol (never collapse into fabrication)
Level 1 (100% info): full structured answer. Level 2 (~70%): answer + explicit "what's missing." Level 3 (30-70%): clearly separate confirmed facts from deduction. Level 4 (<30%): short, honest, point to best external source. Level 5 (0%): "I don't have data on this" + best source to consult. At no level is a data gap filled with fabrication.

## 4-tier memory architecture (for long-running autonomous agents)
1. **Working memory** (ephemeral) — current thoughts/plan, discarded after task completion.
2. **Conversation memory** — fixed window of recent turns + rolling summary.
3. **Task memory** — structured log of generated artifacts, technical decisions, executed commands, stored as deterministic JSON (not vector embeddings).
4. **Long-term memory** — reserved for uncontested facts/preferences; agent may only read from it during a run, writing is deferred to post-validation.

## Integration notes: MCP + safety
- Tools should be exposed as strict API contracts (JSON Schema params, timeouts, defined error handling); agent must ask for clarification rather than guess missing parameters.
- Any state-mutating tool call needs an idempotency key.
- High-risk operations require human-in-the-loop approval.
- Shell/code-execution tools must run sandboxed with immutable audit logging.

## Direct mapping to this vault's existing Cognitive Core
| JARVIS pattern | Existing implementation |
|---|---|
| Amygdala / 12 antibodies | `ReflectionPipeline` + `MemoryController` validation |
| 4-tier memory | `WorkingMemory` (tier 1) + `RecallEngine` (tiers 2-3) + `Lifecycle.ACTIVE` notes (tier 4) |
| Idempotency + human-in-the-loop | `Authorizer` policy matrix + `review()`/`promote()` split between AI_AGENT and HUMAN |
| Immutable audit logging | `AuditLogger` (JSON lines) |

This confirms the vault's cognitive design already follows current best practice; the antibody taxonomy above can be used directly as a checklist when extending `ReflectionPipeline` with new verification rules.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
