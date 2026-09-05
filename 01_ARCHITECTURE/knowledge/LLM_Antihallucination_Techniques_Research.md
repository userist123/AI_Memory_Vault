---
id: llm-antihallucination-techniques
type: knowledge
category: ai-architecture
tags: [hallucination, prompt-engineering, context-engineering, llm-safety]
created: '2026-08-15'
updated: '2026-08-15'
provenance:
  sourcetype: import
  sourceref: 'Engineering-LLM-system-prompts-that-prevent-hallucination.pdf'
confidence: high
verification: unverified
relations:
  - relation: relates_to
    target: 01_ARCHITECTURE/System_Architecture
lifecycle: raw
---

# LLM Anti-Hallucination Techniques (2024-2026 Research Synthesis)

**Graph links:** [[JARVIS_Cognitive_Fortress_Prompt_Pattern]] · [[Romania_Classified_Information_Digital_Security_Reform]] · [[01 Cognitive System Map]] · [[04 Security Integrity Map]] · [[07 Knowledge Domains Map]]

## Core finding
Hallucination is mathematically inevitable for general-purpose LLMs (Xu et al. 2024, formal learning theory) but practically reducible by 50-96% through layered prompt/context engineering. No single technique is sufficient alone.

## Why models hallucinate
- **Incentive problem**: training/eval rewards guessing over admitting uncertainty (Kalai, Nachum, Vempala, Zhang, OpenAI 2025).
- **Sycophancy**: models agree with users even when wrong, in 58% of cases (SycEval 2025).
- **Confidence miscalibration**: GPT-4 assigns highest confidence to 87% of responses, including many wrong ones (Xiong et al., ICLR 2024).
- **Cascade hallucination**: one early error contaminates all downstream reasoning.
- **Lost in the Middle**: U-shaped attention curve — models best use info at start/end of context, degrade in the middle (Liu et al. 2024, TACL).

## Proven techniques (ranked by evidence strength)
1. **Chain-of-Verification (CoVe)** — Dhuliawala et al., ACL 2024 Findings. Generate draft -> list 3-5 verifiable claims -> answer each verification question in ISOLATION (not yes/no) -> revise. Reduces hallucinated entities from 2.95 to 0.68 on list-generation tasks.
2. **RAG grounding** — reduces hallucination 42-68% across benchmarks. Strongest template (Google Vertex AI): treat provided context as the absolute limit of truth; state explicitly when the answer is not in context.
3. **Explicit uncertainty permission** — a single clause like "only answer if you know with certainty" measurably shifts response distribution away from fabrication (Anthropic).
4. **Self-consistency via multi-sampling** — run 5-10 responses at temp 0.7-1.0, check convergence; far more reliable than self-reported confidence.
5. **Defense-in-depth** — production standard is 3 layers: rule-based pre-inference checks, AI-powered inference-time detection, post-inference output filtering. No production system relies on one layer.

## Structural rules for system prompts
- Place critical instructions at the very beginning AND end — never the middle (attention sinks make first/last tokens architecturally privileged).
- Use XML tags or Markdown headers for distinct sections (universal across Anthropic/OpenAI/Google/Perplexity/Cursor).
- Prompt formatting affects performance as much as content (up to 8.4% gains from joint content+format optimization, Microsoft Research 2025).

## Relevance to this vault's Cognitive Core
Directly grounds design choices already implemented: `ReflectionPipeline` mirrors CoVe's factored verification; `RecallEngine`'s multi-signal scoring mirrors the self-consistency principle; `Lifecycle.REVIEW` gating mirrors explicit-uncertainty-permission at the system level.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
