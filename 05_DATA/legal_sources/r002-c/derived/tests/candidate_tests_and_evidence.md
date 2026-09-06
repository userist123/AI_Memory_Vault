# R002-C Candidate Tests and Evidence Artifacts

These are candidate verification tests for the corpus implementation. They are not legal compliance tests.

| ID | Test | Source refs | Evidence artifact |
|---|---|---|---|
| R002C-T01 | Both primary acts have immutable source snapshots | source register | HTML hash + extracted-text hash |
| R002C-T02 | Every derived note has act/article/alineat provenance | all notes | provenance validation report |
| R002C-T03 | Every derived note remains REVIEW | lifecycle rule | lifecycle scan |
| R002C-T04 | No derived note claims NIS2 compliance | non-goal | forbidden-claim scan |
| R002C-T05 | Law 124 amendment item resolves to an OUG target | Law124 Articol unic | amendment-map consistency report |
| R002C-T06 | Source and interpretation are stored separately | corpus layout | path/schema validation |
| R002C-T07 | Legal-review-required items are explicitly enumerated | review register | unresolved-review report |
| R002C-T08 | Unknown applicability is not converted to false | Art.2, 5-10, Annexes | UNKNOWN/NOT_DETERMINED test fixtures |
| R002C-T09 | Later amendments outside the source scope do not silently rewrite the two-source corpus | version boundary | version-gap report |
| R002C-T10 | Generated official URLs resolve | source register | URL probe report |
| R002C-T11 | No confidential/classified/operational data exists in corpus | OUG Art.3; task non-goal | content scan |
| R002C-T12 | Evidence records preserve timestamps and source versions | Art.15, 47, 64-65 | deadline/evidence fixture report |

## Evidence artifacts to produce before owner/legal review

- `source_register.md` with exact SHA-256 values.
- `primary/*/source.html` byte-preserving captures.
- `primary/*/source.txt` extracted complete text.
- `primary/*/HASHES.sha256`.
- `indexes/full_article_index.md`.
- `indexes/amendment_consolidation_map.md`.
- `derived/notes/atomic_review_notes.md`.
- `derived/controls/candidate_technical_controls.md`.
- `review/legal_review_required.md`.
- `review/not_applicable_or_not_yet_determined.md`.
- final `R002-C_HANDOFF.md`.

---

## 🔗 Legături Sinaptice
- [[candidate_technical_controls]]
- [[atomic_review_notes]]
- [[05_DATA/legal_sources/r002-c/README|R002-C Overview]]
- [[07_EVALUATION/README|Evaluation Evidence]]
