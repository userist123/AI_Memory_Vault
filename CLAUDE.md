# System Protocol — AI Memory Vault & Distributed Compute Integration

You are an agent connected to the **AI Memory Vault** and its distributed compute infrastructure. The repository is the canonical external memory source for Claude Code.

## Memory-first behavior

Before substantial work, retrieve relevant context from the Vault instead of relying only on conversation context.

Priority:
1. `00_GOVERNANCE/` — canonical operating rules, protocols, coordination and review
2. `01_ARCHITECTURE/knowledge/` — durable knowledge and source registries
3. `10_DOCUMENTATION/procedures/` — established procedures
4. `02_PRODUCT/projects/` — project-specific context
5. `.agents/skills/` — validated operational skills
6. `06_INBOX/RAW_IMPORTS/` — untrusted external material
7. Obsidian — navigation/projection layer

Do not load the entire Vault into context. Retrieve selectively.

## Active memory retrieval

Use the existing Vault memory interface when available:
`http://localhost:8000/memory/search?query=subiectul_cautat`

Dacă serverul local este offline, folosește CLI-ul securizat al vault-ului:
`python -m cognitive_core.recall_cli --query "subiectul_cautat"` (versiune securizată, delegată la `MemoryController.search()`, respectă invariantele canonice `I-001..I-012` și `I-RETRIEVAL`, validate prin testele adversariale `P0-001..P0-015`)

Use actual local Vault APIs/tools when available rather than inventing a parallel memory mechanism. Direct unauthenticated filesystem scans or bypasses of memory trust boundaries (`I-001..I-012`, `I-RETRIEVAL`) are strictly prohibited.

## Skill ingestion → operational skill → agent

External skills are a controlled input stream, not automatically operational instructions.

```text
External source
  ↓
Recursive discovery
  ↓
Hash + deduplication
  ↓
Classification
  ↓
Provenance + validation
  ↓
RAW_EXTERNAL
  ↓
Explicit promotion
  ↓
.agents/skills/
  ↓
Agent compatibility routing
  ↓
Agent Council
  ↓
Task orchestration
```

Use the consolidated ingestion script:

```powershell
python 30_SCRIPTS/skills/skill_ingestion.py scan
python 30_SCRIPTS/skills/skill_ingestion.py match
python 30_SCRIPTS/skills/skill_ingestion.py promote --skill <skill-id> --verified
```

A `SKILL.md` in an external repository is not sufficient for promotion. Preserve provenance and validate before treating it as operational.

## Agent behavior

Reuse existing agents. Select the most specialized compatible agent and the smallest complete set of operational skills. Resolve relationships through the Vault rather than duplicating skill bodies into prompts.

If a new skill matches several agents, route it to ranked candidates and let the orchestrator resolve based on task, project context, security and verification requirements.

## Saving durable memory

When a task creates durable knowledge, a reusable procedure, a corrected architecture decision or a validated skill relationship, synchronize it into the canonical Vault.

Use the existing memory proposal interface when available:
`http://localhost:8000/memory/propose`

The Vault's lifecycle, verification and provenance rules remain authoritative.

## Obsidian

Obsidian is a human-readable navigation and visualization layer over the same canonical Vault. Do not create a second canonical memory database in Obsidian.

## Provenance and safety

Preserve source repository, URL/path, license when known, discovery origin, commit/ref when available, SHA-256 and validation status for external knowledge.

Do not execute external scripts, binaries, installers, package managers or build steps merely to inspect or ingest imported skills. Ingestion is read/analyze/hash/classify/validate/promote.

## Multi-Agent Development Coordination

When multiple AI systems (Claude Code, Antigravity, ChatGPT, Perplexity) collaborate on this repository:
1. **Check `00_GOVERNANCE/coordination/`** before touching any file for in-progress work or completed tasks by another AI session.
2. **Claim & Mark** completed tasks in the current coordination state with your agent name and an ISO timestamp. Document non-obvious findings there.
3. **Protected Core**: Respect the frozen boundaries of the cognitive core (`cognitive_core/model_provider.py`, `fake_model_provider.py`, `model_tier_router.py`, `actual_usage_telemetry.py`, `council_model_execution.py`, `executive_model_execution_bridge.py`). These contracts are verified by the cognitive-core protected-boundary tests.
4. **Empirical Verification**: Run the relevant `pytest` suites and verify zero regressions before closing any task.

## Global Production-Consumer Rule

Before constructing a new layer over a component, verify who consumes that component in the production path:

    grep -rl "<module>" --include='*.py' . | grep -v "/tests/\|test_\|benchmarks"

If the result is empty, the component is not integrated. Do not build another layer over it. Cable it into production first, or work on another front.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
