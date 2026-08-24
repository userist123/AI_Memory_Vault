---
name: skill-api-design-governance
description: Enforces RESTful SemVer, OpenAPI 3.1 specs, HATEOAS, and deprecation protocols.
---

# API Design & Governance Skill
- URL Path Versioning: `/v1/resources`
- Header Versioning: `Accept: application/vnd.company.v1+json`
- Deprecation Standards: RFC 8594 `Deprecation` and `Sunset` headers.
- Require `Idempotency-Key` headers on POST/PATCH requests to prevent duplicate mutation processing.