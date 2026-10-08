# Severity Downgrade Attestations

**Purpose**: the only way to lower the severity of a Book-to-Memory blocker. Without a matching
entry here, `30_SCRIPTS/verification/validate_blocker_registry.py --base-ref <base>` (run by the
`Repository Hygiene` workflow on every pull request) fails for any blocker whose severity is lower
than on the base branch, in `BLOCKER_REGISTER.md` or in the summary table of `OPEN_BLOCKERS.md`, or
that was deleted. Raising a severity never needs an entry.

An entry is one fenced YAML block, append-only, with these keys:

- `attestation_id`: unique id (`SA-0001`, ...).
- `blocker_id`: the blocker, exactly as written in the register / the table.
- `from_severity`, `to_severity`: the exact transition the owner decided (one entry per step).
- `principal`: `human` or `admin` (the vault's ATTEST matrix; `ai_agent` and any other value are
  refused, and the validator fails while such an entry exists).
- `attested_by`: the owner's name.
- `evidence_ref`: what justifies the decision (commit, PR, document); cannot be empty.
- `attested_at`: ISO-8601 timestamp.

An agent must not add an entry on its own: this file is owner-approved through CODEOWNERS like
every other path, and an entry is a statement by the owner, not by the session that typed it.

No downgrade has been attested so far.
