---
type: core
category: memory
status: active
version: 1.0.0
id: "54b48919-d58a-4502-a20f-2717b022d375"
document_kind: policy
document_status: active
provenance_status: incomplete
relations: []
policy_scope: vault-governance
---

# Memory Protocol

## Memory Classes

| Type | Meaning |
|---|---|
| knowledge | fapt / concept reutilizabil |
| project | stare si context de proiect |
| procedure | pasi verificati |
| decision | alegere si rationale |
| experience | eveniment sau experienta |
| error | esec analizat |
| lesson | regula invatata din experienta |
| preference | preferinta stabila |
| resource | sursa externa |
| hypothesis | idee neconfirmata |

## Write Rules

Create a new note when the information is:

- reusable;
- distinct;
- stable enough;
- relevant to future work.

Update an existing note when:

- the same concept exists;
- the new information improves accuracy;
- the old version should remain as history.

Do not store when:

- it is trivial;
- it is duplicated;
- it is purely conversational noise;
- it contains secrets;
- it is obsolete without historical value.

## Memory Lifecycle

```text
RAW -> CLASSIFIED -> NORMALIZED -> REVIEW -> VERIFIED -> ACTIVE -> SUPERSEDED/ARCHIVED
```

`RAW` is permanent source evidence in `06_INBOX/RAW_IMPORTS/`. Only a derivative can be classified, normalized, reviewed, verified, and promoted. Raw evidence is never rewritten, deleted, or indexed as canonical memory.

## Provenance

Every imported memory should retain, when possible:

- source platform;
- source conversation;
- source date;
- extraction date;
- confidence;
- verification state.

Use the canonical schema in [[Canonical Frontmatter]]. Any normalized or redacted derivative must reference its original raw path. Promotion to `ACTIVE` follows [[Promotion and Human Review]].

## Technology and Version Handling

To maintain memory integrity, the Vault employs technology-aware deduplication and version-aware recall:
- **Technology and Version Metadata**: Notes can include metadata fields `version_range` (specifying version scope, e.g., `"Python 3.12"`, `"PowerShell 7.x"`) and `applies_to` (specifying target technology/product).
- **Deduplication Identity**: Duplicate memory detection requires content similarity above a configured threshold (default `0.85`), matching technology/product identity, matching version ranges, and matching provenance source types.
- **Differentiation**: Memories targeting different technology versions or from different source tiers (e.g. user-sourced vs. AI-inferred) are kept separate. Unknown versions must never cause deduplication overlap.
- **Version-Aware Recall**: Queries containing specific technology versions (e.g., "Python 3.12") boost matching memories (+0.3 confidence score), penalize mismatched versions (-0.3 confidence score), and treat notes lacking version ranges as neutral.
- **Runtime Authority Score**: The system derives an `authority_score` at runtime based on `provenance.source_type` (e.g., `official` has higher authority than `ai`). This score is combined with the note's confidence to rank results during recall, and is **never** persisted in canonical frontmatter metadata.
- **Temporal Validity (`valid_from` / `valid_until`)**: Notes can define `valid_from` (start date of validity) and `valid_until` (expiration date). Notes not yet valid (future `valid_from`) or expired (past `valid_until`) are penalized during recall, but remain retrievable via historical queries.

## Supersession Invariants and Enforcement

Establishing a relationship where a new memory replaces an old one must follow the explicit supersession protocol:
1. **Explicit Request**: An explicit operation request (`old_id`, `new_id`, `evidence`) must be initiated. Lifecycle transitions do not implicitly create supersession links.
2. **Invariants**:
   - Both predecessor and successor memories must exist.
   - Self-supersession is prohibited (`old_id != new_id`).
   - Cyclic supersession paths are prevented.
   - Reciprocal links are automatically updated and kept consistent (`new.supersedes = old`, `old.superseded_by = new`, with matching relation items `replaces` and `replaced_by` in their respective `relations` list).
   - The predecessor note's content, UUID, provenance, and extraction date must remain unchanged.
   - Human-verified memories cannot be automatically superseded by an AI agent.
   - Superseded memory is kept as historical record (lifecycle set to `SUPERSEDED`) and is never physically deleted.
3. **Atomicity**: The transaction must write changes to both notes atomically. If any write fails, the entire transaction is rolled back.

## Audit Event Logging

Critical memory updates and transitions emit structured log entries to `audit_log.jsonl`:
- `supersede`: Emitted upon successful execution of the explicit supersession flow.
- `archive_superseded`: Emitted when the predecessor memory lifecycle is transitioned to `SUPERSEDED`.
- `valid_until_update`: Emitted when the expiration date (`valid_until`) of an active memory is updated.
- `conflict`: Emitted when overlapping, incompatible, or cyclic relations are proposed.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
