# Book-to-Memory Central Blocker Register

Schema Version: 1.0  
Track: BOOK_TO_MEMORY  
PR: 206  
Branch: `research/book-to-memory`  
Base Commit: `77ff539a514f1293056798cf01076c43025742cb`  

Source of truth for all Book-to-Memory blockers. Validated deterministically by `30_SCRIPTS/verification/validate_blocker_registry.py`.

## Active Blockers

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
  record_hash: 3c885fbd1ca195c410f63a88cf32bec2b6dc17404cd1db40136bb4147e2a3c08
  previous_record_hash: null
transition_history_ref: BLOCKER_HISTORY.md
notes: Source remains unavailable for page-level derivation; general cybernetics claims
  must not enter ACTIVE memory.
```

### Blocker `B-0002`: Source identity misattribution in corpus manifest for newell_how_can_human_mind_occur

```yaml
blocker_id: B-0002
schema_version: '1.0'
title: Source identity misattribution in corpus manifest for newell_how_can_human_mind_occur
severity: HARD_BLOCKER
status: TRIAGED
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
  - Bibliographic record corrected to John R. Anderson
  - Research hypotheses and book maps correctly attribute ACT-R principles
  evidence_required:
  - Reconciled metadata in SOURCE_RECOVERY_AUDIT.md
  resolved_at: null
  resolved_by: null
  resolution_evidence: []
integrity:
  record_hash: fd89041a54fb9ea4990b99b16317177beb421ec67357bbf7a2cd0939319933a0
  previous_record_hash: 3c885fbd1ca195c410f63a88cf32bec2b6dc17404cd1db40136bb4147e2a3c08
transition_history_ref: BLOCKER_HISTORY.md
notes: Book content is complete and verified; only attribution key requires reconciliation.
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
  record_hash: 1952124bb81d77449483b2a40fc9aad2f5ca1bbbd30e1e5424ba8e2b5847e902
  previous_record_hash: fd89041a54fb9ea4990b99b16317177beb421ec67357bbf7a2cd0939319933a0
transition_history_ref: BLOCKER_HISTORY.md
notes: Secondary summary cannot substitute for primary neurobiological evidence.
```

### Blocker `B-0004`: Fragmentary single-page jacket blurb for 7688_jkt_au

```yaml
blocker_id: B-0004
schema_version: '1.0'
title: Fragmentary single-page jacket blurb for 7688_jkt_au
severity: HARD_BLOCKER
status: TRIAGED
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
  resolved_at: null
  resolved_by: null
  resolution_evidence: []
integrity:
  record_hash: c807045815b8cf1a7a6e74b977dfa1f7cc25c2609a63faa9ab1b502ea78cf810
  previous_record_hash: 1952124bb81d77449483b2a40fc9aad2f5ca1bbbd30e1e5424ba8e2b5847e902
transition_history_ref: BLOCKER_HISTORY.md
notes: Redundant artifact; full text already available under laird_soar_cognitive_architecture.
```

### Blocker `B-0005`: Secondary non-peer-reviewed source comparison_cognitive_architectures (Wikipedia table excerpt)

```yaml
blocker_id: B-0005
schema_version: '1.0'
title: Secondary non-peer-reviewed source comparison_cognitive_architectures (Wikipedia
  table excerpt)
severity: SOFT_BLOCKER
status: TRIAGED
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
  resolved_at: null
  resolved_by: null
  resolution_evidence: []
integrity:
  record_hash: 8a9d0b8cb95af5b965505bb89277f92f9f2f52335622a065da1b9041bc75bd41
  previous_record_hash: c807045815b8cf1a7a6e74b977dfa1f7cc25c2609a63faa9ab1b502ea78cf810
transition_history_ref: BLOCKER_HISTORY.md
notes: Tertiary material excluded from formal hypothesis derivation.
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
    available_sources: 14/20'
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
    and benchmark cases to verified available sources (14 items); do not declare full
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
  record_hash: 2ff52acb077641bc91817d71be4a8dffb0214d4533d0b4e7374a743f1db22f3b
  previous_record_hash: 8a9d0b8cb95af5b965505bb89277f92f9f2f52335622a065da1b9041bc75bd41
transition_history_ref: BLOCKER_HISTORY.md
notes: Allows independent experimental research on 14 verified sources under PARTIAL_SOURCE_CORPUS
  designation.
```

