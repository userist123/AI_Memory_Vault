---
id: "dc3a864f-c09c-47b5-8b08-1933ccf7b2e2"
type: resource
lifecycle: REVIEW
category: digital_twin
tags: [casa3d, research, lifecycle]
created: "2026-10-11"
updated: "2026-10-11"
provenance:
  source_type: web
  source_ref: "Web research by a Claude subagent on 2026-10-11; every source URL is cited inline in the body"
  source_date: "2026-10-11"
  redaction: not_applicable
  provenance_status: complete
confidence: medium
verification: unverified
relations:
  - type: related_to
    target_id: "f09fc736-92ae-472e-9f6b-395293bfbed8"
---
# Casa3D: project and data lifecycle, plus other non-design product hygiene (research 2026-10-11)

Status: web research, CLAIMED_ONLY (secondary sources unless noted). Not legal advice. Facts marked [verified] were checked on the vendor page itself.

## What the market does (evidence)

| Product | Delete / archive behaviour | Source |
|---|---|---|
| Houzz Pro | Delete is **permanent, no undo, removes all related documents**; blocked if the project has billed/issued POs, voided invoices or pending payments. Archive **was removed**; use status **Closed** (hidden from list, data kept) or **Done** (stays in list). [verified] | https://pro.houzz.com/pro-help/r/how-to-archive-and-unarchive-a-project |
| Mydoma Studio | Projects ("binders") can be archived and unarchived; no public doc on permanent delete found. | https://help.mydomastudio.com/en/collections/120073-projects |
| Canva | Trash keeps designs **30 days**; after purge, support may restore if asked **within 14 days**. | https://www.canva.com/help/deleted-designs/ |
| Notion | Trash **30 days** by default, then a further **30 days** hidden retention before it is gone; Enterprise can change this. | https://www.notion.com/help/duplicate-delete-and-restore-content |
| Google Drive | Trash auto-purges after **30 days**; trashed items still count against storage; owner-trashed files stay shared until purged. | https://support.google.com/drive/answer/14933051 |
| Figma | Help centre: files stay in Trash until restored or deleted forever; deleting a project deletes its files; restoring the project restores them; "delete forever" cannot be undone. | https://help.figma.com/hc/en-us/articles/360047512294 |
| GitHub / Heroku | Irreversible delete needs the **name typed**; button stays disabled until the text matches. | https://www.datacamp.com/tutorial/how-to-delete-a-github-repository |
| Ivy | Bought by Houzz and folded into Houzz Pro; new sign-ups ended. Lesson: users need an export path when a tool dies. | https://pro.houzz.com/for-pros/difference-between-ivy-and-houzz-pro |

Main finding: the 30-day trash window is the de facto standard (Canva, Notion, Drive). The closest competitor, Houzz Pro, has a hard delete and steers users to a Closed status instead. Casa3D can beat it with a trash that has a restore window.

## Ranked: 15 non-design features for Casa3D

1. **Delete project = soft delete into Trash, purged after 30 days.** Set `deleted_at`, hide the project everywhere, and revoke its share links right away (return 410 "proiect indisponibil" (project unavailable)). Restore with one click from Trash. A daily job hard-deletes projects older than 30 days: DB rows plus the render, photo and upload directories on disk. "Delete forever" in Trash needs type-the-name confirmation. Evidence: Canva, Notion, Drive (above). This closes the current gap where the API deletes but the render files stay on disk.
2. **Project status: Lead, Active, Completed, Closed or Archived.** Archived projects are read-only and hidden from the default list, never deleted. Houzz Pro uses Closed and Done for exactly this. Archive is the safe default and delete is the exception.
3. **Block or warn on delete when commercial records exist.** If a project has orders, issued offers or invoices, block the delete (as Houzz does) or keep those financial rows anonymised. Romanian accounting documents must be kept **10 years** (Law 82/1991 art. 25). A bill to cut this to 5 years was reported, so check whether it is in force (https://www.contzilla.ro/arhivarea-documentelor-financiar-contabile/, https://profit.ro/taxe-si-consultanta/lege-perioada-in-care-firmele-sunt-obligate-sa-arhiveze-documentele-financiar-contabile-redusa-drastic-20978356).
4. **Project list: search, filter by status, sort by updated, created, name or client.** Default view is Active, sorted by last updated. Search covers project name, client name and address. Reviews of Mydoma name clutter and navigation as the main complaints ("Hard to maneuver. Lots of tags": https://www.capterra.com/p/155585/Mydoma-Studio/reviews/?page=3).
5. **Duplicate project and save as template.** Copy the plan, rooms, product selections and settings. Never copy client data, share links, orders, visits or renders. Common pattern (Asana "duplicate as template": https://asana.com/pl/inside-asana/create-project-templates-with-duplicate-project). A DesignFiles reviewer asked to "load in basics of past projects".
6. **Export all data (GDPR art. 20).** Offer per-project and whole-account ZIP files with JSON for structured data plus the original files (plans, photos, renders), and CSV for the catalog and orders. PDF alone does not count as machine-readable. Art. 12(3) sets a **1-month** response deadline, extendable by 2 months (https://guidelines.panelfit.eu/the-gdpr/data-subject-rights/right-to-data-portability/, https://secureprivacy.ai/blog/gdpr-right-to-data-portability-article-20-what-it-requires-and-how-to-implement-). Generate the export async and email a link that expires after 7 days. This is also the answer to the "what if the tool dies" problem that Ivy users faced.
7. **Full account deletion (art. 17).** Flow: re-authenticate, offer an export, type-to-confirm, then a 30-day grace period during which the account is locked, with a cancel link by email. After that, hard-delete everything and keep only the legally required accounting records. Document how backups are handled: data ages out with backup rotation, and restored backups re-apply the deletions. The EDPB's 2025 coordinated enforcement report singles out erasure in backups and undefined retention periods as common failures (https://www.edpb.europa.eu/system/files/2026-02/edpb_cef-report_2025_right-to-erasure_en.pdf).
8. **Client data as a data-processing relationship.** The designer is the controller for the end client's data (name, phone, address, home photos) and Casa3D is the processor. Required: a DPA template, a per-client "erase client" action that removes PII and photos while keeping anonymised order rows, and a retention prompt for clients with no activity for 24 months (recommended default, not a legal number).
9. **Share-link lifecycle.** Every link has an expiry (default 90 days, renewable), a revoke button, a view count with last-viewed time, and automatic revocation when the project is archived, trashed or the account deleted. Google Drive keeps trashed files shared until purge, which is a pitfall to avoid.
10. **Orphan-file cleanup and storage quota.** A nightly job finds files on disk with no DB row, and DB rows with no file. Orphan files are deleted after a 7-day grace period, with logging. A Settings page shows storage used per project with a "delete old renders" option (keep the last N per room). Drive counts trash against quota; Casa3D should too, so users know why to empty it.
11. **Backups and restore drills.** Nightly `pg_dump` plus file sync. Keep 7 daily, 4 weekly and 3 monthly copies, encrypted and stored off-host. Test a restore every quarter. Backup retention (max about 90 days) sets the real erasure horizon to state in the privacy policy.
12. **Autosave, versioning and conflict handling.** Autosave on change, debounced to about 2 s, with a "Saved / Saving / Offline" indicator. Use optimistic concurrency (a `version` column, HTTP 409 when two tabs or devices edit the same thing) and show a "reload or overwrite" choice; never silently overwrite. Keep named plan snapshots (last 30 or 30 days). Undo toast for 5 to 10 s on small deletes (room, item) instead of a confirm dialog. Confirm dialogs only for irreversible actions, because users habitually click through them (https://dev.to/137foundry/how-to-choose-between-confirmation-dialogs-undo-windows-and-soft-delete-patterns-1opj).
13. **Site visits under poor network.** Houzz Pro's offline mode is mostly read-only. Notes (and in some regions photos and room scans) are queued in an "Offline Create Queue" and synced on reconnect (https://pro.houzz.co.uk/pro-help/r/offline-mode-on-the-houzz-pro-app). For Casa3D, the minimum is: cache the open project, queue site-visit notes and photos in IndexedDB, show an offline banner and a visible pending-uploads count, and upload photos resumably and compressed (for example, max 2560 px long edge).
14. **Settings: company profile.** Logo, legal name, CUI (tax ID), Reg. Com. (trade register number), IBAN and address appear on every PDF (offer, spec, client presentation). Currency RON with optional EUR display and the exchange rate stored per offer. VAT rate configurable (currently 21% standard in RO; verify) with prices shown with or without VAT. Notifications: email for share-link viewed, client comment, order status, upcoming site visit (24 h before); a daily digest instead of per-event email, with opt-out per type. **e-Factura:** out of scope; mention it and integrate with or export to SmartBill, Oblio or FGO. Since 2025 B2C invoices are reported too, and since 2026 the deadline is 5 working days (https://ec.europa.eu/digital-building-blocks/sites/display/DIGITAL/eInvoicing+in+Romania, https://beancount.io/blog/2026/09/20/romania-e-factura-5-working-days-deadline-cross-border-sellers-guide).
15. **Onboarding, empty states, accessibility, shortcuts, error reporting, performance.**
    - Onboarding: a sample project plus a 3-step checklist. Reviewers ask for onboarding (DesignFiles: https://www.capterra.com/p/179202/DesignFiles/reviews/?page=4) and report that client invitation is confusing.
    - Empty states: each one has one call to action.
    - Accessibility: WCAG 2.1 AA for the non-3D UI (forms, lists, dialogs, focus trap, contrast).
    - Shortcuts: Ctrl+Z/Y, Del, Ctrl+D, Ctrl+S (no-op, saved), and a `?` cheatsheet.
    - Error reporting: an error boundary with a "send report" option, plus server logs with request ID.
    - Performance: the project list paginates at 50 items with lazy thumbnails. Budgets: the list loads in under 1 s at 200 projects; the 3D view stays interactive with 500 items.
    - Why: slowness is the top Houzz Pro complaint ("painfully slow… over a minute": https://www.capterra.co.uk/reviews/199689/houzz-pro; "choppy and slow when… in front of a client": https://capterra.com/p/199689/Houzz-Pro/reviews/). DesignFiles is "a little slow to catch up".

## Audit log (supporting all of the above)
Use an append-only `audit_event` table (who, what, project, when, IP) for delete, restore, archive, export, share-link create/revoke, order status and account deletion. Keep it 12 months, and pseudonymise it on account deletion. Show it per project as "Activity".

## Review complaints, summarised
- **Slow, clunky UI:** Houzz Pro (Capterra summary: "slow, unintuitive, and cumbersome"), DesignFiles, Mydoma.
- **Clutter and navigation:** Mydoma ("Lots of tags", "so many elements").
- **Hard to cancel or leave:** Houzz Pro, "difficult subscription cancellation" category, 98% negative of 42 reviews (https://www.capterra.co.uk/reviews/199689/houzz-pro). This is an argument for self-serve export and account deletion.
- **Client portal confusion:** Mydoma, Programa (https://www.capterra.com/p/248518/Programa/reviews/?page=2).
- **Lost data / can't delete:** no G2, Capterra or Reddit evidence found in this pass (UNKNOWN). Houzz Pro's permanent no-undo delete is the structural risk.

## Pitfalls
- Hard delete with no trash (Houzz) means a single misclick wipes out months of work.
- Deleting DB rows but leaving files on disk (the current Casa3D state) leaks storage and personal data, and breaks GDPR erasure.
- Share links that keep working after a project is deleted or archived.
- Deleting invoices or orders that must legally be kept for 10 years. Separate the financial records from the design data.
- "Are you sure?" on every action trains users to click through. Use undo for small actions, type-the-name for irreversible ones.
- Duplicating a project and copying the client's PII and share tokens into it.
- Export only as PDF, which does not satisfy portability.
- Backups that bring deleted data back after a restore. Keep a deletion ledger and re-apply it.
- Last-write-wins autosave across a laptop and a tablet at the site, which silently loses edits.
- Trash not counted in storage, so users cannot work out why their quota is full.
