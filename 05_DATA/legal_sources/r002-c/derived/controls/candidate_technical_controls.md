# R002-C Candidate Technical Controls

Status of every item: `CANDIDATE_ONLY`, not a legal compliance assertion.

## AI Memory Vault

| Control | Legal refs | Candidate evidence |
|---|---|---|
| Legal-source provenance record | OUG155 Art.1-4; Art.64-68 | act/article/alineat, source URL, source version, content hash |
| Versioned applicability decision record | OUG155 Art.2; Art.5-10 | entity/system context, decision owner, legal-review state |
| Security-risk knowledge template | OUG155 Art.11-13 | risk, control, evidence, reviewer, verification state |
| Supply-chain evidence graph | OUG155 Art.11(8), Art.13(d) | supplier, service, review date, evidence refs |
| Vulnerability knowledge lifecycle | OUG155 Art.13(e), Art.36; Law124 Art.9 | discovery, disclosure, remediation, source refs |
| Incident reporting evidence memory | OUG155 Art.15-16; Law124 Art.5 | event time, assessment, report state, evidence |
| Legal-review gate | all derived notes | no automatic ACTIVE promotion; reviewer decision required |

## LogAnalyzer

| Control | Legal refs | Candidate evidence |
|---|---|---|
| Incident timeline capture | OUG155 Art.15-16 | immutable event timestamps and source/evidence links |
| Reportability decision trail | OUG155 Art.15(6); Law124 Art.5 | criteria used, version, reviewer |
| Vulnerability tracking | OUG155 Art.13(e), Art.36; Law124 Art.9 | finding, affected product/service, remediation evidence |
| Supply-chain monitoring | OUG155 Art.11(8) | vendor/service dependencies and security review events |
| Audit evidence packaging | OUG155 Art.46-59; Law124 Art.13-14 | evidence manifest and hashes |
| Remediation deadline tracking | OUG155 Art.47; Law124 Art.13 | deadline, owner, completion proof |

## Trading journal SaaS

| Control | Legal refs | Candidate evidence |
|---|---|---|
| Service/entity applicability questionnaire | OUG155 Art.2, 5-10; Annexes 1-2 | versioned questionnaire, legal review status |
| ICT supplier inventory | OUG155 Art.11(8), Art.13(d) | supplier/service records, contracts/evidence pointers |
| Security incident journal | OUG155 Art.15-16 | incident chronology, impact assessment, report evidence |
| Vulnerability register | OUG155 Art.13(e), Art.36 | vulnerability state and remediation record |
| Management/security responsibility evidence | OUG155 Art.14; Law124 Art.4 | designated role, training evidence, review date |
| Legal version pinning | OUG155 Art.64-68; Law124 Art.21 | source version and effective-date metadata |

## Explicit limitation

None of these controls establish that a product, operator or entity is an “essential” or “important” entity, satisfies NIS2, or complies with Romanian law. Such determinations remain `LEGAL_REVIEW_REQUIRED`.

---

## 🔗 Legături Sinaptice
- [[atomic_review_notes]]
- [[candidate_tests_and_evidence]]
- [[05_DATA/legal_sources/r002-c/README|R002-C Overview]]
- [[04 Security Integrity Map]]
- [[01_ARCHITECTURE/System_Architecture]]
