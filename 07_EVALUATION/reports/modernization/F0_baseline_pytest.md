---
id: "3f2b0c1e-6a4d-5e8f-9b7a-2c1d0e9f8a71"
title: "F0 - linia de bază pytest (modernizare)"
type: "evidence"
lifecycle: "REVIEW"
created: "2026-11-08T22:30:00Z"
updated: "2026-11-08T22:30:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "07_EVALUATION/reports/modernization/F0_baseline_pytest.md"
  created_by: "claude-code"
tags:
  - "modernization"
  - "baseline"
---

# F0 — linia de bază pytest

| Câmp | Valoare |
|---|---|
| Commit | `0d97d13c3` (origin/main, 2026-11-08) |
| Comandă | `python -m pytest -q --tb=line -p no:cacheprovider` (din rădăcina repo-ului) |
| Colectat | doar `20_TESTS` (`pytest.ini` `testpaths = 20_TESTS`) |
| Passed | 5157 |
| Skipped | 55 |
| Xfailed | 9 |
| Failed | 1 |
| Durată | 1091,48 s (18 min 11 s) |
| Exit code | 1 |

## Singurul eșec

`20_TESTS/test_notes_strict_yaml.py::TestVaultNotesAreReadable::test_no_unlisted_unreadable_file` —
cauza a fost checkpoint-ul nou `00_GOVERNANCE/coordination/tasks/todo-claude-modernization.md`, creat fără
frontmatter în timpul rulării (`kind: no_frontmatter`). După adăugarea frontmatter-ului testul trece.
După reparare am rulat doar testul afectat (trece), nu toată suita: linia de bază de 5158 passed / 0 failed
pe codul de pe `main` este o inferență, NEVERIFICAT printr-o rulare completă.

## Ce nu acoperă linia de bază

Fișierele `test_*.py` din afara `20_TESTS` nu sunt colectate implicit: 02_PRODUCT 54, security 37,
07_EVALUATION 20, 03_IMPLEMENTATION 14, .agents 4 (numărare: `F0_package_inventory.json`). CI le rulează
țintit pe căi, nu prin `pytest` simplu. Starea lor e UNVERIFIED în F0.

## Efecte secundare observate

- Rularea scrie `audit_log.jsonl` în rădăcina repo-ului (ignorat de git; `repository_hygiene.py` îl semnalează).
- Două rulări pytest simultane pe aceeași mașină se ciocnesc la curățarea `pytest-current` (WinError 5,
  apare după sumar, nu schimbă rezultatele).
