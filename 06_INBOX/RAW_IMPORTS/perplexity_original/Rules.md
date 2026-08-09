# Rules

## Core Operational Rules

### 1. Information Integrity
- **Never fabricate information** — If uncertain, mark as [[Uncertain]] or [[To Verify]]
- **Always cite sources** — External links, conversation IDs, timestamps
- **Timestamp everything** — Every note must have created/updated dates
- **Version control** — Track changes with version numbers for critical notes

### 2. Knowledge Management
- **Inbox Zero principle** — Process 06_INBOX within 7 days
- **No orphans** — Every note must link to at least one other note
- **Atomic notes** — One concept per note; split if too broad
- **Progressive summarization** — Layer 1: Raw, Layer 2: Summarized, Layer 3: Key insights

### 3. Memory Operations
- **Read before write** — Check existing knowledge before creating new notes
- **Link aggressively** — Use [[wikilinks]] to connect related concepts
- **Tag consistently** — Use predefined tag taxonomy (see [[Tag_Taxonomy]])
- **Archive, don't delete** — Move outdated info to [[Archive]] with reason

### 4. RAG & Retrieval
- **Context windows** — Always include relevant [[Context_Notes]] in queries
- **Semantic search** — Use tags + keywords + date ranges
- **Confidence scoring** — Mark retrieved info with confidence level (High/Medium/Low)
- **Recency bias check** — Verify older notes aren't more relevant

### 5. Privacy & Security
- **No sensitive data** — Never store passwords, API keys, personal IDs
- **Encryption aware** — Assume vault may be exposed; structure accordingly
- **Access logging** — Track which AI platforms access which notes
- **Redaction protocol** — Mark sensitive info with ==redacted== and [[Reason_Redacted]]

---

## Decision Rules

### When to Create New Note
- [ ] Information is reusable (will be referenced again)
- [ ] Information is referenced by 2+ other notes
- [ ] Information represents a decision, lesson, or pattern
- [ ] Information fills a gap in knowledge graph

### When to Update Existing Note
- [ ] New information contradicts or supersedes old info
- [ ] Note has incomplete or outdated metadata
- [ ] Note can be linked to newly discovered concepts
- [ ] Note's summary can be improved

### When to Archive
- [ ] Information is time-bound and expired (e.g., project completed >6 months ago)
- [ ] Information was proven incorrect
- [ ] Information is redundant with better-structured note
- [ ] Information is no longer relevant to current goals

---

## Quality Standards

### Note Quality Checklist
- [ ] Clear, descriptive title (not "Note 1" or "Meeting")
- [ ] Metadata block complete (type, tags, created, updated)
- [ ] At least 2-3 internal links
- [ ] Structured with headers (##, ###)
- [ ] Contains actionable insight or reference value
- [ ] Written in plain language (future-self understandable)

### Tag Standards
- Use lowercase: `#project`, `#decision`, `#lesson`
- Max 5-7 tags per note
- Use hierarchical tags: `#project/active`, `#knowledge/technical`
- Avoid duplicate tags: pick one canonical form

---

## AI Interaction Rules

### Context Provision
- Always include [[Current_Goals]] in RAG queries
- Include relevant [[Active_Projects]] for context
- Reference [[Identity]] and [[Rules]] for new AI sessions
- Provide [[Recent_Decisions]] for continuity

### Output Standards
- Structure responses with markdown headers
- Use tables for comparisons
- Cite note references as [[Note_Name]]
- Mark uncertain info with ⚠️

### Error Handling
- If contradiction detected: flag with [[Conflict_Resolution_Needed]]
- If info missing: create [[To_Research]] note
- If confused: ask clarifying questions before proceeding
- If error made: log in [[Errors]] with [[Lessons_Learned]]

---

## Maintenance Rules

### Weekly (Every Sunday)
- [ ] Review 06_INBOX — process or archive all items
- [ ] Update [[Active_Projects]] status
- [ ] Check for orphaned notes
- [ ] Review [[To_Research]] queue

### Monthly (First of Month)
- [ ] Audit tag usage — merge duplicates
- [ ] Review [[Archive]] candidates
- [ ] Update [[Goals]] progress
- [ ] Check broken links

### Quarterly
- [ ] Full knowledge graph review
- [ ] Evaluate folder structure
- [ ] Update [[System_Architecture]] if needed
- [ ] Review and refresh [[Identity]]

---

## Enforcement

### Violation Types
- **Type A (Critical):** Fabricated info, missing citations, security breach
- **Type B (Major):** Orphaned notes, missing metadata, tag inconsistency
- **Type C (Minor):** Formatting issues, unclear titles, weak links

### Correction Protocol
1. **Detect** — Automated check or manual review
2. **Flag** — Add [[Needs_Attention]] tag with violation type
3. **Fix** — Correct within 7 days (Type A: 24 hours)
4. **Log** — Record in [[Maintenance_Log]]

---

## Related Files

- [[Identity]] — Core system definition
- [[Goals]] — Strategic objectives
- [[System_Architecture]] — Technical structure
- [[Tag_Taxonomy]] — Tag standards (future)
- [[Maintenance_Log]] — Tracking fixes (future)

---

## Metadata

```yaml
---
type: core
category: rules
tags:
  - rules
  - core
  - operations
  - governance
created: 2026-08-09
updated: 2026-08-09
status: active
---
```
