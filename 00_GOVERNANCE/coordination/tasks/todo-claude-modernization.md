---
id: "78120472-e317-5b6d-9097-fbe6ebcf8149"
title: "Checkpoint: Claude Code - modernizarea AI Memory Vault"
type: "project"
lifecycle: "REVIEW"
created: "2026-11-08T21:00:00Z"
updated: "2026-11-10T14:00:00Z"
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

- [x] WP-04b (vault-worker, 93ef32d94): `07_EVALUATION/reports/modernization/F1_tests_outside_testpaths.{md,json}`,
      30 de grupuri: 1 CI verde, 13 neacoperite verzi, 7 neacoperite roșii, 3 blocate, 6 nu-sunt-teste. Nimic mutat.
- [x] WP-05 CI: `.github/workflows/architecture-boundaries.yml` (93ef32d94 + 6eeb4ac9c deps + fără filtru `paths:`,
      plus `test_package_inventory.py`). CI pe 6eeb4ac9c: success (17 passed).
- [x] WP-05 review F1 (vault-reviewer, Opus): ACCEPTAT CU REZERVE. Probele de import interzis (L0→L4, hard_forbidden,
      unitate nealocată) și refuzul lărgirii baseline-ului: toate pică corect. Reparat: M1 (trigger-e → fără `paths:`),
      L1 (avertisment `.checkpoints` jarvis în raport/JSON), L3 (testul inventarului în workflow).
      M2 — acțiunea proprietarului: `main` nu e protejat (404); `boundaries` trebuie făcut verificare obligatorie.
      L2 — DESCHIS pentru WP-06: ordinea A3 din plan dă MAI PUȚINE muchii în sus decât config-ul (27 perechi / 70 fișiere
      față de 31 / 124); `interfaces` pe L2 importă 8 unități superioare și are ciclu cu `security`. WP-06 decide ordinea
      pe aceste cifre (config sau A3 adaptat), nu pe preferință.

## F2 — ramura `modernize/f2-memory-core` (peste F1, același worktree D:/w/mod-f0)
- [x] WP-06 (2026-11-09, claude-code, design în sesiunea principală; 2 runde vault-reviewer Opus):
      `07_EVALUATION/reports/modernization/F2_memory_core_contract.md` + `F2_layer_order_analysis.py` (dovezi, doar citire).
      Decizii: (1) fațadele nu sunt straturi; pe graful real (fațade rezolvate) ordinea nouă L0..L8 dă 8 perechi /
      11 fișiere în sus = minimul exact (config 11/25, A3 15/28, graful cu alias-uri 31/124). `interfaces` = puncte de
      intrare (L6), nu contracte. (2) Contract v1 Memory Core: controller/storage/core.Lifecycle; canonic = reuniunea
      `cognitive_core`, `memory_controller` devine alias `sys.modules` (azi 2 copii ale modulelor: api_server încarcă
      `authorizer` de 2 ori). Numele fizice NU devin canonice: nucleul protejat folosește importuri relative peste
      pachete (review r1 H1). (3) `memory_vault` = alias de suprafață, păstrat; `DeprecationWarning` AMÂNAT (WP-08 M1) în WP-ul care îl retrage; `packages/cognitive_core` păstrat.
      `vault_runtime` rămâne în interfaces până la WP-14.
- Decizia proprietarului (opțională): excepție Protected Core dacă vrea nume fizice canonice (§5 din document).

- PR draft WP-06: userist123/AI_Memory_Vault#374 (CI verde 9/9).
- [x] WP-07 PR A (2026-11-09; vault-worker Sonnet ×2, vault-reviewer Opus ×2): ramura `modernize/f2-wp07a-facades`,
      PR #375 (bază #374). Commit-uri e0928900e, cdb92ece7, 186f266ed, fca03fb2a. Inventarul rezolvă fațadele la unitatea
      servită (ordinea reală din `__path__`, evaluator simbolic `path_eval`), config L0..L8 + `aliases` (6 intrări),
      baseline 9 intrări (8 perechi / 11 fișiere + hard_forbidden interfaces→products/xau_kinetic). Graful = oracolul
      F2 (44/115, pe tripleți). Review r1: RESPINS (H1: unitate nedeclarată cu `__path__.append`; H2: mutări
      `insert`/`+=`/extend ignorate) → reparate, confirmate cu sonde rc=1. r2: ACCEPT CU REZERVE; L1 reparat de mine
      (avertisment la config lipsă), R7 (obfuscare `__path__`) documentat în `unenforced`. Teste: 101 passed.

- [x] WP-07 PR B (2026-11-10; vault-worker Sonnet, vault-reviewer Opus ×2): ramura `modernize/f2-wp07b-alias`, PR #376
      (bază #375). `memory_controller/_alias.py`: MetaPathFinder, `memory_controller.X is cognitive_core.X` când fișierul e
      identic (4 nume diferite rămân ca înainte). Principal/MemoryController unice; starea la nivel de modul (singleton
      controller, cache recall_cli, rate-limit memory_access, audit logger, lock-uri) devine partajată, intenționat.
      `__name__` prin alias = `cognitive_core.X` (fără consumatori dependenți găsiți). Suita completă: 5256 passed /
      55 skipped / 9 xfailed. Review r1: RESPINS (H1 `python -m memory_controller.X` rupt → reparat, P8/P9);
      r2: ACCEPT CU REZERVE; L-a (spec real al modulului canonic) și L-b (`is_package` pe namespace) reparate de mine,
      test nou care pică pe e3ca72374. Teste de contract P1–P9 + L-a/L-b în
      `20_TESTS/test_memory_core_contract.py`. Notă: `01_ARCHITECTURE/memory/Lessons/Verify_Consumption_Not_Existence.md`
      spune încă „19-line” (memorie canonică, neatinsă; actualizare prin `memory_propose`).

- [x] WP-08 review integral F2 (2026-11-10, vault-reviewer Opus): GATA CU REZERVE. Ratchet-ul pică pe 5 mutații
      (fațadă L0→L4, `__path__.append` nedeclarat, hard_forbidden prin xau_kinetic), identitatea la plafon (10/10),
      `python -m memory_controller.api_server` → HTTP 200, wheel-ul conține `_alias.py`. Suita completă pe 3050a8109:
      5264 passed / 1 failed / 55 skipped / 9 xfailed; eșecul = `todo-claude-wp7b.md` fără frontmatter → șters (conținut
      mutat aici), test reverificat. Rezolvat: M2 (test de echivalență a celor două copii `cognitive_core`), M3
      (checkpoint duplicat), L1 (inventar regenerat). Amânat explicit: M1 (`DeprecationWarning` memory_vault) în WP-ul de
      retragere. Rămân pentru WP-14: L2 (din wheel, `memory_controller.controller` cere `security.verified_reduction`
      din root/security — preexistent).

## F3 — PR-uri #377 (WP-09) → #378 (WP-10) → #379 (WP-11) → #380 (WP-12), stivuite peste #376
- [x] WP-09: `graph/activation` cu Protocol, `memory/controller_factory.py`, ablation în `40_EXPERIMENTS`. Baseline 9→6.
- [x] WP-10: `lifecycle/validation/book_to_memory_*` → `packages/book_to_memory` (L7). Baseline 6→4.
- [x] WP-11: teste provider offline (subproces, socket blocat) + jurnal fără PII; `agent_bridge` dormant
      (`F3_agents_routing_providers.md`). Review r1 RESPINS (reload în proces) → reparat 00536be28.
- [x] WP-12: `data_router` → retrieval, `financial_ingestion` → `80_ARCHIVE/modernization`, polymarket → products. Baseline 4→2.
- [x] WP-13 review integral F3: GATA CU REZERVE; M1/L1/L2/L3/L5 reparate (e7d5d9292). Suita: 5276 passed, 0 failed.

## F4 — PR-uri #381 (WP-14a) → #382 (WP-14b) → #383 (WP-15)
- [x] WP-14a: un singur pachet `security` (L0), `require_owner_principal` în `security/owner_principal.py`.
      Ratchet-ul P3 a prins un al doilea `Principal` (import fizic `security.authorizer`) → reparat d4a0d38aa;
      garda de consumatori inversată (19a544288). Wheel-ul importă acum `memory_controller.controller`.
- [x] WP-14b: `vault_runtime` → `security/vault_secret_runtime.py` (+ alias `sys.modules`), `git_hooks` → interfaces,
      `scripts/` din rădăcină arhivat. **Baseline ratchet 2→0.** Suita: 5289 passed, 0 failed.
- [x] WP-15: prefixele rămân (`01_KNOWLEDGE` = rădăcina de date a stocării); test anti-derivă pe requirements.
- Notă: worker-ul Sonnet a căzut o dată cu eroare de acces API (`oauth_org_not_allowed`); WP-14b/WP-15 și
  reparațiile au fost făcute în sesiunea principală, review-urile pe Opus.

- [x] WP-16 review integral F4 (vault-reviewer Opus): GATA CU REZERVE, fără H/M. Suita pe clonă: 5291 passed, 0 failed.

## F5 — PR #384 (WP-17), peste #383
- [x] WP-17: README/README.en (produsele în `03_IMPLEMENTATION/products/`), nota de cale WP-14a și linkuri relative
      în `docs/security/AUDIT_REMEDIATION.md`, `security.tests` exclus din wheel, `*.egg-info/` în `.gitignore`.
- [x] WP-18 review final de release (vault-reviewer Opus): **GATA CU REZERVE**, 10 puncte: ratchet `baseline_edges=0`,
      identitate (un `Principal`, plafon P3 = 9), wheel (importurile merg, fără teste/produse), CLI-uri, nucleu protejat
      neatins, 0 teste slăbite (+748 assert, 0 șterse; 5222 → 5355 colectate), confidențialitate curată, CI verde pe
      #376/#380/#383/#384. Suita completă pe HEAD final 22c39ff5d: **5291 passed, 55 skipped, 9 xfailed, 0 failed**.
      L1 (ratchet-ul pica după un `pip wheel` local, din cauza `*.egg-info`) reparat + test; L2 (acest checkpoint) actualizat.
- Rămase, documentate, fără impact la runtime: linkuri `file:///` istorice în `08_RESEARCH/BOOK_TO_MEMORY/PHASE*`
  (preexistente); căi istorice în rapoartele de evaluare; shim-ul `xau_kinetic` din wheel indică în afara instalării
  (preexistent); `01_ARCHITECTURE/memory/Lessons/Verify_Consumption_Not_Existence.md` spune „19-line” (memorie
  canonică: actualizare prin `memory_propose`, decizia proprietarului).

## Pașii proprietarului (modernizarea e completă pe ramuri)
1. Protejează `main` și fă `boundaries` verificare obligatorie (azi `main` e neprotejat).
2. Merge strict în ordine #372 → #373 → … → #384, cu merge commit sau rebase (nu squash: PR-urile sunt stivuite).
   Nu te opri la #382/#383: #383 conține remedierile WP-14b, #384 excluderea testelor din wheel.

## Pași mai vechi (F1, închiși)
WP-04b (nou): decizia pentru cele 129 de fișiere de test din afara `20_TESTS`, cu baseline propriu.
WP-05: review F1 cu sonda de import interzis + legarea checker-ului într-un workflow CI (pas separat).
Opțional: R1/R2 (argv `-c` și șiruri shell în .py) înainte de a te baza pe ratchet pentru importuri din șiruri.
