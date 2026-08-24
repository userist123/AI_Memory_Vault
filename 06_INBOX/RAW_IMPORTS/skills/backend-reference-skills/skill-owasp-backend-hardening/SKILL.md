---
name: skill-owasp-backend-hardening
description: Audits code against OWASP API Top 10, preventing BOLA, SSRF, injection, and broken auth.
---

# OWASP Backend Hardening Skill
- BOLA (Broken Object Level Authorization) defense (`WHERE user_id = :auth_user_id`).
- Mass assignment prevention with strictly typed DTOs (Pydantic v2, FluentValidation, Zod).