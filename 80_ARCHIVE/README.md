# 80_ARCHIVE — Historical and Inactive Assets

This directory contains archived, inactive, deprecated, or snapshot assets preserved for historical reference and forensics.

## Contents

- legacy_duplicates/: Deprecated duplicated notes and assets.
- legacy_zips/: Historical archive packages.
- obsidian/: Historical Obsidian vault configuration and notes.
- scratch/: Experimental scripts and intermediate artifacts.
- snapshots/: Periodic codebase state snapshots.
- modernization/: Production-package modules without a production consumer, moved out during the AI Memory Vault modernization (WP-12: `interfaces/financial_ingestion.py`, a package-to-product import with no consumer; WP-14b: `root_scripts/`, the repository-root `scripts/` left over from the 2026-09-04 migration, whose canonical copies are in `30_SCRIPTS/` and `40_EXPERIMENTS/harnesses/`). Restore with `git mv` back into `03_IMPLEMENTATION/packages/` only together with a real consumer and a layer-legal home.
