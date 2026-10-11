# Review: investigation report usefulness, grammar and terminology (2026-10-12)

Input: a real investigation PDF produced on the owner's development PC on 2026-10-11 (9 pages), read as text, plus the source of
every string. Read-only review (independent reviewer); code facts are CODE_VERIFIED by reading, term sources are marked.
Owner's demand: the report must be useful for every reader (controller without IT knowledge, IT/CSIRT analyst, manager, legal),
Romanian grammar must be correct, and ad-hoc translated terms must be replaced with established terminology.

Defects already fixed before this review (commit 46a7d828d and a85e9cf75): event-log export failure (EvtExportLog flag 0x10000 → 0x2000),
Defender Platform folder treated as user-writable, NVIDIA service arguments classified as the binary path, `\\?\` prefix in AF13,
T1052.001 on unknown/authorised media.

Abbreviations: IRP = `LogAnalyzer.Dfir.Windows/Investigation/InvestigationReportPdf.cs`, FC = `LogAnalyzer.Dfir.Core/Analysis/FindingContract.cs`,
RC = `…/Analysis/RuleContracts.cs`, COR = `…/Analysis/Correlation.cs`, MED = `…/Analysis/Wp14Rules.Media.cs`, AF = `…/Analysis/AntiForensics.cs`,
VR = `LogAnalyzer.Verification/VerificationReport.cs`.

## 0. False statements (most severe first)
1. "REZULTATE INVALIDE … nu mai pot fi atribuite sursei achiziționate" is printed for an empty Prefetch file whose SHA-256 still matches.
   `EvidencePreflight.cs:108` maps a format mismatch to UNVERIFIED; `ReportIntegrity.cs:16-20` counts it as invalid; `InvestigationPipeline.cs:239`
   repeats it. Fix: a separate notice ("fișier gol sau format neașteptat; amprenta este intactă"); "invalid" only for a changed hash (MUTATED).
2. "ceasul sistemului a fost schimbat" appears in 13 findings while AF05 says there was no manual clock change. `TimeFacts.cs:60-62` counts every
   Kernel-General 1 (routine time sync) — AF:157-160 filters svchost/Reason≠1, TimeFacts does not. It also prints "încrederea LOW → LOW" (`TimeFacts.cs:84`).
3. LOG-TAMPER (F-0001) holds only Security 1100 at normal shutdowns but says "Ștergerea e dovedită", "Activitatea dinaintea ștergerii nu mai e
   în jurnal", has T1070.001 and the title "Jurnale șterse…" (`RC:44-45`, `COR:699-706`).
4. F-0007/F-0008 come from Amcache (presence only) but are titled "Program rulat" with "Execuție dovedită (Prefetch)" (`COR:579,583`, `COR:601`).
5. F-0006 is CONTRADICTED only because the correlated partner's evidence lies outside its own window (`Checks.cs:137-141`, `COR:581-584`); the
   shared-folder correlation (`COR:572`) has no time limit, so any program in %TEMP% links to any %TEMP% script months apart.

## A. Usefulness per reader
- Page 1 lacks: a "Concluzie pe scurt" box (no compromise established / what is serious / what could not be checked); "Ce înseamnă pentru
  dumneavoastră"; "Pași următori" (`RecommendedNextSteps` and `HumanSummary` are computed but IRP never prints them); "Ce poate și ce nu poate
  dovedi acest raport"; for legal readers, that this is not a technical-scientific finding report or expert report (art. 172 C.proc.pen.; judgement).
- Misleading headline: "13 RIDICATE" in red, mostly Defender/Lenovo/Teams; "Nicio potrivire (3 reguli aplicate)" reads as clean.
- Repetition: generic "Limită:" lines in every finding (FC:73-86); irrelevant parser limits (ServicesParser on USB findings); `Take(6)` (IRP:120)
  drops the relevant limit (alphabetical order); 23 identical "cod 87" errors (`Collectors.cs:56`); 7 Defender/Lenovo tasks as separate findings;
  AF04 says 35 anomalies, shows 20 without saying so (AF:146); F-0009 repeats names.
- Readability: English labels (HIGH/CORRELATED/Execution/tip: CORRELATION, IRP:109,111); raw `\VOLUME{guid}` paths; short hashes with "…" and the
  full value nowhere; each time twice (local and UTC); section numbers 1,2,3,5,6; "Alternativă:" means "innocent explanation" in findings but
  "alternative source" in gaps.

## B. Grammar (original → corrected → source)
| Original | Corrected | Source |
|---|---|---|
| `prefetch/prefetch_mam)..` | `…prefetch_mam).` | EvidencePreflight.cs:155, IRP:89 |
| `Failed, cod 87: The parameter is incorrect.` | `eșuat, cod 87 (parametru incorect)` | Collectors.cs:56, EventLogExport.cs:38 |
| `formatul „empty”` | `fișierul este gol` | EvidencePreflight.cs:155 |
| `1 observații` / `(1 evenimente)` | `1 observație` / `(1 eveniment)` | MED:221, Wp11Rules.Vss.cs:80 |
| `de 70 ori`, `132 programe`, `35 anomalii`, `74 evenimente`, `2 din 80 servicii`, `3078 intrări` | Romanian "de" after 20+ (`de 70 de ori`, `132 de programe`, …, `3.078 de intrări`) | COR:394, COR:596, AF:146, AuditCoverage.cs:38, AF:285, AF:336 |
| `19,988` | `19.988` (ro-RO) | IRP:65 |
| `2 Verificat … 1 Contrazis`; `ATENȚIE: 1 CONTRADICTED … în Vault` | `2 coroborate … 1 contrazisă`; `1 constatare contrazisă … în baza de cunoștințe` | VR:138, VR:149-150 |
| `nu poate dovedi: Că fișierul` | `nu poate dovedi că fișierul` | FC:81, ParserCapabilities.cs:141-171 (test FindingContractTests.cs:141) |
| `planificată. serviciul de jurnalizare` | capital after full stop | LogClearAssessment.cs:135-143 |
| `…nedefinit). registru nedefinit:` | `…). Registrul nu este definit:` | MED:88,211,223 |
| `Prezență, nu copiere …; prezență, nu copiere.` | once | MED:216,218 |
| `aprobată … dar necompletată în profil` | `aprobată …, dar netrecută în profil` | Wp11Rules.Tools.cs:101 |
| `Software aprobat în profil este Info (aprobat în profil).` | `Programele aprobate în profil apar doar informativ.` | RC:234 |
| `fereastra revendicată`; `nu e de încredere` | `intervalul declarat`; `nu este de încredere` | Checks.cs:141,155 |
| `Prefetch-ul lui X referă:` | `…face referire la:` | COR:408 |
| `fișier de log al uneltei` on a registry key | label by real source type | Wp11Rules.Tools.cs:71 |
| `Live:Services`, `EventLog:System`, `AmcacheParser` | `instantaneu servicii`, `jurnalul Sistem`, `Amcache` | CaseVerifier.cs:138, FC:81 |
| `UNKNOWN`, `DETECTED`/`UNDETERMINED`, `NOT_AVAILABLE` | `NEDETERMINAT`, `DETECTAT`/`NEDETERMINAT`, `INDISPONIBIL` | MED:229, AF:13, IRP:89,156,187 |
| `GTB Standard Time`; `orele marcate UTC sunt universale` | `ora României (UTC+03:00)`; `orele cu „UTC” sunt în timp universal, restul în ora stației` | IRP:44, ReportFooter.cs:37 |

## C. Glossary (MS = Microsoft ro-RO UI, from memory — confirm in the Microsoft Language Portal; LEG = standard legal vocabulary; J = judgement)
| Current | Recommended | Basis |
|---|---|---|
| locație scriabilă | locație în care utilizatorii pot scrie | J ("scriabil" is not in DEX) |
| Susținut de dovezi / Verificat | Susținută de o singură sursă / Coroborată (două surse independente) | LEG ("probe coroborate") |
| Corelat / Observat / Deducție / Candidat | Asociată (în timp sau cale) / Observată direct / Dedusă / Indiciu – de confirmat | J; LEG ("indicii") |
| Contrazis / Necunoscut | Contrazisă / Nedeterminată | J (agreement with "constatare") |
| încredere MEDIUM | grad de încredere: mediu | J |
| Probă / dovadă | element de probă / probe, one word throughout | LEG (art. 97 C.proc.pen.) |
| Goluri de probă | Surse lipsă sau incomplete | LEG/J |
| Ancoră custodie / audit | Amprenta lanțului de custodie (ultima înregistrare nr. …) | LEG/J |
| Istoricul probei (Chain of Custody) | Lanțul de custodie | LEG |
| Jurnale șterse / golit | golirea jurnalului | MS |
| copie shadow | copie umbră (Volume Shadow Copy) | MS (confirm) |
| mediu amovibil; registru | suport de stocare amovibil; "registrul de evidență a suporturilor" vs "registry" | J/LEG; MS "Editor registry" |
| task programat | activitate planificată (Planificator de activități) | MS |
| unealtă de acces la distanță | aplicație de acces de la distanță | MS/J |
| mascarare | deghizare (already in ItemMeaning) | J |
| Anti-forensics | Ascunderea sau ștergerea urmelor (anti-forensics) | J |
| fotografie live | instantaneu al stării live | J |
| timeline / parsat | cronologie / citit și interpretat | J |
No Romanian translation of ISO/IEC 27037/27041/27042/27043 was found; none was relied on.

## D. Priority
1. Fix the five false statements. 2. Page 1: verdict, meaning, next steps, what the report proves and does not prove. 3. Romanian state names,
English codes only in an end legend. 4. Generic limitations once; relevance filter instead of `Take(6)`. 5. Group repeats. 6. Glossary.
7. Grammar table. 8. One time convention (local with UTC in brackets), Romanian time-zone name. 9. Readable paths, full hash once, a legal audience.
10. Consistent section numbering.

## Risks and unknowns
- AF04 "RecordID reutilizat" and AF05 inversions cluster at boot within milliseconds: possibly a record-reading order artefact, not reuse.
  Not checked; it drives a red DETECTED (T1070.001).
- Limitation text changes break the exact-string assertion at `LogAnalyzer.Dfir.Tests/FindingContractTests.cs:141`.
