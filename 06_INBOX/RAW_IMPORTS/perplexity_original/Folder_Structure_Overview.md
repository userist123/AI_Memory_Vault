# Vault Folder Structure

## Complete Overview

This document provides a **complete reference** for the AI Vault folder structure, including all folders, their purposes, and example contents.

---

## Root Structure

```
AI_Vault/
├── 00_CORE/
├── 01_KNOWLEDGE/
├── 02_PROJECTS/
├── 03_PROCEDURES/
├── 04_MEMORY/
├── 05_RESOURCES/
├── 06_INBOX/
├── Templates/
├── RAG_Structure.md
├── Knowledge_Graph_Schema.md
└── Folder_Structure_Overview.md
```

---

## 00_CORE/

**Purpose:** Foundational system definitions and governance

```
00_CORE/
├── Identity.md
├── Rules.md
├── Goals.md
├── System_Architecture.md
├── Tag_Taxonomy.md (future)
└── Changelog.md (future)
```

| File | Purpose | Status |
|------|---------|--------|
| Identity.md | System identity and purpose | ✅ Complete |
| Rules.md | Operational guidelines | ✅ Complete |
| Goals.md | Strategic objectives | ✅ Complete |
| System_Architecture.md | Technical documentation | ✅ Complete |
| Tag_Taxonomy.md | Tag standards | 🔴 Planned |
| Changelog.md | Version history | 🔴 Planned |

---

## 01_KNOWLEDGE/

**Purpose:** Domain knowledge and reference material

```
01_KNOWLEDGE/
├── Technical/
│   ├── Cybersecurity/
│   ├── Software_Development/
│   ├── System_Administration/
│   └── AI_ML/
├── Domain/
│   ├── Finance_Trading/
│   ├── Psychology/
│   └── Personal_Development/
├── Concepts/
│   ├── RAG/
│   ├── Knowledge_Graphs/
│   └── Memory_Systems/
└── Reference/
    ├── APIs/
    ├── Tools/
    └── Best_Practices/
```

### Example Notes

**01_KNOWLEDGE/Technical/Cybersecurity/**
- `[[Network_Security_Basics]]`
- `[[Encryption_Methods]]`
- `[[Threat_Modeling]]`

**01_KNOWLEDGE/Domain/Finance_Trading/**
- `[[Trading_Strategies]]`
- `[[Risk_Management]]`
- `[[Market_Analysis]]`

**01_KNOWLEDGE/Concepts/RAG/**
- `[[RAG_Theory]]`
- `[[Embedding_Models]]`
- `[[Retrieval_Strategies]]`

---

## 02_PROJECTS/

**Purpose:** Active and completed project tracking

```
02_PROJECTS/
├── Active/
│   ├── AI_Vault_Build/
│   ├── Trading_Bot/
│   └── Certifications/
├── Completed/
│   ├── 2026_Q3/
│   │   └── Project_Name.md
│   └── Archive/
└── Backlog/
    └── Ideas/
```

### Project Status

| Status | Location | Criteria |
|--------|----------|----------|
| Active | 02_PROJECTS/Active/ | Currently being worked on |
| Completed | 02_PROJECTS/Completed/YYYY_QN/ | Finished this quarter |
| Backlog | 02_PROJECTS/Backlog/ | Future consideration |
| Archive | 02_PROJECTS/Completed/Archive/ | Old completed projects |

---

## 03_PROCEDURES/

**Purpose:** Step-by-step operational procedures

```
03_PROCEDURES/
├── Import/
│   ├── Export_ChatGPT.md
│   ├── Export_Claude.md
│   ├── Export_Gemini.md
│   └── Classification_Workflow.md
├── Maintenance/
│   ├── Weekly_Review.md
│   ├── Monthly_Audit.md
│   └── Archive_Process.md
├── RAG/
│   ├── Query_Formulation.md
│   ├── Context_Assembly.md
│   └── Response_Generation.md
└── Troubleshooting/
    └── Common_Issues.md
```

### Procedure Templates

All procedures use [[Template_Procedure]] format:
- Purpose
- Scope
- Prerequisites
- Step-by-step instructions
- Verification
- Rollback
- Change log

---

## 04_MEMORY/

**Purpose:** Historical conversations and experiences

```
04_MEMORY/
├── Conversations/
│   ├── By_Date/
│   │   ├── 2026/
│   │   │   ├── 08_August/
│   │   │   └── 07_July/
│   │   └── 2025/
│   └── By_Topic/
│       ├── Technical/
│       ├── Personal/
│       └── Decisions/
├── Experiences/
│   ├── Successes/
│   ├── Failures/
│   └── Lessons_Learned/
├── Decisions/
│   ├── Career/
│   ├── Technical/
│   └── Personal/
└── Patterns/
    ├── Behavioral/
    └── Cognitive/
```

### Memory Classification

| Type | Location | Template |
|------|----------|----------|
| Conversations | 04_MEMORY/Conversations/ | N/A (raw export) |
| Experiences | 04_MEMORY/Experiences/ | [[Template_Experience]] |
| Decisions | 04_MEMORY/Decisions/ | [[Template_Decision]] |
| Patterns | 04_MEMORY/Patterns/ | [[Template_Lesson]] |

---

## 05_RESOURCES/

**Purpose:** External references and curated content

```
05_RESOURCES/
├── Links/
│   ├── Articles/
│   ├── Tutorials/
│   └── Documentation/
├── Books/
│   ├── Summaries/
│   └── Notes/
├── Tools/
│   ├── Software/
│   └── Services/
└── People/
    ├── Experts/
    └── Contacts/
```

### Resource Notes

**05_RESOURCES/Links/Articles/**
- `[[Article_Title]]` with URL, summary, tags

**05_RESOURCES/Books/Summaries/**
- `[[Book_Title_Summary]]` with key takeaways

**05_RESOURCES/Tools/Software/**
- `[[Tool_Name]]` with purpose, setup, usage

---

## 06_INBOX/

**Purpose:** Unprocessed incoming information

```
06_INBOX/
├── Unprocessed/
│   ├── 2026-08-09_Note_1.md
│   └── 2026-08-09_Note_2.md
├── Processing/
│   └── Being_Classified.md
└── To_Archive/
    └── Ready_For_Filing.md
```

### Inbox Workflow

1. **Capture:** New info goes to `06_INBOX/Unprocessed/`
2. **Process:** Move to `06_INBOX/Processing/` during review
3. **Classify:** Determine destination folder
4. **File:** Move to final location
5. **Archive:** Move outdated to `06_INBOX/To_Archive/` before deletion

---

## Templates/

**Purpose:** Note templates for consistency

```
Templates/
├── Template_Knowledge.md
├── Template_Project.md
├── Template_Procedure.md
├── Template_Decision.md
├── Template_Experience.md
├── Template_Error.md
├── Template_Lesson.md
└── Template_Preference.md
```

### Template Usage

In Obsidian:
1. Install **Templates** plugin
2. Set template folder to `Templates/`
3. Use hotkey to insert template
4. Fill in fields

---

## Root Files

| File | Purpose |
|------|---------|
| `RAG_Structure.md` | RAG pipeline documentation |
| `Knowledge_Graph_Schema.md` | Graph structure definition |
| `Folder_Structure_Overview.md` | This file - folder reference |

---

## Folder Naming Conventions

### Numbering System

| Prefix | Purpose |
|--------|---------|
| `00_` | Core system files |
| `01_` | Knowledge domains |
| `02_` | Projects |
| `03_` | Procedures |
| `04_` | Memory |
| `05_` | Resources |
| `06_` | Inbox |

### Benefits

- **Alphabetical ordering:** Folders appear in logical order
- **Visual hierarchy:** Easy to scan
- **Consistent structure:** Predictable navigation

---

## File Naming Conventions

### General Rules

- **Use PascalCase:** `SystemArchitecture.md`
- **Or Snake_Case:** `System_Architecture.md`
- **Avoid spaces:** Use underscores or hyphens
- **Be descriptive:** `Trading_Strategy_Notes.md` not `Notes1.md`

### Template Files

- Prefix: `Template_`
- Example: `Template_Knowledge.md`, `Template_Project.md`

---

## Tag Structure

### Hierarchical Tags

```
#type/knowledge
#type/project
#type/procedure
#type/decision
#type/experience
#type/error
#type/lesson
#type/preference

#domain/technical
#domain/finance
#domain/psychology
#domain/relationships

#status/active
#status/completed
#status/archived
#status/draft

#priority/high
#priority/medium
#priority/low
```

---

## Storage Estimates

| Folder | Estimated Notes | Est. Size |
|--------|-----------------|-----------|
| 00_CORE/ | 10 | 50 KB |
| 01_KNOWLEDGE/ | 500 | 2.5 MB |
| 02_PROJECTS/ | 100 | 500 KB |
| 03_PROCEDURES/ | 50 | 250 KB |
| 04_MEMORY/ | 1000 | 5 MB |
| 05_RESOURCES/ | 200 | 1 MB |
| 06_INBOX/ | 50 | 250 KB |
| **Total** | **~1910** | **~9.5 MB** |

---

## Backup Strategy

### Recommended Setup

- **Local:** Obsidian vault on local drive
- **Cloud Sync:** Obsidian Sync, Dropbox, or Google Drive
- **Version Control:** Git repository (optional)
- **Frequency:** Real-time sync + daily backup

### Backup Locations

1. **Primary:** Local SSD
2. **Secondary:** Cloud storage (sync)
3. **Tertiary:** External drive (weekly backup)

---

## Migration Path

### Phase 1: Foundation (Week 1-2)
- [x] Create 00_CORE files
- [x] Create Templates
- [x] Create RAG and Graph docs
- [ ] Create sample knowledge notes

### Phase 2: Import (Week 3-4)
- [ ] Export from ChatGPT/Claude/Gemini
- [ ] Classify into folders
- [ ] Apply templates
- [ ] Add links and tags

### Phase 3: Optimization (Week 5-8)
- [ ] Set up RAG pipeline
- [ ] Build knowledge graph
- [ ] Implement search
- [ ] Create automation scripts

---

## Related Files

- [[Identity]] — System purpose
- [[System_Architecture]] — Technical design
- [[RAG_Structure]] — RAG pipeline
- [[Knowledge_Graph_Schema]] — Graph structure

---

## Metadata

```yaml
---
type: reference
category: system
tags:
  - structure
  - folders
  - reference
  - organization
created: 2026-08-09
updated: 2026-08-09
status: active
source: manual
confidence: high
---
```

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
