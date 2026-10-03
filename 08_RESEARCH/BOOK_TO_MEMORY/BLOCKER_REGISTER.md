# Central Blocker Registry

**Registry Schema**: `1.0`  
**Specification Ref**: `00_GOVERNANCE/RESEARCH-TRACK-CONTRACT.md`  
**Verification Engine**: `30_SCRIPTS/verification/validate_blocker_registry.py`  
**Transition Log**: `08_RESEARCH/BOOK_TO_MEMORY/BLOCKER_HISTORY.md`  

---

### Blocker `B-0001`: Missing exact unabridged source copy for Wiener Cybernetics (fragmentary quotes only)

```yaml
blocker_id: B-0001
schema_version: '1.0'
title: Missing exact unabridged source copy for Wiener Cybernetics (fragmentary quotes
  only)
severity: HARD_BLOCKER
status: AWAITING_EXTERNAL_EVIDENCE
scope:
  track: BOOK_TO_MEMORY
  pr: 206
  branch: research/book-to-memory
  source_ids:
  - wiener_cybernetics
  hypothesis_ids: []
  experiment_ids: []
detected:
  timestamp: '2026-10-03T15:00:00Z'
  commit: 77ff539a514f1293056798cf01076c43025742cb
  actor: antigravity
  method: audit
evidence:
  primary:
  - '06_INBOX/Carti/Creier cibernetic/ilide.info-wiener-cybernetics-pr_14c41245c1e661efb9361d53417d2473.txt
    (size: 7,780 bytes, sha256: 674f5aa5c7bdfd0674e5c256f5d07d389ecde17544bf48183a8684357cb87800)'
  supporting:
  - 08_RESEARCH/BOOK_TO_MEMORY/SOURCE_RECOVERY_AUDIT.md
root_cause:
  summary: Local inbox file contains only a 7.7 KB selection of quotations and chapter
    fragments, not the unabridged 200+ page 1948/1961 MIT Press edition.
  confidence: HIGH
impact:
  affected_scope: wiener_cybernetics research mapping and chapter-level page citations
  invalidates_experiment: true
  invalidates_corpus: false
  affects_production: false
  affects_ci: false
remediation:
  required_action: Recover verified unabridged edition or mark source permanently
    unavailable for empirical benchmark derivation.
  proposed_action: Mark wiener_cybernetics as SOURCE_UNAVAILABLE in partial corpus
    freeze; do not derive H1 cases from it.
  owner_approval_required: false
dependencies:
  blocks:
  - B-0006
  blocked_by: []
  depends_on: []
  duplicates: []
  supersedes: []
closure:
  criteria:
  - Exact unabridged source copy available with verified SHA-256 and TOC
  - Bibliographic identity confirmed
  - Reproducible page-level citation mapping
  evidence_required:
  - Verified source SHA-256 and exact page alignment
  resolved_at: null
  resolved_by: null
  resolution_evidence: []
integrity:
  record_hash: c163ee9a4458e7562347a4086e755c204b15ec404b9ab11c1a3bc6b5616d1c86
  previous_record_hash: '0000000000000000000000000000000000000000000000000000000000000000'
transition_history_ref: BLOCKER_HISTORY.md
notes: Full 1961 2nd edition monograph (~212 pages, MIT Press) is unavailable in workspace
  and cannot be retrieved without external acquisition; remains AWAITING_EXTERNAL_EVIDENCE
  per rule 6.
```

### Blocker `B-0002`: Source identity misattribution in corpus manifest for newell_how_can_human_mind_occur

```yaml
blocker_id: B-0002
schema_version: '1.0'
title: Source identity misattribution in corpus manifest for newell_how_can_human_mind_occur
severity: HARD_BLOCKER
status: CLOSED
scope:
  track: BOOK_TO_MEMORY
  pr: 206
  branch: research/book-to-memory
  source_ids:
  - newell_how_can_human_mind_occur
  hypothesis_ids:
  - H1-BOOK-002
  experiment_ids: []
detected:
  timestamp: '2026-10-03T15:00:00Z'
  commit: 77ff539a514f1293056798cf01076c43025742cb
  actor: antigravity
  method: audit
evidence:
  primary:
  - '06_INBOX/Carti/Memorie procedurală+Judecatăheuristici+Routing/ilide.info-how-can-the-human-mind-occur-in-the-physical-universe-pr_926d879337b6016eac9eaf30756bbcc4.txt:
    Title ''How Can The Human Mind Occur In The Physical Universe?'', Author John
    R. Anderson (2007, Oxford University Press)'
  supporting:
  - 08_RESEARCH/BOOK_TO_MEMORY/BOOK-anderson-human-mind-map.md
root_cause:
  summary: Manifest key name newell_how_can_human_mind_occur incorrectly attributes
    the book to Allen Newell instead of John R. Anderson (ACT-R, 2007).
  confidence: HIGH
impact:
  affected_scope: Source attribution in corpus manifest and downstream hypothesis
    naming
  invalidates_experiment: false
  invalidates_corpus: false
  affects_production: false
  affects_ci: false
remediation:
  required_action: Reconcile bibliographic metadata; ensure hypotheses explicitly
    reference John R. Anderson (2007) and ACT-R theory.
  proposed_action: Document verified authorship in SOURCE_RECOVERY_AUDIT.md and update
    BOOK_TO_HYPOTHESIS_MAPPING.md.
  owner_approval_required: false
dependencies:
  blocks: []
  blocked_by: []
  depends_on: []
  duplicates: []
  supersedes: []
closure:
  criteria:
  - Bibliographic record corrected and spurious download artifact decommissioned
  - Research hypotheses and book maps correctly attribute ACT-R principles to verified
    primary sources
  evidence_required:
  - Reconciled metadata in SOURCE_RECOVERY_AUDIT.md and re-grounded H1-BOOK-002 in
    BOOK_TO_HYPOTHESIS_MAPPING.md
  resolved_at: '2026-10-03T16:15:00Z'
  resolved_by: antigravity
  resolution_evidence:
  - 08_RESEARCH/BOOK_TO_MEMORY/SOURCE_RECOVERY_AUDIT.md
  - 08_RESEARCH/BOOK_TO_MEMORY/BOOK_TO_HYPOTHESIS_MAPPING.md
  - 08_RESEARCH/BOOK_TO_MEMORY/CORPUS_FREEZE.json
integrity:
  record_hash: d09d5529991c7e53d3b832770b0c7b11dacb05892b8debd1eee6677316d1636f
  previous_record_hash: c163ee9a4458e7562347a4086e755c204b15ec404b9ab11c1a3bc6b5616d1c86
transition_history_ref: BLOCKER_HISTORY.md
notes: Attribution ambiguity and corrupted download artifact resolved. Source marked
  SOURCE_UNAVAILABLE; ACT-R declarative recall re-grounded in primary source wcs_1488
  (Ritter et al. 2019).
```

### Blocker `B-0003`: Incomplete secondary Bookey summary guide for why_we_forget instead of primary text

```yaml
blocker_id: B-0003
schema_version: '1.0'
title: Incomplete secondary Bookey summary guide for why_we_forget instead of primary
  text
severity: HARD_BLOCKER
status: AWAITING_EXTERNAL_EVIDENCE
scope:
  track: BOOK_TO_MEMORY
  pr: 206
  branch: research/book-to-memory
  source_ids:
  - why_we_forget
  hypothesis_ids: []
  experiment_ids: []
detected:
  timestamp: '2026-10-03T15:00:00Z'
  commit: 77ff539a514f1293056798cf01076c43025742cb
  actor: antigravity
  method: audit
evidence:
  primary:
  - '06_INBOX/Carti/Ontologie+Memorie-episodică-semantică-procedurală/Why We Forget
    and How To Remember Better PDF.txt: ''Unlock the Secrets to Enhancing Your Memory
    and Recall Written by Bookey'' (size: 165,133 bytes)'
  supporting:
  - 08_RESEARCH/BOOK_TO_MEMORY/SOURCE_RECOVERY_AUDIT.md
root_cause:
  summary: Local file is a third-party commercial executive summary (Bookey), not
    the primary unabridged Oxford University Press 2023 volume by Budson & Kensinger.
  confidence: HIGH
impact:
  affected_scope: Primary page citation for why_we_forget
  invalidates_experiment: true
  invalidates_corpus: false
  affects_production: false
  affects_ci: false
remediation:
  required_action: Obtain verified unabridged monograph or mark source as secondary
    summary; do not infer primary claims.
  proposed_action: Mark as NEEDS_VERIFICATION in inventory; exclude from primary benchmark
    derivation.
  owner_approval_required: false
dependencies:
  blocks:
  - B-0006
  blocked_by: []
  depends_on: []
  duplicates: []
  supersedes: []
closure:
  criteria:
  - Primary unabridged text available or explicit secondary status acknowledged without
    primary claims
  evidence_required:
  - Verified primary source hash
  resolved_at: null
  resolved_by: null
  resolution_evidence: []
integrity:
  record_hash: 0231943fe7a1924a9f566cfe8d05ad7be63cc109a9e542b3bbed4033afcf1564
  previous_record_hash: d09d5529991c7e53d3b832770b0c7b11dacb05892b8debd1eee6677316d1636f
transition_history_ref: BLOCKER_HISTORY.md
notes: Full unabridged monograph (Oxford University Press, 2023, 25 chapters, 320
  pages) is unavailable in workspace; Bookey summary cannot substitute; remains AWAITING_EXTERNAL_EVIDENCE
  per rule 4.
```

### Blocker `B-0004`: Fragmentary single-page jacket blurb for 7688_jkt_au

```yaml
blocker_id: B-0004
schema_version: '1.0'
title: Fragmentary single-page jacket blurb for 7688_jkt_au
severity: HARD_BLOCKER
status: CLOSED
scope:
  track: BOOK_TO_MEMORY
  pr: 206
  branch: research/book-to-memory
  source_ids:
  - 7688_jkt_au
  hypothesis_ids: []
  experiment_ids: []
detected:
  timestamp: '2026-10-03T15:00:00Z'
  commit: 77ff539a514f1293056798cf01076c43025742cb
  actor: antigravity
  method: audit
evidence:
  primary:
  - '06_INBOX/Carti/Memorie procedurală+Judecatăheuristici+Routing/7688_jkt_au.txt
    (size: 3,664 bytes, 1 page)'
  supporting:
  - 08_RESEARCH/BOOK_TO_MEMORY/SOURCE_RECOVERY_AUDIT.md
root_cause:
  summary: 7688_jkt_au is only the dust jacket / promotional blurb of Laird 2012,
    which exists fully under laird_soar_cognitive_architecture.
  confidence: HIGH
impact:
  affected_scope: Independent source status of 7688_jkt_au
  invalidates_experiment: false
  invalidates_corpus: false
  affects_production: false
  affects_ci: false
remediation:
  required_action: Mark 7688_jkt_au as superseded by and merged into laird_soar_cognitive_architecture;
    do not treat as a separate 20th source.
  proposed_action: Consolidate citation mapping under laird_soar_cognitive_architecture.
  owner_approval_required: false
dependencies:
  blocks: []
  blocked_by: []
  depends_on: []
  duplicates: []
  supersedes: []
closure:
  criteria:
  - Documentation clarifies 7688_jkt_au is jacket blurb superseded by laird_soar_cognitive_architecture
  evidence_required:
  - Cross-reference in SOURCE_RECOVERY_AUDIT.md
  resolved_at: '2026-10-03T16:15:00Z'
  resolved_by: antigravity
  resolution_evidence:
  - 08_RESEARCH/BOOK_TO_MEMORY/SOURCE_RECOVERY_AUDIT.md
  - 08_RESEARCH/BOOK_TO_MEMORY/CORPUS_FREEZE.md
integrity:
  record_hash: 345d35c850fc7269d817f9573f1768deada4c37087793f7be173c58bcbbaefea
  previous_record_hash: 0231943fe7a1924a9f566cfe8d05ad7be63cc109a9e542b3bbed4033afcf1564
transition_history_ref: BLOCKER_HISTORY.md
notes: 7688_jkt_au verified as 1-page promotional dust jacket containing zero unique
  scientific evidence; consolidated under verified primary monograph laird_soar_cognitive_architecture.
```

### Blocker `B-0005`: Secondary non-peer-reviewed source comparison_cognitive_architectures (Wikipedia table excerpt)

```yaml
blocker_id: B-0005
schema_version: '1.0'
title: Secondary non-peer-reviewed source comparison_cognitive_architectures (Wikipedia
  table excerpt)
severity: SOFT_BLOCKER
status: CLOSED
scope:
  track: BOOK_TO_MEMORY
  pr: 206
  branch: research/book-to-memory
  source_ids:
  - comparison_cognitive_architectures
  hypothesis_ids: []
  experiment_ids: []
detected:
  timestamp: '2026-10-03T15:00:00Z'
  commit: 77ff539a514f1293056798cf01076c43025742cb
  actor: antigravity
  method: audit
evidence:
  primary:
  - '06_INBOX/Carti/Memorie procedurală+Judecatăheuristici+Routing/ilide.info-comparison-of-cognitive-architectures-pr_9d193230972c843d495b101ff8b88235.txt
    (size: 3,884 bytes, 3 pages)'
  supporting:
  - 08_RESEARCH/BOOK_TO_MEMORY/SOURCE_RECOVERY_AUDIT.md
root_cause:
  summary: Source is a 3-page Wikipedia table printout, not a primary peer-reviewed
    monograph or scientific publication.
  confidence: HIGH
impact:
  affected_scope: Authority and evidentiary status for cognitive architecture comparisons
  invalidates_experiment: false
  invalidates_corpus: false
  affects_production: false
  affects_ci: false
remediation:
  required_action: Rely exclusively on primary architectural sources (Laird 2012,
    Anderson 2007, Newell 1990, Ritter 2019) for architectural hypotheses.
  proposed_action: Classify source as tertiary overview; do not derive normative cognitive
    invariants from it.
  owner_approval_required: false
dependencies:
  blocks: []
  blocked_by: []
  depends_on: []
  duplicates: []
  supersedes: []
closure:
  criteria:
  - Primary sources identified for all architectural comparisons
  evidence_required:
  - Mapping in BOOK_TO_HYPOTHESIS_MAPPING.md
  resolved_at: '2026-10-03T16:15:00Z'
  resolved_by: antigravity
  resolution_evidence:
  - 08_RESEARCH/BOOK_TO_MEMORY/SOURCE_RECOVERY_AUDIT.md
  - 08_RESEARCH/BOOK_TO_MEMORY/BOOK_TO_HYPOTHESIS_MAPPING.md
integrity:
  record_hash: 5a762edf52a4987e35c267b7366952f1716009f36839329eec738b12d7aaf865
  previous_record_hash: 345d35c850fc7269d817f9573f1768deada4c37087793f7be173c58bcbbaefea
transition_history_ref: BLOCKER_HISTORY.md
notes: comparison_cognitive_architectures classified as tertiary overview; excluded
  from hypothesis derivation; primary architectural sources established as Laird 2012,
  Newell 1990, Ritter et al. 2019.
```

### Blocker `B-0006`: Partial source corpus precludes declaring full 20-source H1 benchmark completion

```yaml
blocker_id: B-0006
schema_version: '1.0'
title: Partial source corpus precludes declaring full 20-source H1 benchmark completion
severity: HARD_BLOCKER
status: MITIGATION_IN_PROGRESS
scope:
  track: BOOK_TO_MEMORY
  pr: 206
  branch: research/book-to-memory
  source_ids:
  - 7688_jkt_au
  - wiener_cybernetics
  - why_we_forget
  - comparison_cognitive_architectures
  hypothesis_ids:
  - H1-BOOK-001
  - H1-BOOK-002
  - H1-BOOK-003
  - H1-BOOK-004
  - H1-BOOK-005
  experiment_ids: []
detected:
  timestamp: '2026-10-03T15:00:00Z'
  commit: 77ff539a514f1293056798cf01076c43025742cb
  actor: antigravity
  method: audit
evidence:
  primary:
  - '08_RESEARCH/BOOK_TO_MEMORY/CORPUS_FREEZE.json: corpus_type: PARTIAL_SOURCE_CORPUS,
    available_sources: 15/20'
  supporting:
  - 08_RESEARCH/BOOK_TO_MEMORY/SOURCE_RECOVERY_AUDIT.md
  - 08_RESEARCH/BOOK_TO_MEMORY/H1-CURRENT-CORPUS-GATE.md
root_cause:
  summary: Because 3 sources are fragmentary/blurbs and 3 require verification/attribution
    reconciliation, the source corpus cannot be declared complete for all 20 manifest
    items.
  confidence: HIGH
impact:
  affected_scope: Global Book-to-Memory H1 track completion status
  invalidates_experiment: true
  invalidates_corpus: false
  affects_production: false
  affects_ci: false
remediation:
  required_action: Freeze corpus strictly as PARTIAL_SOURCE_CORPUS; restrict H1 hypotheses
    and benchmark cases to verified available sources (15 items); do not declare full
    20-source H1 completion without all unabridged sources.
  proposed_action: Implement pre-registered PARTIAL_SOURCE_CORPUS freeze and testable
    hypothesis subset.
  owner_approval_required: false
dependencies:
  blocks: []
  blocked_by:
  - B-0001
  - B-0003
  depends_on: []
  duplicates: []
  supersedes: []
closure:
  criteria:
  - All 20 sources either recovered with complete verified unabridged text or formally
    decommissioned from manifest with owner approval
  - H1 benchmark rerun across complete frozen corpus
  evidence_required:
  - Complete corpus freeze manifest with 20/20 available sources
  resolved_at: null
  resolved_by: null
  resolution_evidence: []
integrity:
  record_hash: e17f2f6cf19e7ee529fcbd649a01060c195e59c58cf2e66ac6fce6a6aace5a89
  previous_record_hash: 5a762edf52a4987e35c267b7366952f1716009f36839329eec738b12d7aaf865
transition_history_ref: BLOCKER_HISTORY.md
notes: Maintained active (MITIGATION_IN_PROGRESS) strictly under PARTIAL_SOURCE_CORPUS
  designation; blocked by external evidence resolution on B-0001 and B-0003.
```
