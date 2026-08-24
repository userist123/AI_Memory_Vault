---
name: memory-skill-ingest
description: Scan, validate, promote and route external skills into the operational skill corpus.
---

# /memory-skill-ingest

Use this command when the Vault receives new external skill sources.

## Workflow

1. Run discovery without executing imported code.
2. Generate SHA-256 and provenance-aware records.
3. Deduplicate by content hash and normalized skill identity.
4. Classify each candidate.
5. Review provenance/validation.
6. Promote explicitly verified candidates into `.agents/skills/`.
7. Generate agent compatibility routing.
8. Update the operational registry.
9. Report ambiguous/conflicting candidates instead of silently promoting them.

## CLI

```powershell
python scripts/skill_ingestion.py scan
python scripts/skill_ingestion.py match
python scripts/skill_ingestion.py promote --skill <skill-id> --verified
```

## Safety invariant

Never execute source repository code as part of ingestion. Ingestion is read/analyze/hash/classify/promote, not install/build/run.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
