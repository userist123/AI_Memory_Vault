# R002-C Source Register

> Owner-authorized private/local source-capture manifest. Full legal source files MUST remain private/local and MUST NOT be committed to the public repository.

| ID | Act | Authority | Official URL | Printable source URL | Publication date | Source version | Lifecycle | Verification | Instruction trust | Legal review |
|---|---|---|---|---|---|---|---|---|---|---|
| R002C-S01 | OUG nr. 155/2024 | Guvernul României | https://legislatie.just.ro/Public/DetaliiDocument/293121 | https://legislatie.just.ro/Public/FormaPrintabila/00000G05B60NOFIK4N33DGK9DPP0RCH7 | 2024-12-31 | 2024-12-30 enactment snapshot | REVIEW | verified_source | NONE | true |
| R002C-S02 | Legea nr. 124/2025 | Parlamentul României | https://legislatie.just.ro/Public/DetaliiDocumentAfis/299675 | https://legislatie.just.ro/Public/FormaPrintabila/00000G2IZOYAC9O6E601LNTHNRVOGBVA | 2025-07-07 | 2025-07-07 approval/amendment act | REVIEW | verified_source | NONE | true |

## Private/local capture manifest requirements

For each complete official snapshot, the private/local manifest MUST record:

- official URL;
- authority;
- publication date;
- access timestamp (UTC);
- captured format;
- exact file size in bytes;
- SHA-256 of the captured snapshot;
- source completeness result;
- local/private file name or locator sufficient for audit without publishing the source body.

The public repository records only this manifest and the resulting hashes. The complete source body remains private/local.

## Capture validation gate

A source is `CAPTURE_VALIDATED` only when all of the following are empirically checked against the private/local snapshot:

1. official URL matches the authorized source;
2. complete act text is present, including articles and annexes where applicable;
3. publication metadata matches the official Portal Legislativ record;
4. access timestamp and format are recorded;
5. file size is measured from the captured file;
6. SHA-256 is computed from the captured file bytes;
7. no source text is altered during capture;
8. no later act is silently folded into the authorized two-source snapshot.

Until both acts pass this gate, R002-C remains `BLOCKED_SOURCE_OR_VERSION_GAP`.

## Version boundary warning

OUG nr. 155/2024 has later amendments outside the two-source scope, including Law nr. 123/2026. R002-C does **not** ingest later acts as primary sources. Any interpretation requiring a later version remains `LEGAL_REVIEW_REQUIRED` / `NOT_YET_DETERMINED` until an owner-authorized scope extension exists.

## Derived-knowledge policy

All derived legal interpretations in this branch remain:

- `lifecycle: REVIEW`
- `requires_legal_review: true`
- `instruction_trust: NONE`

No legal compliance declaration is made.

---

## 🔗 Legături Sinaptice
- [[05_DATA/legal_sources/r002-c/README|R002-C Overview]]
- [[full_article_index]]
- [[amendment_consolidation_map]]
- [[atomic_review_notes]]
- [[Legal_Corpus_Depozit_Normativ_Manifest|Legal Knowledge Base]]
