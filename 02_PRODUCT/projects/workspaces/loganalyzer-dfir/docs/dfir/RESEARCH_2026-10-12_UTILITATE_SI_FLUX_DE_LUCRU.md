# Research 2026-10-12: usefulness and workflow (fewer pages, one screen per job)

> Owner concern: the application still makes the work too heavy: too many pages and steps, and unrelated tools nested together. This document
> looks only at angles the 2026-10-11 research did not cover. Already covered, not repeated here: plain-language result patterns and anti-patterns
> (`RESEARCH_2026-10-11_UX_NESPECIALISTI.md`), step-reduction patterns from a comparable tool and the proposed transfer-register fields
> (`RESEARCH_2026-10-11_COMPARATIE_INSPECTRA.md` §4–§5), rule noise, baselining, time zones and offline rule updates in DFIR tools
> (`RESEARCH_2026-10-11_DFIR_TRIAJ_CE_LIPSESTE.md` §3), NIS2 deadlines, RSIT taxonomy and IOC exchange (`RESEARCH_2026-10-11_CSIRT_RAPORTARE.md`),
> the legal checklist and gaps G1–G19 (`RESEARCH_2026-10-11_PROBE_VALOARE_JURIDICA.md`), device-control product features and Windows USB
> artefacts (`RESEARCH_2026-10-11_MEDII_DE_STOCARE.md`), and the offline data-pack update (`ANALYSIS_2026-10-11_APPLICATION_UPDATES.md`).
>
> Evidence labels: `CODE_VERIFIED` (read or grep in this worktree on 2026-10-12), `DOCUMENT_VERIFIED` (read in a public source), `CLAIMED_ONLY`
> (vendor or secondary source not checked against the primary), `[NESIGUR]` (uncertain). Effort: **S** < 1 day, **M** 1–3 days, **L** > 3 days
> (estimates, not measured). Quotes are at most one short phrase per source; the rest is summarised.

---

## 0. Executive summary

1. **The app is organised by technical module, the users think in jobs.** `LogAnalyzer.App/MainWindow.xaml` hosts 24 `TabItem` pages; the
   "Avansat (toate paginile)" expander alone lists about 21 of them (one, „Conturi locale și USB” / „Active Directory”, shares page 8) under four technical headings. Inside pages there is more nesting:
   `InvestigationView.xaml` has 11 sub-tabs, `PolicyView.xaml` 5, `MediaCenterView.xaml` 4, `StationControlView.xaml` 4 (`CODE_VERIFIED`).
   The role profiles (`LogAnalyzer.Core/Services/Edition/RoleProfile.cs`) already cut the main sidebar to 7–8 entries per role, which is the
   right direction, but the entries are still page names, and the job ("finish today's control and sign the report") still spans 4–5 pages
   with 5 separate PDF generators (`ControlReportPdf`, `ChecksReportPdf`, `TransferReportPdf`, `InvestigationReportPdf`, `IncidentPdfReport`).
2. **Pattern to adopt: a visit/case hub with a task list, wizards only inside a task.** The evidence (Fedora installer redesign, GOV.UK
   "Complete multiple tasks", NN/g progressive disclosure) says: strict wizards for short dependent sequences, a task list with statuses for long
   jobs done in any order or over several sessions, and never more than one level of "advanced". The controller's visit and the analyst's case
   are both long, multi-part jobs; today neither has a single place that says what is done and what is left.
3. **The transfer register is already a separate page** (commit `0154f9b80`, `TransferRegisterView.xaml`, sidebar entry
   `N("Registrul de transferuri", TabTransferRegister)` in `RoleProfile.cs`). What is still missing is what device-control products put next to
   such a register: reconciliation (every storage-medium session on the station must match a transfer or be flagged), request/approval states,
   and the register's summary inside the control report.
4. **DFIR lessons not yet applied:** a review state per finding that the analyst can set and undo, with comments that travel to the report and to
   later cases (Cyber Triage); results visible while processing still runs (Autopsy); a portable read-only case for the next reader (AXIOM);
   presets whose content is visible (KAPE compound targets are criticised as opaque).
5. **The CSIRT handover is not a report, it is a package**: summary, current status and next steps, evidence register with hashes, UTC timeline in
   a format the receiving tool loads, IOC list with confidence. The existing "Plaso / Timesketch CSV" export writes the legacy l2t header
   (`LogAnalyzer.Core/Services/SuperTimelineExportService.cs:20`) without the three columns Timesketch documents as mandatory.
6. **Confidence language:** the code has `Confidence { Low, Medium, High }` (`LogAnalyzer.Dfir.Core/Model/Enums.cs:12`) but no fixed verbal
   scale. Research on IPCC and intelligence wording shows that readers misread probability words unless each word is tied to a fixed range and the
   range is printed next to it.
7. **Measure "fewer clicks" as task success and time, not clicks.** Five-user rounds, repeated; task completion, time-on-task, SEQ per task, SUS
   per round; KLM to compare designs before building. The existing `docs/dfir/WP18_USABILITY_SCRIPT.md` already has the tasks; it lacks targets.

---

## 1. Task-oriented workflow for occasional, non-expert users

### 1.1 Findings

- **Wizard vs hub.** Fedora's installer team replaced a hub-and-spoke design with a wizard because users had to make repeated round trips to the
  hub and could not tell which spokes they still had to visit; the wizard lets a newcomer focus on one discrete task at a time
  ([Fedora Community Blog](https://communityblog.fedoraproject.org/?p=10855), [Fedora discussion](https://discussion.fedoraproject.org/t/anaconda-is-getting-a-new-suit-and-a-wizard/38076)).
  Design systems add the limits: long step content overwhelms, and in time-pressed work multi-step wizards feel slow
  ([Trimble Modus wizard](https://modus.trimble.com/components/web/wizard)). A hybrid "overview page that lists all steps and lets you jump" is the
  common compromise ([Pattern Playground, hybrid patterns](https://pattern-playground.onrender.com/patterns/hybrid-patterns)). `DOCUMENT_VERIFIED`
  (secondary summaries for the latter two).
- **Task list (GOV.UK "Complete multiple tasks").** Use it only for long transactions with several tasks that may need more than one session;
  first try to remove tasks; show it at the start and at every return; keep the statuses few (Completed / Incomplete, add In progress or
  "Cannot start yet" only if research shows a need); red only for a saved error
  ([GOV.UK Design System pattern](https://design-system.service.gov.uk/patterns/complete-multiple-tasks/),
  [GDS design notes 2023](https://designnotes.blog.gov.uk/2023/12/15/working-as-a-community-to-iterate-the-task-list-pattern/)). In 2023 the whole
  row became clickable and a hint line per task was added. `DOCUMENT_VERIFIED`.
- **Progressive disclosure.** What appears on the first screen tells users it is important; secondary options go to one subsidiary level; offering
  several routes into the same secondary options is usually a mistake; staged disclosure (wizards) is the sequential variant
  ([NN/g](https://www.nngroup.com/articles/progressive-disclosure/)). `DOCUMENT_VERIFIED` (partial read).
- **How inspection apps structure a visit.** Field inspection tools put a title page first with fields that fill themselves (date and time of the
  start, the inspector from the signed-in user, the site), then the questions, then corrective actions created from flagged items (owner, priority,
  due date), then a signature, then a report exported as PDF/Word/Excel/link
  ([SafetyCulture sample inspection](https://public-library.safetyculture.io/products/safetyculture-sample-inspection-kbyorsy6lwo501d0),
  [site safety check sheet](https://public-library.safetyculture.io/products/site-safety-check-sheet-pHMyU)). ISO 19011-style audits run
  preparation → opening meeting → evidence and findings → closing meeting → report → follow-up, and record strengths as well as weaknesses
  ([Advisera, 13 steps](https://advisera.com/9001academy/knowledgebase/13-steps-for-iso-9001-internal-auditing-using-iso-19011/),
  [The Auditor, closing meeting](https://www.theauditoronline.com/conducting-the-audit-closing-meeting-sharing-the-results/)). `DOCUMENT_VERIFIED`
  (templates, secondary summaries; the standard itself not read).
- **What reviewers complain about.** Inspection apps: approvals stall when photos, notes or checklist items are left incomplete, so somebody chases
  them; the mobile client is more limited than the desktop; report export is not flexible enough
  ([Capterra, SafetyCulture reviews](https://www.capterra.com/p/141080/iAuditor/reviews/), [GetApp](https://www.getapp.com/operations-management-software/a/iauditor/)).
  GRC suites: steep learning curve that comes from configurability and feature depth, dated interfaces, setup that needs specialist help, worst for
  small teams ([G2 ERM category](https://www.g2.com/categories/enterprise-risk-management-erm/enterprise),
  [InvGate roundup](https://blog.invgate.com/grc-software), [Tekpon, AdaptiveGRC](https://tekpon.com/software/adaptivegrc/reviews/)). `CLAIMED_ONLY`
  (aggregators).

### 1.2 What LogAnalyzer does today (`CODE_VERIFIED`)

- Role-specific sidebar of 7–8 pages plus "Avansat (toate paginile)" with about 21 more (`MainWindow.xaml` lines ~130–185; `RoleProfile.cs` lines 89–175).
- Home has at most five intents per role, guarded by a test (`RoleProfiles.Violations`, `RoleProfile.cs:181`).
- The control itself is a 3-step wizard ("Ce stație? / Ce perioadă? / Ce proceduri?") ending in a result page (`docs/utilizare/GHID_DE_UTILIZARE.md` §4).
- The rest of a visit lives on other pages: media (`MediaCenterView.xaml`, 4 tabs), transfers (`TransferRegisterView.xaml`), users register,
  manual checks (sub-tab "Verificări făcute de dumneavoastră" in `StationControlView.xaml`), and the report (button on the result page, plus a
  separate transfer-register PDF).
- The "Scopul cazului stației" dialog appears only when the first PDF is requested (guide §4.5), i.e. at the end, not on a title page.

### 1.3 Gap

There is no single "visit" object on screen. The controller must know that a complete control = station checks + manual checks + media review +
transfer review + scope + report, and must visit each page. Context fields are asked late (scope at PDF time). Nothing shows "3 of 5 done".

### 1.4 Recommendations

- **W1 (M).** Controller Home becomes the visit's task list: *Datele controlului* (title page: station, controller, period, scope — prefilled),
  *Verificarea automată*, *Verificări făcute de dumneavoastră*, *Suporturi și transferuri*, *Raport și semnare*. Statuses only: „De făcut”,
  „În lucru”, „Gata”, „Nu se poate încă” (with the reason). Whole row clickable, one hint line each. Resume on reopen.
- **W2 (S).** Move the "Scopul cazului stației" fields to the first task (title page) and prefill from the previous control; never ask at PDF time.
- **W3 (S).** Keep wizards only inside a task (the 3-step control stays); no wizard spans pages.
- **W4 (M).** Flagged items → "Ce urmează" actions with owner and due date, printed in the report and shown at the next control (ISO follow-up).
- **W5 (S).** Record "în regulă" areas explicitly in the report (auditors record strengths), not only non-conformities.

---

## 2. What DFIR practitioners praise and criticise (new lessons only)

### 2.1 Findings

- **Review state with undo, and comments that persist.** Cyber Triage scores items Bad / Suspicious and lets the examiner set Good (never
  overridden by automation) or Unknown (undo a score without declaring the item safe); comments on an item go into the report and are shown to
  later investigations that meet the same item; the summary groups bad items by ATT&CK tactic; the first job is described as confirming Bad
  and deciding Suspicious ([Cyber Triage, scores and labels](https://docs.cybertriage.com/en/latest/chapters/scores.html),
  [host-level examination](https://docs.cybertriage.com/en/latest/chapters/analysis.html),
  [3.14 release](https://www.cybertriage.com/blog/3-14-release-brings-new-uis-hayabusa-baselining-and-much-more/)). `DOCUMENT_VERIFIED`.
- **Review while processing runs.** Autopsy runs ingest modules in the background and shows results "as soon as" modules report them; older
  versions had an inbox of new results with read/unread tracking
  ([Autopsy 4.4 quick start](https://sleuthkit.org/autopsy/docs/user-docs/4.4/quick_start_guide.html),
  [4.21 quick start](https://sleuthkit.org/autopsy/docs/user-docs/4.21.0/quick_start_guide.html)). `DOCUMENT_VERIFIED`.
- **Portable case praised, full processing before preview criticised.** AXIOM users praise a portable case that partner investigators can open;
  they complain about long processing on large cases, slow opening, and not being able to preview without processing everything
  ([G2 Magnet Forensics reviews](https://www.g2.com/products/magnet-forensics-magnet-forensics/reviews_and_filters),
  [Software Advice](https://www.softwareadvice.com/digital-forensics/magnet-axiom-profile)). `CLAIMED_ONLY` (review sites).
- **Presets must show their content.** KAPE compound targets (e.g. `!SANS_Triage`) are convenient, but what they collect depends on nested
  targets the user must inspect; environment-specific targets live in a separate local folder
  ([TryHackMe KAPE room](https://tryhackme.com/room/kape), [Velociraptor triage pt 1](https://docs.velociraptor.app/blog/2019/2019-10-02_triage-with-velociraptor-pt-1-253f57ce96c0/)).
  `DOCUMENT_VERIFIED` (training material; no first-hand complaint thread found).
- **Stacking and simple filters instead of a query language.** Velociraptor added GUI table transformations and built-in stacking because most
  analysts only need to sort, filter and count; before, counting by category required a second query
  ([Rapid7 0.6.5](https://blog.rapid7.com/2022/06/24/velociraptor-version-0-6-5-table-transformations-multi-lingual-support-and-better-vql-error-handling-let-you-dig-deeper-than-ever/),
  [Velociraptor 0.72](https://docs.velociraptor.app/blog/2024/2024-03-10-release-notes-0.72/)). `DOCUMENT_VERIFIED`.
- **Severity floor as the default view.** Hayabusa filters by minimum level and its summary lists the top rules
  ([Hayabusa v4.0.0 release](https://newreleases.io/project/github/Yamato-Security/hayabusa/release/v4.0.0)). `CLAIMED_ONLY` (release notes mirror).

### 2.2 What LogAnalyzer does today (`CODE_VERIFIED`)

- Findings carry `Confidence { Low, Medium, High }` (`LogAnalyzer.Dfir.Core/Model/Enums.cs:12`) and the "Ce știm / Ce suspectăm / Ce nu putem
  demonstra" panel (`LogAnalyzer.App/Views/Controls/KnowThinkDontKnowPanel.xaml.cs`).
- Analyst marking of findings (false positive / accepted risk) is proposed but open (`RESEARCH_2026-10-11_UX_NESPECIALISTI.md` R13;
  `RESEARCH_2026-10-11_CSIRT_RAPORTARE.md` gap 10).
- Investigation results sit in 11 sub-tabs of one page (`InvestigationView.xaml`): Lanțul incidentului, Constatări, Integritate air-gap, Secvențe,
  Cronologie, Graful probelor, Anti-forensics, Cronologie politici, Analiză AI, Colectare la distanță, Goluri de probă. Several of these are
  views of the same findings (air-gap, sequences, anti-forensics) or of the same timeline.
- Separate "Amenințări și detecții", "Analiza riscului", "Tehnici de atac (MITRE)" and "Firul atacului" pages exist outside the case page
  (`MainWindow.xaml`), i.e. the same findings can be reached from four places.

### 2.3 Gap

No per-finding review state owned by the analyst; no "start reading while the pipeline runs"; no read-only case package for the next reader;
collection presets do not list their content up front; the same findings are spread over many tabs and pages.

### 2.4 Recommendations

- **D1 (M).** One "Constatări" list per case with a review column: „Neverificat”, „Confirmat”, „Fals pozitiv”, „Normal aici” (= Good, sticks),
  „Nesigur” (undo). Comment field; the comment goes to the report and is offered again when the same rule + object appears in a later case.
  Air-gap integrity, sequences, anti-forensics and MITRE become **filters/groupings** of this list, not tabs or pages.
- **D2 (M).** Show findings as soon as each analyser finishes (progressive results, "N noi" badge); never block the case page until the whole
  pipeline ends.
- **D3 (M).** "Caz portabil": a read-only export (report + findings + timeline + evidence manifest + viewer-friendly HTML or the app in read-only
  mode) for the CSIRT or legal reader.
- **D4 (S).** Every collection preset shows, before it runs, the exact list of sources it will read, the estimated size, and what it cannot read
  without administrator rights.
- **D5 (S).** Default severity floor (Medium+) on the findings list with the hidden count shown ("12 constatări Low ascunse — arată").

---

## 3. Device control and transfer registers: how products separate the jobs

### 3.1 Findings

- **Separate stages.** Ivanti's documented workflow is: deploy → monitor in audit mode (devices still work, everything logged) → build device and
  media collections → write policies → tune → report gaps and compliance
  ([Ivanti Device Control workflow](https://help.ivanti.com/ht/help/en_US/IES/2023/UG_DC/device-control-workflow.htm)). Inventory, policy, logs
  and reports are distinct modules. `DOCUMENT_VERIFIED`.
- **Reports are their own area.** Endpoint Protector's "Reports and Analysis" groups system logs, device-control logs and shadows, file tracing
  (with a hash per traced file and the device serial), admin actions and statistics; exports are filtered then generated as CSV/XLSX/PDF
  ([Netwrix docs, reports](https://docs.netwrix.com/docs/endpointprotector/admin/reports),
  [export KB](https://docs.netwrix.com/docs/kb/endpointprotector/administration-security-and-monitoring/export-event-logs-from-reports-and-analysis)).
  `DOCUMENT_VERIFIED`.
- **Offline exception = request → approval → time-limited code → audit.** Endpoint Protector's offline temporary passwords are requested with a
  justification, scoped to device/computer/user, valid 30 minutes to 30 days
  ([Netwrix, offline temporary password](https://docs.netwrix.com/docs/endpointprotector/admin/otpassword)); DeviceLock's temporary white list
  uses a device code valid 24 h, signed by the administrator into an unlock code
  ([Acronis DeviceLock docs](https://www.acronis.com/en/support/documentation/DeviceLock/docs/15-DeviceLock-Temporary-White-List.15.2.htm));
  Ivanti's endpoint wizard asks device class, permissions, lifetime and user, and the admin confirms the request key
  ([Ivanti, temporary permission offline](https://help.ivanti.com/ht/help/en_US/IDAC/latest/device-control/create-temporary-permission-offline.htm)).
  `DOCUMENT_VERIFIED`.
- **Role separation inside the console**: some admins may only see reports, help-desk staff may only issue temporary passwords
  ([Endpoint Protector blog](https://endpointprotector.com/blog/?p=1931)). `CLAIMED_ONLY`.
- **What users ask for**: better and more flexible reporting, a unified interface flow; reviewers cite limited reporting and an interface that
  takes time to learn ([SaaSworthy](https://www.saasworthy.com/product/endpoint-protector/reviews),
  [GetApp](https://www.getapp.co.uk/software/119015/endpoint-protector)). `CLAIMED_ONLY` (small samples).

### 3.2 What LogAnalyzer does today (`CODE_VERIFIED`)

- `MediaCenterView.xaml`: tabs „Acum”, „Toate dispozitivele”, „Protecțiile Windows”, „Registrul de medii”; live alert banner on every page
  (`MainWindow.xaml` "Vezi dispozitivul" / "Am văzut").
- Transfer register: own page since commit `0154f9b80` (`TransferRegisterView.xaml`, `TransferViewModel.cs`), one prefilled form, SHA-256 per
  file, automatic gap-free number, append-only with audit chain (`LogAnalyzer.Dfir.Core/Registers/TransferRegister.cs`), PDF
  (`LogAnalyzer.Dfir.Windows/Registers/TransferReportPdf.cs`).
- `TransferChecks.Check` cross-checks each transfer against the media register (medium registered, active, level not above the medium's)
  (`TransferRegister.cs:144`). The reverse check — a storage session seen on the station with **no** transfer entry — does not exist: no rule in
  `LogAnalyzer.Dfir.Core/Analysis/Wp14Rules.Media.cs` reads the transfer register (grep).
- A transfer row has `Approver` and `Witness` but no state (requested / approved / executed / cancelled).

### 3.3 Gap

The register and the evidence do not meet: the controller cannot see "sessions without a transfer entry" in the control result. The control PDF
does not summarise the transfer register for the period, so a separate PDF is needed. There is no request/approval state, so "approved before
it happened" cannot be shown.

### 3.4 Recommendations

- **M1 (M).** Reconciliation check in the station control: every storage-medium session in the control period is matched to a transfer row (serial
  + time window); unmatched sessions → DE VERIFICAT with the session evidence; transfers with no session on the station → note.
- **M2 (S).** The control report includes a one-table summary of the transfer register for the control period (count, unmatched, problems) and
  the media-register discrepancies; the full register PDF stays an annex.
- **M3 (M).** Optional states on a transfer: „Cerut” → „Aprobat” (different person) → „Efectuat” → „Verificat” / „Anulat”; each change in the
  audit chain. Stays one form for the common case (approve-and-execute at once by two signed-in people).
- **M4 (S).** Keep media pages split by job: *Suporturi* (live + inventory + Windows protections), *Registrul de medii*, *Registrul de
  transferuri*. Reports for both registers reachable from the report task, not only from their pages.

---

## 4. What a CSIRT expects from a triage (handover package)

### 4.1 Findings

- **Handover content.** A 2026 study of CSIRT shift handovers lists: current situation, work done, steps under way, likely next steps; participants
  also wanted a post-incident review section and a section on tooling problems
  ([arXiv 2601.07788](https://arxiv.org/html/2601.07788v1)). `DOCUMENT_VERIFIED`.
- **Package parts seen across practitioner guides**: non-technical summary (impact, scope, actions), technical report with the evidence chain, a
  separate machine-readable IOC file with a confidence label per indicator, evidence register (ID, collector, time, SHA-256, location, transfers),
  UTC timeline with a source per row and noted clock drift, open questions, encrypted delivery
  ([aiwg forensics methodology](https://cdn.jsdelivr.net/npm/aiwg@2026.7.12/docs/frameworks/forensics-complete/methodology.md),
  [simplified.guide](https://www.simplified.guide/network-security/incident-network-evidence-collect),
  [university CSIRT form](https://www.calstatela.edu/sites/default/files/its-2817_departmentrepresentativeschecklist.doc)). `CLAIMED_ONLY`
  (practitioner material, not a standard).
- **Timeline format.** Timesketch CSV/JSONL import requires `message`, `datetime` (ISO 8601 with offset) and `timestamp_desc`; missing columns can be
  mapped by hand in the upload dialog ([Timesketch import guide](https://timesketch.org/guides/user/import-from-json-csv/)). `DOCUMENT_VERIFIED`.
- **ENISA guidance** on Implementing Regulation (EU) 2024/2690 (June 2025, non-binding) gives, per requirement, examples of evidence that show
  compliance, including incident handling ([ENISA publication](https://www.enisa.europa.eu/publications/nis2-technical-implementation-guidance)).
  Implication for the report: each claim about handling should point to an artefact (log, record, decision) — the evidence-first structure
  LogAnalyzer already uses. Specific retention figures circulating in blogs were not found in the ENISA text `[NESIGUR]`.

### 4.2 What LogAnalyzer does today (`CODE_VERIFIED`)

- STIX 2.1 and MISP JSON exports (`LogAnalyzer.App/ViewModels/MainViewModel.cs:1653–1683`, `StixMispExportService`); the MISP type bug is already
  logged in the CSIRT research.
- "Super-Timeline Plaso / Timesketch CSV" export with the legacy l2t header `Date,Time,Timezone,MACB,…`
  (`LogAnalyzer.Core/Services/SuperTimelineExportService.cs:20`); no `timestamp_desc` anywhere in non-test code (grep). Timesketch would need manual
  column mapping.
- `DfirCasePackagingService` zips a case with a manifest and the timeline (`LogAnalyzer.Core/Services/DfirCasePackagingService.cs:43–86`), wired
  from `MainViewModel` (the older shell), not from the role-based "Raport de incident" intent.
- Report audiences: Management, ItSecurity, Forensic, IncidentResponse, Audit, Everything (`LogAnalyzer.Dfir.Core/Reporting/ReportHeaderProfile.cs:54`).

### 4.3 Gap

The pieces exist but in different places (old shell exports, new case page, report dialog). There is no "current status / next steps / open
questions" section, no IOC confidence label, and the timeline is not loadable by Timesketch as is.

### 4.4 Recommendations

- **C1 (M).** One action „Predă cazul (pachet CSIRT)” on the case: report PDF for the chosen audience + `timeline.jsonl` and `.csv` in Timesketch
  columns (UTC) + IOC as STIX 2.1, MISP and plain CSV + evidence manifest with SHA-256 + chain-of-custody log, all listed and hashed in one
  manifest. Reuse `DfirCasePackagingService`.
- **C2 (S).** Add `message`, `datetime` (ISO 8601 UTC), `timestamp_desc` columns to the timeline export (keep l2t as a second option).
- **C3 (S).** Report section „Situația acum”: contained / open / next three steps / open questions / tooling problems (from the handover study).
- **C4 (S).** Confidence label per IOC (confirmat / probabil / neconfirmat) and per finding in exports.

---

## 5. Reports that non-technical readers (managers, legal) can use

### 5.1 Findings (new versus the legal research)

- **Fixed probability words with numbers.** ICD 203 uses seven likelihood terms with bands (almost no chance 1–5 %, very unlikely 5–20 %,
  unlikely 20–45 %, roughly even 45–55 %, likely 55–80 %, very likely 80–95 %, almost certain 95–99 %) and asks products to state the basis and
  the analyst's confidence separately ([ICD 203 bands as transcribed in MISP taxonomies](https://eugit.opencloud.lu/MISP/misp-taxonomies/commit/16306f55d88a20b4ec14fc5fab3189aae8481726);
  primary: dni.gov ICD 203). The UK yardstick uses deliberately non-continuous bands (remote chance ≤5 %, highly unlikely 10–20 %, unlikely 25–35 %,
  realistic possibility 40–<50 %, likely 55–75 %, highly likely 80–90 %, almost certain ≥95 %) chosen to match average readers' understanding
  ([GOV.UK, explaining uncertainty](https://www.gov.uk/government/publications/explaining-uncertainty-in-uk-intelligence-assessment/explaining-uncertainty-in-uk-intelligence-assessment)).
  `DOCUMENT_VERIFIED` (UK), `CLAIMED_ONLY` (ICD 203 transcription).
- **Words alone are misread.** Across 24 countries, laypeople read IPCC probability words as closer to 50 % than intended; adding the numeric range
  next to the word improved agreement and differentiation; readers rarely opened a separate definitions table
  ([Budescu et al. 2014, Nature Climate Change](https://ideas.repec.org/a/nat/natcli/v4y2014i6d10.1038_nclimate2194.html),
  [Fordham summary of the 2009 study](https://now.fordham.edu/?p=12425)). `DOCUMENT_VERIFIED` (abstracts).
- **Forensic evaluative reporting.** The ENFSI guideline recommends evaluating findings against two competing propositions, preferably at activity
  level, with a verbal scale unified within the institution; critics note cliff-edge effects of verbal scales
  ([ENFSI guideline v3.0 copy](https://roebuckforensics.com.au/wp-content/uploads/2024/07/ENFSI-Guideline.pdf),
  [NFI news](https://www.forensicinstitute.nl/news/news/2015/05/19/european-guideline-for-evaluative-reporting-in-forensic-science)).
  `DOCUMENT_VERIFIED` (summary). For a station control this means: state the question ("was data copied to an unregistered medium?"), the two
  explanations, and which the evidence supports, not an unexplained "High".

### 5.2 What LogAnalyzer does today (`CODE_VERIFIED`)

- `Confidence { Low, Medium, High }` without a printed definition; the result card asks „Sunt probele de încredere?” (guide §4.2).
- A hedge-word filter exists for AI text (`LogAnalyzer.Ai/EvidenceReasoner.cs:115`) but there is no controlled vocabulary for the report.
- Audience-specific reports exist (§4.2); the separation fact / interpretation / conclusion is gap G12 of the legal research.

### 5.3 Recommendations

- **R1 (S).** One Romanian likelihood scale used everywhere a conclusion is stated, always printed with its range, e.g. „foarte puțin probabil
  (10–20 %)”, „probabil (55–75 %)”, „aproape sigur (≥ 95 %)” (UK bands, non-continuous); a test that rejects other probability words in report
  templates.
- **R2 (S).** Keep two separate statements per conclusion: *cât de probabil* (scale) and *cât de solide sunt probele* (sources read, gaps) — never
  merged into one "High".
- **R3 (S).** Management report page 1 = verdict sentence, impact, decisions needed, deadlines; everything else after. For findings that support a
  disciplinary or legal step, print the question and the competing explanation (ENFSI style).

---

## 6. Usability testing a small team can run, and targets for "fewer clicks"

### 6.1 Findings

- **Five users per round, several rounds.** Nielsen: five users find most problems of a uniform user group; spend the budget on three rounds of
  five with fixes in between rather than one study of fifteen ([NN/g](https://nngroup.com/articles/why-you-only-need-to-test-with-5-users/)).
  Critics show single groups of five can miss many problems, hence the rounds ([Keylime](https://info.keylimeinteractive.com/is-5-users-for-early-usability-research-enough-its-about-iterative-design)).
  Controllers and CSIRT analysts are two different groups: five each. `DOCUMENT_VERIFIED`.
- **Clicks are the wrong metric.** A reanalysis of 620 tasks found no correlation between click count and success, and users did not quit after
  three clicks more than after twelve ([Center Centre / UIE, testing the three-click rule](https://articles.centercentre.com/?p=17)).
  `DOCUMENT_VERIFIED`. Measure success, time, and perceived ease.
- **Standard scores.** SUS average 68 (50th percentile), ≥ 80 is a common industrial goal ([MeasuringU SUS](https://measuringu.com/interpret-sus-score/));
  SEQ (one 7-point question after each task) averages about 5.5 ([MeasuringU SEQ](https://measuringu.com/seq10/)). `DOCUMENT_VERIFIED`.
- **Predict before building.** The keystroke-level model estimates expert time from operators (pointing ≈ 1.1 s, button ≈ 0.1 s, mental
  preparation ≈ 1.35 s) and is meant for comparing designs, not novices ([KLM, Wikipedia summary](https://en.wikipedia.org/wiki/GOMS)).
  `DOCUMENT_VERIFIED` (secondary).
- **Accessibility check for a WPF app.** Accessibility Insights for Windows FastPass: automated UIA checks plus a recorded tab-stop test, under five
  minutes, recommended before each UI check-in ([FastPass](https://accessibilityinsights.io/docs/windows/getstarted/fastpass/)). `DOCUMENT_VERIFIED`.

### 6.2 What LogAnalyzer does today

`docs/dfir/WP18_USABILITY_SCRIPT.md` defines scenarios A (controller, 5 tasks), B (duty officer, 5 tasks) and C (analyst), timing and "stops > 30 s",
success = no help and no "Avansat". No numeric targets, no SEQ/SUS, no baseline run recorded (S6 still `PARTIAL` per the script).

### 6.3 Recommendations and targets

- **U1 (S).** Add to the script: SEQ after every task, SUS after each scenario, and the targets below; record a **baseline round now** (before the
  navigation change) so the improvement is measured, not claimed.
- **U2 (S).** KLM estimate of the controller visit (start → signed PDF incl. media and transfer review) for today's design and for the proposed
  hub; build the cheaper one.
- **U3 (S).** FastPass run on every page touched; zero automated failures; tab order follows the visual order.

| Measure | Target (round 3) | Note |
|---|---|---|
| Scenario A completion without help, without „Avansat” | 5/5 users | today unmeasured |
| Visit time A2→A5 (control + media + transfers + report) | ≤ 15 min on a prepared station, median | set real value after baseline |
| SEQ per task | ≥ 5.5 each | MeasuringU average |
| SUS per role | ≥ 68 round 1, ≥ 80 round 3 | MeasuringU |
| Pages visited for a complete control | ≤ 3 (Home hub, task page, report) | today ≥ 5 (`CODE_VERIFIED` page list) |
| Separate PDFs to produce for one control | 1 (annexes inside) | today 2–3 |
| Terms asked „ce înseamnă …?” | 0 repeated across users | each one goes to the glossary |

---

## 7. Prioritised backlog (top 15)

| # | Item | Angle | Effort | Why first |
|---|---|---|---|---|
| 1 | Controller Home = visit task list with statuses and resume (W1) | 1 | M | removes page-hunting; owner's main complaint |
| 2 | Scope/title-page fields at the start, prefilled (W2) | 1 | S | removes the late blocking dialog |
| 3 | Reconciliation: storage sessions without transfer entry → DE VERIFICAT (M1) | 3 | M | where the register and the evidence meet |
| 4 | Control report includes media + transfer register summary; one PDF per visit (M2) | 3 | S | one signed document instead of 2–3 |
| 5 | Baseline usability round with SEQ/SUS and targets (U1) | 6 | S | proves the change helps |
| 6 | One findings list with review state + comments that persist (D1) | 2 | M | collapses 5+ tabs/pages; analyst feedback loop |
| 7 | Case hub for CSIRT role: Probe → Constatări → Cronologie → Răspuns → Predare (§8) | 1, 2 | L | same pattern as the visit hub |
| 8 | Handover package action (C1) | 4 | M | what the receiving CSIRT actually needs |
| 9 | Timesketch-ready timeline columns (C2) | 4 | S | small fix, removes manual mapping |
| 10 | Likelihood scale with ranges + separate evidence strength (R1, R2) | 5 | S | prevents misreading by managers/legal |
| 11 | Progressive results while the pipeline runs (D2) | 2 | M | analyst starts reading earlier |
| 12 | „Situația acum” section and IOC confidence labels (C3, C4) | 4 | S | handover continuity |
| 13 | Collection presets show their content and admin needs (D4) | 2 | S | trust, fewer failed collections |
| 14 | Transfer states Cerut/Aprobat/Efectuat (M3) | 3 | M | approval before execution becomes visible |
| 15 | Portable read-only case for the next reader (D3) | 2 | M | legal/CSIRT readers without the full app |

---

## 8. Proposed navigation

Principle: the sidebar lists **jobs of the role**; each job is one page; a page has at most one level of tabs; technical views become filters of a
job page; everything else stays under one „Instrumente avansate” group, never nested inside a job page. Page numbers refer to the current
`TabItem` indices in `MainWindow.xaml` / constants in `RoleProfile.cs:68`.

### 8.1 Control station role (Stație de control)

| New sidebar entry | Contains (merges) | Current pages |
|---|---|---|
| **Acasă — Controlul de azi** | visit task list (W1), last control, title-page task | Home (22) |
| **Verificarea stației** | 3-step wizard, result, Verificări, Verificări făcute de dumneavoastră, Utilizatori, Cronologia acțiunilor | Control stație (14) |
| **Suporturi** | Acum, Toate dispozitivele, Protecțiile Windows | Registru medii (19) minus the register tab |
| **Registrul de medii** | the register only, „Preia de pe stație” | tab of (19) → own page |
| **Registrul de transferuri** | form, list, problems, reconciliation (M1) — *already separate since `0154f9b80`* | Transfer (23) |
| **Raport și semnare** | pre-report check, one PDF with annexes, copy to medium, open | buttons on (14) + `TransferReportPdf` |
| *Administrare (admin only, collapsed)* | Registru utilizatori, Profil de proceduri, Politici, Persoane și carduri | 20, 18, 17, 21 |

Removed from the controller sidebar: „Investigație completă (caz)” (16) — reachable only through the intent „Analizează probe aduse de pe altă
stație”, which opens the CSIRT-style case page in its own window or mode.

### 8.2 Incident support role (Stație de sprijin răspuns la incidente)

| New sidebar entry | Contains (merges) | Current pages / sub-tabs |
|---|---|---|
| **Acasă — Cazul** | case task list: Probe primite, Constatări revizuite, Cronologie verificată, Răspuns, Predare; deadlines | Home (22) |
| **Probe** | collect here, import, remote collection, evidence gaps, chain of custody | Colectare date (12), Istoricul probelor (9), sub-tabs „Colectare la distanță”, „Goluri de probă” of (16) |
| **Constatări** | one list with review state (D1); filters: air-gap, secvențe, anti-forensics, MITRE tactic, risc | Amenințări și detecții (3), Analiza riscului (5), MITRE (10), sub-tabs „Constatări”, „Integritate air-gap”, „Secvențe”, „Anti-forensics” of (16) |
| **Cronologie** | one timeline with views: lanțul incidentului, toate evenimentele, politici | Cronologia incidentului (4), Firul atacului (6), sub-tabs „Lanțul incidentului”, „Cronologie”, „Cronologie politici” of (16) |
| **Răspuns** | containment with confirmation, response-action log | Izolare procese suspecte (13) |
| **Raport și predare** | audience report, handover package (C1), NIS2 notification | report dialog of (16), exports in `MainViewModel` |
| *Instrumente avansate (collapsed)* | Explorator EVTX (1), Registru Windows (2), Graful probelor, Analiză AI, Informații despre amenințări (7), Domeniu și e-mail (15), Monitorizare în timp real (11, network edition), Panou general (0) | |

### 8.3 What becomes separate rather than nested

- **Transfer register** — separate (done); next: reconciliation and report summary (M1, M2).
- **Media register** — separate from live/inventory *Suporturi*, because the register is an administrative record edited by the administrator,
  while *Suporturi* is observation.
- **Live monitoring** — its own tool page, never mixed with case tables (the senior review already separated live events from case data).
- **Policies (GPO import/approval/apply)** — administration, not part of a control visit; out of the controller's job list.
- **AI analysis, evidence graph, raw EVTX/registry explorers** — expert tools, one level under „Instrumente avansate”.

Effect (estimate, to be measured by U1/U2): controller sidebar from 8 page entries + about 21 hidden to 6 job entries + 4 admin; CSIRT case work from
1 page × 11 tabs + 6 related pages to 6 job pages with at most 3 views each.

---

## 9. Risks and unknowns

- The SafetyCulture, GRC and device-control complaints come from review aggregators with small samples (`CLAIMED_ONLY`).
- ICD 203 bands were taken from a secondary transcription; check the dni.gov PDF before printing them. The proposal uses the UK bands, which were
  read at the primary source.
- Merging tabs changes many views; the role-profile guard tests (`RoleProfiles.Violations`) and `UserGuideAppendixTests` must follow the new
  structure; the user guide must be rewritten with it.
- The l2t vs Timesketch import behaviour was read from documentation, not tested with an actual upload `[NESIGUR]`.
- No user test has been run yet; all page-count and time targets above are hypotheses until the baseline round (U1).
