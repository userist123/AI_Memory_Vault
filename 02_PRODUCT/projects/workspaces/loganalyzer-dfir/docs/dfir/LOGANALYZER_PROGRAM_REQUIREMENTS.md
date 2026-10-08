# LogAnalyzer — Program Requirements
## Version 1.0 — 2026-10-08

### 1. Product goal
LogAnalyzer is a complete investigation, DFIR, verification, audit, response and Memory Vault gateway platform. It must answer:
- What happened?
- Is something wrong?
- Why do you say that?
- What evidence supports it?
- What should I do next?

### 2. Architecture
The system must separate:
`Frontend -> Application/Investigation API -> Domain/Analysis Engine -> Evidence/Detection/Correlation/Verification -> Audit/Response/Vault`

The frontend never decides forensic truth. Backend services return structured facts, evidence, provenance, verification state, limitations and actions.

### 3. Evidence and integrity
Implement and enforce:
- stable EvidenceId;
- SHA-256 and acquisition hashes;
- format/magic-byte validation;
- mutation detection;
- provenance;
- chain of custody;
- evidence lifecycle;
- source locators;
- explicit unavailable/partial states.

If evidence mutates, affected results must not silently remain trusted.

### 4. Parsers
Production parsers must have stable ID, version, input/output contract, provenance binding, error handling, tests and a real production consumer.

Target artifacts include EVTX, Prefetch, BAM, Amcache, ShimCache, SRUM, Registry, Scheduled Tasks, services, autoruns, browser and filesystem/network artifacts where supported.

A facade without a production consumer is not a production feature.

### 5. Semantic model
Never equate artifact presence with execution.

Explicitly distinguish:
`OBSERVATION, PRESENCE, EXECUTION, CONFIGURATION, CORRELATION, INFERENCE, ATTRIBUTION`

Attribution requires separate evidence. Correlation does not automatically mean causation.

### 6. Timeline
Provide normalized timestamps while preserving source timestamps, timezone/uncertainty, provenance, filters, correlation and drill-down to evidence.

### 7. Detection
Support where implemented:
- IOC;
- Sigma;
- YARA/YARA Lite;
- application/case rules;
- rule version/hash;
- timeout handling;
- provenance.

Every detection includes rule, evidence, severity, status and verification.

### 8. Evidence Graph
Represent entities and typed, evidence-backed relationships. Relationships require evidence or explicit derivation. Do not infer causality merely from graph connectivity.

### 9. Investigation pipeline
Target flow:
`Case -> Evidence -> Integrity -> Parsing -> Provenance -> Timeline -> Correlation -> Detection -> Graph -> Verification -> AI -> Policy -> Response -> Reporting -> Vault proposal`

Every transformation and gap is auditable.

### 10. InspectraVeritas / verification
Veritas is an independent verification layer, not a duplicate parser platform.

It must check:
- evidence sufficiency;
- provenance;
- semantic correctness;
- temporal consistency;
- graph consistency;
- contradictions;
- missing evidence;
- unsupported claims;
- AI claims.

Verdicts:
`VERIFIED, SUPPORTED, UNPROVEN, CONTRADICTED, REJECTED, UNKNOWN, NOT_ASSESSED`

### 11. AI
AI must be evidence-constrained:
1. verify evidence integrity;
2. build evidence catalog;
3. record model/digest when available;
4. hash prompt/response;
5. preserve raw response;
6. validate claims;
7. bind accepted claims to evidence;
8. reject unsupported claims;
9. record limitations.

AI is never the final authority and never writes directly to active Vault state.

### 12. Policy/compliance
Compliance must follow:
`Requirement -> Control -> Evidence -> Assessment -> Status`

Statuses:
`SATISFIED, NOT_SATISFIED, NOT_ASSESSED`

Do not claim formal compliance without an actual mapping and evidence.

### 13. Controlled response
Every action follows:
`REQUEST -> AUTHORIZE -> APPLY -> VERIFY -> VERIFIED / NOT_VERIFIED / FAILED / REJECTED`

Never report success merely because a command was issued. Dangerous actions require explicit confirmation, impact information and verification.

### 14. Reporting
Provide:
- executive summary;
- technical report;
- forensic report;
- incident report;
- audit report;
- complete report;
- machine-readable JSON/JSONL/CSV and valid OSCAL where supported.

Reports preserve evidence, provenance and limitations.

### 15. Memory Vault
Only proposals may leave LogAnalyzer:
`Finding -> Verification -> Evidence validation -> Vault proposal -> Human/authority review -> ACTIVE`

Unverified hypotheses and unsupported AI output must never become active facts.

### 16. Audit
Audit investigation lifecycle, evidence changes, parsers, transformations, findings, verification, AI, policy, response, reports and Vault proposals.

### 17. Frontend
Primary navigation must use user goals:
- Home
- Analyze
- What happened?
- Is it safe?
- Investigate
- Verify
- AI
- Actions
- Reports
- Memory
- Advanced

Technical concepts remain available through drill-down, tooltips and expert mode.

### 18. User language
Translate technical terms:
- EVTX -> Windows logs
- Prefetch -> Program start history
- BAM -> Program activity
- Amcache -> Application history
- Evidence Graph -> Connections between events
- Provenance -> Where this information came from
- Chain of Custody -> Evidence history
- IOC -> Suspicious indicator
- Sigma -> Security detection rule
- YARA -> File/content detection rule
- Correlation -> Related events
- Verification -> Evidence check

The technical name remains available in advanced views.

### 19. Finding contract
Every important finding exposes:
`What happened -> Why -> Evidence -> What is unknown -> Verify -> What should I do`

Machine model:
`Claim, HumanSummary, TechnicalSummary, SemanticType, Severity, Evidence[], Contradictions[], MissingEvidence[], Verification, Confidence, Limitations[], Provenance, AuditTrail`

### 20. Standard states
`OBSERVED, CORRELATED, SUPPORTED, VERIFIED, INFERRED, UNPROVEN, CONTRADICTED, REJECTED, UNKNOWN, NOT_ASSESSED`

### 21. Testing
Require unit, integration, E2E, real corpus, regression, differential, adversarial and AI evidence-validation tests.

Green CI is not equivalent to forensic validation.

### 22. Release gate
A forensic release should require:
- tests passing;
- corpus validation;
- differential validation or documented waiver;
- evidence integrity validation;
- AI claim validation;
- response verification;
- audit integrity;
- no critical unresolved findings.

### 23. Definition of Done
A feature is DONE only when it is implemented, wired into a real production path, tested, evidence-backed, auditable, exposed correctly in UI, secure, documented and independently verified where appropriate.
