---
id: "c515d87d-2b46-44b1-b615-4b44ab04d850"
title: "History rewrite 2026-10-31 (owner privacy)"
type: "project"
lifecycle: "REVIEW"
created: "2026-10-31T00:00:00Z"
updated: "2026-10-31T00:00:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "00_GOVERNANCE/history/COMMIT_MAP_2026-10-31.tsv"
  created_by: "claude-code"
---

# History rewrite, 2026-10-31 (owner privacy)

On the owner's explicit request the whole Git history (all branches and tags) was rewritten to remove the owner's
personal identifiers: name, Windows profile path, machine name and personal e-mail, in file contents, file paths,
commit messages and author/committer fields. Five personal files and about a hundred old build artifacts
(`__pycache__`, `bin/`, `obj/`) that carried the profile path were dropped from every commit.

**Commit IDs changed.** Every SHA cited in a document written before the rewrite is an *old* SHA. Translate it with
`COMMIT_MAP_2026-10-31.tsv` (old → new, full 40-character SHAs; an abbreviated SHA is the prefix of one row).
Reports were deliberately not edited: they are evidence and keep citing what they cited.

**Re-frozen for privacy.** Three frozen samples contained the owner's name and were redacted, so their recorded
SHA-256 was re-recorded in the same commit as this note (and, of the coordination files, only `projects/LOGANALYZER_DFIR/CURRENT.md`, whose live-state SHA had to resolve, was translated): `07_EVALUATION/edge_audit/audit_sample_50.json`,
`07_EVALUATION/edge_audit_v2/audit_sample_declared_50.json` and the retrieval benchmark v3 labeling corpus. No
label, verdict or count changed; only the redacted bytes did.

**Not removable from here:** GitHub keeps pull-request refs (`refs/pull/*`) and cached commits with the old history
until GitHub Support purges them; existing clones and forks keep the old history.
