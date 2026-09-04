---
id: 93957bef-db59-5278-a5c5-94a29ded7b16
type: knowledge
lifecycle: REVIEW
category: llm/prompt_caching_kv_sharing
tags:
- llm-apps
- pai
- prompt-caching
- kv-cache
- prefix-sharing
- vllm
- radix-attention
- cost-optimization
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Pai-Designing-LLM-Apps-Ch7-Ch8
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/EXPERT_RAG_Speculative_Decoding_and_Prefix_Caching.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/PRODUCTION_RAG_Structured_Output_and_JSON_Constrained_Decoding.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# LLM Apps Scaling: Prompt Caching, KV Cache Sharing și Arbori de Prefix

**Sursă**: Suhas Pai, *Designing Large Language Model Applications* (Capitolele 7-8: Performance & Cost Optimization)
**Domeniu**: Optimizare de Latență și Cost, Reutilizarea Computațiilor, Serving Eficient

---

## 1. Problema Costului și Latenței

### 1.1 Anatomia unei Cereri LLM

```
Cerere = [System Prompt | Few-shot Examples | Context RAG | User Query]
          ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
          Prefix COMUN între cereri (60-90% din tokens)
```

**Observație critică**: System prompt-ul + exemplele sunt **identice** între cereri. Recalcularea KV cache-ului pentru acestea la fiecare cerere este irosire pură.

### 1.2 Impactul Financiar

| Scenariu | System Prompt | Context | User Query | Cost/cerere |
| :--- | :--- | :--- | :--- | :--- |
| Fără caching | 2000 tok (\$0.005) | 3000 tok (\$0.0075) | 500 tok (\$0.00125) | \$0.01375 |
| Cu prefix caching | ~~2000 tok~~ cache hit | 3000 tok (\$0.0075) | 500 tok (\$0.00125) | \$0.00875 |
| **Economie** | | | | **36% reducere** |

---

## 2. Prompt Caching (API-Level)

### 2.1 Mecanismul

```
Cerere 1: [System₁ | Context₁ | Query₁] → procesare completă → cache System₁ KV
Cerere 2: [System₁ | Context₂ | Query₂] → cache HIT pe System₁ → procesare doar Context₂ + Query₂
```

### 2.2 Implementare API (Anthropic/OpenAI)

```python
# Anthropic cache_control
messages = [
    {"role": "system", "content": [
        {"type": "text", "text": system_prompt, 
         "cache_control": {"type": "ephemeral"}}  # cacheable
    ]},
    {"role": "user", "content": user_query}  # not cached
]
```

### 2.3 Condiții pentru Cache Hit

| Condiție | Detaliu |
| :--- | :--- |
| **Prefix identic** | Primii N tokeni trebuie să fie identici byte-pentru-byte |
| **TTL** | Cache expiră după 5-60 minute (depinde de provider) |
| **Dimensiune minimă** | De obicei ≥1024 tokens pentru a justifica costul cache-ului |
| **Același model** | Cache-ul e specific modelului și versiunii |

---

## 3. KV Cache Sharing (Server-Level)

### 3.1 Ce Este KV Cache

La fiecare layer de atenție, modelul calculează:
- $K = W_K \cdot X$ (Key matrix)
- $V = W_V \cdot X$ (Value matrix)

Aceste matrice sunt stocate în **KV cache** pentru a evita recalcularea la token-generare incrementală.

### 3.2 Radix Attention (vLLM/SGLang)

```
Cereri concurente:
  Cerere A: [System | Context_A | Query_A]
  Cerere B: [System | Context_B | Query_B]

Radix Tree:
  [System] ← shared KV cache
      ├── [Context_A] ← branch A
      └── [Context_B] ← branch B
```

**Principiu**: Cereri cu **prefix comun** partajează aceleași KV cache entries folosind un **radix tree** (trie).

### 3.3 PagedAttention (vLLM)

Problema: KV cache pre-alocat irosește memorie (un request cu 2048 tokens alocă pentru 8192).

Soluția PagedAttention:
```
KV cache = pagini de dimensiune fixă (ex: 16 tokens/pagină)
Request A: [Pagina 1] [Pagina 2] [Pagina 3] ← alocate dinamic
Request B: [Pagina 1 SHARED] [Pagina 4] ← partajare prefix + pagini noi
```

**Rezultat**: 2-4x mai multe cereri concurente pe aceeași GPU.

---

## 4. Strategii de Optimizare la Nivel de Aplicație

### 4.1 Prompt Reuse Patterns

| Pattern | Descriere | Economie |
| :--- | :--- | :--- |
| **Static System Prompt** | System prompt identic pentru toate cererile | 30-50% |
| **Few-shot Template** | Exemple fixe + query variabil | 40-60% |
| **Context Prefix Sorting** | Grupează cererile cu context similar | 20-30% |
| **Prompt Deduplication** | Elimină duplicatele din batch | 10-40% |

### 4.2 Batch Processing cu Prefix Sharing

```python
# Sortare cereri pentru maximizarea prefix sharing
requests = sorted(requests, key=lambda r: r.system_prompt + r.context[:200])

# Batch-urile cu prefix comun beneficiază de KV cache sharing
for batch in group_by_prefix(requests, prefix_length=1024):
    results = llm.batch_generate(batch)  # prefix KV calculat o singură dată
```

### 4.3 Cache Warming

```
La startup:
  1. Trimite o cerere "dummy" cu system prompt-ul → populează cache-ul
  2. Cererile reale beneficiază imediat de cache hit
  3. Re-warm la fiecare TTL/2
```

---

## 5. Aplicabilitate în Memory Vault

- **Council System Prompt**: Prompt-ul de sistem al consiliului este stabil → candidat ideal pentru caching
- **Batch Memory Search**: Cererile de search cu aceleași instrucțiuni pot fi batch-uite cu prefix sharing
- **Skill Loading**: Skill-urile încărcate sunt prefix comun → cache-abile

---

## Referințe Obsidian

- [[EXPERT_RAG_Speculative_Decoding_and_Prefix_Caching]]
- [[PRODUCTION_RAG_Structured_Output_and_JSON_Constrained_Decoding]]
- [[CAPSTONE_Transformer_FlashAttention_Tiling_and_IO_Awareness]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
