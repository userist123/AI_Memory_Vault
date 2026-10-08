# LogAnalyzer — Product UX Contract
## Human-Language Frontend Contract
### Version 1.0 — 2026-10-08

## 1. Core rule
Do not simplify the evidence; simplify its presentation.

Every important result has five layers:
1. Simple — What happened?
2. Explanation — Why?
3. Evidence — What proves it?
4. Technical — How was it established?
5. Raw — Show the source record.

## 2. Main navigation
```text
Home
Analyze
  - My computer
  - Files and evidence
  - Incident
  - Existing case
What happened?
  - Suspicious activity
  - Programs
  - Users
  - Connections
  - Files
  - History
Is it safe?
  - Threats
  - Security problems
  - Unusual activity
  - System checks
Investigate
  - Timeline
  - Connections between events
  - Evidence
  - Indicators
  - Search
Verify
  - Verify a finding
  - Verify a report
  - Independent audit
  - Contradictions
AI
  - Analyze case
  - Explain finding
  - Ask the case
Actions
  - Recommendations
  - Remediation
  - Isolation
  - Action history
Reports
  - Summary
  - Technical
  - Forensic
  - Incident
  - Audit
  - Complete
Memory
Advanced
```

## 3. Home screen
Answer:
- Is there a problem?
- How serious?
- Is evidence trustworthy?
- What was found?
- What next?

Example:
`ATTENTION — 3 items require review — 1 high-risk activity — 2 security issues — Evidence integrity: OK`

Never display SAFE merely because no detection fired. Incomplete coverage must be explicit.

## 4. Finding card
Every finding shows:
- title;
- human summary;
- severity;
- verification state;
- Why?;
- evidence;
- unknowns;
- recommended action.

Example:
`PowerShell started from a document`
`HIGH`
`PowerShell was started by Microsoft Word and executed a script from a temporary folder.`

Buttons:
`WHY? | SHOW EVIDENCE | VERIFY | WHAT SHOULD I DO?`

## 5. Universal Why?
The explanation contains:
- observation;
- evidence;
- reasoning;
- limitations;
- verification.

Never show only a confidence percentage.

## 6. Evidence display
Three levels:
- summary: "4 pieces of evidence support this result";
- evidence list;
- technical/raw details.

Technical details include EvidenceId, SHA-256, source, parser/version, timestamp and locator.

## 7. Verification language
Use explicit states, not unexplained percentages.

| Internal | Romanian UI |
|---|---|
| OBSERVED | Observat |
| CORRELATED | Corelat |
| SUPPORTED | Susținut de dovezi |
| VERIFIED | Verificat |
| INFERRED | Deducție |
| UNPROVEN | Nedemonstrat |
| CONTRADICTED | Contrazis |
| REJECTED | Respins |
| UNKNOWN | Necunoscut |
| NOT_ASSESSED | Neevaluat |

## 8. Know / Think / Don't know
Reusable component:
```text
CE ȘTIM
✓ PowerShell a fost pornit.
✓ Scriptul a existat.
✓ A existat o conexiune.

CE SUSPECTĂM
⚠ Activitatea poate fi malițioasă.

CE NU PUTEM DEMONSTRA
? Cine a inițiat-o.
? Intenția.
? Atribuirea.
```

## 9. Timeline
Simple:
`08:41 Ai deschis un document`
`08:42 Word a pornit PowerShell`
`08:42 PowerShell a rulat un script`

Technical:
Event ID, parent/child process, command line, EvidenceId, source.

Forensic:
hash, locator, parser, timestamp quality.

## 10. Graph
Simple label: "Conexiuni între evenimente".

Example:
`Document -> Word -> PowerShell -> Script -> External server`

Expert view exposes entity, relationship, evidence, locator, source hash, derivation and verification.

Every graph must have a text/table alternative.

## 11. AI
AI is an assistant, not authority.

Allowed user actions:
- Explain this finding
- Summarize the case
- What evidence supports this?
- What evidence is missing?
- Could this be a false positive?
- What should I investigate next?

AI answers must cite case evidence and explicitly state limitations.

Example:
"The activity is consistent with PowerShell execution initiated by Word. Supported by Security event 4688, PowerShell telemetry and Prefetch. The evidence does not establish malicious intent."

## 12. Case chat
The assistant must answer from the case only and must not invent evidence.

For example:
"I cannot establish that the file was executed. Amcache and ShimCache show application history/presence, but are insufficient alone to prove execution."

## 13. Independent audit
Button:
`RUN INDEPENDENT AUDIT`

Explain:
"This checks whether the investigation's conclusions are actually supported by available evidence."

Show:
`Claims checked / Supported / Unproven / Contradicted / Unknown`

Each claim must be drillable.

## 14. Response UX
Before destructive action:
- what will happen;
- why;
- impact;
- verification method;
- rollback if available.

After:
`Requested -> Applied -> Verified`

Never claim success without verification.

## 15. Reports
Ask:
`What is this report for?`
- Management
- IT/Security
- Forensic investigation
- Incident response
- Audit/compliance
- Everything

Do not require users to understand JSON/OSCAL first.

## 16. Memory Vault
Use:
`SAVE TO MEMORY VAULT`

Before proposal:
- evidence supported;
- provenance available;
- integrity verified;
- no unresolved contradiction.

States:
`PROPOSED / REVIEWED / ACTIVE / REJECTED`

## 17. Localization
All user-facing strings use localization keys. Romanian is first-class; English is supported. Technical identifiers, hashes and rule IDs are not translated.

## 18. Accessibility
Support keyboard navigation, screen readers, adequate contrast, non-color-only status, scalable text, accessible tables and graph alternatives.

## 19. Backend/UI contract
Backend returns structured objects; frontend renders them.

Conceptual:
```json
{
  "id": "finding-001",
  "title_key": "finding.execution.title",
  "summary_key": "finding.execution.summary",
  "semantic_type": "EXECUTION",
  "severity": "HIGH",
  "status": "SUPPORTED",
  "evidence": [],
  "contradictions": [],
  "missing_evidence": [],
  "verification": {},
  "limitations": [],
  "recommended_actions": [],
  "audit": {}
}
```

Frontend must never derive forensic truth from display strings.

## 20. Operation states
`NOT_STARTED / RUNNING / COMPLETED / PARTIAL / FAILED / CANCELLED / BLOCKED`

Do not confuse "analysis completed" with "finding verified".

## 21. Coverage
Always show missing evidence:
```text
Windows logs ✓
Prefetch ✓
Registry ✓
BAM ⚠ Partial
SRUM ✗ Not available
Overall coverage: PARTIAL
```

A partial clean result is not proof of a clean system.

## 22. First-run flow
```text
What do you want to do?
[ Check this computer ]
[ Analyze evidence ]
[ Investigate an incident ]
[ Open existing case ]
```

Then:
```text
What are you trying to find out?
[ What happened? ]
[ Is something suspicious? ]
[ Was a program executed? ]
[ Was the computer compromised? ]
[ I need a forensic report ]
[ I need a complete audit ]
```

These intents map to backend workflows.

## 23. Product identity
LogAnalyzer is:
"the system that investigates, explains, verifies and documents what happened."

It is not merely:
- a log viewer;
- an AI chatbot;
- a parser collection;
- an automated malware verdict engine.

## 24. Final UX acceptance test
A non-forensic user must be able to:
1. start an investigation;
2. understand the result;
3. understand why;
4. open evidence;
5. see unknowns;
6. see missing evidence;
7. request verification;
8. understand actions;
9. understand action impact;
10. create a useful report.

An expert must be able to inspect exact evidence, provenance, hashes, parsers, rules, graph relationships, AI traces, verification and audit records.

## 25. Final rule
```text
START WITH THE USER'S QUESTION.
SHOW THE ANSWER.
SHOW WHY.
SHOW THE EVIDENCE.
SHOW THE TECHNICAL DETAILS.
NEVER HIDE WHAT IS UNKNOWN.
```
