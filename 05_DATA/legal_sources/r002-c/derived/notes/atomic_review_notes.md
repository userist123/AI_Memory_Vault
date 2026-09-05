# R002-C Atomic Legal REVIEW Notes

All notes below are derived knowledge, not primary source text. Every note remains `lifecycle: REVIEW`, `verification: derived_from_verified_source`, `requires_legal_review: true`.

---

## R002C-N001 — Scope and objective
```yaml
id: R002C-N001
lifecycle: REVIEW
verification: derived_from_verified_source
requires_legal_review: true
source_refs:
  - OUG155/2024 Art.1
  - OUG155/2024 Art.2(1)
```
Claim: OUG 155/2024 establishes a legal/institutional framework and measures/mechanisms for a high common level of cybersecurity nationally; its stated purposes include cyber-risk management and incident-reporting obligations for essential and important entities.
Interpretation: Treat the act as a source of obligations and institutional roles, not as an automatic classification of any particular project or company.

## R002C-N002 — Exclusions and special applicability
```yaml
id: R002C-N002
lifecycle: REVIEW
verification: derived_from_verified_source
requires_legal_review: true
source_refs:
  - OUG155/2024 Art.2(2)-(5)
  - Law124/2025 Art.1
```
Claim: Art. 2 contains exclusions/special regimes including specified defence/public-order/national-security bodies, classified-information systems, and DORA-covered entities with a limited set of OUG provisions; Law 124 modifies the non-derogation clause in Art. 2(5).
Interpretation: Applicability must be determined from the exact entity, system and regulatory context; do not infer applicability from “NIS2” branding alone.

## R002C-N003 — Definitions
```yaml
id: R002C-N003
lifecycle: REVIEW
verification: derived_from_verified_source
requires_legal_review: true
source_refs:
  - OUG155/2024 Art.3
  - OUG155/2024 Art.4
  - Law124/2025 Art.3
```
Claim: The OUG defines cybersecurity concepts, entities, incidents, risks, networks/systems, DNS/cloud/data-centre/CDN/managed/security-managed services and related terms; Law 124 modifies the social-networking-platform definition.
Interpretation: Derived technical controls should link to the legal term actually used, not to a broader informal synonym.

## R002C-N004 — Essential and important entities
```yaml
id: R002C-N004
lifecycle: REVIEW
verification: derived_from_verified_source
requires_legal_review: true
source_refs:
  - OUG155/2024 Art.5
  - OUG155/2024 Art.6
  - OUG155/2024 Art.9
  - OUG155/2024 Art.10
```
Claim: The OUG establishes categories and criteria for essential/important entities and directs impact/risk determination through Art. 10 criteria and DNSC methodology.
Interpretation: Entity classification is a legal/applicability decision and must not be automated as a final result by this corpus.

## R002C-N005 — Risk management
```yaml
id: R002C-N005
lifecycle: REVIEW
verification: derived_from_verified_source
requires_legal_review: true
source_refs:
  - OUG155/2024 Art.11(1)-(8)
  - OUG155/2024 Art.12
  - OUG155/2024 Art.13
  - Law124/2025 Art.4
```
Claim: Essential and important entities must implement proportionate technical, operational and organisational measures; Art. 13 lists areas including risk analysis, cryptography, supply chain, vulnerability management, access control, incident management, continuity and MFA/secure communications.
Interpretation: These clauses are suitable inputs for candidate control design, but the legal adequacy of a specific control remains for legal/security review.

## R002C-N006 — Governance and management responsibility
```yaml
id: R002C-N006
lifecycle: REVIEW
verification: derived_from_verified_source
requires_legal_review: true
source_refs:
  - OUG155/2024 Art.14
  - Law124/2025 Art.4
```
Claim: Management bodies approve and oversee cybersecurity risk-management measures; Law 124 adds accredited training, regular staff training, permanent contacts, resource allocation and designation of responsible persons within a stated 30-day period after the DNSC identification/registration decision.
Interpretation: A product can track governance evidence and deadlines, but must not declare an entity legally compliant.

## R002C-N007 — Supply-chain security
```yaml
id: R002C-N007
lifecycle: REVIEW
verification: derived_from_verified_source
requires_legal_review: true
source_refs:
  - OUG155/2024 Art.11(8)
  - OUG155/2024 Art.13(d)
```
Claim: Supply-chain security is expressly part of risk-management measures and includes consideration of direct suppliers/service providers, their vulnerabilities, product quality and security-development practices.
Interpretation: Vendor-risk records, evidence links and supplier-review events are candidate controls, not proof of statutory compliance.

## R002C-N008 — Vulnerability handling
```yaml
id: R002C-N008
lifecycle: REVIEW
verification: derived_from_verified_source
requires_legal_review: true
source_refs:
  - OUG155/2024 Art.13(e)
  - OUG155/2024 Art.36
  - Law124/2025 Art.9-10
```
Claim: The framework includes vulnerability management/disclosure, and Law 124 specifically modifies Art. 36(4) to require relevant essential/important ICT producers/providers to transmit vulnerability information to DNSC and remediate vulnerabilities within a period agreed with DNSC.
Interpretation: LogAnalyzer can model vulnerability lifecycle evidence and deadlines; legal review is required before turning that model into a statutory obligation rule.

## R002C-N009 — Incident reporting
```yaml
id: R002C-N009
lifecycle: REVIEW
verification: derived_from_verified_source
requires_legal_review: true
source_refs:
  - OUG155/2024 Art.15
  - OUG155/2024 Art.16
  - Law124/2025 Art.5
```
Claim: Essential/important entities have incident-reporting duties; Art. 15 contains timing/content rules and Art. 16 permits voluntary reporting by other entities. Law 124 modifies the significance threshold wording in Art. 15(6).
Interpretation: Systems may record incident timestamps, reportability assessments and evidence, but should preserve the distinction between legal source text and an implementation rule.

## R002C-N010 — DNSC responsibilities
```yaml
id: R002C-N010
lifecycle: REVIEW
verification: derived_from_verified_source
requires_legal_review: true
source_refs:
  - OUG155/2024 Art.2(1)(c)
  - OUG155/2024 Art.25
  - OUG155/2024 Art.26
  - OUG155/2024 Art.36
  - Law124/2025 Art.10
```
Claim: DNSC is designated as the competent national cybersecurity authority and receives supervisory, control, coordination and vulnerability-related responsibilities across the act.
Interpretation: DNSC references in software should be represented as provenance/authority metadata, not as an instruction channel or operational authority.

## R002C-N011 — Supervision, audit and evidence
```yaml
id: R002C-N011
lifecycle: REVIEW
verification: derived_from_verified_source
requires_legal_review: true
source_refs:
  - OUG155/2024 Art.46-59
  - Law124/2025 Art.13-14
```
Claim: The OUG establishes supervision/control and cybersecurity audit mechanisms; Law 124 changes the remediation-plan implementation and evidence flow in Art. 47(5) and modifies serious-violation criteria in Art. 50.
Interpretation: Evidence stores should preserve who/what/when/source references and immutable evidence hashes where appropriate.

## R002C-N012 — Sanctions
```yaml
id: R002C-N012
lifecycle: REVIEW
verification: derived_from_verified_source
requires_legal_review: true
source_refs:
  - OUG155/2024 Art.60-61
  - Law124/2025 Art.15-20
```
Claim: Art. 60-61 define contraventions, sanctioning ranges and competent authorities; Law 124 substantially modifies contravention categories, sanction ranges and enforcement allocation.
Interpretation: A software control should not present calculated penalties as legal conclusions without legal review and a versioned rule set.

## R002C-N013 — Timelines
```yaml
id: R002C-N013
lifecycle: REVIEW
verification: derived_from_verified_source
requires_legal_review: true
source_refs:
  - OUG155/2024 Art.15
  - OUG155/2024 Art.18(2)
  - OUG155/2024 Art.47(4)-(6)
  - OUG155/2024 Art.64-65
  - Law124/2025 Art.13
```
Claim: The act contains multiple explicit reporting, registration, remediation, audit and subordinate-regulation deadlines.
Interpretation: Deadline tracking is a candidate product capability; each deadline must retain its exact legal reference and version.

## R002C-N014 — Transitional/final provisions
```yaml
id: R002C-N014
lifecycle: REVIEW
verification: derived_from_verified_source
requires_legal_review: true
source_refs:
  - OUG155/2024 Art.64-68
  - Law124/2025 Art.21
```
Claim: The final chapter governs continuing effects of prior measures, effective-date treatment, subordinate regulations, repeals, transitional staffing provisions and annexes.
Interpretation: Historical events and current obligations must not be conflated; the corpus needs explicit version dates and temporal provenance.

## R002C-N015 — Legal-source confidentiality boundary
```yaml
id: R002C-N015
lifecycle: REVIEW
verification: derived_from_verified_source
requires_legal_review: true
source_refs:
  - OUG155/2024 Art.3(2)-(3)
  - Law124/2025 Art.2
```
Claim: The act addresses confidentiality/security-commercial interests in information handling and Law 124 adds explicit DNSC confidentiality/public-interest treatment.
Interpretation: R002-C must not ingest operational, classified, confidential or incident-specific information; only public legal text and derived non-sensitive knowledge are permitted.
