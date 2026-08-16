# CLAUDE.md — Operating Instructions for Claude Code

## 1. Persistent Memory & Vault Operating Contract
This repository is the persistent cognitive vault and memory system. Always respect the rules defined in `AGENTS.md` and `.agents/rules/vault_cognitive_rules.md`.

- **Security Invariants (P0-P15)**: AI agents only write memories with `verification: unverified`, `lifecycle: REVIEW`, and `provenance: ai` or `inference`.
- **Knowledge Hierarchy**: Official project docs > Test execution > AI inference. Never overwrite verified facts.

---

## 2. Distributed GPU Compute Delegation (Ollama on Colab / Kaggle / Local)

When you receive a heavy programming, quantitative math, memory synthesis, or refactoring task, you can offload it directly to the remote GPU nodes (Google Colab, Kaggle, or Local Ollama) using the built-in CLI dispatcher:

### Running Tasks on Remote GPUs:

```bash
# 1. Delegare automata catre cel mai rapid nod activ (Colab -> Kaggle -> Local):
python cognitive_core/dispatch_cli.py --node auto --role coder --prompt "<descrierea sarcinii de cod>"

# 2. Delegare explicita pe Nodul Kaggle (GPU 2x Tesla T4 - 32B Coder):
python cognitive_core/dispatch_cli.py --node kaggle --role coder --prompt "<sarcina grea de arhitectura / cod>"

# 3. Delegare explicita pe Nodul Google Colab (GPU Tesla T4 - 14B Coder):
python cognitive_core/dispatch_cli.py --node colab --role coder --prompt "<sarcina rapida de optimizare>"

# 4. Evaluare & Critica Reflexion (Critic Agent):
python cognitive_core/dispatch_cli.py --node auto --role critic --prompt "<codul sau ipoteza de verificat>"
```

---

## 3. Configuration & Node Status
All endpoints and models are managed dynamically in `compute_nodes.json`.

- `colab`: `https://extends-representing-humanity-arlington.trycloudflare.com` (14B Coder / GPU T4)
- `kaggle`: `https://oriented-walks-minds-required.trycloudflare.com` (32B Coder / 2x T4)
- `local`: `http://localhost:11434` (glm-4.7-flash)

When running tasks locally in Claude Code, invoke `python cognitive_core/dispatch_cli.py` via bash execution to delegate computation to the GPU servers.
