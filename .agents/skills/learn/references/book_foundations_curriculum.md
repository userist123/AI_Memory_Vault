# Foundation Books Curriculum: 6 Pillars of AI & Data Engineering

This diagnostic and interactive curriculum grounds learners in the six core engineering and cognitive pillars of the AI Memory Vault system.

---

## Pillar 1: Distributed Storage & Reliability (Kleppmann, *DDIA*)

### Objectives
- Master storage engine architectures: LSM-Trees (SSTables) vs. B-Trees.
- Understand Write-Ahead Logging (WAL) and atomic transactions (`BEGIN IMMEDIATE`, SQLite isolation).
- Differentiate consistency models: Linearizability vs. Eventual Consistency, Read-After-Write consistency.
- Handle failure modes: Split-brain, network partitions (CAP theorem), and clock skew.

### Diagnostic Check
> **Question**: Why does an LSM-tree typically outperform a B-tree on write-heavy workloads, and what tradeoff is incurred?  
> **Explanation**: LSM-trees convert random disk writes into sequential append-only log operations in memory (memtable) and flush to immutable SSTables on disk. The tradeoff is higher read amplification and periodic compaction overhead (write amplification).

---

## Pillar 2: Rational Agents & Systematic Search (Russell & Norvig, *AIMA 4e*)

### Objectives
- Formulate agent environments using the PEAS model (Performance, Environment, Actuators, Sensors).
- Implement informed heuristic search: $A^*$ search with admissible and consistent heuristics ($f(n) = g(n) + h(n)$).
- Apply Markov Decision Processes (MDPs): Bellman equation, Value Iteration, and Policy Iteration.
- Enforce bounded associative expansion in knowledge graphs to prevent combinatorial state explosions.

### Diagnostic Check
> **Question**: What occurs when a search heuristic $h(n)$ overestimates the true cost to reach the goal in $A^*$ graph search?  
> **Explanation**: The heuristic becomes inadmissible, meaning $A^*$ is no longer guaranteed to find the optimal (lowest-cost) path, as it may prematurely terminate along a suboptimal branch.

---

## Pillar 3: Agent Architecture & Tool Orchestration (Zvarydchuk, *Building Agent-Powered Applications*)

### Objectives
- Implement the ReAct (Reasoning + Acting) cycle with explicit observation boundaries.
- Design least-privilege multi-agent councils: Router, Retrieval, Verifier, Consolidator, and Critic.
- Enforce tool call verification and error recovery (retry, replan, human-in-the-loop escalation).
- Implement state checkpointing (`wm.json`, `plan.json`) with atomic filesystem writes.

### Diagnostic Check
> **Question**: Why should an autonomous AI agent never be granted root or unbounded file-modification tools without human attestation?  
> **Explanation**: Unbounded tools create privilege escalation vectors where untrusted retrieved data (indirect prompt injections) can commandeer the reasoning loop and cause irreversible data corruption.

---

## Pillar 4: LLM Application Design & RAG Pipelines (Pai, *Designing LLM Applications*)

### Objectives
- Build production RAG pipelines: Semantic chunking, embedding generation, dense vs. sparse retrieval.
- Implement bi-temporal knowledge graph retrieval and multi-hop link traversal.
- Guard against prompt injections using strict XML demarcation boundaries (`<untrusted_memory>`).
- Evaluate retrieval quality using IR metrics: Precision@K, Recall@K, MRR (Mean Reciprocal Rank), and NDCG.

### Diagnostic Check
> **Question**: How does XML data demarcation (`<untrusted_memory>...</untrusted_memory>`) protect an LLM from indirect prompt injection?  
> **Explanation**: It creates an explicit syntactic boundary in the prompt token stream, instructing the model to treat the enclosed tokens strictly as passive reference data rather than executable instructions.

---

## Pillar 5: Production ML Systems & Continual Learning (Huyen, *Designing ML Systems*)

### Objectives
- Architect the ML data flywheel: Collecting telemetry, human corrections, and edge cases.
- Distinguish types of drift: Concept drift ($P(Y \mid X)$ changes) vs. Covariate shift ($P(X)$ changes).
- Formulate evaluation strategies: Offline held-out benchmark splits vs. online A/B testing and shadow deployments.
- Prevent training-serving skew through idempotent feature transformations and data schema contracts.

### Diagnostic Check
> **Question**: If a model's input feature distributions change while the underlying relationship between inputs and outputs remains constant, which type of drift has occurred?  
> **Explanation**: Covariate shift (change in $P(X)$ while $P(Y \mid X)$ remains invariant).

---

## Pillar 6: Deep Learning Representations & Attention (Ekman, *Learning Deep Learning*)

### Objectives
- Mathematical mechanics of Scaled Dot-Product Attention:
  $$\text{Attention}(Q, K, V) = \text{softmax}\left(\frac{QK^T}{\sqrt{d_k}}\right)V$$
- Gradient dynamics, backpropagation through time, and residual connections ($x + \text{Sublayer}(x)$).
- Parameter-efficient fine-tuning (PEFT): Low-Rank Adaptation (LoRA) matrix decomposition:
  $$W = W_0 + \Delta W = W_0 + \frac{\alpha}{r}BA$$
- Optimization algorithms: AdamW, cosine learning rate schedules with warmup, and gradient clipping.

### Diagnostic Check
> **Question**: Why is the scaling factor $\frac{1}{\sqrt{d_k}}$ essential in scaled dot-product attention?  
> **Explanation**: As the key dimension $d_k$ grows large, the dot products grow large in magnitude, pushing the softmax function into regions with tiny gradients (gradient saturation). Scaling by $\sqrt{d_k}$ preserves variance around 1 and ensures healthy gradient flow.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[01_KNOWLEDGE/BOOKS/DDIA_Distributed_Storage_Reliability]]
- [[01_KNOWLEDGE/BOOKS/AIMA_Rational_Agents_and_Search]]
- [[01_KNOWLEDGE/BOOKS/Agent_Architecture_and_Tool_Orchestration]]
- [[01_KNOWLEDGE/BOOKS/LLM_Application_Design_and_RAG_Pipelines]]
- [[01_KNOWLEDGE/BOOKS/Production_ML_Systems_and_Continual_Learning]]
- [[01_KNOWLEDGE/BOOKS/Deep_Learning_Representations_and_Attention]]
- [[Knowledge Graph Home]]
