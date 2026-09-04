---
id: 3da82664-b867-5249-a239-cea0dacf8826
type: knowledge
lifecycle: REVIEW
category: agents/context_window_management
tags:
- agent-architecture
- zvarydchuk
- context-window
- conversation-memory
- summarization
- sliding-window
- token-budget
- multi-turn
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Zvarydchuk-Building-Agent-Powered-Apps-Ch8-Ch9
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/PRODUCTION_Agent_Tool_Grounding_and_Verification_Chains.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/MASTERY_Agent_Memory_Consolidation_and_Sleep.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# Agent Scaling: Managementul Ferestrei de Context și Memoria Multi-Turn

**Sursă**: Vasyl Zvarydchuk, *Building Agent-Powered Applications* (Capitolele 8-9: Memory & Context Management)
**Domeniu**: Buget de Tokeni, Sumarizare Progresivă, Strategii de Compresie a Contextului

---

## 1. Problema Ferestrei Finite

### 1.1 Constrângerea Fundamentală

| Model | Context Window | ~Pagini Text | Cost per 1M tokens |
| :--- | :--- | :--- | :--- |
| GPT-4o | 128K tokens | ~200 pagini | \$2.50 input |
| Claude 3.5 | 200K tokens | ~320 pagini | \$3.00 input |
| Gemini 1.5 | 1M+ tokens | ~1600 pagini | \$1.25 input |

Chiar cu 1M tokens, o conversație de agent pe zile/săptămâni **depășește** orice fereastră.

### 1.2 Degradarea Performanței

```
Tokens utilizați vs Acuratețe:

  100%  █████████████████████████████████
   90%  █████████████████████████████
   80%  ████████████████████████
   70%  ██████████████████
        ├────────┼────────┼────────┤
        0%      33%      66%     100%
              Utilizare Context
```

Studii arată că acuratețea scade **semnificativ** după ~60-70% umplere, cu efect "lost in the middle" (informația din mijloc e neglijată).

---

## 2. Strategii de Management

### 2.1 Sliding Window (FIFO)

```
Turn 1: [System | User₁ | Assist₁]
Turn 2: [System | User₁ | Assist₁ | User₂ | Assist₂]
Turn 3: [System | ~~~~~~ eliminat ~~~~~~ | User₂ | Assist₂ | User₃ | Assist₃]
```

**Avantaj**: Simplu, O(1) per turn
**Dezavantaj**: Pierde context critic din turnurile timpurii

### 2.2 Sumarizare Progresivă (Rolling Summary)

```
Fiecare N turnuri:
  summary_new = LLM.summarize(summary_old + last_N_turns)
  context = [System | summary_new | last_K_turns]
```

**Avantaj**: Reține informația esențială
**Dezavantaj**: Pierdere de detalii, cost LLM adițional, risc de drift semantic

### 2.3 Stratificare pe Importanță

```
Strat 1 (Permanent):  System prompt + instrucțiuni critice
Strat 2 (Sumarizat):  Rezumat progresiv al conversației
Strat 3 (Recent):     Ultimele K turnuri (verbatim)
Strat 4 (On-demand):  Retrieval din memorie externă (RAG)
```

Bugetul per strat:
- Strat 1: ~10-15% din fereastră
- Strat 2: ~15-20%
- Strat 3: ~40-50%
- Strat 4: ~15-25%

### 2.4 Token Budget Controller

```python
class TokenBudgetController:
    def __init__(self, max_tokens: int, reserved_output: int):
        self.max_input = max_tokens - reserved_output
        self.strata = {
            "system": 0.12,     # 12% system prompt
            "summary": 0.18,   # 18% rolling summary
            "recent": 0.45,    # 45% recent turns
            "retrieval": 0.20, # 20% RAG context
            "buffer": 0.05     # 5% safety margin
        }
    
    def allocate(self) -> dict:
        return {k: int(v * self.max_input) for k, v in self.strata.items()}
```

---

## 3. Memoria Externă (Long-Term Memory)

### 3.1 Arhitectura cu 3 Niveluri

```
┌──────────────────────────────────────────┐
│  Working Memory (Context Window)          │
│  ← fereastră limitată, acces rapid        │
├──────────────────────────────────────────┤
│  Short-Term Memory (Session Store)        │
│  ← sumarizări, key-value per sesiune     │
├──────────────────────────────────────────┤
│  Long-Term Memory (Vector DB / Vault)     │
│  ← knowledge base, embedding search      │
└──────────────────────────────────────────┘
```

### 3.2 Retrieval-Augmented Conversation

La fiecare turn:
1. **Extract** entitățile/intențiile din mesajul utilizatorului
2. **Search** memoria long-term cu query-ul extras
3. **Inject** rezultatele relevante în contextul curent (Strat 4)
4. **Generate** răspunsul cu contextul augmentat

### 3.3 Consolidare (din Tier 4)

Periodic, agentul **consolidează** turnurile conversației în memorie long-term:
- Extrage fapte, decizii, preferințe
- Le stochează cu metadate (timestamp, conversație, confidence)
- La reîntâlnire → retrieval din memoria consolidată

---

## 4. Compresia Contextului

### 4.1 Tehnici de Compresie

| Tehnica | Raport | Pierdere | Implementare |
| :--- | :--- | :--- | :--- |
| **Sumarizare LLM** | 5-10x | Moderată | Un apel LLM per sumarizare |
| **Eliminare mesaje** | 2-5x | Variabilă | Sliding window simplu |
| **Embedding compression** | 10-50x | Mare | Gist tokens (Mu et al.) |
| **Selective pruning** | 2-3x | Mică | Elimină exemple/formatare |

### 4.2 Gist Tokens (Compresie Neurală)

```
Context original (1000 tokens) → Encoder → 10 gist tokens → Prefixed la prompt
```

Avantaj: Compresie extremă menținând semantica
Dezavantaj: Necesită model specializat, nu funcționează cu API-uri standard

---

## 5. Aplicabilitate în Memory Vault

### 5.1 Council Context Budget (din `AGENTS.md`)

```
MAX_MEMORY_RESULTS = 5
MAX_GRAPH_EXPANSION = 1 hop
MAX_SPECIALIST_OUTPUT = 600 tokens
MAX_SYNTHESIS_INPUT = 2500 tokens
```

Aceste limite sunt exact un **Token Budget Controller** aplicat la nivel de council.

### 5.2 Principiul Prime Directive

> *"Better memory beats more memory. Better routing beats more agents."*

Echivalent: compresia inteligentă > fereastră mai mare.

---

## Referințe Obsidian

- [[PRODUCTION_Agent_Tool_Grounding_and_Verification_Chains]]
- [[MASTERY_Agent_Memory_Consolidation_and_Sleep]]
- [[CAPSTONE_Agent_Swarm_Blackboard_and_Dynamic_Orchestration]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
