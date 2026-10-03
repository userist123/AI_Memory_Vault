# H1 Corpus Construction Gate — Current Branch Audit

Date: 2026-10-03
Branch: `research/book-to-memory`
Research PR: #206
Main base: `427b44edbb9b113b87a582b8b49734f195c2363f`

## Result

**STATUS: BLOCKED before benchmark instantiation.**

The research branch contains the corpus manifest, validator, and book maps, but the manifest's referenced Raw Inbox book files are not present in the Git repository tree accessible from this branch. Therefore a reproducible current-vault labeling corpus cannot be generated from the repository alone at this point.

This is a data-availability gate, not a retrieval failure.

## Evidence

`30_SCRIPTS/ingestion/corpus_manifest.json` lists 20 source files under `06_INBOX/Carti/...`.

The repository tree does not expose `06_INBOX/Carti` as a repository directory on `research/book-to-memory`. Direct repository-content lookup for that path returns 404.

Consequences:

1. The book source corpus cannot be reconstructed from Git alone.
2. The current book maps can be reviewed, but page-level evidence from the repository copies cannot be re-derived from this branch.
3. H1 cases must not be invented from the maps.
4. A current labeling corpus must not be substituted with the historical v2/v3 corpus.
5. A benchmark corpus hash cannot legitimately be frozen until the exact source snapshot is available.

## Retrieval eligibility audit

The production search path applies an AI-agent lifecycle floor of `ACTIVE + REVIEW` when no explicit lifecycle filter is supplied. RAW is unconditionally excluded. HUMAN searches are not subject to that default floor.

Therefore an H1 case must record enough request context to distinguish:

- principal;
- explicit lifecycle filter, if any;
- classifier-inferred lifecycle/type filters;
- RAW exclusion;
- AI-agent floor exclusion;
- final candidate/context visibility.

The structural validator already validates principal values, but it does not yet encode the full request-time eligibility state. That is intentional until the corpus is built against the exact runtime snapshot; adding a guessed policy would risk turning benchmark policy into an undocumented production assumption.

## Current generator assessment

`30_SCRIPTS/evaluation/build_labeling_corpus.py` is suitable as a starting point because it:

- loads the production index;
- excludes ARCHIVED;
- excludes notes without IDs;
- emits stable note identifiers and shortened excerpts;
- records real graph links only when both endpoints exist;
- records the vault commit and note/link counts.

It is **not sufficient as the final H1 evidence packet**, because the shortened excerpt is not enough to prove exact required-fact evidence for every case, and runtime lexical/entity reachability must be measured against the production retrieval path.

## Required next gate

Before creating H1 cases:

1. Make the exact current vault corpus available to the research execution environment.
2. Run the corpus builder against that snapshot.
3. Freeze the resulting vault commit and canonical corpus hash.
4. Run structural validation.
5. Measure BM25/entity reachability with the exact production retrieval path.
6. Record query classification and lifecycle/type eligibility.
7. Only then select/freeze H1 cases.
8. Run the baseline before implementing any associative retrieval mechanism.

## Non-goals

This audit does not:

- change production retrieval;
- change ranking;
- promote any memory;
- import book text into retrieval;
- treat historical v2/v3 results as current H1 evidence;
- infer an associative mechanism from the literature.

## Book-to-Memory chain

The current work remains:

`BIOLOGICAL FACT -> ENGINEERING HYPOTHESIS -> EXPERIMENT -> VAULT MECHANISM`

The final arrow remains intentionally blocked until the experiment has a frozen, reproducible corpus and a measured production baseline.

## Source-Copy Gate Resolution Note (PR #206 Update)

As documented in `SOURCE_RECOVERY_AUDIT.md`, `CORPUS_FREEZE.md`, and `BLOCKER_REGISTER.md`:
- The local filesystem copies under `06_INBOX/Carti` were forensically audited across all 20 manifest entries.
- 15 sources are fully available and verified on disk.
- 5 sources are marked SOURCE_UNAVAILABLE (`7688_jkt_au`, `comparison_cognitive_architectures`, `newell_how_can_human_mind_occur`, `why_we_forget`, `wiener_cybernetics`).
- 0 sources require verification.
- The corpus is frozen as `PARTIAL_SOURCE_CORPUS` with canonical SHA-256 hash `4d8b77543350f02411e3bb9eb2d765f39dd39891a542c95823010bb2bc5cc59a`.
- Active blockers are formally tracked under `B-0001` through `B-0006` in `BLOCKER_REGISTER.md` (where `B-0002`, `B-0004`, `B-0005` are CLOSED, and `B-0001`, `B-0003`, `B-0006` remain active HARD_BLOCKERs) and validated by `validate_blocker_registry.py`.

