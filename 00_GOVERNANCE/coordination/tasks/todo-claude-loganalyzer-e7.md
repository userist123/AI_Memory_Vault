# todo-claude-loganalyzer-e7

- **Task:** E7, user activity: ShellBags, RecentDocs, Office MRU, Recycle Bin `$I` (comparison row E7, docs/dfir/RESEARCH_2026-10-12_COMPARATIE_AIRGAP_LOGANALYZER_INSPECTRA.md:135).
- **Branch:** `claude/loganalyzer-e7-user-activity`, worktree `.claude/worktrees/la-e7` (build ONLY here: Defender exclusion), base `claude/loganalyzer-device-monitor` 56dd08d0c.
- **PR order:** after #303 (E6) and the device monitor PR.
- **Updated:** 2026-10-21. Owner chose E7 over the users register (2026-10-21).

## Slices
1. **Parsers on evidence that is already there or imported** (this slice): shell item decoder, ShellBags (UsrClass.dat and NTUSER BagMRU),
   RecentDocs and Office MRU in UserHiveParser, `$I` parser. Timeline events + parser descriptors + capabilities + tests.
2. **Live collection:** NTUSER.DAT and UsrClass.dat of every profile (raw copy from VSS, like Amcache), `$Recycle.Bin\<SID>\$I*` of every
   fixed volume; full profile only; read-only; SHA-256 at collection.
3. **Use:** rules (classified folders browsed, removable volume browsed, deleted from the stick / deleted after a transfer), UI tab, report rows, docs, comparison row E7.

## Evidence semantics (must be written in the code and texts)
- BagMRU key last-write time = the last time Explorer updated that folder's entry; not "first opened", not proof of reading a file.
- Times inside a shell item (DOS date, 2 s resolution, local time without zone) are the folder's own MAC times when it was browsed, NOT access times.
- RecentDocs: only the first entry of MRUListEx gets the key's last-write time; the others are ordered, undated.
- Office MRU `[T…]` = last open by that Office app (FILETIME, UTC).
- `$I`: deletion time (FILETIME UTC), original path, original size; `$R` missing = emptied/removed content, not "never existed".

## Threat model / probe list (hostile hives and files; same list goes to the reviewer)
- Shell item size 0, 1, or larger than the remaining buffer; ItemID list without terminator; nested lists.
- BagMRU recursion: depth limit and visited set (key that names itself / cycles), max total nodes; no stack overflow.
- UTF-16 / ASCII strings without terminator, odd lengths, beyond the item; extension block (0xBEEF0004) with wrong size/version/offset.
- `$I` v2 name length negative, 0, or beyond the file; v1 shorter than 544 bytes; unknown version; file over MaxInputBytes.
- Office MRU value without `*`, bad hex in `[T…]`, FILETIME beyond the calendar (raw kept, unknown time).
- MRUListEx with indexes that do not exist, duplicates, no 0xFFFFFFFF terminator.
- Unknown shell item types: reported as „element necunoscut (tip 0xNN)” with hex, never dropped silently, never a crash.
- No exception escapes a parser for bad input: InvalidDataException -> reported gap, the rest of the hive still parsed.

## Done
- 2026-10-21, slice 1 (vault-worker, Sonnet) on `claude/loganalyzer-e7-user-activity`:
  - `Core/FileSystem/ShellItems.cs` (bounded shell item / ItemID list decoder, DOS times kept naive-local, ext block 0xBEEF0004 v3/7/8/9),
    `Core/FileSystem/RecycleBinInfoParser.cs` ($I v1/v2, `recycle_info` fingerprint in `EvidenceFingerprint`, import type by `$I` name + content),
    `Windows/Parsers/UserActivityParsers.cs` (`ShellBagsParser` for `usrclass_hive`, shared walker; RecentDocs, Office MRU, NTUSER BagMRU wired into `UserHiveParser` v1.1),
    `RawRegistry.ChildOffsets/ReadChild` + `RawKey.CellOffset` (linear tree walk, visited set by cell offset), UsrClass.dat detection in `InvestigationPipeline.RegistryHiveType`,
    capabilities/RuleContracts/FindingContract labels updated, parser count 17 -> 19 in two existing tests.
  - Tests: ShellItemsTests, RecycleBinInfoParserTests, UserActivityParserTests (54 new): every probe of the list above has a test (size 0/1/oversize, no terminator, nested list, depth, real cycle by patched list, node cap 20 000, bad MRUListEx, ext block size/version/offset, $I v1/v2 hostile, Office bad hex / no `*` / beyond-calendar FILETIME, unknown type with hex, damaged key -> gap + rest parsed).
  - Suites: Dfir.Tests 2036 passed / 30 skipped / 0 failed (baseline 1982); App.Tests 629 passed.

## Next
- vault-reviewer (Opus) with the probe list above. Known review points: long-name offsets of ext block v7/8/9 come from public docs, validated on synthetic data only (parser maturity TESTED); 0x74/0x61/0x00/network items are best effort; a damaged nk makes its PARENT unreadable (RawRegistry reads child names), so loss is one level above the damage, reported as a gap.
- Slice 2 (live collection of NTUSER.DAT / UsrClass.dat / `$Recycle.Bin\<SID>\$I*`), slice 3 (rules, UI, report rows, docs, comparison row E7).

## Review round 1 fixes (2026-10-21)
- v3 ext block long name at +0x14 (builder fixed, hand-written byte-array test); BagMRU walk checks visited before ReadChild + ct in loop + 40 000 read cap (shared-list n=200 test); missing MRUListEx -> anomaly gap, no "newest" time; descriptors honest (UserHive: new parts synthetic only; ShellBags cites UserActivityParserTests); `$I*` FileNames removed so a "file"-typed `$I` is skipped, not failed.
- Suites: Dfir.Tests 2041 passed / 30 skipped / 0 failed; App.Tests 629 passed.
