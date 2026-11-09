---
id: "78120472-e317-5b6d-9097-fbe6ebcf8149"
title: "Checkpoint: Claude Code - modernizarea AI Memory Vault"
type: "project"
lifecycle: "REVIEW"
created: "2026-11-08T21:00:00Z"
updated: "2026-11-09T12:00:00Z"
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
6. `cognitive_core/` din rădăcină = shim de `__path__` + 4 CLI-uri, câștigă pe sys.path (pytest.ini:16).
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
- [x] WP-02 matrice de decizie (eu, 09c1c9860): `07_EVALUATION/reports/modernization/F0_module_decision_matrix.md`.
- [x] WP-03 review F0 (vault-reviewer, Opus): ACCEPTAT CU REZERVE → `F0_review_report.md`.
      Reparat: H1 (memory_vault are consumatori CI/test/pyproject), M1 (umbrirea cognitive_core depinde de
      context), M2 (routing, polymarket), L1 (artefact regenerat), L3, L4. Reziduu → WP-04: L2, L5.
- PR draft: userist123/AI_Memory_Vault#372 (ramura modernize/f0-inventory).

## F1 — ramura `modernize/f1-boundaries` (peste F0, același worktree D:/w/mod-f0)
- [x] WP-04 (2026-11-09, claude-code; vault-worker Sonnet, 2 runde vault-reviewer Opus):
      commit 3554fb79d + e8b4c3306 + documentare reziduuri. `04_CONFIG/architecture_layers.json`
      (L0..L8, L8 = fațade; 31 muchii în sus în baseline, 2 hard_forbidden interfaces→xau_kinetic → WP-12),
      `30_SCRIPTS/verification/architecture_boundaries.py` (ratchet: nou/creștere/stale/nealocat/coliziuni/
      importuri dinamice; `--write-baseline` doar strânge, lărgirea cere `--allow-widen --owner-wp --reason`),
      `package_inventory.py`: L2 reparat (memory_vault → 16 surori, `shim_target_source`), `string_import_refs`
      (249), test-path importat de producție = producție. Teste: 50 passed (4 fișiere, rulat de mine).
      Review r1: ACCEPTAT CU REZERVE (F1–F5, reparate). Review r2 (delta): ACCEPTAT CU REZERVE, nimic blocant;
      R1–R6 (rezidual, fără instanțe în repo) documentate în `unenforced` din config.
      Ordinea straturilor e decizia mea din dependențele reale (Masterplan-ul A3 negăsit pe disc) — de comparat.
- Necunoscut: ce workflow CI rulează `20_TESTS/test_architecture_boundaries.py` pe PR (→ WP-05).
- Notă: pytest pe Windows cere `--basetemp=<dir propriu>` dacă rulează alt pytest (WinError 5).

## Următorul pas (sesiune nouă)
WP-04b (nou): decizia pentru cele 129 de fișiere de test din afara `20_TESTS`, cu baseline propriu.
WP-05: review F1 cu sonda de import interzis + legarea checker-ului într-un workflow CI (pas separat).
Opțional: R1/R2 (argv `-c` și șiruri shell în .py) înainte de a te baza pe ratchet pentru importuri din șiruri.
