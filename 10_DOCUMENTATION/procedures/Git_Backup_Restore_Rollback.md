---
id: "0c4c8b76-85c4-4fde-a14a-4bde0b840009"
type: procedure
lifecycle: ACTIVE
category: vault-maintenance
tags: [procedure, git, backup, rollback]
created: 2026-08-09
updated: 2026-08-09
provenance:
  source_type: user
  source_ref: "Foundation Hardening Plan, 2026-08-09"
confidence: very_high
verification: verified
relations:
  - target_id: "00b606ec-9dda-4a8f-a797-73de0f22a025"
    type: implements
    target: "[[Integrity Check]]"
---

# Git, Backup, Restore, and Rollback

## Purpose

Preserve recoverable history while protecting raw provenance.

## Procedure

1. Inspect the target files and run the Integrity Check before a material change.
2. Review the change set; never include credentials or other secrets.
3. Commit coherent, validated changes with a descriptive message when Git is available.
4. Keep an independent backup of the vault, including `06_INBOX/RAW_IMPORTS/`, before a migration or mass modification.
5. Restore by copying from a verified backup or by reverting a reviewed commit; verify raw evidence paths and canonical links after restore.
6. Roll back by creating a corrective commit or restoring selected files. Do not delete raw sources to make a rollback appear clean.

## Failure handling

If Git is unavailable, record that limitation and create a timestamped filesystem backup before material changes. Never claim that a backup or restore succeeded without checking the result.

## Related

- [[Storage Conventions]]
- [[Integrity Check]]

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[12 Projects and Procedures Map]]
- [[Knowledge Graph Home]]
- [[Knowledge Graph Home]]
