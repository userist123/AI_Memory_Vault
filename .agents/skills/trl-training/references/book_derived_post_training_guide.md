# Book-Derived Post-Training Alignment & Adaptation Guide (TRL)

This guide bridges foundational literature (*Designing Large Language Model Applications* by Suhas Pai, *Building Agent-Powered Applications* by Vasyl Zvarydchuk, *Designing Machine Learning Systems* by Chip Huyen, *Learning Deep Learning* by Magnus Ekman, and *AIMA 4th Ed.* by Stuart Russell & Peter Norvig) with Hugging Face's Transformers Reinforcement Learning (TRL) execution suite.

---

## 1. The Adaptation Decision Tree (Pai Ch 5-8 & Zvarydchuk Ch 7)

Before allocating GPU compute to training or fine-tuning, evaluate the task along two primary axes: **Context Mutability** and **Capability vs. Knowledge Demand**.

```text
                                  [Task Requirement]
                                           |
                   +-----------------------+-----------------------+
                   |                                               |
         [Knowledge Retrieval]                           [Behavioral / Format / Style]
                   |                                               |
         Is domain data static?                                    |
          /                \                                       |
        (No)               (Yes)                                   |
         |                   |                                     |
    [Dynamic RAG]   Context window sufficient?                     |
   (Dense + Graph)    /             \                              |
                    (Yes)           (No)                           |
                      |               |                            |
               [Few-Shot Prompt]   [Domain SFT]                    |
                                      +----------------------------+
                                                                   |
                                                      Requires complex reasoning,
                                                      tool syntax, or safety alignment?
                                                                   |
                                                     +-------------+-------------+
                                                     |                           |
                                               [Format/Syntax]         [Preference / Policy]
                                                     |                           |
                                                 [TRL SFT]              Supervised pairs or
                                                (LoRA / QLoRA)          online feedback?
                                                                         /             \
                                                                    (Pairs)        (Online/Rule)
                                                                       |                 |
                                                                   [TRL DPO]         [TRL GRPO / RLOO]
```

### Decision Matrix

| Dimension | In-Context Prompting | Graph/Dense RAG | TRL SFT | TRL DPO / KTO | TRL GRPO / RLOO |
|---|---|---|---|---|---|
| **Primary Goal** | Task steering, 0-shot execution | Dynamic fact retrieval | Structural format, tool calling, style | Pairwise human preference alignment | Multi-step reasoning, mathematical verification |
| **Data Requirements** | 1–5 demonstrations | Chunked markdown / vector index | 1k–50k instruction-output pairs | 500–10k chosen/rejected pairs | Verifiable reward function (exact match, parser) |
| **Compute Overhead** | Inference-only | Embedding compute + indexing | Moderate (1–8 GPUs, LoRA/QLoRA) | Moderate (Reference model in VRAM) | Higher (Generation rollouts + policy updates) |
| **Catastrophic Forgetting** | Zero | Zero | High risk if data is uncurated | Low-to-Moderate (controlled by $\beta$) | Low (KL penalty or clipping) |
| **Literature Provenance** | Pai Ch 5 | Pai Ch 6-7, Zvarydchuk Ch 5 | Huyen Ch 6, Pai Ch 8 | Pai Ch 8, Russell-Norvig Ch 17 | Pai Ch 8, Ekman Ch 14 |

---

## 2. Mathematical Formulations of Loss Functions

### A. Supervised Fine-Tuning (SFT) Cross-Entropy Loss
Under standard causal language modeling on sequence $X = (x_1, \dots, x_N)$ where tokens $1 \dots K$ represent the prompt and $K+1 \dots N$ represent the target completion:

$$\mathcal{L}_{\text{SFT}}(\theta) = -\sum_{t=K+1}^{N} \log P_\theta(x_t \mid x_{<t})$$

Prompt token masking (`--packing` with attention masks or `DataCollatorForCompletionOnlyLM`) ensures gradients only backpropagate through completion tokens.

### B. Direct Preference Optimization (DPO)
DPO (Rafailov et al., 2023; Pai Ch 8) parameterizes the Bradley-Terry preference model directly through the language model policy $\pi_\theta$, eliminating the need for an explicit reward model network $r_\psi$:

$$\mathcal{L}_{\text{DPO}}(\theta; \pi_{\text{ref}}) = -\mathbb{E}_{(x, y_w, y_l) \sim \mathcal{D}}\left[\log \sigma\left(\beta \log \frac{\pi_\theta(y_w \mid x)}{\pi_{\text{ref}}(y_w \mid x)} - \beta \log \frac{\pi_\theta(y_l \mid x)}{\pi_{\text{ref}}(y_l \mid x)}\right)\right]$$

- $y_w$: Preferred (winning) completion.
- $y_l$: Dispreferred (losing) completion.
- $\pi_{\text{ref}}$: Frozen reference model (prevents policy drift).
- $\beta$: Temperature parameter (typically $0.05 \le \beta \le 0.2$), controlling divergence from the reference policy.

### C. Group Relative Policy Optimization (GRPO)
GRPO (Shao et al., 2024; deep reasoning alignment without a critic) samples a group of $G$ outputs $\{y_1, y_2, \dots, y_G\}$ for prompt $x$ from policy $\pi_{\theta_{\text{old}}}$ and normalizes rewards within the group:

$$\tilde{A}_i = \frac{r(x, y_i) - \text{mean}(\{r(x, y_j)\}_{j=1}^G)}{\text{std}(\{r(x, y_j)\}_{j=1}^G) + \epsilon}$$

The objective optimizes the clipped surrogate reward with a per-token KL divergence penalty against $\pi_{\text{ref}}$:

$$\mathcal{L}_{\text{GRPO}}(\theta) = -\frac{1}{G}\sum_{i=1}^{G}\left[\min\left(\frac{\pi_\theta(y_i \mid x)}{\pi_{\theta_{\text{old}}}(y_i \mid x)}\tilde{A}_i, \text{clip}\left(\frac{\pi_\theta(y_i \mid x)}{\pi_{\theta_{\text{old}}}(y_i \mid x)}, 1-\epsilon, 1+\epsilon\right)\tilde{A}_i\right) - \beta D_{\text{KL}}(\pi_\theta \parallel \pi_{\text{ref}})\right]$$

---

## 3. Production ML Data Flywheel & Hygiene (Chip Huyen Ch 3, 4, 8)

1. **Feedback Loop & Label Hygiene**:
   - Collect real failure cases and low-confidence inference traces.
   - Filter human and AI annotations for formatting anomalies, boilerplate leakage, and hallucinated function calls.
   - Demarcate synthetic training data with strict provenance metadata (`source_type: ai`, `verification: unverified`).
2. **LoRA Hyperparameter Dynamics (Ekman Ch 14)**:
   - Rank $r$: 16 to 64 balances representation capacity and parameter efficiency.
   - Scaling $\alpha$: Typically set to $2 \times r$ (e.g., $r=32, \alpha=64$) or $\alpha = r$.
   - Target Modules: Adapt all linear projection layers (`q_proj, k_proj, v_proj, o_proj, gate_proj, up_proj, down_proj`) to avoid representation bottlenecks.
3. **Preventing Catastrophic Forgetting**:
   - Mix 5% to 15% general instruction data (e.g., SlimOrca or OpenAssistant) into specialized domain training datasets.
   - Keep learning rates strictly controlled: $\le 2 \times 10^{-5}$ for full fine-tuning, $\le 2 \times 10^{-4}$ for LoRA.

---

## 4. TRL Command Recipes with Book Principles

### SFT with Masked Loss & LoRA (Pai & Huyen)
```bash
trl sft \
  --model_name_or_path Qwen/Qwen2.5-Coder-7B-Instruct \
  --dataset_name custom_vault_instructions \
  --learning_rate 2.0e-4 \
  --num_train_epochs 2 \
  --packing \
  --per_device_train_batch_size 4 \
  --gradient_accumulation_steps 4 \
  --use_peft \
  --lora_r 32 \
  --lora_alpha 64 \
  --lora_target_modules all-linear \
  --output_dir ./models/qwen-vault-sft
```

### DPO Alignment for Safe Agentic Tool Calling (Zvarydchuk & Russell-Norvig)
```bash
trl dpo \
  --model_name_or_path ./models/qwen-vault-sft \
  --dataset_name tool_calling_preferences \
  --learning_rate 5.0e-7 \
  --beta 0.1 \
  --per_device_train_batch_size 2 \
  --gradient_accumulation_steps 8 \
  --max_steps 500 \
  --output_dir ./models/qwen-vault-dpo
```

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[01_KNOWLEDGE/BOOKS/Production_ML_Systems_and_Continual_Learning]]
- [[01_KNOWLEDGE/BOOKS/LLM_Application_Design_and_RAG_Pipelines]]
- [[01_KNOWLEDGE/BOOKS/Agent_Architecture_and_Tool_Orchestration]]
- [[Knowledge Graph Home]]
