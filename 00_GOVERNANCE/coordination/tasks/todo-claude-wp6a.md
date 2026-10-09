# Checkpoint — claude-wp6a (LogAnalyzer WP6a: glossary, verification labels, finding card, accessibility baseline)

- **Task:** second part of queue item 7 of `00_GOVERNANCE/coordination/projects/LOGANALYZER_DFIR/STAGE2_PLAN.md`; WP6 in
  `docs/dfir/CONTRACT_AUDIT_STAGE1.md` §7, split by the orchestrator: **WP6a** (this PR) and **WP6b** (RO/EN resource layer, U17, next PR).
  Closes rows **R18.1-R18.12, U4, U5, U6, U7, U8** and the **U18** baseline for the investigation page. Read those rows and
  `docs/dfir/LOGANALYZER_PRODUCT_UX_CONTRACT.md` (state labels, finding card, Why, evidence levels, Know/Think/Don't know) first.
- **Branch:** `loganalyzer/wp6a-finding-card`, from main @ `1e30f285` (WP5 #250 merged).

Paths are relative to `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`.

## Already in place (reuse)
- `Finding`: title, description, severity, classification, `ClassificationReason`, `AlternativeExplanations`, `MissingEvidence`, next steps,
  `SupportingEvidence` (`EvidenceRef`), `AirGap`, `Sequence`; WP4 verifier verdicts (`LogAnalyzer.Verification`).
- `TimelineEvent` provenance (source, SHA-256, parser + version, locator), `ParseResult`, `EvidenceItem`.
- Detail sheet (`DetailSheet.cs`), `SeverityBadge`, InvestigationView findings grid, WP5 Home page and `CaseLoader`.

## Spec
1. **Glossary (R18.1-R18.12)** as data in `Dfir.Core` (one table: technical term → Romanian human phrase → English human phrase → short
   explanation). The 12 terms of R18 are the minimum (EVTX → „Jurnale Windows”, Prefetch → „Istoricul pornirii programelor”, BAM → „Activitatea
   programelor”, Amcache → „Istoricul aplicațiilor”, Evidence Graph → „Legături între evenimente”, Provenance → „De unde provine informația”,
   Chain of Custody → „Istoricul probei”, IOC → „Indicator suspect”, Sigma → „Regulă de detecție de securitate”, YARA → „Regulă de detecție pe
   fișiere/conținut”, Correlation → „Evenimente legate”, Verification → „Verificarea probelor”). The human phrase is shown in normal views; the
   technical name stays visible in advanced/technical views and as a tooltip. Apply to the sidebar, Home, the investigation page and the coverage matrix.
2. **Verification state labels (U7):** one mapping for all 10 states (Observat, Corelat, Susținut de dovezi, Verificat, Deducție, Nedemonstrat,
   Contrazis, Respins, Necunoscut, Neevaluat), used by the findings grid, the finding card, the graph view and reports. Remove the ad-hoc
   English wording paths (e.g. `GraphExplorer.Wording`, the converter in `ProcessContainmentView.xaml.cs`) by routing them through the mapping;
   behaviour of the views otherwise unchanged.
3. **Finding card (U4)** as a reusable control: title, human summary, severity (text + icon, never colour only), verification state,
   and the actions **De ce?**, **Arată dovezile**, **Verifică**, **Ce trebuie să fac**. The findings grid stays (add the card as the selected-item
   panel, do not remove the grid).
4. **Universal „De ce?” (U5):** observation, evidence, reasoning (`ClassificationReason`), limitations (`MissingEvidence`,
   `AlternativeExplanations`), verification verdict. Never only a percentage/score; where a legacy score exists it is shown only next to its factors.
5. **Evidence in three levels (U6):** summary line („4 probe din 2 surse”), list (source, time, short description), technical (EvidenceId, SHA-256,
   source path, parser + version, timestamp, locator). Reuse the detail-sheet data; do not duplicate it.
6. **Know / Think / Don't know (U8):** reusable control with a documented backend rule: CE ȘTIM = observed/verified statements with evidence;
   CE SUSPECTĂM = deductions/candidates (classification not Direct) with their reason; CE NU PUTEM DEMONSTRA = missing evidence, unobserved
   steps (WP14b), gaps that affect the finding. Unit-tested on the rule, not on XAML.
7. **Accessibility baseline (U18) on the investigation page, Home and the new controls:** `AutomationProperties.Name`/`HelpText` on interactive
   elements, keyboard access (tab order, access keys on the four actions), no colour-only status, text that scales (no fixed tiny fonts in the new
   controls), DataGrid column headers named. A test that scans the new XAML for interactive elements without an automation name.
8. **Both editions**, no page removed, no host settings changed. Romanian UI; English strings come with WP6b.
9. **Tests (TDD):** glossary completeness (12 terms, each with RO and EN), state mapping covers all 10 states, Know/Think/Don't-know rule,
   evidence levels from a fixture finding, the automation-name scan; App.Tests run only on Windows CI: grep App.Tests and UI.Tests for every text
   you change and update the assertions in the same commit.

## Verification required before push
- Build 0 errors. Dfir 48 / UI 6 known Linux-only failures only (diff names against the baseline); Edition green.
- Full Python suite once on the final head → 0 failed. New .md files listed in `20_TESTS/fixtures/unreadable_notes_allowlist.json`.
- Security gates fixed only through their documented exception mechanism, never by obfuscation.

## Done
- 2026-10-09T18:45Z claude-orchestrator: spec written; branch created.

## Next
- Implement 1-9.

## Blockers
- None known.
