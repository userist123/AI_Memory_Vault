# Module 9: Vault & TRL Post-Training CLIs

## Key Commands

### 1. Memory Vault Retrieval & Observability CLI

The unified secure retrieval policy (`I-RETRIEVAL`) mandates that offline memory queries must pass through `cognitive_core.recall_cli`, and diagnostic inspection passes through `cognitive_core.observability.trace_cli`.

| Command | Purpose |
|---|---|
| `python -m cognitive_core.recall_cli --query "text"` | Execute secure memory retrieval through `MemoryController.search()` |
| `python -m cognitive_core.recall_cli --query "text" --role verifier` | Query memory scoped strictly under Verifier agent privilege |
| `python -m cognitive_core.recall_cli --query "text" --format json` | Retrieve results formatted as structured JSON for automation pipelines |
| `python -m cognitive_core.observability.trace_cli --query "text"` | Trace complete 14-stage retrieval lifecycle without modifying state |
| `python -m cognitive_core.observability.trace_cli --query "text" --ab-activation` | Measure rank shifts and Kendall's tau between baseline and graph activation |
| `python -m cognitive_core.observability.trace_cli --outcomes` | Scan execution logs and categorize utility into 4 tiers |

### 2. TRL Post-Training Alignment CLI

TRL commands provide state-of-the-art model adaptation and reinforcement learning alignment from the terminal.

| Command | Purpose |
|---|---|
| `trl sft --model_name_or_path <M> --dataset_name <D> --use_peft` | Supervised Fine-Tuning with LoRA parameter-efficient adaptation |
| `trl dpo --model_name_or_path <M> --dataset_name <D> --beta 0.1` | Direct Preference Optimization using paired chosen/rejected datasets |
| `trl grpo --model_name_or_path <M> --dataset_name <D>` | Group Relative Policy Optimization (reasoning alignment without critic) |
| `accelerate launch --config_file <C> -m trl sft ...` | Multi-GPU distributed training with gradient accumulation and ZeRO |

---

## Security Boundaries & Rules

1. **`I-RETRIEVAL` Invariant**: Never execute raw `os.walk` or unauthenticated filesystem scans to bypass memory trust boundaries. Always invoke `python -m cognitive_core.recall_cli`.
2. **`I-001` Invariant**: AI agents cannot self-verify memories (`verification = "verified"` is gated to humans).
3. **Data Isolation**: Retrieved context must be demarcated with XML tags (`<untrusted_memory>`) when feeding models to prevent prompt injection.

---

## Practice Quiz

1. **Question**: Which CLI command is the authorized offline fallback for searching the AI Memory Vault?
   - **A)** `grep -rnw "query" 01_KNOWLEDGE/`
   - **B)** `python -m cognitive_core.recall_cli --query "query"`
   - **C)** `python -m memory_controller.raw_scan --search "query"`
   - **D)** `find . -name "*.md" | xargs grep "query"`
   - **Answer**: **B** (+15 XP). Direct unauthenticated grep/find scans violate `I-RETRIEVAL`.

2. **Question**: When fine-tuning a 7B parameter model on a single 24GB GPU, which TRL flag enables parameter-efficient adapters?
   - **A)** `--full_precision`
   - **B)** `--zero_stage 3`
   - **C)** `--use_peft`
   - **D)** `--gradient_checkpointing_off`
   - **Answer**: **C** (+15 XP). `--use_peft` attaches LoRA adapters, drastically cutting VRAM requirements.

3. **Question**: In DPO training, what parameter controls the divergence penalty from the reference model?
   - **A)** `--learning_rate`
   - **B)** `--beta`
   - **C)** `--lora_alpha`
   - **D)** `--clip_range`
   - **Answer**: **B** (+15 XP). $\beta$ regulates the KL penalty implicit in the Bradley-Terry preference loss.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[01_KNOWLEDGE/BOOKS/LLM_Application_Design_and_RAG_Pipelines]]
- [[01_KNOWLEDGE/BOOKS/Production_ML_Systems_and_Continual_Learning]]
- [[Master_Skills_Catalog_251]]
- [[Knowledge Graph Home]]
