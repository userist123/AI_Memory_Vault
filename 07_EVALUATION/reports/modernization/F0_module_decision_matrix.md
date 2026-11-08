---
id: "9c4e2a7b-1d3f-5b6a-8e0c-4f7a2b9d1e35"
title: "F0 - matricea de decizie a modulelor (WP-02)"
type: "decision"
lifecycle: "REVIEW"
created: "2026-11-08T22:45:00Z"
updated: "2026-11-08T22:45:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "07_EVALUATION/reports/modernization/F0_package_inventory.json"
  created_by: "claude-code"
tags:
  - "modernization"
  - "architecture"
  - "decision"
---

# F0 — matricea de decizie a modulelor (WP-02)

Sursa cifrelor: `F0_package_inventory.json` (commit 0485abe59, 2194 fișiere .py). Consumatorii sunt minime:
413 fișiere încarcă module prin căi la runtime, nevăzute de AST. Nicio decizie de arhivare nu se execută
fără grep-ul de producție din CLAUDE.md (Global Production-Consumer Rule) rulat în WP-ul care o aplică.

## 1. Ce arată inventarul despre arhitectura reală (corectează planul)

1. **`cognitive_core` din rădăcină este fațada publică a nucleului**, nu un duplicat. Este un shim `__path__`
   peste 9 pachete (graph, interfaces, learning, lifecycle, memory, observability, providers, retrieval,
   security) plus 4 CLI-uri (`recall_cli`, `vault_cli`, `closed_loop_learning`, `telegram_vault_bot`), citate
   în CLAUDE.md. Importurile `cognitive_core.X` sunt calea dominantă (memory 238, providers 173, learning 158,
   interfaces 144). Planul (WP-14) propunea mutarea lui în arhivă: **anulat**.
2. **`packages/cognitive_core`, `packages/memory_vault`, `packages/memory_controller`** sunt și ele shim-uri
   `__path__` (20, 32, 17 LOC), fără cod propriu. `packages/cognitive_core` e umbrit de cel din rădăcină
   (pythonpath `.` primul) și nu poate fi importat după nume. `memory_controller` e numele folosit de 96 de
   fișiere de producție, deci e o fațadă de compatibilitate activă.
3. **Coliziune `security`**: `root/security` (66 fișiere, 247 importuri `security.X`) câștigă asupra
   `packages/security` (9 module: authority, authorizer, mutation_gate, ...). Modulele din `packages/security`
   ajung la consumatori prin fațada `cognitive_core`. Două pachete cu același nume = risc real de import greșit.
4. **Graful nu e stratificat strict**: memory → memory_controller (8), observability → retrieval (4) și
   → memory_controller (3), security → interfaces, interfaces → memory_controller (9). Prin shim-uri apar
   cicluri. Un test cu 7 straturi stricte (A3 din plan) ar pica din prima. WP-04 trebuie să fie un
   *ratchet*: muchiile existente intră într-o listă de bază datată, orice muchie nouă interzisă pică.
5. **O singură încălcare pachet → produs**: `packages/interfaces/financial_ingestion.py:30` importă `xau_kinetic`.

## 2. Decizie pe modul

| Modul | LOC | Prod / Test / CI | Decizie | WP | Condiție de acceptare |
|---|---|---|---|---|---|
| `packages/memory` | 4554 | 68 / 103 / 3 | PĂSTREAZĂ — nucleul canonic L1 | WP-06/07 | contract scris; consumatorii neschimbați trec |
| `packages/vault_access` | 1912 | 6 / 9 / 1 | PĂSTREAZĂ — control acces / manifest de rute | WP-06 | rol distinct de memory documentat |
| `packages/lifecycle` | 7029 | 21 / 30 / 0 | PĂSTREAZĂ — stări memorie | WP-10 | pipeline admitere prin lifecycle testat |
| `packages/learning` | 1496 | 6 / 26 / 0 | PĂSTREAZĂ | WP-10 | consumator prod confirmat prin grep |
| `packages/retrieval` | 5511 | 56 / 43 / 4 | PĂSTREAZĂ | WP-09 | — |
| `packages/graph` | 1562 | 17 / 21 / 0 | PĂSTREAZĂ | WP-09 | — |
| `packages/interfaces` | 4959 | 25 / 45 / 0 | PĂSTREAZĂ; scoate `financial_ingestion` din nucleu | WP-12 | zero importuri pachet → produs |
| `packages/providers` | 793 | 24 / 30 / 0 | PĂSTREAZĂ; implicit local/offline | WP-11 | apel extern doar cu configurare explicită |
| `packages/observability` | 2932 | 14 / 32 / 0 | PĂSTREAZĂ | WP-11 | fără date personale în loguri |
| `packages/routing` | 1433 | 2 / 9 / 5 | PĂSTREAZĂ — `route_cli` funcțional (verificat azi) | WP-11 | — |
| `packages/security` | 629 | 76 / 89 / 0 | PĂSTREAZĂ; rezolvă coliziunea de nume cu `root/security` | WP-14 | un singur pachet servește `security.*` |
| `packages/memory_controller` | 17 (shim) | 96 / 118 / 2 | PĂSTREAZĂ ca fațadă de compatibilitate | WP-06 | documentat ca alias, nu ca strat |
| `packages/memory_vault` | 32 (shim) | 0 / 0 / 0 | ARHIVEAZĂ (reversibil) | WP-07 | grep prod + loader-e la runtime = 0 |
| `packages/cognitive_core` | 20 (shim) | 0 / 0 / 0, umbrit | ARHIVEAZĂ (reversibil) | WP-07 | idem; suita trece după mutare |
| `packages/agent_bridge` | 708 | 0 / 3 / 3 | [DECIZIE ÎN WP-11]: cablează în calea de dispatch a routerului, altfel arhivează | WP-11 | consumator prod real sau mutare în 80_ARCHIVE |
| `packages/polymarket` | 4107 | 4 / 30 / 14 | MUTĂ ÎN PRODUCTS (14 workflow-uri de actualizat) | WP-12 | CI polymarket verde după mutare |
| `packages/xau_kinetic` | 5 (shim) | 37 / 15 / 0 | PĂSTREAZĂ shim-ul ca fațadă a produsului | WP-12 | — |
| `products/xau_kinetic` | 6485 | 3 / 1 / 1 | PĂSTREAZĂ în products (are `__init__` lipsă) | WP-12 | testele produsului colectate și verzi |
| `root/cognitive_core` | 62 | 44 / 100 / 1 | PĂSTREAZĂ — fațada publică + CLI-uri | — | rămâne la rădăcină |
| `root/security` | 6623 | 7 / 1 / 11 | PĂSTREAZĂ conținutul; [DECIZIE ÎN WP-14] redenumire sau unire | WP-14 | 37 de teste din `security/` colectate |
| `root/scripts` | 1259 | 0 / 3 / 10 | MUTĂ în `30_SCRIPTS/` cu actualizarea căilor din CI | WP-14 | 10 referințe CI actualizate, testele trec |

## 3. Schimbări de plan care decurg

- WP-04: test de straturi ca ratchet (muchii existente datate în `04_CONFIG/architecture_layers.json`), nu
  ca interdicție totală; include coliziunile de nume (`cognitive_core`, `security`) ca verificare separată.
- WP-14: nu mută `cognitive_core/` din rădăcină; tratează coliziunea `security`.
- Testele din afara `20_TESTS` (129 de fișiere): un WP nou în F1 decide includerea lor în `testpaths` sau
  rularea lor țintită, cu baseline propriu înainte.
- WP-15: requirements identice (12/12) → unificarea e mecanică; se actualizează cele 14 referințe la v6.
- Nicio redenumire `01_ARCHITECTURE`; `01_KNOWLEDGE` (2 fișiere, 182 referințe) și `08_RESEARCH`
  (72 referințe, CI) se decid în WP-15 pe cost.
