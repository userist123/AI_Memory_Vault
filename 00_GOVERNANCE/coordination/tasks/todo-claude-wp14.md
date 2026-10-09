# Checkpoint — claude-wp14 (LogAnalyzer WP14: air-gap integrity and media control)

- **Task:** queue item 6 (second part) of `00_GOVERNANCE/coordination/projects/LOGANALYZER_DFIR/STAGE2_PLAN.md`.
  - Lessons-learned rows 15-22, 48, 50-53, 55 and 103 (`docs/dfir/LESSONS_LEARNED_MAPPING.md`, WP14 line).
  - Owner decisions in `docs/dfir/CONTRACT_AUDIT_STAGE1.md` §8:
    - 16: media register with registration number;
    - 17: clearances entered manually, one global admin;
    - 19: zones and transfers;
    - 21: classification levels;
    - 24: no "Conform" on absence.
- **Split:**
  - **WP14a** (this branch): registers plus observation.
  - **WP14b** (next PR): combined sequences.
    - Control gap followed by media activity (row 63).
    - SMB/NAS → staging → USB (row 69).
    - Portable software + USB + large archive (row 82).
    - Classified document → print → scan → USB (with WP13/WP16a).
- **WP14a branch:** `loganalyzer/wp14a-media-airgap`, from main @ `b02c4a43`. **Merge main after WP11-T1 (#245) merges, before
  implementing**, because WP14a reuses its rules and data-list pattern.

Paths are relative to `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`.

## Already in place (reuse)
- USB: `LogAnalyzer.Dfir.Windows/Parsers/UsbDevicesParser.cs` gives serial, VID/PID, first install, last arrival/removal and
  drive letter. The `Partition/Diagnostic` channel is collected.
- Network: NetworkProfile 10000/10001, WLAN-AutoConfig 8001/8003, `StationFacts.cs`.
- Station controls: `LogAnalyzer.Dfir.Windows/Audit/ControlEvaluator.cs` D01 (USB) and N01/N02 (network).
  - **D01 still returns `ControlStatus.Conform` when nothing is observed.** That violates decision 24, so fix it: absence →
    "nu s-a observat în sursele colectate" (DeVerificat/Nedeterminat with coverage reason), never Conform.
  - Check N01 the same way.
- WP15a procedure profile: zones, approved transfer channels and authorized network destinations (decision 19).
- WP3: `CaseScope` network category (air-gapped / standalone / connected) and classification.
- WP11-T1: rule pattern (`Wp11Rules*.cs` partials, `Analysis/Data/*.json` lists, `RuleContracts.cs`).
- WP4: the verifier gives every new finding a verdict.

## WP14a spec
1. **Classification levels (decision 21)** in `Dfir.Core/Model`:
   - NATO: COSMIC TOP SECRET, NATO SECRET, NATO CONFIDENTIAL, NATO RESTRICTED.
   - EU: TRES SECRET UE/EU TOP SECRET, SECRET UE/EU SECRET, CONFIDENTIEL UE/EU CONFIDENTIAL, RESTREINT UE/EU RESTRICTED.
   - National (HG 585/2002 / Legea 182/2002): strict secret de importanță deosebită, strict secret, secret, secret de serviciu.
   - No "unclassified" level (decision 21). An item without a level is "nemarcat" (not a level).
   - An equivalence table (NATO ↔ EU ↔ national) used only for comparison. It is data, and its source is cited in a doc
     comment.
2. **Media register (decision 16):**
   - A new `Dfir.Core/Registers/MediaRegister.cs` with these fields:
     - registration number;
     - serial;
     - VID/PID (optional);
     - type (USB / HDD / SSD / CD / DVD / other);
     - classification (point 1);
     - assigned user;
     - zone;
     - validity from/to;
     - status (active / withdrawn / destroyed);
     - notes.
   - Manual entry plus CSV/JSON import, with per-line validation errors (same UX as the WP15a profile).
   - Store: `%PROGRAMDATA%\LogAnalyzer\registers\media.json`.
   - Each case using the register gets a snapshot with SHA-256 in custody (`RecordOutput`).
   - Every register edit goes to an append-only hash-chained register audit (reuse `HashChain`), with who/when/before/after.
3. **Users and clearances register (decision 17):**
   - Fields:
     - person;
     - account(s) (DOMAIN\user, SID optional);
     - clearance level(s) (point 1) with validity;
     - need-to-know notes;
     - zones allowed.
   - Entered by the global administrator.
   - The app has no user authentication yet. Implement the register as data with the same audit chain. The edit UI shows
     "editare permisă administratorului global; autentificarea în aplicație nu este încă implementată". Record the
     authentication gap under Blockers. Do NOT invent a login system in this PR.
4. **Observed media vs register:**
   - Observed media are USB from UsbDevicesParser, plus CD/DVD.
   - Each observed medium is classified AUTHORIZED / REGISTERED / UNREGISTERED / UNAUTHORIZED / UNKNOWN:
     - AUTHORIZED: registered, active, valid at the observed time, and the user/zone match;
     - REGISTERED: registered but the user/zone/validity does not match;
     - UNREGISTERED: serial not in the register;
     - UNAUTHORIZED: withdrawn/destroyed or out of validity;
     - UNKNOWN: no serial, or no register defined (empty register = "registru nedefinit", never "conform").
   - Rule `MEDIA-UNREGISTERED`, `MEDIA-UNAUTHORIZED`, etc. in `RuleContracts.cs`. Severity:
     - on a classified case scope: UNREGISTERED Medium, UNAUTHORIZED High;
     - unclassified scope: one level lower.
5. **USB completeness and CD/DVD (rows 16-17):**
   - USB: Kernel-PnP / DriverFrameworks-UserMode (2003/2100/2102), Partition/Diagnostic 1006 and Storage events where
     collected; first/last connection per device.
   - CD/DVD as a separate artifact: IMAPI events / `cdrom` device class, LNK `DriveType=CDROM`, `.iso` mounts (VHDMP / Storage),
     burn tools via the data list.
   - "Write/copy to medium" is claimed only with evidence (LNK/JumpList target on that volume, 4663 with object on that drive,
     USN on removable volume). Otherwise say "prezență, nu copiere".
6. **NIC / Wi-Fi / Bluetooth / DHCP on air-gapped scope (rows 21, 22, 48):**
   - These apply only when the case scope network category is AirGappedNetwork or StandalonePc.
   - Sources:
     - NIC enable/disable / new adapter (Kernel-PnP net class, NetworkProfile);
     - WLAN 8001 (SSID) / 11000-11005;
     - Bluetooth (BTHUSB / BthEnum device install, Bluetooth pairing events where collected);
     - DHCP-Client/Operational 50036/50037/50065/1103.
   - Rules: `AIRGAP-NETWORK-CONNECTED`, `AIRGAP-WIFI-ASSOCIATED`, `AIRGAP-BLUETOOTH-PAIRED`, `AIRGAP-DHCP-LEASE`, `AIRGAP-NIC-ADDED`.
   - A connection to an authorized destination or zone channel in the profile is Info.
   - Add the DHCP-Client channel to `EventLogCollector.Channels` (absent channel = "indisponibil").
7. **AIR-GAP INTEGRITY category (rows 50, 103):**
   - `Finding.Category = "Air-gap integrity"` with a subcategory (data list from row 50).
   - Each air-gap finding carries: channel, authorized? (profile), observed, when, who, object, classification (case /
     register), transfer direction, destination, evidence.
8. **Fix D01/N01** "Conform on absence" (above), with tests.
9. **UI:**
   - App views "Registru medii" and "Registru utilizatori" (edit / import / paste / validate, like the WP15a profile view),
     reachable from the sidebar.
   - The investigation view lists air-gap findings in their category.
   - No page removed. Both editions.
10. **Tests (TDD):**
    - classification table round trip;
    - register validation/import;
    - each media status;
    - empty register → "nedefinit";
    - each air-gap rule positive and negative, including an authorized channel → Info;
    - CD/DVD distinct from USB;
    - "prezență, nu copiere" without write evidence;
    - D01/N01 no longer Conform on absence;
    - Edition green.

## Verification required before push
- Build 0 errors.
- Dfir 48 / UI 6 known Linux-only failures only; Edition green.
- Full Python suite once on the final head → 0 failed.
- New .md files are listed in `20_TESTS/fixtures/unreadable_notes_allowlist.json`.
- Security gates are fixed only through their documented exception mechanism, never by obfuscation (COMMON_EXEC rule).

## Done
- 2026-10-09T12:05Z claude-orchestrator: spec written; branch created.

- 2026-10-09 claude-wp14a (agent): items 1-3 done and tested (`Model/SecrecyLevels.cs`, `Dfir.Core/Registers/*`: media + users registers, CSV/JSON import,
  store, hash-chained `register_audit.jsonl`, case snapshot with SHA-256 in custody). `Wp14SecrecyLevelTests`, `Wp14RegisterTests`.

- 2026-10-09 claude-wp14a: item 8 (D01/N01/N02 no Conform on absence) done; items 4-7 done: `Wp14Rules*.cs` (MEDIA-* x7, AIRGAP-* x5), `Wp14Data` + `Data/airgap_lists.json`,
  `Finding.AirGap` (AirGapDetail), `Wp14Analysis` called by `InvestigationPipeline`, new EVTX channels + capabilities, 12 RuleContracts entries,
  python rule-catalog producers extended. Dfir tests: 48 known Linux failures only.

- 2026-10-09 claude-wp14a: item 9 done (`RegisterViewModel<T>` + `RegisterView`, sidebar "Registru medii" / "Registru utilizatori", investigation tab "Integritate air-gap"
  with a "zona sistemului" field); `LESSONS_LEARNED_MAPPING.md` rows 15-18, 21, 22, 48, 50, 53, 55, 103 updated. Item 10 tests: `Wp14*Tests` (Dfir), `RegisterViewModelTests` (App, Windows only).

## Next
- Final merge of origin/main, full .NET + Python verification, final push (see the report). WP14b (combined sequences) is the next PR.

## Blockers
- App user authentication does not exist. Decision 17's "only the global admin creates accounts" cannot be enforced in-app yet.
  The register is data plus an audit chain; enforcement needs an owner decision on authentication (Windows account / local PIN /
  smart card).
- Defaults chosen without an owner decision (change them in one place if the owner differs):
  - Case scope with classification "Unspecified" is treated like unclassified for severity (only an explicit Classified scope gets the higher level).
  - MEDIA-REGISTERED / MEDIA-UNKNOWN: Low on classified, Info otherwise; MEDIA-AUTHORIZED: Info. AIRGAP-NETWORK-CONNECTED and -WIFI-ASSOCIATED: High on classified
    (Medium otherwise), -BLUETOOTH-PAIRED / -DHCP-LEASE / -NIC-ADDED (physical): Medium (Low otherwise), authorised by the profile: Info.
  - The zone of the analysed system is not part of the case scope; the operator declares it in the investigation view ("zona sistemului"). Undeclared zone: a medium
    whose register row names a zone is REGISTERED (zone not confirmable), never AUTHORIZED.
  - The register has no withdrawal/destruction date, so a withdrawn/destroyed medium is UNAUTHORIZED for any observation (the finding says the observation may predate it).
  - The equivalence table NATO/EU/national (Legea 182/2002 art. 15, HG 585/2002) is entered from decision 21; the owner should check it against the text in force.
  - LNK / JumpList / USN rows carry no device serial, so activity is tied to a medium only through the drive letter MountedDevices recorded last (stated in the finding).
  - Kernel-PnP 400 is used for "adapter added"; 410 (started) is not, because it fires at every boot. Enable/disable of an adapter has no event in the collected sources.

