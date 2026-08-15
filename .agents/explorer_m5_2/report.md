# Milestone 5 Evaluation Report: TRACe Metrics & IR Ranking Benchmarks

## Executive Summary
This report delivers an exhaustive, read-only architectural investigation and mathematical validation of the evaluation engine located in `cognitive_core/evaluation.py` and its accompanying test suites. The evaluation system provides quantitative quality assessment across two core paradigms:
1. **TRACe Cognitive Framework**: Utilization, Relevance, Adherence, and Completeness.
2. **Standard Information Retrieval (IR) Ranking Benchmarks**: Precision@K, Recall@K, Reciprocal Rank (RR), Mean Reciprocal Rank (MRR), and Normalized Discounted Cumulative Gain (NDCG@K).

All 399 unit, integration, and adversarial tests in the repository pass with **0 failures** (`399 passed in 44.99s`), including all evaluation and recall lineage tests in `cognitive_core/tests/test_evaluation_and_recall_lineage.py`.

---

## 1. Architectural Overview & Component Map

| Component / Function | Location | Role / Metric | Mathematical Formulation / Invariant |
|---|---|---|---|
| `RetrievalEvaluator` | `cognitive_core/evaluation.py:5-118` | Master evaluation class | Integrates TRACe + IR ranking benchmarks |
| `utilization` | `cognitive_core/evaluation.py:15-30` | TRACe: Utilization | $\text{Min}(1.0, \frac{|\{note \mid \text{matched\_keywords} / |\text{keywords}| \ge 0.2\}|}{|\text{retrieved\_notes}|})$ |
| `relevance` | `cognitive_core/evaluation.py:32-40` | TRACe: Relevance | $\frac{1}{N} \sum_{i=1}^{N} \text{Sim}(query, note_i)$ |
| `adherence` | `cognitive_core/evaluation.py:42-50` | TRACe: Adherence | $\text{Sim}(response, \bigoplus_{i=1}^N note_i)$ (Defaults to $1.0$ if no provider) |
| `completeness` | `cognitive_core/evaluation.py:52-58` | TRACe: Completeness | $\frac{|\{gid \in \text{gold\_ids} \mid gid \in \text{retrieved\_ids}\}|}{|\text{gold\_ids}|}$ ($1.0$ if gold is empty) |
| `precision_at_k` | `cognitive_core/evaluation.py:62-71` | IR: Precision@K | $\frac{|\text{top\_k} \cap \text{relevant\_ids}|}{|\text{top\_k}|}$ ($0.0$ if $k \le 0$ or empty) |
| `recall_at_k` | `cognitive_core/evaluation.py:73-80` | IR: Recall@K | $\frac{|\text{top\_k} \cap \text{relevant\_ids}|}{|\text{relevant\_ids}|}$ ($1.0$ if relevant is empty) |
| `reciprocal_rank` | `cognitive_core/evaluation.py:82-88` | IR: Reciprocal Rank | $\frac{1}{\text{rank}_1}$ for first relevant doc, else $0.0$ |
| `mean_reciprocal_rank`| `cognitive_core/evaluation.py:90-96` | IR: Mean Reciprocal Rank | $\frac{1}{|Q|} \sum_{q \in Q} \text{RR}(q)$ |
| `ndcg_at_k` | `cognitive_core/evaluation.py:98-117`| IR: NDCG@K | $\frac{\text{DCG@K}}{\text{IDCG@K}} = \frac{\sum_{i=1}^k \frac{rel_i}{\log_2(i+1)}}{\sum_{j=1}^k \frac{rel_j^*}{\log_2(j+1)}}$ ($0.0$ if $\text{IDCG} = 0$) |

---

## 2. In-Depth Analysis of TRACe Metrics

### 2.1 Utilization (`utilization`)
- **Objective**: Evaluates whether retrieved context was genuinely incorporated into the generated agent response rather than ignored (hallucination / context discarding).
- **Implementation**:
  ```python
  def utilization(self, retrieved_notes: List[Dict[str, Any]], generated_response: str) -> float:
      if not retrieved_notes or not generated_response:
          return 0.0
      used_count = 0
      resp_lower = generated_response.lower()
      for note in retrieved_notes:
          content = note.get("content", "").lower()
          keywords = [w for w in content.split() if len(w) > 4]
          if not keywords:
              continue
          matched_keywords = sum(1 for kw in keywords if kw in resp_lower)
          if matched_keywords / len(keywords) >= 0.2:
              used_count += 1
      return min(1.0, used_count / len(retrieved_notes))
  ```
- **Behavioral Properties**:
  - Null & empty safety: Returns `0.0` when `retrieved_notes` or `generated_response` is falsy.
  - Thresholding: Requires $\ge 20\%$ keyword overlap per note to count as "utilized".
  - Normalization: Bounded in $[0.0, 1.0]$ via `min(1.0, used_count / len(retrieved_notes))`.
- **Edge Cases & Findings**:
  - *Short Keyword Suppression*: If a note contains only concise technical terms with length $\le 4$ (e.g., `"SQL DB API key"`, `"Git CI/CD"`), `keywords` is empty, causing the note to be skipped and evaluated as unused (`0.0`).
  - *Naive Substring Matching*: `kw in resp_lower` performs substring matching on unstemmed, unstripped words from `.split()`. Attached punctuation (e.g. `"pool,"`) may prevent matching.

### 2.2 Relevance (`relevance`)
- **Objective**: Measures semantic alignment between the user/agent query and the retrieved memory notes.
- **Implementation**:
  ```python
  def relevance(self, retrieved_notes: List[Dict[str, Any]], query: str) -> float:
      if not retrieved_notes or not query or not self.semantic_provider:
          return 0.0
      scores = [
          self.semantic_provider.compute_similarity(query, note.get("content", ""))
          for note in retrieved_notes
      ]
      return sum(scores) / len(scores) if scores else 0.0
  ```
- **Behavioral Properties**:
  - Safeguarded against missing `semantic_provider`, empty queries, or empty candidate lists.
  - Correct arithmetic mean over note similarity scores.
  - Division by zero is impossible due to early return guards.

### 2.3 Adherence (`adherence`)
- **Objective**: Verifies factual grounding of the response against retrieved source material.
- **Implementation**:
  ```python
  def adherence(self, generated_response: str, retrieved_notes: List[Dict[str, Any]]) -> float:
      if not generated_response or not retrieved_notes:
          return 0.0
      if not self.semantic_provider:
          return 1.0
      combined_sources = " ".join([n.get("content", "") for n in retrieved_notes])
      return self.semantic_provider.compute_similarity(generated_response, combined_sources)
  ```
- **Behavioral Properties**:
  - If `semantic_provider` is omitted, defaults to `1.0` (optimistic fallback).
  - Evaluates similarity between the response and the aggregated text of all retrieved notes.

### 2.4 Completeness (`completeness`)
- **Objective**: Measures the recall fraction of required gold standard reference documents.
- **Implementation**:
  ```python
  def completeness(self, retrieved_notes: List[Dict[str, Any]], gold_reference_ids: List[str]) -> float:
      if not gold_reference_ids:
          return 1.0
      retrieved_ids = {n.get("id") for n in retrieved_notes if n.get("id")}
      matched = sum(1 for gid in gold_reference_ids if gid in retrieved_ids)
      return matched / len(gold_reference_ids)
  ```
- **Behavioral Properties**:
  - Vacuous truth handling: When `gold_reference_ids` is empty, returns `1.0` (100% complete).
  - Robust to notes missing `"id"` key.
  - Zero division protected.

---

## 3. In-Depth Analysis of IR Ranking Benchmarks

### 3.1 Precision@K (`precision_at_k`)
- Slices top $K$: `top_k = retrieved_ids[:k]`.
- Guarded for $K \le 0$ (returns `0.0`) and empty lists (returns `0.0`).
- Divides by `len(top_k)`: If $K > \text{len}(retrieved\_ids)$, precision is evaluated over the returned slice (e.g. 1 relevant doc out of 1 returned doc with $K=5$ yields $1.0$).

### 3.2 Recall@K (`recall_at_k`)
- Slices top $K$: `top_k = retrieved_ids[:k]`.
- If `relevant_ids` is empty, returns `1.0` (vacuously complete recall).
- Divides by `len(relevant_ids)`. If $K \le 0$, returns `0.0`.
- Upper bound strictly $1.0$, zero division guarded.

### 3.3 Reciprocal Rank & Mean Reciprocal Rank (`reciprocal_rank`, `mean_reciprocal_rank`)
- RR uses 1-indexed rank `enumerate(retrieved_ids, start=1)`. First match returns $1.0 / rank$. If no match, returns $0.0$.
- MRR averages RR across queries. Safeguards against empty rankings, empty relevant sets, or mismatched list lengths (`len(rankings) != len(relevant_sets)` returns `0.0`).

### 3.4 Normalized Discounted Cumulative Gain (`ndcg_at_k`)
- Discount factor: $\log_2(i + 1)$. At rank $i=1$, $\log_2(2) = 1.0$.
- IDCG computes ideal DCG by sorting all relevance scores descending and summing top $K$.
- Division by zero protection: `if idcg == 0.0: return 0.0`.
- Perfect ranking produces $1.0$; inverted rankings produce mathematically correct discounted scores.

---

## 4. Empirical Test Results & Verification

### Test Suite Execution
- **Target File**: `cognitive_core/tests/test_evaluation_and_recall_lineage.py`
  - `test_trace_metrics_computation`: **PASSED**
  - `test_ir_metrics_precision_recall_mrr_ndcg`: **PASSED**
  - `test_recall_inherits_score_from_superseded_node`: **PASSED**
- **Full Repository Test Suite**:
  - Total tests collected: **399**
  - Total tests passed: **399** (100% pass rate across 37 test modules)
  - Total failures / errors: **0**
  - Duration: **44.99 seconds**

### Edge Case Empirical Probing
A comprehensive verification matrix was executed against boundary conditions:
- $K \le 0$ for Precision@K, Recall@K, NDCG@K $\rightarrow$ all correctly output $0.0$.
- Empty relevance dictionary or all-zero relevance in NDCG@K $\rightarrow$ correctly outputs $0.0$ with zero division avoided.
- Vacuous gold sets in Completeness $\rightarrow$ correctly outputs $1.0$.
- Single item rankings with $K > 1$ in Precision@K $\rightarrow$ outputs $1.0$ without out-of-bounds indexing.
- Superseded node lineage with 10% freshness bonus $\rightarrow$ verified active successor node correctly inherits unpenalized score $\times 1.1$.

---

## 5. Identified Edge Cases & Concrete Improvement Proposals

1. **Tokenization in `utilization()`**:
   - *Current*: `[w for w in content.split() if len(w) > 4]`
   - *Observation*: Ignores 3-4 letter acronyms (e.g. `SQL`, `WAL`, `API`, `Git`) and retains punctuation.
   - *Recommendation*: Use `re.findall(r'\b[a-zA-Z0-9_-]{3,}\b', content.lower())` to extract tokens with length $\ge 3$.

2. **Precision@K Fixed Cutoff Option**:
   - *Current*: `relevant_count / len(top_k)`
   - *Observation*: When fewer than $K$ items are returned, precision is computed over the returned subset.
   - *Recommendation*: Introduce an optional parameter `strict_cutoff: bool = False` allowing standard TREC benchmark normalization (`relevant_count / k`).

3. **Multi-Document Adherence Fidelity**:
   - *Current*: Concatenates all retrieved notes into `combined_sources`.
   - *Observation*: With Jaccard semantic similarity, large combined source strings penalize responses due to large set union.
   - *Recommendation*: Support per-note maximum alignment or sentence-level citation verification.
