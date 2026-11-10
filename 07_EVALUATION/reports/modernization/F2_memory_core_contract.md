---
id: "5d2b8e41-7a3c-5f19-9e6d-2c4a1b8f7e03"
title: "F2 - contractul Memory Core, ordinea straturilor și alias-urile publicate (WP-06)"
type: "decision"
lifecycle: "REVIEW"
created: "2026-11-09T18:00:00Z"
updated: "2026-11-09T18:00:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "07_EVALUATION/reports/modernization/F2_layer_order_analysis.py"
  created_by: "claude-code"
tags:
  - "modernization"
  - "architecture"
  - "decision"
---

# F2 — contractul Memory Core, ordinea straturilor și alias-urile publicate (WP-06)

Ramura `modernize/f2-memory-core` (peste `modernize/f1-boundaries`). Cifrele se reproduc cu
`python 07_EVALUATION/reports/modernization/F2_layer_order_analysis.py` (doar citire, ~1 min).
Sondele de import (secțiunea 2) s-au rulat dintr-un director gol, cu `03_IMPLEMENTATION/packages` pe `sys.path`.

## 0. Pe scurt

1. Fațadele (`memory_controller`, `cognitive_core`, `memory_vault`) nu sunt straturi. Sunt reuniuni de
   `__path__` peste pachetele-surori. Ratchet-ul WP-04 le tratează ca unități proprii, așa că 99 din cele
   124 de fișiere de „datorie în sus” sunt artefacte ale alias-urilor, nu dependențe reale.
2. Pe graful real, cu fațadele rezolvate, rămân 8 perechi în sus, în 11 fișiere, cu ordinea aleasă aici.
   Acesta e minimul exact peste toate ordinile posibile (calcul DP). Config-ul WP-04 ar da 11/25,
   ordinea A3 din plan 15/28.
3. Fațadele nu sunt nici alias-uri inofensive.
   - Nucleul nu se poate importa decât prin ele: inclusiv fișierele din nucleul protejat folosesc importuri
     relative care trec granița de pachet.
   - Două fațade încarcă de două ori aceleași fișiere, cu clase distincte.
   - Contractul fixează o singură identitate de modul: reuniunea `cognitive_core` e numele canonic, iar
     `memory_controller` devine alias al ei. Numele fizice (`memory.controller`) **nu** devin canonice:
     asta ar cere modificarea nucleului protejat (review r1, H1).
4. `memory_vault` funcționează doar la suprafață: `memory_vault.memory.controller` aruncă
   `ModuleNotFoundError`. Decizia: se depreciază, nu se șterge.

## 1. Ordinea straturilor (reziduul L2 din F1)

### 1.1 Cifrele

Muchia (A, B) numără fișierele non-test din A care importă B. Același criteriu ca în WP-04.

| Graf / ordine | Perechi în sus | Fișiere în sus |
|---|---:|---:|
| graful cu alias-uri (inventarul WP-04), ordinea din config | 31 | 124 |
| graful real, ordinea din config | 11 | 25 |
| graful real, ordinea A3 din Masterplan | 15 | 28 |
| **graful real, ordinea WP-06** | **8** | **11** |
| graful real, minimul peste toate ordinile totale | — | 11 |

- Graful cu alias-uri are 63 de perechi în 213 fișiere; graful real are 44 de perechi în 115 fișiere.
- Niciun import de fațadă nu a rămas nerezolvat.
- Concluzie: diferența dintre config și A3, discutată în F1 (31/124 față de 27/70), venea aproape în
  întregime din fațade. După rezolvarea lor, nici A3, nici config-ul nu sunt optime.

### 1.2 Ordinea aleasă (de jos în sus)

| Strat | Unități | Rol |
|---|---|---|
| L0_foundation | packages/security, packages/providers, packages/routing, packages/graph | politici, furnizori, rutare, graf |
| L1_lifecycle | packages/lifecycle | stări și propuneri de memorie |
| L2_policy_adapter | root/security | politica de securitate a repo-ului |
| L3_retrieval | packages/observability, packages/retrieval | căutare, context, telemetrie |
| L4_memory_core | packages/memory, packages/vault_access | **Memory Core** și controlul accesului |
| L5_products | products/xau_kinetic, packages/polymarket | produse |
| L6_entrypoints | packages/interfaces, packages/agent_bridge | CLI-uri, server API/MCP, harness |
| L7_cognition | packages/learning | învățare în buclă închisă |
| L8_apps | root/cognitive_core, root/scripts | CLI-uri publice și scripturi |

Fațadele (`packages/memory_controller`, `packages/cognitive_core`, `packages/memory_vault`,
`packages/xau_kinetic`) ies din straturi și devin `aliases` în config. Un import printr-o fațadă se
atribuie unității care deține submodulul.

Constatări care susțin ordinea:

- **`packages/interfaces` nu conține contracte.** Conține `api_server`, `memory_mcp_server`, `recall_cli`,
  `vault_cli`, `telegram_vault_bot`, `real_execution_harness` și `financial_ingestion`, adică puncte de
  intrare. Pe L2, cum era în config, importa 8 unități superioare pe graful cu alias-uri și 5 pe graful
  real (memory, lifecycle, vault_access, xau_kinetic, root/security). Pe L6 nu mai importă nimic de deasupra.
- **Produsele stau sub L6**, fiindcă punctele de intrare le folosesc. Regula `hard_forbidden`
  (pachet → produs) rămâne neschimbată, deci `interfaces → xau_kinetic` pică în continuare (WP-12).
- **`packages/security` și `root/security` sunt pe straturi diferite (L0 și L2)** și au aceeași coliziune
  de nume. Decizia aparține WP-14.

### 1.3 Datoria reală rămasă: 11 fișiere

| Muchie | Fișier | Import | Remediere propusă |
|---|---|---|---|
| graph → memory | `graph/activation.py` | `memory_controller.controller` | WP-10: injectare prin parametru sau protocol |
| lifecycle → memory | `lifecycle/validation/book_to_memory_pipeline.py`, `book_to_memory_retrieval.py` | `memory_controller.controller` | WP-10: `lifecycle/validation/book_to_memory_*` sunt experimente → `07_EVALUATION` |
| lifecycle → interfaces | `lifecycle/validation/book_to_memory_lifecycle.py` | `interfaces.vault_runtime` | WP-14 (vezi mai jos) |
| memory → interfaces | `memory/orchestrator.py` | `cognitive_core.recall_cli` | WP-10: scoate căutarea din CLI într-o funcție din retrieval |
| retrieval → memory | `retrieval/financial_search.py`; `retrieval/memory_ablation_benchmark.py` | `memory.data_router`; `cognitive_core.orchestrator` | WP-12 (financiar); benchmark-ul → `07_EVALUATION` |
| retrieval → interfaces | `retrieval/memory_ablation_benchmark.py` | `cognitive_core.real_execution_harness` | idem |
| security → interfaces | `security/git_hooks.py` | `memory_controller.git_integration` | WP-14 |
| vault_access → interfaces | `vault_access/audit.py`, `vault_access/router.py` | `interfaces.vault_runtime` | WP-14 (vezi mai jos) |

- `interfaces/vault_runtime.py` (243 de linii, doar stdlib: director de stare, cheia HMAC, jurnalul de
  utilizare) e importat din trei straturi inferioare, deci locul lui e în fundație.
- **Mutarea se amână până după WP-14** (review r1, M3). În `packages/security` n-ar avea un nume de import
  stabil: de la rădăcină, `security` e `root/security`. N-ar fi accesibil decât prin fațade, cu încărcare
  dublă și `_LOG_LOCK` (linia 40) dublat. În plus, testele fac `monkeypatch` pe atributele modulului
  (`20_TESTS/redteam/test_redteam_memory.py:173-178`), iar un reexport cu `import *` le-ar rupe.
- Cele 3 fișiere rămân în baseline, cu `owner_wp` WP-14.

## 2. Contractul Memory Core

### 2.1 Ce s-a măsurat

**Sonde de import** (director gol, doar `03_IMPLEMENTATION/packages` pe cale):

| Import | Rezultat |
|---|---|
| `memory.controller` | **ModuleNotFoundError** `memory.authorizer` |
| `memory_vault.memory.controller` | **ModuleNotFoundError** `memory_vault.memory.authorizer` |
| `memory_controller.controller` | OK → `memory/controller.py` |
| `cognitive_core.controller` | OK → `memory/controller.py` |
| `cognitive_core.recall_cli` | OK → `interfaces/recall_cli.py` |

- Cauza: `memory/controller.py` folosește importuri relative (`.authorizer`, `.validation.schema`) spre
  module care stau fizic în `security` și `lifecycle`. Ele se rezolvă doar într-o reuniune de `__path__`.
- Pe scurt: **fațadele țin structura în picioare.**

**Identitate dublă:**

- `memory_controller.controller is cognitive_core.controller` dă `False`. `MemoryController` și
  `authorizer.Principal` există de două ori: 35 de module sub fiecare prefix, dacă se încarcă ambele.
- În procese reale:
  - `interfaces.api_server` încarcă 11 fișiere sub două nume, inclusiv `authorizer`, în ambele fațade;
  - `cognitive_core.recall_cli` încarcă 10, de exemplu `memory.data_router` și `memory_controller.data_router`;
  - `interfaces.memory_mcp_server` încarcă 0.
- `lifecycle/proposal_queue.py:35` compară `type(approver).__name__ != "Principal"` în loc de `isinstance`.
  Pare o ocolire a acestei probleme.
- Riscul e latent: verificările `isinstance` între fațade pică, iar starea la nivel de modul (cache-uri,
  lock-uri) se dublează. N-am găsit azi o eroare activă. Neverificat: dacă vreun modul încărcat de două
  ori ține stare partajată care contează.

**Consumatori reali:** 28 de simboluri din `packages/memory` sunt importate de cod de producție din afara
lui, plus 35 doar din teste. Cele mai folosite:

- `controller.MemoryController`: 50 de fișiere de producție / 78 de test;
- `storage.FileStorageEngine`: 35 / 27;
- `controller.Lifecycle`: 11 / 32;
- `controller.StorageEngine`: 10 / 49;
- `RANKING_ARM_*`: 16 / 9;
- `storage.SQLiteStorageEngine`: 6 / 16.

### 2.2 Decizia — contractul v1

1. **Suprafața publică a Memory Core** e formată din trei module din `packages/memory`:
   - `controller`: `MemoryController`, `StorageEngine`, `Lifecycle`, `GraphExpansionDegraded`,
     `AGENT_LIFECYCLE_FLOOR`, `RANKING_ARM_*`;
   - `storage`: `FileStorageEngine`, `SQLiteStorageEngine`, `deserialize`, `CANONICAL_FOLDERS`,
     `LEGACY_WRITE_ROOTS`;
   - `core.Lifecycle`.

   Restul nu face parte din contract, chiar dacă stă fizic în `packages/memory`:
   - modulele cognitive `orchestrator`, `working_memory`, `recall`, `attention`, `reasoning`,
     `executive`, `global_workspace`, `sensor_buffer`, `data_router` sunt candidate de mutare în WP-10;
   - `planning`, `plan_complexity_analyzer.py` și `council_budget_controller.py` sunt **nucleu protejat**
     (CLAUDE.md, Protected Core): nu se mută și nu se modifică.
2. **O singură identitate de modul: reuniunea `cognitive_core` e canonică.**
   - Revizuit după review-ul r1 (H1). Fișierele protejate depind de reuniunea de `__path__` prin importuri
     relative care trec granița de pachet. De exemplu:
     - `observability/actual_usage_telemetry.py:30` importă `.model_provider`, care stă în providers;
     - `interfaces/council_model_execution.py:37-39` și `interfaces/executive_model_execution_bridge.py:7-8`
       importă din observability și providers.
   - Sub numele fizice aceste module pică (sondă: `interfaces.council_model_execution` →
     `No module named 'interfaces.actual_usage_telemetry'`). Prin `cognitive_core.*` merg.
   - Prin urmare, numele fizice **nu** pot deveni canonice fără a modifica nucleul protejat, iar
     modificarea lui e decizia proprietarului, nu a acestui WP.
   - **Ținta:** `memory_controller.X is cognitive_core.X` pentru orice submodul. `memory_controller` devine
     alias în `sys.modules` al lui `cognitive_core`: un finder care mapează `memory_controller.X` pe
     `cognitive_core.X`, nu o a doua reuniune.
   - Verificat: `__path__` al lui `memory_controller` e o submulțime a celui al lui `cognitive_core`.
     Numele care se rezolvă diferit sunt patru:
     - cu doar `packages/` pe cale, `benchmarks` (`retrieval` față de `interfaces`);
     - când se pornește din rădăcina repo-ului, unde `cognitive_core` e copia din rădăcină, se adaugă
       `recall_cli`, `vault_cli` și `telegram_vault_bot`: `interfaces` față de shim-ul din rădăcină
       (review r2, M1).
   - După alias, `memory_controller.recall_cli` ar deveni shim-ul. Ar fi alt obiect de modul, fără numele
     `_privat`, iar un `monkeypatch` pe alias n-ar mai atinge modulul real.
   - Azi nu există niciun consumator `memory_controller.{recall_cli,vault_cli,telegram_vault_bot}`, deci
     riscul e latent. WP-07 tratează explicit toate cele 4 nume și le acoperă cu test.
   - Fișierele din nucleul protejat nu se ating.
   - **Numele fizice** (`memory.*`, `retrieval.*` și celelalte) rămân importabile acolo unde funcționează
     azi. Importurile din cod de producție prin nume fizice ale modulelor care depind de reuniune sunt
     datorie. Ele se măsoară, se pun sub ratchet și se migrează treptat, nu în WP-07.
3. **Căi de import publice:**
   - `cognitive_core` e canonic: fațada CLI-urilor (comenzile din CLAUDE.md) și reuniunea de care depinde
     nucleul protejat;
   - `memory_controller` e alias de compatibilitate (96 de fișiere de producție);
   - nicio cale existentă nu se rupe.
4. **Testul de contract** (WP-07, `20_TESTS/test_memory_core_contract.py`):
   - toate simbolurile de la punctul 1 se importă prin `cognitive_core` și prin `memory_controller` și sunt
     *identice* (`is`);
   - `interfaces.api_server` nu încarcă `authorizer` sub două nume. Numărul de fișiere încărcate sub două
     nume de `interfaces.api_server` și de `cognitive_core.recall_cli` (azi 11 și 10) intră sub ratchet:
     scade sau rămâne, nu crește;
   - `record_observed_memory_trace` din `ContextPackBuilder`, încărcat prin numele canonic, este funcția
     reală din observability, nu stub-ul din `retrieval/context/pack_builder.py:8-13` (review r1, H2).
     - Azi, `cognitive_core.recall_cli` și `interfaces.api_server` folosesc funcția reală, prin
       `memory_controller.context.pack_builder` (sondă rulată).
     - Sub numele fizic `retrieval.context.pack_builder`, tracing-ul devine no-op fără nicio eroare.
     - Fișierul e protejat (ContextPackBuilder), deci fallback-ul nu se schimbă. Testul doar îl păzește.

## 3. `memory_vault` și `packages/cognitive_core` (alias-uri publicate în wheel)

- **`packages/memory_vault`**:
  - Are `__path__ = [packages/]`, deci `memory_vault.memory` *este* pachetul `memory`, iar importurile lui
    relative peste granița de pachet pică (sonda din secțiunea 2.1).
  - Merge doar la nivel de `__init__`; asta e tot ce verifică CI-ul R001 (`r001-enforcement.yml:96`) și
    `20_TESTS/integration/test_memory_vault_import.py:14`.
  - **Decizie:** îl păstrăm o versiune, ca alias de suprafață documentat, fără promisiunea că importurile
    adânci merg. WP-07 adaugă un `DeprecationWarning` la import care trimite spre `cognitive_core`.
  - Retragerea din `pyproject.toml:15` se face într-un WP ulterior, împreună cu linia CI R001 și testul de
    integrare, numai după grep-ul de consumatori pe `.py`/`.yml`/`.toml`/șiruri (regula din F0).
  - Variante respinse:
    - ștergerea acum, pentru că wheel-ul e publicat și consumatorii externi sunt necunoscuți (reziduu F0);
    - repararea importurilor adânci, pentru că ar cere o a treia mapare de nume peste reuniune, pentru un
      alias pe care nu-l folosește niciun cod de producție.
- **`packages/cognitive_core`**:
  - Câștigă în wheel și când `.` lipsește de pe cale.
  - Sonda arată că `cognitive_core.recall_cli` se rezolvă prin el la `interfaces/recall_cli.py`, adică
    același cod ca shim-ul din rădăcină, care face `from interfaces.recall_cli import *`.
  - **Decizie:** îl păstrăm, ca reuniunea canonică în wheel. WP-07 adaugă un test de echivalență: lista de
    pachete din `__path__` e aceeași în copia din rădăcină și în cea din `packages/`, în afară de directorul
    rădăcinii.
  - Cele 4 shim-uri CLI din rădăcină rămân, ca fațadă publică (decizia F0).
  - Notă (review r1, L5): `cognitive_core/closed_loop_learning.py` reexportă `packages.learning`, nu
    `interfaces`.

## 4. Schimbarea checker-ului și a config-ului (WP-07)

Ordinea din secțiunea 1.2 nu se poate aplica doar în config. Fără rezolvarea fațadelor, ar muta
datoria pe alte perechi. WP-07 are două PR-uri.

**PR A (unelte și config, fără risc la runtime):**
1. `package_inventory.py` rezolvă importurile prin fațade la unitatea care deține submodulul, după logica
   din `F2_layer_order_analysis.py`.
   - Rezolvarea se face prin `__path__` declarat, nu prin nume. Shim-urile din rădăcină se urmăresc până la
     ținta reexportului.
   - Are teste sintetice pentru fiecare fațadă, pentru ordinea din `__path__` și pentru `memory_vault`
     (`memory_vault.<pachet>.<modul>` → `packages/<pachet>`).
2. `04_CONFIG/architecture_layers.json`:
   - straturile din secțiunea 1.2;
   - o secțiune `aliases` pentru cele **5** fațade: `memory_controller`, `cognitive_core` (ambele copii),
     `memory_vault`, `xau_kinetic` și `packages/security`, care își extinde `__path__` cu `root/security`
     (review r1, L6), fiecare cu `__path__` declarat;
   - o regulă care pică dacă `__path__`-ul real al unui shim diferă de cel declarat.
3. Baseline-ul se regenerează.
   - Apar perechi noi, de exemplu `graph → memory`, care înainte era `graph → memory_controller`. Lărgirea
     se face explicit: `--allow-widen --owner-wp WP-07 --reason "reatribuire după rezolvarea fațadelor
     (WP-06)"`.
   - Diff-ul baseline-ului trebuie să arate exact cele 8 perechi / 11 fișiere din secțiunea 1.3.

**PR B (runtime, risc ridicat):**

4. Alias-ul `memory_controller` → `cognitive_core` (secțiunea 2.2/2) și testul de contract
   (secțiunea 2.2/4).
   - Ăsta e cel mai riscant pas: atinge încărcarea tuturor modulelor.
   - Brief-ul pentru `vault-worker` conține lista de sonde din secțiunea 2.1 și din 2.2/4, cu modelul de
     amenințare: verificările de autorizare cu `isinstance` pe `Principal`, stub-ul tăcut de tracing,
     lock-urile la nivel de modul.
   - Înainte de schimbare, WP-07 inventariază starea la nivel de modul din modulele încărcate de două ori.
   - Testele și documentele care fixează azi forma shim-ului se actualizează în același PR, fără slăbirea
     verificărilor (review r2, L1):
     - `00_GOVERNANCE/VAULT_STATE.md:27` (shim de 19 linii);
     - `20_TESTS/test_vault_state_accuracy.py:171-173` (cere `__path__` în shim);
     - `20_TESTS/memory_controller/test_git_isolation.py:44` (`find_spec("memory_controller.controller")`);
     - `20_TESTS/test_cognitive_core_import_order.py:43-46` (comentariul despre `graph`/`learning`).
   - Review-ul `vault-reviewer` (WP-08) rulează din nou sondele. Suita completă rulează o dată, înainte de merge.

## 5. Impactul asupra WP-urilor următoare

| WP | Schimbare față de plan |
|---|---|
| WP-07 | Secțiunea 4: PR A (unelte/config), apoi PR B (alias la runtime). |
| WP-08 | Review cu sondele de identitate, încărcare dublă și tracing. Suita completă rulează o dată. |
| WP-09/10 | Ținta e închiderea celor 11 fișiere din secțiunea 1.3, nu „mutarea pe straturi”. Modulele cognitive din `memory/` sunt candidate pentru `learning`. Importurile de producție prin nume fizice ale modulelor dependente de reuniune se migrează pe `cognitive_core`. |
| WP-12 | Neschimbat: `interfaces → xau_kinetic` (hard_forbidden) și `retrieval/financial_search.py`. |
| WP-14 | Coliziunea `security` are acum cifre: `packages/security` stă pe L0, `root/security` pe L2. Tot aici: locul final al lui `interfaces/vault_runtime.py` (3 fișiere din datoria din secțiunea 1.3). |
| Proprietar | Dacă vrei ca numele fizice să devină canonice, trebuie o excepție de la Protected Core: înlocuirea importurilor relative peste granița de pachet din `actual_usage_telemetry.py`, `council_model_execution.py` și `executive_model_execution_bridge.py`. Fără ea, `cognitive_core` rămâne canonic. Am ales varianta care nu atinge nucleul. |

## 6. Ce nu s-a verificat

- Dacă vreun modul încărcat de două ori ține stare la nivel de modul care contează în producție
  (UNKNOWN; WP-07 îl inventariază înainte de PR B).
- Consumatorii externi ai wheel-ului `ai-memory-vault` (reziduu F0, încă necunoscut).
- Ordinea A3 e transcrisă din review-ul F1, nu dintr-un Masterplan deschis aici, pentru că fișierul nu
  se găsește pe disc.
- Calculul ignoră importurile relative și cele din șiruri, la fel ca ratchet-ul WP-04 (`unenforced` R1–R4).
- Ordinea WP-06 e *una* dintre ordinile optime. DP-ul găsește și altele, de exemplu cu `agent_bridge` mai
  jos. Am ales-o pe cea cu straturi care au sens funcțional.

## 7. Review

- r1 (`vault-reviewer`, Opus): ACCEPT CU REZERVE. Cifrele și sondele s-au reprodus exact.
  - H1 (nucleul protejat folosește importuri relative peste granița de pachet) a schimbat ținta din
    secțiunea 2.2/2: `cognitive_core` e canonic, numele fizice nu.
  - H2 (stub-ul tăcut de tracing) a intrat în testul de contract.
  - M3 a amânat mutarea lui `vault_runtime` la WP-14.
  - M4 și L5 (rezolvarea `memory_vault` și a shim-urilor din rădăcină) sunt reparate în script.
  - L6 (`packages/security` e a cincea fațadă) e adăugat la secțiunea 4.
  - L7 („8 unități”) e corectat.
- r2 (`vault-reviewer`, Opus, doar delta): ACCEPT CU REZERVE.
  - Toate remedierile din r1 sunt prezente. Cifrele s-au reprodus. Nucleul protejat nu e atins.
  - M1 (4 nume se rezolvă diferit, nu 1) e corectat în §2.2/2.
  - L1 (testele și VAULT_STATE care fixează forma shim-ului) e adăugat la PR B.
  - Reviewerul a cerut explicit: fără altă rundă.
