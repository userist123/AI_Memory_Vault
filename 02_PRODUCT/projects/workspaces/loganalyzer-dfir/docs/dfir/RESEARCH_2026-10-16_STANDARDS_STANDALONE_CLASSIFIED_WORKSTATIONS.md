# Research: protecting classified information on standalone (air-gapped) workstations

Date: 2026-10-16. Scope: public sources only. Status of every statement: `DOCUMENT_VERIFIED` when the
cited text was read in this session (or is quoted verbatim in the repository's own verified quote file),
`CLAIMED_ONLY` / `UNVERIFIED` where marked. Engineering proposals are labelled **[design]** and are not
requirements of any standard.

Terminology used below: RO "stație de lucru autonomă" / "SPAD exploatat în mod autonom" = standalone
workstation; SIC = CIS; AAS = Agenția de Acreditare de Securitate (RO security accreditation authority);
CSTIC = the unit's ICT security component led by the "funcționar de securitate TIC"; SAA = Security
Accreditation Authority; SecOPs = PrOpSec (RO) = Security Operating Procedures.

## Sources read

| Key | Document | Where |
|---|---|---|
| HG585 | HG 585/2002, Annex (national standards), INFOSEC chapter, art. 244-328 and art. 79 | Verbatim quotes and cross-check in `HG585_QUOTE_SOURCES.md` / `HG585_ACCREDITATION_REQUIREMENTS.md` (this folder). Consolidated public copies: the URL used there was not recovered; `sie.ro/pdf/legislatie/585.pdf` returned a non-PDF page in this session. |
| I2 | ORNISS Order 16/2014, INFOSEC 2 "Directiva principală privind domeniul INFOSEC", MO 262/10.04.2014 | https://legeaz.net/monitorul-oficial-262-2014/ordinul-guvernului-romaniei-orniss-16-2014/anexa-directiva-principala |
| DS2 | ORNISS Order 18/2014, guide on structure/content of PrOpSec for SIC (DS 2), MO 242/04.04.2014 | https://legeaz.net/monitorul-oficial-242-2014/ordinul-guvernului-romaniei-orniss-18-2014/anexa-ghid |
| SRI-SSv | SRI note on accreditation of SIC handling "secret de serviciu" (model documentation, 23 annexes) | https://www.sri.ro/assets/files/informatii-clasificate/Precizari_referitoare_la_acreditarea_SIC_SSv.pdf |
| NATO-MD | AC/35-D/2005-REV2, NSC "INFOSEC Management Directive for CIS", 18 Oct 2010, NATO UNCLASSIFIED, publicly disclosed | https://bip.abw.gov.pl/download/1/1213/AC35D2005REV2.pdf |
| EU-C | Council Decision 2013/488/EU, Annex III (handling) and Annex IV (Information Assurance), Art. 10 | https://www.legislation.gov.uk/eudn/2013/488/annex/IV ; https://www.legislation.gov.uk/eudn/2013/488/annex/III ; https://www.legislation.gov.uk/eudn/2013/488/article/10/data.html |
| EU-COM | Commission Decision (EU, Euratom) 2015/444, Chapter 5 (Art. 34-38) | https://www.legislation.gov.uk/eudn/2015/444/chapter/5 |
| NIST | SP 800-53 Rev. 5, AU-9 and enhancements | https://csf.tools/reference/nist-sp-800-53/r5/au/au-9/ |
| DE | BayVSA Anlage 5, VS-NfD-Merkblatt (Bavaria, 2025) | https://www.verkuendung-bayern.de/files/baymbl/2025/571/anhang/Anlage_05.pdf |

## 1. Summary table

"Auto" = what a station-control tool can check on the workstation itself. "Org" = what only the
organisation / AAS can confirm (the tool can at most record the declaration and who made it).

| # | Requirement | Source | Auto (tool can verify) | Org (needs confirmation) |
|---|---|---|---|---|
| 1 | Standalone systems are not exempt; AAS sets specific protection measures for a permanently disconnected SPAD | HG585 art. 282; NATO-MD ¶46(b) (outline SSRS for small standalone) | No network adapters active / no connections; profile = standalone | Accreditation statement, mode of operation, AAS-specific measures |
| 2 | Automatic, software-enforced access records ("registre de acces") for classified electronic information; retention agreed AAS-CSTIC | HG585 art. 291 | Audit policy enabled (logon, object access on classified folders), log size/retention vs configured value, gaps in record sequence | Retention period value, which folders are classified |
| 3 | Security logs sufficient to trace compromise; retention and analysis periods approved by AAS | I2 art. 43 | Log channels present, not cleared, continuous | AAS-approved periods |
| 4 | PrOpSec must define logged events, periodic audit review, responsibility, real-time alert reaction, retention; protection/overwrite of audit data | DS2 art. 27(2)(a)-(g), art. 25(2)(f) | Evidence of review (reviewer sign-off records in tool), alert handling | Content of PrOpSec |
| 5 | Security staff check audit information for failures and unauthorised activity | NATO-MD ¶8(i) | Report lists unauthorised attempts, failures | That review was done by security staff, not the admin |
| 6 | Separation of duties: no single person knows/accesses all programs and security keys; programming vs operation separated | HG585 art. 275-276 | Admin accounts distinct from user accounts; admin is not the audit reviewer in the tool's role model | Staffing of roles |
| 7 | Two-person rule for any modification to the system | HG585 art. 277; DS2 art. 21(1)(a.3) | Config changes in the tool require a second approver; detect OS changes (software installs, policy changes) and flag missing approval | Who the second person was, physically |
| 8 | IA functions may be combined only if conflicts of interest are avoided | EU-C Annex IV ¶43 | Warn when same identity holds admin + auditor roles | Formal designation |
| 9 | Media identified (serial number + classification mark), defined issue/receipt/withdrawal/destruction procedures, content/level records; register for top levels | HG585 art. 294; NATO-MD ¶92-93 | Per-device serial (USB VID/PID/serial), whitelist vs register, first/last connection, files written/read | Physical marking, paper register entries |
| 10 | Periodic spot checks of physical presence and content of media (at least annually for the highest level) | HG585 art. 295; NATO-MD ¶8(f), ¶93 | Scheduled reminder; content scan of presented media vs register | Physical presence |
| 11 | Media keep the highest classification ever stored; erase only per PrOpSec; destroy if not declassifiable | HG585 art. 290, 296; NATO-MD ¶95-96; EU-C Annex IV ¶37-38 | Media that touched a classified station flagged as classified; no "unclassified" re-use without a recorded sanitisation event | Sanitisation method, destruction certificate |
| 12 | Released media contain only authorised information; recipients have appropriate provisions and need-to-know | NATO-MD ¶8(g)-(h) | Transfer manifest (file list + hashes) compared with approved list | Recipient authorisation |
| 13 | Information transferred between systems protected in transit and at the receiving system | HG585 art. 287 | Encryption state of media; receiving station logs arrival | Receiving system accreditation level |
| 14 | Interconnection (incl. data exchange paths) approved by AAS; other systems treated as untrusted | I2 art. 35(3); EU-C Annex IV ¶33; NATO-MD ¶88-90 | Detection of any network/Bluetooth/Wi-Fi/modem activity | Approval of any link |
| 15 | Malware checks per AAS requirements; external media checked before use | HG585 art. 312; DS2 art. 13(2)(a), 22(1)(g), 26(2)(b) | AV engine/signature date; scan event recorded before media is opened | AV product approval |
| 16 | Authorised configuration baseline documented; change control; real-time/periodic configuration checks | DS2 art. 32(2)(b)-(i); NATO-MD ¶72, ¶76(d); HG585 art. 328 | Hash/compare of baseline (policies, services, drivers, installed software) | Approval of baseline |
| 17 | Start-up/shutdown procedures, periodic tamper checks, hardware changes authorised | DS2 art. 24(2)(a)-(f) | Boot configuration, Secure Boot/firmware settings readable from OS, hardware inventory diff | Physical seals, BIOS password custody |
| 18 | TEMPEST protection for state-secret systems / CONFIDENTIAL and above | HG585 art. 302-303; EU-C Art. 10; EU-COM Art. 36(6); I2 art. 42 | Inventory of attached peripherals vs approved list (Wi-Fi/BT radios, webcams, unapproved printers) | Zoning, equipment certification |
| 19 | Approved cryptography for storage/transmission; portable devices encrypted | EU-COM Art. 36(4)-(5); NATO-MD ¶100; I2 art. 41 | Disk/media encryption status | Product approval (national catalogue) |
| 20 | Accreditation with validity; re-accreditation on change; periodic inspection (NATO: at least every 24 months for COSMIC TOP SECRET) | I2 art. 21-23, 58, 60; NATO-MD ¶15-17, ¶73-74; EU-COM Art. 37 | Expiry date countdown; change events that trigger re-accreditation | The statement itself |
| 21 | Incidents reported immediately to the SIC security administrator; major ones to AAS/ORNISS; evidence preserved | DS2 art. 8(2)(e), 19(2)(e); I2 art. 50-52; EU-COM Art. 36(3) | Incident record generation, evidence hashes | Reporting chain |
| 22 | Private hardware/media prohibited for classified processing | NATO-MD ¶101-102; DE Merkblatt 1.4 | Unknown device serials flagged | Policy |

## 2. Requirements by area

### A. Security auditing and accountability

* **What must be recorded.** HG 585 requires that automatic access records for classified electronic
  information be kept in access registers and be produced "necondiționat prin software", with retention
  agreed between AAS and CSTIC (HG585 art. 291(1)-(2)). INFOSEC 2 requires security logs to detect
  malicious activity and faults, with traceability proportionate to potential harm, to support AAS audit,
  and with AAS-approved retention and analysis periods (I2 art. 43(1)-(4)). The detailed event list is
  delegated to the system's PrOpSec, which must list security-relevant events, describe periodic
  inspection of audit records, assign responsibility for running and validating the tools, set real-time
  alert handling and audit-file retention (DS2 art. 27(2)(a)-(g)), and cover protection/overwrite of audit
  data (DS2 art. 25(2)(f)). No public Romanian text found in this session lists the concrete event types
  (logon, file access, USB, print, admin actions); these are therefore an organisation/AAS decision
  recorded in PrOpSec. Printing appears only indirectly: printouts and printer ribbons are listed as
  document types (DS2 art. 22(3)) and hard-copy control under configuration (DS2 art. 25(2)(i)).
* **Who reviews.** NATO assigns to the *security management staffs* the checking of audit information
  for event/process failure and unauthorised user and system activity (NATO-MD ¶8(i)), and lists their
  separate duty of keeping the record of authorised users and issuing passwords (¶8(b)-(c)). The SAA
  checks implementation through periodic inspections (¶5(f), ¶16) and a "level 1" assessment includes
  interviews with "security officers, system and security administrators" and produces a formal report
  to the head of the organisation (¶81). Romanian audits are carried out by or under AAS coordination,
  with automated tools recommended (I2 art. 25-27).
* **Real-time tools.** NATO requires real-time security management with approved tools for CONFIDENTIAL+
  systems in system-high, compartmented or MLS mode, including identification of unauthorised activity
  and configuration checks (NATO-MD ¶75-76). A single-user dedicated-mode standalone is not explicitly
  covered by ¶75; the SAA decides.
* **EU.** Council Annex IV does not mention audit trails explicitly; the nearest provisions are least
  privilege (¶19), individual accountability awareness (¶21(c)) and periodic assessments/inspections
  (¶11, ¶48(e)). The Commission SAA may audit or inspect a CIS at any time and withdraw permission to
  operate (EU-COM Art. 37(6)).

### B. Removable media and data transfer

* **Accounting.** For "secret" level: a serial number and classification mark per medium, defined
  procedures for issue, receipt, withdrawal, destruction or retention, and manual or printed records of
  content and level; for "strict secret" and above, detailed media information in a register
  (HG585 art. 294(3)). NATO requires all removable media holding classified information to be treated as
  documents, marked with the highest classification ever stored, and, for NATO SECRET and above, kept
  in the Registry System with periodic spot checks; annual inventory for COSMIC TOP SECRET
  (NATO-MD ¶92-93). EU registration applies when CONFIDENTIEL UE and above material arrives at or leaves
  an entity, and registration may be performed by processes within a CIS (EU-C Annex III ¶18-19, ¶21).
* **Spot checks.** Periodic checks of presence and content (that media do not hold higher-classified
  data); full periodic inventory for "strict secret"; at least annual checks for the highest level
  (HG585 art. 295).
* **Copying, re-use, sanitisation, destruction.** Copying per PrOpSec (HG585 art. 289); re-usable media
  keep their highest classification until downgraded, then reclassified or destroyed (art. 290); erase
  only per PrOpSec (art. 296). Destruction of "strict secret", "secret" and "secret de serviciu" is
  recorded in a report signed by two authorised persons (HG585 art. 79(1)). NATO: media that held NATO
  SECRET or above may be destroyed but not declassified for public release (NATO-MD ¶96). EU: witnessed
  destruction and a destruction certificate kept 5-10 years (EU-C Annex III ¶44-45); media
  destroyed/reused/downgraded under approved procedures (Annex IV ¶37-38).
* **Transfer approval and destination.** Security staff ensure released media contain only authorised
  information and that receiving organisations have proper provisions and need-to-know
  (NATO-MD ¶8(g)-(h)). Information transferred between systems must be protected in transit and at
  the receiving system (HG585 art. 287). Interconnections require AAS approval (I2 art. 35(3)). PrOpSec
  must define who imports/exports documents and how media are checked (DS2 art. 22(1)(g)).
* **Two-person / dual control.** HG 585 makes the two-person rule mandatory for any modification of a
  system and requires PrOpSec to name the situations where it applies (HG585 art. 277, definition in
  art. 237); DS2 requires it in areas where SIC equipment is installed (art. 21(1)(a.3)). No public text
  found that mandates two-person control specifically for each media export; it can be imposed via
  PrOpSec.
* **Cross-domain.** Where systems of different highest classification are interconnected and users of
  the lower one lack clearance for the higher, the interconnection mechanisms must be evaluated and
  certified (NATO-MD ¶64); MLS requires evaluation/certification (NATO-MD ¶62-63; HG585 art. 323, 325).
  A manual "sneakernet" transfer from a higher to a lower system is a downgrade of the medium and must
  follow approved procedures (HG585 art. 290; NATO-MD ¶95).

### C. Classified file/folder access control and unauthorised export

* Access only on clearance and need-to-know; users authorised for the class and level of information
  (HG585 art. 272, 274; I2 art. 17-19). Access control is "the first line of defence" covering
  identification, authentication, authorisation and record-keeping (I2 art. 46); the access model is
  chosen by the operating authority (I2 art. 47); minimum identification/authentication requirements
  are set by ORNISS regulations (I2 art. 49) - not public.
* Security measures must prevent or detect unauthorised disclosure (HG585 art. 260). Detection of
  unauthorised export follows from art. 291 (software access records) combined with the media rules
  in B. The tool can verify object-access auditing on the declared classified folders and correlate
  file reads with media writes; which folders are classified is an organisation declaration.

### D. Other checks commonly required

* **TEMPEST / peripherals:** HG585 art. 302-303; EU-C Art. 10; EU-COM Art. 36(6); I2 art. 42 (details in
  non-public ORNISS directives). NATO cites SDIP-293 for emission security (NATO-MD ¶8(e)) - not public.
* **Boot/BIOS/hardware:** DS2 art. 24(2) (start-up/shutdown procedures, periodic tamper checks with
  modules locked in the enclosure, authorised hardware changes). No public text found that prescribes
  specific firmware settings.
* **Encryption:** approved crypto (EU-COM Art. 36(4)-(5); I2 art. 41, national INFOSEC catalogue, ORNISS
  orders 34/2014, 45/2014, 52/2016 per legeaz.net index); portable devices and their media encrypted
  (NATO-MD ¶100).
* **Malware:** HG585 art. 312; DS2 art. 13(2)(a), 26(2)(b),(e) (infections reported to sender and AAS).
* **Baseline/configuration:** DS2 art. 32; HG585 art. 309 (periodic checks that original software is
  protected), art. 328; NATO-MD ¶72-73, ¶76(d).
* **Accreditation lifecycle:** I2 art. 21-24, 58, 60 (validity and re-accreditation set by AAS);
  SecOPs approved before CONFIDENTIAL+ operation (NATO-MD ¶51); Romanian "secret de serviciu" model
  documentation published by SRI (SRI-SSv).
* **Incidents:** DS2 art. 8(2)(e), 16(3) (loss of portable devices/media reported immediately),
  19(2)(e); I2 art. 50-52 (priority categories, digital evidence preserved with integrity).

## 3. Tamper-evidence: showing the local administrator did not falsify the report

What the standards actually say (requirements):

1. **Separate the administrator from the reviewer.** No single person may hold all programs and
   security keys (HG585 art. 275); programming and operation separated, with procedures to detect
   violations (art. 276); CSTIC sits under the security structure/officer (art. 244(1)) and is led by
   the ICT security officer (art. 246); security administrator roles may be cumulated "if appropriate"
   (art. 268(3)) - so on small sites one person may hold several roles, which the AAS must accept.
   EU allows combining IA functions only without conflict of interest (EU-C Annex IV ¶43). NATO gives
   audit checking to security management staffs, distinct from system/network administrators
   (NATO-MD ¶8(a), ¶8(i)).
2. **Two people for any modification** (HG585 art. 277).
3. **Independent oversight.** SAA/AAS periodic inspection and level-1 assessment with a formal report to
   the head of the organisation (NATO-MD ¶74, ¶81); AAS audits (I2 art. 25-27); Commission SAA may audit
   at any time (EU-COM Art. 37(6)).
4. **Protect the audit data.** PrOpSec covers protection and overwrite of audit data (DS2 art. 25(2)(f));
   retention approved by AAS (I2 art. 43(4); HG585 art. 291(2)).
5. **Engineering comparator (US).** NIST AU-9: protect audit information and tools from unauthorised
   modification/deletion and alert on it; enhancements: write-once media (1), store on a physically
   separate system (2), cryptographic integrity (3), management limited to a subset of privileged users
   (4), dual authorisation to move/delete (5), read-only access for reviewers (6), store on a component
   with a different OS (7) (NIST page above).

None of the public European/Romanian texts found prescribes digital signatures or hash chains for
reports. The following is therefore **[design]**, derived from items 1-5:

* Hash-chain every log record and every generated report (each includes the hash of the previous one
  and a monotonic sequence number), so deletion or edit breaks the chain on later verification.
* Sign reports with a key the administrator does not hold (e.g. a token kept by the security officer /
  funcționar de securitate TIC); the administrator can produce, but not sign, a report.
* Export the chain head and the signed report to write-once media or paper kept by the security officer
  (AU-9(1)/(2) analogue); the next inspection checks continuity against that external copy.
* Record OS log-clearing events, audit-policy changes, clock changes and tool reinstallation as
  first-class findings; a gap is itself evidence.
* Require a second approver for tool configuration changes (art. 277) and show both identities in the
  report.
* Limits: a full-control administrator on a standalone host can still stop the tool or alter the OS
  before events are written; only external anchoring (signed exports held by someone else) makes that
  detectable after the fact, not impossible.

## 4. Media transfer chain verification across several stations

Requirements found: per-medium identity and marking (HG585 art. 294; NATO-MD ¶92); records of issue,
receipt, withdrawal and destruction (HG585 art. 294(3)(a)); registration on entry and exit of an entity
(EU-C Annex III ¶19); released media contain only authorised information (NATO-MD ¶8(g)); recipient
authorised (¶8(h)); protection at the receiving system (HG585 art. 287); periodic spot checks of presence
and content (HG585 art. 295). No public text describes a technical cross-station confirmation protocol.

**[design]** How confirmation can work without any network:

1. **Export at station A:** the tool writes a signed transfer manifest onto the medium: medium serial,
   source station ID, operator, approver (two-person), timestamp, intended destination station ID, list
   of files with SHA-256, classification. A copy of the manifest stays in A's hash-chained log.
2. **Import at station B:** the tool verifies the manifest signature, checks that B is the named
   destination, re-hashes files, and records a signed receipt (in B's log and appended to the medium).
   Destination mismatch, extra files or hash differences are findings.
3. **Return/reconciliation:** the medium (or a separate collection medium carried by the security
   officer) brings receipts back; or the security officer periodically collects signed log extracts
   from all stations onto a reconciliation station. Every export without a matching import, every import
   without an export, and every medium seen on an unlisted station is flagged.
4. **Paper register parity:** the reconciliation output is compared with the manual media register
   required by HG585 art. 294, since the paper register remains the legal record.
5. **Limits:** a copy made on an uncontrolled machine cannot be seen; the chain proves where the medium
   was inserted on controlled stations only. Device-identity spoofing (cloned serials) is possible;
   per-medium tokens written to a hidden area or hardware-encrypted media with unique IDs reduce it.

## 5. Gaps and unknowns

* **NATO:** C-M(2002)49 public parts were not retrieved in this session. AC/35-D/2004 (Primary Directive
  on INFOSEC) and the C3B INFOSEC technical and implementation directives are referenced by
  AC/35-D/2005-REV2 ¶2 but no public copy was located; their numbers are not given here to avoid
  guessing. SDIP-293 is referenced (¶8(e)) and is not public. AC/35-D/2005-REV2 itself is 2010; a later
  revision may exist (`UNKNOWN`).
* **EU:** Council Security Committee IA guidelines (e.g. CIS accreditation guidelines, doc. 10346/14,
  listed on data.consilium.europa.eu) were not read; other IA guidelines (audit, removable media) are
  `UNKNOWN`. Commission implementing rules on CIS security referenced by 2015/444 Art. 36(9), 37(7) not
  read.
* **Romania:** INFOSEC 3 (ORNISS Order 484/2003, referenced by I2 art. 23 and DS2) not retrieved;
  ORNISS identification/authentication and TEMPEST regulations (I2 art. 42, 49) are non-public; a
  complete public list "INFOSEC 1 ... 11" was not confirmed - only INFOSEC 2, INFOSEC 3 and the national
  INFOSEC catalogue orders are confirmed. HG 781/2002 (secret de serviciu), HG 353/2002 (NATO
  information in Romania) and Legea 182/2002 were not reviewed for this note. The concrete list of
  audit events and retention periods is set per system by AAS/PrOpSec and is not public.
* **Comparators:** 32 CFR 117.18 (NISPOM) could not be fetched (eCFR blocked automated access); DCSA
  DAAPM not read; UK HMG policy not covered; German federal VSA text not retrieved (only Bavarian
  VS-NfD Merkblatt, which says VS-NfD processing requires approved VS-IT incl. data carriers and bans
  private hardware/software, item 1.4).
* **Interpretation risk:** whether a standalone in dedicated mode needs real-time tooling (NATO-MD ¶75)
  and whether two-person control applies to each media export are AAS decisions.
