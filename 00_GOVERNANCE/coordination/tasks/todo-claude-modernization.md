---
id: "78120472-e317-5b6d-9097-fbe6ebcf8149"
title: "Checkpoint: Claude Code - modernizarea AI Memory Vault"
type: "project"
lifecycle: "REVIEW"
created: "2026-11-08T21:00:00Z"
updated: "2026-11-08T22:30:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "00_GOVERNANCE/coordination/tasks/todo-claude-modernization.md"
  created_by: "claude-code"
tags:
  - "checkpoint"
  - "modernization"
  - "coordination"
---

# todo-claude-modernization — modernizarea AI Memory Vault (Masterplan + Plan, 18 WP)

Sursă plan: Masterplan_si_Plan_Executabil_Adaptat.docx (ChatGPT → Gemini, 08.11.2026).
Ramură F0: `modernize/f0-inventory`, worktree `D:/w/mod-f0`, bază origin/main `0d97d13c3`.

## Corecții aplicate planului (verificate pe repo, 2026-11-08)
1. Rapoartele/dovezile NU merg în `50_ARTIFACTS/` (README-ul lui: exporturi versionate). Merg în
   `07_EVALUATION/reports/modernization/`.
2. Validator de căi există deja: `30_SCRIPTS/verification/navigation_links.py` + `20_TESTS/test_navigation_links.py`.
   WP-17 îl extinde (scope + docs de arhitectură), nu creează un validator paralel.
3. Nucleul cognitiv înghețat (CLAUDE.md:62, AGENTS.md:169, `20_TESTS/test_protected_core_boundaries.py`):
   WP-10/WP-14 nu modifică modulele protejate; doar le conectează.
4. `pytest.ini` colectează doar `20_TESTS` (356 fișiere). Alte teste: 02_PRODUCT 54, security 37,
   07_EVALUATION 20, 03_IMPLEMENTATION 14 — baseline-ul le raportează separat.
5. `products/` conține doar `xau_kinetic`; `polymarket` e în `packages/`. Încălcare existentă:
   `packages/interfaces/financial_ingestion.py` importă `xau_kinetic` (pachet → produs).
6. `cognitive_core/` din rădăcină = shim de `__path__` + 4 CLI-uri, câștigă pe sys.path (pytest.ini:14).
   Nu se mută înainte de graful de importuri complet (WP-14).
7. `01_ARCHITECTURE` nu se redenumește (~767 fișiere referă). WP-15 decide doar 01_KNOWLEDGE (2 fișiere,
   182 referințe) și 08_RESEARCH (72 referințe, folosit de CI).
8. Cele două requirements au aceleași 12 nume; v6 e folosit de 4 workflow-uri + 4 fișiere → unificarea
   actualizează și acestea.
9. CI: 49 workflow-uri, pytest țintit pe căi; nu există test de straturi (WP-04 e nou, justificat).

## Decizii pentru întrebările planului (variantele sigure, reversibile; proprietarul poate schimba)
- Q1 polymarket/xau_kinetic: nimic nu se mută până la WP-12; țintă implicită products/ (mutare git, reversibilă).
- Q2 providers: local/offline implicit; extern doar cu configurare explicită.
- Q3 prefixe: fără redenumire 01_ARCHITECTURE; restul decis în WP-15 pe numărul de referințe.

## Stare
- [x] WP-01 baseline: `python -m pytest -q` pe origin/main 0d97d13c3 → 5157 passed, 55 skipped, 9 xfailed,
      1 failed (cauza: acest fișier fără frontmatter; reparat, testul trece), 18 min.
      Raport: `07_EVALUATION/reports/modernization/F0_baseline_pytest.md`.
- [x] WP-01 inventar AST (vault-worker, commit 0485abe59): `30_SCRIPTS/verification/package_inventory.py`,
      `07_EVALUATION/reports/modernization/F0_package_inventory.{json,md}`, test 2 passed (rulat de mine).
      Observație ulterioară: testele scriu `audit_log.jsonl` în rădăcina repo-ului (ignorat de git, semnalat de hygiene).
      Notă: două rulări pytest simultane se ciocnesc pe `pytest-current` (WinError 5) — una singură o dată.
- [ ] WP-02 matrice de decizie (design, eu)
- [ ] WP-03 review F0 (vault-reviewer)
