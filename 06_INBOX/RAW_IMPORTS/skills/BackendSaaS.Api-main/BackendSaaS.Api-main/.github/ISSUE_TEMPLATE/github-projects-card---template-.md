---
name: 'GitHub Projects Card - Template '
about: Template for GitHub Projects card .-
title: "[EPIC-NN] — Short title in the infinitive"
labels: ''
assignees: ''

---

**Type:** Feature | Chore | Bug | Spike
**Priority:** High | Medium| Low
**Phase:** Phase 0-5 (according to the roadmap)
**Depends On:** [EPIC-NN, EPIC-NN] (or "None")

>**Commit Convention:**
```git
<type>(<ID>): <short description in imperative mood>

<optional body explaining the "why", not the "what" — the diff already shows the what>

<optional footer: Closes #<issue number>>
```

### Description

What needs to be done and why, in 2–4 lines. It is not the detailed technical "how-to,"
but rather the objective of the card.

### Acceptance Criteria - AC
- [ ] Acceptance criteria #1.
- [ ] Acceptance criteria #2.
- [ ] Acceptance criteria #3.

### Definition of Done - DoD
- [ ] The code compiles without warnings.
- [ ] It meets all the card's acceptance criteria.
- [ ] It follows the layering convention (no business logic in the Controller,
      no outward references from the Domain layer, etc.).
- [ ] It includes at least one test if the card is a Feature type (unit or integration,
      depending on the layer).
- [ ] Error handling uses domain exceptions, not loose try/catch blocks
      manually returning BadRequest.
- [ ] No hardcoded secrets, connection strings, or keys in the code.
- [ ] Merged into the main branch (or the designated integration branch).
- [ ] The commit/PR references the card ID (e.g., "AUTH-02: implement login").


### Technical Notes
Implementation decisions, links to library documentation,
reference snippets, or questions to resolve during implementation.
