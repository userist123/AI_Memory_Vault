# Defender Wacatac forensic report

Scope: local repository `userist123/AI_Memory_Vault_CODEX_READY`, observed Defender history through 2026-09-04, and `origin/main` at the time of review. No flagged content was executed, parsed as code, or restored from quarantine.

## Detection inventory

Defender records named `Trojan:Script/Wacatac.B!ml`, `Trojan:Script/Wacatac.C!ml`, and `Trojan:Script/Wacatac.H!ml` against Markdown payload references. The observed path families were:

- `.agents/skills/pentest-tools/src-hunter/references/payloader/waf-bypass.md`
- `.agents/skills/pentest-tools/src-hunter/references/playbooks/path-traversal.md`
- `.agents/skills/pentest-tools/src-hunter/references/playbooks/xss.md`
- `.agents/skills/src-hunter/references/payloader/waf-bypass.md`
- `.agents/skills/src-hunter/references/playbooks/path-traversal.md`
- `.agents/skills/src-hunter/references/playbooks/xss.md`
- RAW copies under both `.../plugins/agentic-awesome-skills-claude/.../src-hunter/` and `.../plugins/agentic-awesome-skills/.../src-hunter/`, including `waf-bypass.md`, `path-traversal.md`, `xss.md`, the Chinese web-payload Markdown file, and `tools/系统命令.md`.

Several Defender resource strings included a trailing `->(UTF-8)` display annotation; that is telemetry formatting, not a filename. Repeated records for the same path were deduplicated by normalized path.

## Hashes and actual type

The current Git blobs for the three executable-looking names are all Markdown, not binaries:

| Content family | Bytes | SHA-256 | Type | PE/embedded `MZ` |
|---|---:|---|---|---|
| `waf-bypass.md` | 174029 | `75daa5f5870df15cf9574267e4a3360ab0513e5f4af5ce886a0759b0d12b7069` | `text/markdown` | no/no |
| `path-traversal.md` | 32685 | `f98da7c3f908dc4320193b728319bbd59528b87899ab785400d70378660e72e0` | `text/markdown` | no/no |
| `xss.md` | 30690 | `c1ed31e52c5354225efbce0d0a04d2882f78c8d3780c3a70a57759d3db5d9352` | `text/markdown` | no/no |
| `xss跨站脚本.md` (historical RAW) | 44495 | `2e1ec33087f87e6f953be9cd4e349e7dd04c4f519bce5fa6edf028bf50db79e2` | `text/markdown` | no/no |

All listed hashes were computed from Git blob bytes and contain neither a PE header at offset 0 nor an embedded `MZ` signature.

The Chinese-named Markdown variants and `系统命令.md` were absent from the current `origin/main` tree after the security-removal history, so no current working-file SHA can honestly be reported for them. Their Defender records remain evidence of historical detections, not proof of their current bytes.

## Upstream comparison

The three surviving content families match byte-for-byte with `https://github.com/sickn33/agentic-awesome-skills` `main` at review time:

- `waf-bypass.md`: same full SHA-256;
- `path-traversal.md`: same full SHA-256;
- `xss.md`: same full SHA-256.

This establishes upstream origin for those hashes. It does not establish that the upstream content is safe to execute; it establishes that the repository contains the same Markdown corpus.

## Git history and derivatives

The raw import first appears in commit `a09e298cf328abfb1bf22e7c963014654fb4b21c` (2026-09-02 23:38:32 +03:00), message `feat(inbox): ingest and upload complete raw external skill repositories corpus`. The canonical extraction appears in `787c8e6ac5b9a6d29a9b1af4b9fc6cf158d2a679` (2026-09-03 00:28:34 +03:00). The subsequent security-removal commit is `619757a2ae0be015db68db0db9219cdf74bba66e` (2026-09-03 01:08:55 +03:00).

The identical hashes prove derivatives/copies in `.agents/` and RAW for the three surviving families. No executable derivative was found in the Git blobs. The local Defender state shows the flagged files are currently absent or quarantined; no restoration was attempted.

## Verdict

**SECURITY TEST CONTENT** (high confidence for file type/origin; not a claim that every payload string is harmless). The evidence is consistent with a Markdown penetration-testing payload corpus copied from the upstream project, which triggered Defender script heuristics. It is not evidence of a compiled malware executable. Exact content for files already quarantined/deleted is `UNKNOWN` locally, so a binary-level conclusion about those historical bytes is not possible from the current filesystem.

Defender was not disabled, no exclusion was added, and no flagged content was executed.

## Current filesystem recheck

At recheck time, six surviving local files were readable as Markdown but had different current filesystem hashes from the historical/origin Git blobs, indicating local content or line-ending changes. Their current hashes were:

- canonical `xss跨站脚本.md`: `67B784A7159B73D1ED18B41B2DF55C624725C8B6281A0F28B1F7F58F815B8B07`;
- canonical `xss.md`: `149A8AF564913A0E36E6E263B1308E09A021141911B519338AED544720F200FD`;
- RAW `path-traversal.md`: `E62147E431DC1767CE568D2363A09159A8F97BEBB5DB027F30358444076E547C`;
- RAW `系统命令.md`: `A463E1D15DC807F48CB9D0B372E55894CF793A5FF6ADD13DE14535EEF5B2FE85`.

The two canonical copies of each xss family had identical current hashes. All six had MIME `text/markdown`, no `MZ` signature at offset zero, and no embedded byte sequence `MZ`. This filesystem recheck was read-only.


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
