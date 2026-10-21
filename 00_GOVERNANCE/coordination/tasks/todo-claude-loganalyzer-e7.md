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

## Review round 2 fixes (2026-10-21, final)
- Findings: (1) `$I` record with original extension (`$I...lnk/.evtx/.pf/.automaticDestinations-ms`) typed by extension, deletion record lost -> `ImportType` checks `$I` name + `recycle_info` content BEFORE the extension switch. (2) MaxBagReads bounded reads, not work (200 keys x 20 000 subkeys = 7.4 s measured) -> `MaxBagNameReads = 2_000_000` on subkey names read, own gap text ("peste N nume de subchei BagMRU, limita parserului de lucru"), testable via `maxNameReads` parameter (default = production constant). (3) raw NUL byte in ShellItemsTests.cs replaced by `\0` (git sees text).
- Tests added: 4-case theory + non-`$I` .lnk/.evtx keep type (RecycleBinInfoParserTests), name-budget test (UserActivityParserTests). Dfir.Tests 2047 passed / 30 skipped / 0 failed; App.Tests 629 passed.
- Review rounds used: 2 (max). Slice 1 CLOSED pending owner.
- Next: slice 2 (done, below), then slice 3.

## Slice 2 done (2026-10-21, vault-worker Sonnet, NOT yet reviewed)
- `LogAnalyzer.Dfir.Windows/Acquisition/UserArtifactsCollector.cs` (new; `CollectionProfile.FullForensic` only; elevation required), registered in
  `InvestigationPipeline.AllCollectors`; coverage family "registry" lists it. NTUSER.DAT + `AppData\Local\Microsoft\Windows\UsrClass.dat` of every
  ProfileList SID via `esentutl /y src /vss /d dest` (same mechanism as Amcache/SRUM, no new copy path); `$Recycle.Bin\<SID>\$I*` of every fixed
  volume copied directly; `$R` existence/size only, written into the evidence notes (`RecycleBinInfoParser.RNote`), content never copied.
- `RecycleBinInfoParser` v1.1: for items collected by `UserArtifactsCollector` (collector name + "acquired" checked; imports ignore the note) the `$R`
  state recorded at collection wins over "is $R next to the stored copy" (else every collected `$I` would read "absent"); new field `RSize`.
- UI label "Complet (+ hive-uri utilizatori, Coș $I)"; docs: GHID §4.6, WINDOWS_TOOLING_COMPAT (esentutl row, §2.5 artifacts), capability text.
- Tests: `UserArtifactsCollectorTests` (47 incl. theory cases), end to end through `InvestigationPipeline.Run(collect:false)`: UserHiveParser,
  ShellBagsParser, RecycleBinInfoParser parse the collected items; RContent=present + RSize come from collection. Dfir.Tests 2094 passed / 30 skipped /
  0 failed (baseline 2047); App.Tests 629 passed.
- Not verified on a real station (no elevated run with real VSS in this session): esentutl /vss on the NTUSER.DAT of a logged-on user, real ProfileList.

## Slice 2 probe list (same list to the reviewer)
- Reparse points / junctions / symlinks: every existing component from the volume root to the hive is checked (`ReparseOnPath`) before the VSS copy;
  `$Recycle.Bin` itself, SID folders and `$I` entries are checked by attribute and skipped with a note; after reading a `$I` its path is re-checked and
  the copy deleted if it became a link. Tests: junction as profile folder, junction `AppData\Local` on the way to UsrClass, `$Recycle.Bin` junction,
  SID-folder junction, `$I`-named junction. Residual: TOCTOU between the check and esentutl's own open; file symlinks not tested (need the
  create-symlink privilege), same attribute check applies.
- SID names that are not SIDs: ProfileList keys (e.g. `.bak`) and `$Recycle.Bin` folders failing the SID pattern are skipped with a gap note.
  Tests: `Profile_keys_and_recycle_folders_that_are_not_sids_are_skipped_with_a_note`, `Sid_check` theory.
- Caps: 200 profiles, 10 000 `$I` per SID, 20 000 total, `$I` over `RecycleBinInfoParser.MaxInfoFileBytes` (128 KiB) not copied (checked on size and
  on bytes read), 1 000 SID folders per bin, 200 000 entries examined per SID folder; every cap hit is a gap. Tests: profile cap, per-SID + total cap,
  oversize `$I`, production values pinned.
- VSS snapshot always released: esentutl owns its non-persistent snapshot; the collector lists shadow copies (WMI) before and after, in `finally`
  (also on failure/cancellation; the "VSS used" flag is set before each copy) and reports + audits (`vss.leftover`) any new one; it does not delete it
  itself (destructive, not authorized). Unlistable shadows -> gap only when VSS was used. Tests: leftover reported + audited, cancellation inside the
  copy still checks, unlistable list.
- File changing during copy: hives come from the snapshot (immutable while read), hashed on registration. `$I` read twice: the first read is written
  and hashed; a different or failed second read -> note on the evidence + gap. No existing source/copy hash comparison found to follow.
  Tests: changed second read (hash = first read), unchanged says so.
- Path traversal in names: `$I` names must be a single child name (`IsSafeChildName`: no separators, `.`/`..`, `:`, invalid chars, trailing dot or
  space), stored under `Raw/UserActivity/RecycleBin/<vol>/<SID>/` with `SafeName` + full-path containment check; profile paths must be fully qualified
  local drive paths without `..`, UNC or unexpanded `%`. Tests: `Child_name_check`, `Profile_path_check` theories, `..` profile refused before any copy.
- Gaps (Romanian): not elevated (NotAvailable), VSS copy failed (exit code; partial file deleted), ProfileList unreadable, profile folder missing or
  unreadable, hive missing / unreadable / a folder, Recycle Bin unreadable / not a folder / a link, caps. Not testable here: ACL-unreadable folder.

## Next (after slice 2)
- Slice 2 review (2026-10-21, main session: the router sent the Agent reviewer to a local model, refused for high-risk code): probes checked in code and
  tests (ProfileImagePath UNC/relative/../%, reparse on path and in $Recycle.Bin, TOCTOU re-check after the $I copy, ArgumentList for esentutl, SID-validated
  destination folders, CreateNew + Unique, caps, leftover shadow reported not deleted, $R spoof needs Collector=UserArtifactsCollector + acquired).
  Fixed: `ReparseOnPath` failed open on an unreadable component; now refuses the path. Residual: TOCTOU between the hive reparse check and esentutl.
  Suites after the fix: Dfir.Tests 2094 passed / 30 skipped / 0 failed (before the one-line fix), related 89/89 after; App.Tests 629.
- Real station run 2026-10-21 (owner's station, elevated without a UAC prompt, owner approval in chat): `LiveUserFact` test
  `UserArtifactsLiveTests` (admin + `LADFIR_LIVE_USER=1`, prints counts only): collector Success, 11 items (3 NTUSER, 3 UsrClass, 5 $I), 0 gaps,
  no leftover shadow; esentutl /vss copies the logged-on user's NTUSER.DAT. Pipeline: UserHiveParser and ShellBagsParser Success; timeline
  ShellBags 2174, RecentDocs 644, Office MRU 122, Recycle Bin 5. Long names (ext block v7/8/9) validated: 1 of 912 items with an 8.3 name kept
  the short name only. Unknown shell item types 113 of 2174 (0x6F 13, 0x52 12, 0xAD 9, 0x7E 5, 0x01 4, ...): reported with hex, candidates for a
  later decoder pass. Parse gaps 2 (BagMRU missing in two other profiles' UsrClass.dat: correct).
- Hive transaction logs (.LOG1/.LOG2) are not copied (dirty hives may miss recent writes): owner decision whether to add them (2 more snapshots per profile).
- Slice 3 (rules, UI tab, report rows, docs, comparison row E7).

## Slice 3 done (2026-10-21, vault-worker Sonnet, NOT yet reviewed)
- Engine `Core/Analysis/UserActivityAnalysis.cs` (pure: rows, relevance, time meaning per source, `ReportLines`), rules `Wp14Rules.UserActivity.cs`
  (`USER-ACTIVITY-CLASSIFIED/-REMOVABLE/-DELETED`, DE VERIFICAT = Candidate + Medium at most; possible-removable letters Low; one finding per folder / letter,
  no NECONFORM from these traces), 3 catalog contracts in `RuleContracts.cs` (CLASSIFIED-ACCESS text corrected: it no longer says "no rule uses them").
- Source strings: `ShellBags`, `RecentDocs`, `OfficeMRU` (not "Office MRU"), `RecycleBin`. RecentDocs holds only the name: it never matches a folder (stated).
- Removable: by drive letter. Confirmed = letter in USBSTOR (USB source) device events (Partition events carry no letter) or `AirGapFacts.RemovableLetters`; other letters than C: = "possible" (Low),
  except paths inside a configured classified folder (their letter is not guessed). Letter reuse caveat in the finding text.
- UI: tab "Activitatea utilizatorului" on the INVESTIGATION page (`InvestigationView.xaml`, `InvestigationViewModel.FillUserActivity`, cap 5 000 rows). The
  "Informații clasificate" tab lives on the live Station Control, which has no timeline (StationFacts), so it was not used. `InvestigationResult.UserActivityView()`.
- Reports: investigation PDF (after "Limitări generale", inside the Findings section: no new ReportSection), control PDF section 3b via
  `ControlReport.UserActivity` (null today in the control flow: the line says "nu a fost evaluată de acest raport"; wiring an investigation of the same station
  into the control is a follow-up, not forced). JSON of the control report carries `UserActivity` when set.
- No hook into ClassifiedAccess exits / anti-forensics: those records are audit-based (4663/4660); mixing weak $I traces into the exit verdicts would change
  their evidence class. Not forced.
- Docs: GHID §4.7, comparison row E7 (HAVE for the four artifacts with the real-station counts; Zone.Identifier / MOTW NOT done), no check-guide annex exists for these rules.
- Tests: `UserActivityRulesTests` (21, Dfir.Tests), `UserActivityUiTests` (6, App.Tests).
- Suites after slice 3: Dfir.Tests 2115 passed / 31 skipped / 0 failed; App.Tests 635; UI.Tests 353; Edition.Tests 45.
- Next: Opus review of slice 3 (probe list: hostile paths in Analyze, wording against the semantics above, no NECONFORM), then PR order unchanged.

## Slice 3 review round 1 (Opus) and fixes (2026-10-21, claude-sonnet)
- 1 MEDIUM reopened case: `Analysis/user_activity.json` (UserActivityContext: classified folders used, removable letters, parser Fields of the 4 sources) written by the pipeline, read by CaseLoader; absent or not matching timeline.csv => "neevaluabil la redeschidere: ..." (no relevance, no "nedefinite").
- 2 MEDIUM known folders: `KnownFolders` (Dfir.Core/FileSystem) resolves Documents/Desktop/Downloads/Pictures/Music/Videos/profile by GUID with the profile folder (note "Profil <SID> (<path>)" or hive path ...\Users\name\NTUSER.DAT / ...\AppData\Local\Microsoft\Windows\UsrClass.dat) and Rețea\server\share to \server\share; field `ResolvedPath`, displayed path kept. Limits (redirected folders, unresolved profile) in the contract and GHID 4.7.
- 3 wording: RecentDocs never matches (contract); letters confirmed by USBSTOR and AirGapFacts only (comment and this file); "alias" wording removed.
- 4 comparison row E7: real run validated collection and parsing only. 5 `Drive()` requires an ASCII letter; tests for control characters and 50 000 events.
- Suites: Dfir.Tests 2131 passed / 31 skipped / 0 failed; App.Tests 636; UI.Tests 353; Edition.Tests 45.
- Next: round 2 reviews only this delta; then the PR order unchanged.

## Slice 3 review round 2 and final owner decisions (2026-10-21, claude-sonnet)
- Round 2 findings (owner-side decision after the last review, no further rounds): (1) the drive letter was taken from the resolved path; (2) known folders were
  resolved from the hive path of an import (the examiner's machine) and from a note on any item; (3) the known-folder GUID was only read from the first BagMRU item,
  so Explorer's default "Acest PC" -> Documente layout never resolved; (4) CaseLoader: a mismatching context said "nu au fost salvate" and a malformed or huge
  user_activity.json refused the whole case.
- Implemented, each with a regression test (UserActivityFinalTests, UserActivityReviewTests):
  1. `UserActivityAnalysis.Build`: Drive/DriveLetter/Possible/Removable only from the displayed path; ResolvedPath only for classified-folder matching.
  2. `KnownFolders.ProfileDirOf(EvidenceItem)`: only the note "Profil <SID> (<path>)" of an item with Collector == UserArtifactsCollector and AcquisitionMethod == "acquired"
     (same trust rule as the $R state); no fallback to the hive path; device prefixes in the note refused. Imported: ResolvedPath empty, row field `ResolutionNote`
     ("nu au putut fi plasate pe discul stației"), shown in the row detail; disclosed in RuleContracts.cs and GHID 4.7.
  3. Parser: first known-folder root GUID at any depth anchors (`Frame.RootIndex`), later segments appended; synthetic hive "Acest PC\Documente\Clasificat" resolves and USER-ACTIVITY-CLASSIFIED fires.
  4. `UserActivityContext.Read` caps the file at 64 MB; CaseLoader catches Json/InvalidData/IO: only the view becomes "neevaluabil la redeschidere: <cause>", findings still load;
     mismatch text says the saved context does not match timeline.csv.
- Suites: Dfir.Tests 2138 passed / 31 skipped / 0 failed; App.Tests 636; UI.Tests 353; Edition.Tests 45.
- E7 status: DONE pending owner. Next: PR after #303 and the device-monitor PR (order unchanged).
- Not done / later: unknown shell item types 0x6F/0x52/0xAD/0x7E/0x01 (decoder pass); .LOG1/.LOG2 copying (owner decision); Zone.Identifier / MOTW not done;
  `ControlReport.UserActivity` has no production caller yet (the control PDF says "nu a fost evaluată de acest raport").
