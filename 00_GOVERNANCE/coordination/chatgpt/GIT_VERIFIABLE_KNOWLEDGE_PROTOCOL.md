# Git-Verifiable Knowledge Protocol

## Purpose

Preserve project-operating knowledge that can be directly verified and executed through GitHub, so it is not left only in conversational context.

## Rule

When a repository fact, workflow improvement, contract, invariant, tool capability, or architectural decision can be:

1. inspected directly from GitHub;
2. modified directly through GitHub;
3. verified directly through GitHub metadata, diffs, commits, files, or workflow evidence;
4. and does not require a local checkout for correctness;

record it in the repository's appropriate governance/evidence location.

## Direct GitHub-First Operations

Prefer direct GitHub operations for source files and evidence when they are sufficient:

- fetch complete UTF-8 file content with blob retrieval when normal file retrieval is truncated;
- update complete text files with their current blob SHA;
- create isolated test, evidence, and governance files directly on the intended branch;
- use commit metadata and diffs as the authoritative change record;
- use workflow-run/job APIs for CI evidence;
- keep branch scope explicit on every write.

## Large-File Rule

Never reconstruct a large source file from a truncated response.

For a large file:

`fetch_file → obtain current SHA → fetch_blob → modify complete content → update_file`

The complete current blob is the source of truth for full-file replacement.

## Verification Rule

A claim is not considered verified merely because a change was committed.

Verification must be based on available evidence appropriate to the claim:

- source-level invariants → tests/static inspection;
- repository structure → direct repository inspection;
- CI status → workflow-run evidence;
- security behavior → regression/security tests;
- integration readiness → contract + harness + bypass audit.

Queued workflows are not green evidence.

## Concurrency / Ownership Rule

Keep independent agent tracks isolated.

- Antigravity owns retrieval/corpus integration until explicitly handed off.
- ChatGPT owns the runtime-security/lifecycle closure track until explicitly handed off.
- `main` is protected from exploratory writes unless explicitly authorized.
- `PROJECT_BRAIN/PROJECT_STATE.md` is not modified by autonomous security work.

## Evidence-As-Memory Principle

Any durable discovery that changes how the project should be operated should be captured in a versioned, reviewable artifact when technically appropriate.

Examples:

- verified connector capabilities;
- validated security invariants;
- canonical path mappings;
- architectural handoff rules;
- evidence-generation commands;
- limitations that could otherwise be rediscovered incorrectly.

## Non-goals

This protocol does not require every conversational statement to be persisted.
Only durable, project-relevant, technically verifiable knowledge belongs here.
