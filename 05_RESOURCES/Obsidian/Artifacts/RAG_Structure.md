---
id: "7f2c65ef-300c-4c5d-91c4-faf285f5f499"
document_kind: specification
document_status: active
category: ai-architecture
provenance_status: incomplete
implementation_status: documentation_only
relations: []
---

# RAG Structure

## Overview

This document defines the **Retrieval-Augmented Generation (RAG)** architecture for the AI Vault, including query processing, retrieval strategies, context assembly, and response generation.

---

## RAG Pipeline Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      RAG Pipeline                           │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐    │
│  │   QUERY     │    │  RETRIEVE   │    │   AUGMENT   │    │
│  │  PROCESSING │ -> │   (SEARCH)  │ -> │  (CONTEXT)  │    │
│  └─────────────┘    └─────────────┘    └─────────────┘    │
│                           │                    │            │
│                           ▼                    ▼            │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐    │
│  │   STORE     │ <- │   GENERATE  │ <- │   PROMPT    │    │
│  │ (RESPONSE)  │    │  (ANSWER)   │    │  ASSEMBLY   │    │
│  └─────────────┘    └─────────────┘    └─────────────┘    │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

## 1. Query Processing

### 1.1 Query Analysis

**Input:** Natural language query from user or AI

**Process:**
1. **Intent Classification**
   - Factual (what is X?)
   - Procedural (how do I do X?)
   - Decision (should I do X?)
   - Memory (what did I learn about X?)
   - Creative (generate X based on Y)

2. **Entity Extraction**
   - Key concepts
   - Named entities
   - Temporal references
   - Action verbs

3. **Time Horizon Detection**
   - Current (now, today)
   - Historical (past, learned)
   - Future (planning, goals)

**Output:** Structured query object

```json
{
  "intent": "factual",
  "entities": ["RAG", "context", "retrieval"],
  "time_horizon": "current",
  "original_query": "How does RAG work?",
  "confidence": 0.92
}
```

---

### 1.2 Query Expansion

**Goal:** Improve recall by expanding query terms

**Techniques:**
- **Synonym expansion:** "RAG" → ["retrieval", "augmented generation", "search"]
- **Hyponym expansion:** "trading" → ["forex", "stocks", "crypto"]
- **Related concepts:** "memory" → ["learning", "knowledge", "experience"]
- **Tag mapping:** Use [[Tag_Taxonomy]] to find related tags

**Output:** Expanded query set

```json
{
  "original": "RAG retrieval",
  "expanded": [
    "RAG retrieval",
    "retrieval augmented generation",
    "semantic search context",
    "knowledge retrieval pipeline"
  ]
}
```

---

### 1.3 Query Routing

**Goal:** Route query to appropriate search strategy

**Routes:**

| Query Type | Search Strategy | Target Folders |
|------------|----------------|----------------|
| Factual | Full-text + semantic | 01_KNOWLEDGE, 05_RESOURCES |
| Procedural | Full-text + tag | 03_PROCEDURES |
| Decision | Tag + metadata | 04_MEMORY/Decisions |
| Memory | Date + tag + semantic | 04_MEMORY/Conversations |
| Project | Tag + status | 02_PROJECTS |
| Creative | Broad semantic | All folders |

---

## 2. Retrieval (Search)

### 2.1 Search Strategies

#### Strategy A: Full-Text Search

**When:** Factual queries, exact matches needed

**Process:**
1. Tokenize query
2. Search across note content
3. Score by TF-IDF
4. Return top-k results

**Configuration:**
```yaml
full_text_search:
  min_term_frequency: 2
  max_results: 50
  include_metadata: true
```

---

#### Strategy B: Semantic Search

**When:** Conceptual queries, synonym matching

**Process:**
1. Generate query embedding
2. Compare with note embeddings (cosine similarity)
3. Return top-k by similarity score

**Configuration:**
```yaml
semantic_search:
  model: all-MiniLM-L6-v2
  similarity_threshold: 0.3
  max_results: 30
```

---

#### Strategy C: Tag-Based Search

**When:** Categorical queries, filtered results

**Process:**
1. Extract tags from query or route
2. Match notes with those tags
3. Optionally filter by metadata (date, type, status)

**Configuration:**
```yaml
tag_search:
  exact_match: false
  include_hierarchical: true
  max_results: 40
```

---

#### Strategy D: Hybrid Search

**When:** Complex queries, multiple intents

**Process:**
1. Run multiple strategies in parallel
2. Normalize scores (0-1 range)
3. Weighted combination:
   - Full-text: 0.3
   - Semantic: 0.5
   - Tag: 0.2
4. Reciprocal Rank Fusion (RRF) for final ranking

**Configuration:**
```yaml
hybrid_search:
  strategies:
    - full_text: 0.3
    - semantic: 0.5
    - tag: 0.2
  rrf_k: 60
  max_results: 20
```

---

### 2.2 Retrieval Ranking

**Scoring Formula:**

```
final_score = (
  relevance_score * 0.5 +
  recency_boost * 0.2 +
  confidence_weight * 0.15 +
  link_authority * 0.15
)
```

**Components:**

| Component | Calculation | Range |
|-----------|-------------|-------|
| relevance_score | TF-IDF or cosine similarity | 0-1 |
| recency_boost | exp(-days_since_update / 180) | 0-1 |
| confidence_weight | {high: 1.0, medium: 0.7, low: 0.4} | 0.4-1.0 |
| link_authority | incoming_links / max_incoming_links | 0-1 |

---

### 2.3 Re-Ranking

**Goal:** Improve precision by re-ranking top-k results

**Process:**
1. Take top-k from initial retrieval (k=50)
2. Apply cross-encoder model for pairwise scoring
3. Re-rank by new scores
4. Select top-n for context (n=10-15)

**Configuration:**
```yaml
reranking:
  enabled: true
  model: cross-encoder/ms-marco-MiniLM-L-6-v2
  top_k_before: 50
  top_n_after: 10
```

---

## 3. Augmentation (Context Assembly)

### 3.1 Context Selection

**Input:** Ranked list of retrieved notes

**Process:**
1. **Deduplication:** Remove near-duplicate content
2. **Diversity:** Ensure coverage of different aspects
3. **Token Budget:** Select notes until context window limit

**Algorithm:**
```python
def select_context(notes, max_tokens=4000):
    context = []
    current_tokens = 0
    
    for note in notes:
        note_tokens = count_tokens(note.content)
        
        if current_tokens + note_tokens <= max_tokens:
            context.append(note)
            current_tokens += note_tokens
    
    return context
```

---

### 3.2 Context Formatting

**Format:**

```markdown
# Context for Query: "<original_query>"

## Note 1: [[Note_Title]]
**Type:** <type> | **Tags:** <tags> | **Date:** <created> | **Confidence:** <confidence>

<note_content>

---

## Note 2: [[Note_Title]]
...

---

[END OF CONTEXT]
Total notes: N | Total tokens: M
```

---

### 3.3 Metadata Injection

**Always Include:**
- Original query
- Number of retrieved notes
- Date range of notes
- Confidence distribution

**Example:**

```yaml
retrieval_metadata:
  query: "How does RAG work?"
  total_notes_retrieved: 12
  date_range: 2026-01-15 to 2026-08-09
  confidence: {high: 8, medium: 3, low: 1}
  folders: [01_KNOWLEDGE, 03_PROCEDURES]
```

---

## 4. Prompt Assembly

### 4.1 System Prompt

```markdown
You are an AI assistant with access to a personal knowledge vault.
Answer the user's query using the provided context.

Guidelines:
1. Use only information from the context
2. Cite sources as [[Note_Title]]
3. Mark uncertain information with ⚠️
4. If context is insufficient, say so
5. Structure answers with headers and lists
```

---

### 4.2 User Prompt Template

```markdown
## Context

<context_notes_formatted>

## Query

<user_query>

## Instructions

- Answer based on the context above
- Cite sources as [[Note_Title]]
- If information is missing, note it explicitly
- Structure your response clearly

## Response


```

---

### 4.3 Prompt Configuration

```yaml
prompt_config:
  system_prompt: "system_prompt_v1.txt"
  user_template: "user_template_v1.txt"
  max_context_tokens: 4000
  max_response_tokens: 2000
  temperature: 0.7
  top_p: 0.9
  include_citations: true
```

---

## 5. Generation (Response)

### 5.1 Response Generation

**Model:** LLM (e.g., GPT-4, Claude, local model)

**Parameters:**
```yaml
generation_config:
  model: gpt-4
  temperature: 0.7
  max_tokens: 2000
  top_p: 0.9
  frequency_penalty: 0.5
  presence_penalty: 0.5
```

---

### 5.2 Citation Enforcement

**Rule:** Every factual claim must cite a source

**Format:** `[[Note_Title]]`

**Validation:**
```python
def validate_citations(response, context_notes):
    citations = extract_citations(response)
    valid_notes = [note.title for note in context_notes]
    
    for citation in citations:
        if citation not in valid_notes:
            flag_invalid_citation(citation)
```

---

### 5.3 Uncertainty Marking

**When:**
- Context is incomplete
- Information is contradictory
- Confidence is low

**Format:**
- ⚠️ Uncertain: <statement>
- Context suggests X, but verification needed
- Multiple sources disagree: [[Note_A]] says X, [[Note_B]] says Y

---

## 6. Optional Response Logging (Future Runtime)

### 6.1 Response Metadata

**Do not log every response to canonical memory.** If a future runtime records operational logs, it must keep them outside canonical memory and must not promote them without the lifecycle and review rules in [[Memory Lifecycle]].

```yaml
response_log:
  id: UUID
  timestamp: ISO8601
  query: <original_query>
  retrieved_notes: [note_ids]
  response_tokens: N
  latency_ms: M
  model: <model_name>
  user_feedback: <rating or null>
```

---

### 6.2 Response Storage

**Location:** future operational log storage outside canonical memory folders; never `04_MEMORY/` or `06_INBOX/RAW_IMPORTS/`.

**Format:**

```markdown
---
type: conversation
date: YYYY-MM-DD
query: <original_query>
notes_used: [Note_1, Note_2, Note_3]
model: <model_name>
---

# Query

<original_query>

# Response

<generated_response>

---

**Retrieval Stats:**
- Notes retrieved: N
- Total context tokens: M
- Response tokens: K
- Latency: X ms
```

---

## 7. Performance Optimization

### 7.1 Caching

**Cache:**
- Query embeddings (avoid re-computation)
- Frequent queries (exact match)
- Note embeddings (update on change)

**Cache Invalidation:**
- Note content changes
- Periodic (every 24h)
- Manual (admin command)

---

### 7.2 Indexing

**Indexes:**
- Full-text index (inverted index)
- Embedding index (vector database)
- Tag index (hash map)
- Metadata index (structured DB)

---

### 7.3 Latency Targets

| Stage | Target | Current |
|-------|--------|---------|
| Query processing | <100ms | TBD |
| Retrieval | <500ms | TBD |
| Context assembly | <200ms | TBD |
| Generation | <3000ms | TBD |
| **Total** | **<4000ms** | TBD |

---

## 8. Quality Metrics

### 8.1 Retrieval Quality

| Metric | Target | Measurement |
|--------|--------|-------------|
| Recall@10 | >0.85 | Relevant notes in top 10 |
| Precision@10 | >0.70 | Relevant fraction in top 10 |
| MRR (Mean Reciprocal Rank) | >0.75 | Average inverse rank of first relevant |

---

### 8.2 Generation Quality

| Metric | Target | Measurement |
|--------|--------|-------------|
| Factual accuracy | >0.90 | Verified against context |
| Citation coverage | >0.95 | Claims with citations |
| User satisfaction | >0.80 | User ratings |

---

## 9. Error Handling

### 9.1 Retrieval Failures

| Error | Response |
|-------|----------|
| No results | "No relevant information found in vault" |
| Low confidence | "Found some information, but confidence is low" |
| Contradictory | "Sources disagree: [[A]] says X, [[B]] says Y" |

---

### 9.2 Generation Failures

| Error | Response |
|-------|----------|
| Context too long | "Query returned too much context; please narrow" |
| Model error | "Generation failed; retrying..." |
| Timeout | "Response timed out; query may be too complex" |

---

## 10. RAG Configuration Summary

```yaml
rag_config:
  query_processing:
    intent_classification: true
    entity_extraction: true
    query_expansion: true
    max_expansions: 5
    
  retrieval:
    strategies:
      - full_text: 0.3
      - semantic: 0.5
      - tag: 0.2
    max_results: 50
    rerank: true
    rerank_top_k: 10
    
  context:
    max_tokens: 4000
    include_metadata: true
    deduplicate: true
    diversity_sampling: true
    
  generation:
    model: gpt-4
    temperature: 0.7
    max_tokens: 2000
    cite_sources: true
    
  logging:
    store_responses: false
    log_retrieval_stats: false
    track_user_feedback: true
    
  performance:
    cache_embeddings: true
    cache_frequent_queries: true
    target_latency_ms: 4000
```

---

## Related Files

- [[System_Architecture]] — Overall system design
- [[03_PROCEDURES/RAG/Query_Formulation]] — How to write RAG queries (future)
- [[01_KNOWLEDGE/Concepts/RAG]] — RAG theory (future)
- [[01_KNOWLEDGE/Concepts/Knowledge_Graphs]] — Graph integration (future)

---

## Metadata

```yaml
---
type: knowledge
category: technical/ai
tags:
  - rag
  - retrieval
  - search
  - architecture
  - technical
created: 2026-08-09
updated: 2026-08-09
status: active
source: manual
confidence: high
---
```
