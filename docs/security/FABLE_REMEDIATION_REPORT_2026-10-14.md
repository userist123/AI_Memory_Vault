# AI Memory Vault — Raport de remediere controlată (FABLE, 2026-10-14)

Intrări: `AI_Memory_Vault_Audit.md` (audit extern limitat la pagina GitHub + README), `AI_Memory_Vault_Remediation_Plan.md`, `FABLE_AI_Memory_Vault_Remediation_Prompt.md`. Regula de lucru: nimic nu e `FIXED` fără reproducere pe SHA-ul de bază, test de acceptanță executat și diff care nu slăbește un control.

## 1. Baseline

| | |
|---|---|
| Repository | `https://github.com/userist123/ai_memory_vault_codex_ready` (GitHub rezolvă case-insensitive; ID-001 este un defect de referință externă, nu al repo-ului) |
| Branch de bază / de lucru | `main` → `fable/remediation-2026-10-14` |
| SHA de bază | `0b967fe88195b3df76072cb178b6d7388119751c` (commit din 2026-10-12) |
| SHA final | `c883858a370f36c3e86fe9098928aac6945137d1` — un singur commit pe `fable/remediation-2026-10-14`; nimic push-uit/merge-uit |
| Working tree inițial | curat (`git status --short` gol, remote `origin` fetch/push) |
| Istoric local | 2 025 commits pe `main` (un singur root); doar `main` fetch-uit — remote-ul are ~190 alte branch-uri |
| Mediu | Python 3.13.16, `requirements.txt` instalat; `gitleaks` indisponibil; `gh` fără acces la repo (HTTP 403) |
| Suita de bază | `python -m pytest -q -p no:cacheprovider` → **3830 passed, 22 skipped, 9 xfailed, exit 0** (722 s) |

Bootstrap citit integral: `AGENTS.md`, `CLAUDE.md`, `README.md`, `00_GOVERNANCE/VAULT_STATE.md`, `00_GOVERNANCE/coordination/{BOOTSTRAP_ALL_AGENTS_V1,README,tasks/README}.md`, `projects/AI_MEMORY_VAULT/CURRENT.md`.

## 2. Matricea constatărilor

Status: `REPRODUCED` (R), `PARTIALLY_REPRODUCED` (PR), `NOT_REPRODUCED` (NR), `UNVERIFIABLE` (U). Toate pe SHA `0b967fe8`. Toate liniile sunt cele de la bază, înainte de modificări.

### Acces și write paths (audit ID-007, plan P0.2/P0.3)

| ID | Status | Sev. | Path / simbol | Lanț apel | Dovadă (comandă, rezultat) | Verdict |
|---|---|---|---|---|---|---|
| A-01 | R | High (latent) | `interfaces/memory_mcp_server.py:166-172` `_note_eligibility`; `interfaces/vault_cli.py:41`; `vault_access/core.py:146-152` | `vault_read` → `VaultAccess._route_for` → `policy.decide` → `note_eligibility` → `memory_access._readable` (întoarce record body-withheld ≠ None) | probă pe vault temporar (`make_vault`): `active_quarantined`: `memory_get` REFUSED, `vault_read ok=True body=True`. Corpusul real: 0 note cu `quarantined: true` | **FIXED** |
| A-02 | R | Medium (latent) | `interfaces/memory_access.py:138-203`; `memory_mcp_server.py:137-139` | `memory_get/memory_search` → controller (fără `access_policy.yaml`) | notă cu `classification: RESTRICTED`: `memory_get` SERVED, `vault_read` NOT_FOUND. 0 note etichetate azi | **BLOCKED** (decizie de arhitectură: un singur policy service) |
| A-03 | PR | Medium | `memory_access.py` (fără `redact()`); `recall_cli.py` printează `content[:1500]` | idem | doar cod; nu s-a plantat secret | **BLOCKED** (aceeași decizie ca A-02) |
| A-04 | R | Medium | `interfaces/api_server.py:134,136,180-186` | REST `/search`, `/note`, `/chat` → `storage.query/get` direct, fără controller, audit, redactare; `OLLAMA_HOST` neverificat loopback | probă: ARCHIVED/NORMALIZED/quarantined servite de REST | **BLOCKED** (contract REST = view de owner; decizie owner) |
| A-05 | R | High | `memory/controller.py:1800-1876` `supersede`, `archive`; `security/authorizer.py` (SUPERSEDE include AI_AGENT) | orice apelant in-process → `storage.set` pe `slot-*` | `test_controller_refuses_to_retire_an_ontology_slot` era `xfail(strict)`; probă: `AI supersede slot-06 → ALLOWED`, fișierul devine `lifecycle: SUPERSEDED` | **FIXED** |
| A-06 | NR | Medium (gardă fragilă) | `controller.update/propose` | — | `update(AI_AGENT, slot)` refuzat doar de schema (`category` lipsă), nu de policy | **FIXED** prin A-05 (regulă explicită) |
| A-07 | R | Medium | `controller.py:1688-1691` | `update(AI_AGENT, ACTIVE)` permite `relations`, `verification`≠verified | probă: downgrade `partially_verified → unverified` ALLOWED | **BLOCKED** (contract documentat în VAULT_STATE; decizie owner) |
| A-08 | R | Medium | `controller.py:1490-1580` `propose` | apelant direct → `storage.set` peste id existent | probă: HUMAN propune REVIEW, AI_AGENT re-propune același id → OVERWRITTEN, `source_type: user → ai` | **FIXED** |
| A-09 | R | Medium | `30_SCRIPTS/ingestion/purge_rejected_rows.py:171-180, 525-535` | `--disposition-file` → `load_manifest` fără `validate_against_slots` | probă: manifest cu 1 rând din 93, `decided_by: ""` → `deleted_count: 1` | **FIXED** |
| A-10 | R | Medium | `30_SCRIPTS/verification/write_path_audit.py:classify()`; `.github/workflows/write-path-audit.yml` | — | `classify()` cheia pe `memory_controller/controller.py` (inexistent) → 0 `CANONICAL_CONTROLLER`; testele de slot nu rulau în niciun workflow țintit | **FIXED** (inventarul rămâne advisory la `DIRECT_STORAGE_MUTATION` — ar cere allowlist, decizie owner) |

### Date personale și secret scanning (plan P0.1/P2.1)

| ID | Status | Sev. | Path | Dovadă | Verdict |
|---|---|---|---|---|---|
| B-01 | R | **High** | istoric `main`: `02_PRODUCT/projects/imported/bot/trade/` (4 documente: 2 facturi PDF, 1 docx + 1 pdf „repayment"), adăugate `039e490cb` (2026-09-06), șterse `40a2b473a` (2026-09-17), absente din HEAD | scan de istoric (script în scratch, `python3 -I`): 60 314 blob-uri scanate; 2 PDF-uri conțin IBAN-uri RO valide mod-97 (10 și 12 apariții). Niciun CNP. Nicio valoare copiată | **BLOCKED** — necesită rescriere de istoric (§8) |
| B-02 | U | High | docstring-ul guard-ului menționează și „extras de cont" + „2 bugete personale" | nu există în istoricul local de `main`; probabil pe branch-uri nefetch-uite; `09_SECURITY/incident_register/R001_*` listează 10 categorii de secrete PENDING rotație | **UNVERIFIABLE** |
| B-03 | R | High | `30_SCRIPTS/verification/personal_data_guard.py:76,78,68-74,64-67,40` | `_IBAN_RO` cerea forma compactă majusculă — forma **cu spații** din factura scursă nu era detectată; CNP cu `=`/`-` înainte ratat; nume de documente lipsă; doar working tree | **FIXED** |
| B-04 | R | Medium | `.gitleaks.toml` (7 allowlist-uri), `exempt_area_secret_scan.py:1-4` | 13 356 din 17 631 fișiere urmărite (75,75 %) excluse din gitleaks; docstring-urile spuneau „83 %, 16 675 din 20 030"; `.gitleaksignore` cu 2 commit-uri absente local | **FIXED** (docstring); rulare gitleaks live: **UNVERIFIABLE** |
| B-05 | R | Medium | `exempt_area_secret_scan.py:40-49` | 8 reguli, fără Telegram/Slack webhook/`GOCSPX-`/Mongo URI/Swarm/JWT; doar push/PR pe `main`; fără istoric | **PARTIALLY FIXED** (nemodificat; raportat) |
| B-06 | PR | Medium | `.gitleaks.toml:14`, `.gitignore:70-74`, `repository_hygiene.py::check_inbox` | 66 952 adăugări istorice sub RAW_IMPORTS niciodată scanate de gitleaks; azi 0 fișiere urmărite; gate-ul `check_inbox` exista deja | **FIXED** (test negativ adăugat) |
| B-07 | R / NR | Low | istoric: chei third-party în proiecte importate (firebase, google-font, tutoriale), PEM-uri de test; `.../site/frontend/src/contexts/AuthContext.js:10,18` utilizatori mock cu parole literale + telefon RO (în HEAD) | niciun secret first-party activ găsit în istoricul local | raportat; owner review |

### Cold-start și validarea căilor (audit ID-002/010, plan P1.1/P1.2)

| ID | Status | Sev. | Path | Dovadă | Verdict |
|---|---|---|---|---|---|
| C-01 | R | High | `coordination/BOOTSTRAP_ALL_AGENTS_V1.md:8-10`; `UNIVERSAL_AGENT_MEMORY_PROTOCOL_V1.md:28,33-36,172` | `ls 09_COORDINATION` → No such file; fișierele vii sunt în `00_GOVERNANCE/coordination/` | **FIXED** |
| C-02 | R | High | `projects/AI_MEMORY_VAULT/CURRENT.md:5-6` (`7224e6ac…`), `agents/{ANTIGRAVITY,CLAUDE_SONNET,CODEX,LUNA,PERPLEXITY}/CURRENT.md` | `git cat-file -e <sha>^{commit}` → exit 128 pentru toate, cu excepția `LOGANALYZER_DFIR` | **FIXED** (marcate `HISTORICAL_SNAPSHOT`; niciun SHA inventat) |
| C-03 | R | Med-High | 21 × `.agents/agents/*/agent.md:9-13`, `.agents/skills/*`, `00_GOVERNANCE/skills/*`, `commands/memory.md`, `interfaces/memory_v6_cli.py:72`, `storage/path_resolver.py:90-92` | `99_SYSTEM/Agent_Capability_Registry.md`, `Council_Context_Budget.md` nu există nicăieri | **PARTIALLY FIXED** (default CLI mutat la `08_OBSERVABILITY/reports/`; profilele/skills — decizie owner) |
| C-04 | R | Medium | `20_TESTS/test_vault_state_accuracy.py:178-184` | fallback `REPO.rglob(Path(r).name)`; repro: homonim în director greșit acceptat; 14 referințe din VAULT_STATE treceau doar prin basename | **FIXED** |
| C-05 | R | Medium | `20_TESTS/test_readme_references.py:26-27`, `test_agent_docs_memory_interfaces.py` | regex cerea ≥1 caracter după `/` și doar rădăcini numerotate; `09_COORDINATION/` trecea | **FIXED** |
| C-06 | NR | Info | `validate_repository_layout.py`, `repository_hygiene.py` | lucrează pe `git ls-files` + prefixe exacte | — |
| C-07 | R | Low | `README.md:14-17`, `.claude/skills/cost-router/hook_self_update.py:26`, `CURRENT.md:4` | variante de capitalizare; remote real lowercase | raportat (cosmetic) |
| C-08 | R | Medium | `AGENTS.md:23,52` | granița proiecției Obsidian nu e enunțată pentru `00_CORE`/`09_COORDINATION` în contractele agenților; niciun test | raportat (editarea `AGENTS.md` = decizie owner) |
| C-09 | — | Low | `CLAUDE.md:109` citează `cognitive_core/model_provider.py` (real: `providers/model_provider.py`) | corectat, apoi **revenit**: `20_TESTS/test_claude_contract.py` fixează textul exact | **BLOCKED** (test-contract al owner-ului) |

### Storage, regăsire, graf, nucleu cognitiv (audit ID-003/004/005/011, plan P1.3/P1.4/P1.5/P1.7)

| ID | Status | Sev. | Path | Dovadă | Verdict |
|---|---|---|---|---|---|
| D-01 | R | High | `memory/storage/file_engine.py:104,121-128,145-146,245-246` | 6 fișiere plantate (yaml corupt, fără delimitatori, utf-8 invalid, id listă, frontmatter scalar, fără id) → toate SKIPPED, 3/6 fără nicio urmă; vault real: 1357 `.md` scanate, 1064 indexate, **293 invizibile** (176 fără id, 117 YAML malformat); duplicate-UUID hard-fail intact | **FIXED** |
| D-02 | R | Medium | `controller.py:121` (`GRAPH_EXPANSION_BUDGET=None`), `:1009-1018`, `:1036-1101`; `VAULT_STATE.md:54` cita linii greșite | tabel măsurat: 26 note/AI_AGENT → 0 pentru orice buget; 256 note → 0/0/1/5/5/5 pentru B=None/0/1/5/200/1000; owner view → 0 mereu; status raportat `"ok"` | **FIXED** (status veridic; semantica bugetului neschimbată — decizie owner) |
| D-03 | R | Medium | `retrieval/context/retrieval.py:160-166`, `candidate_generation.py:178-180`; `retrieval/context/budget.py:165` | cut la 200 candidați înainte de scoring; `CANDIDATE_LIMIT_CUT` 2 cazuri agent_p5, 17 human; `load_agent_budget()` citește `config/agent_budgets.json` (inexistent) → `max_notes=50` în loc de 5 din `04_CONFIG` | **BLOCKED** (schimbarea punctului de operare re-baselinează benchmark-urile) |
| D-04 | R | High (latent) | `retrieval.py:115-129`; `controller.py:947-976` | cache hit întoarce `fused_ranking: []` → sortare `(0.0, id)` → ordine inversă; repro: apel 2 → `['m-29','m-28',…]`; pe vault-ul real cache-ul nu e lovit azi (16 KiB soft limit), ar deveni live cu `max_notes=5` | **FIXED** |
| D-05 | NR | Info | `candidate_generation.py:13-14` | regăsirea implicită e BM25 + entity RRF; `OllamaEmbedder` intenționat neimportat; `qdrant_retrieval` consumat doar de `memory_v6_cli` | DEFAULT_LEXICAL confirmat; README corect |
| D-06 | R | Medium | `README.md:31`, `README.en.md:31,740` vs `controller.py:1908-1912` și `VAULT_STATE.md:55,57` | README: „NOT WIRED"; cod: importat și invocat sub flag-uri `False` implicit; `test_cognitive_core_search_wiring.py` acoperă ambele stări; singleton de modul construit la import cu root greșit | **FIXED** (README + test; singleton lazy) |
| D-07 | PR | Low-Med | `07_EVALUATION/curriculum/openstax_ch08_frozen_test_set.json` (22 întrebări: 12 + 10 capcane), fără sidecar `.sha256`; `README.md:551` „frozen in a commit before extraction" | set + note extrase + rezultate în același commit `0bb6791a5`; ordinea neverificabilă din istoric | **FIXED** (sidecar + test + reformulare) |

### Routing și verifier (audit ID-006, plan P1.6)

| ID | Status | Sev. | Path | Dovadă | Verdict |
|---|---|---|---|---|---|
| E-01 | R | High | `routing/dispatcher.py:266-271` | cu executabile false: primary `claude` rulat, verifier `agy` **niciodată**; `PENDING_VERIFICATION` terminal | **BLOCKED** (contract nou `verify()` — decizie owner) |
| E-02 | R | Medium | `routing/route_cli.py:51-56` | rc 0 pentru FAILED/PENDING/BLOCKED | **FIXED** |
| E-03 | — | Low | dispatcher | timeout, agent lipsă, PLANNED, LOCAL_ONLY, agent necunoscut → toate fail-closed (control pozitiv); 5 slăbiciuni mici raportate | — |
| E-04 | R | Medium | `routing/`, `agent_bridge/` | niciun importator de producție; `relay` nu există; `windows_named_pipe` = doar string validat | concordă cu VAULT_STATE |
| E-05 | R | Low | `A2AAdapter` | niciun runtime cu `transport: a2a`; doar `urlopen` mock | raportat |
| E-06 | PR | Low | teste cu executabile false fără markere | docs oneste; nume `test_agent_dispatch_real_config`/`test_b2_real_bridge_*` induc în eroare | **FIXED** (markere `fake_executable`, `mock_transport` înregistrate) |
| E-07 | — | — | `.mcp.json` + 3 configuri | server MCP pornit local cu client stdio scriptat: 9 tool-uri listate, `memory_search` → 2 rezultate. Client real (Claude Code/Codex/…): **UNVERIFIABLE** | — |
| E-08 | R | Low-Med | `observability/memory_trace.py:60` | suita scria în `telemetry/observed_memory_traces.jsonl` (gitignored, dar mutat la fiecare rulare) | **FIXED** |

### CI/CD (audit ID-008/009, plan P2.2/P2.3)

| ID | Status | Sev. | Dovadă | Verdict |
|---|---|---|---|---|
| F-01 | R | Medium | `security-boundary.yml:4-11,36`: auditul de workflow-uri nu rula pe PR-uri care modifică doar un workflow | **FIXED** |
| F-02 | U | — | `gh api …/branches/main/protection` și `…/check-runs` → HTTP 403 „GitHub access … not enabled for this session" | **UNVERIFIABLE** (branch protection, status CI pentru HEAD) |
| F-03 | R | Low | 17 pași `actions/checkout` în 16 workflow-uri fără `persist-credentials: false` | **FIXED** |
| F-04 | R | Info | 13 workflow-uri fără `concurrency` | raportat |
| F-05/F-06 | NR | — | 0 `pull_request_target`, 93/93 `uses:` pin-uite la SHA de 40 caractere, 49/49 job-uri cu `timeout-minutes`, `permissions: contents: read` în 44 fișiere (+ per-job în restul), 0 interpolări `github.event.*` în `run:` | — |
| ID-009 | NR | — | `test_readme_references.py` verifică bidirecțional numele workflow-urilor (fnmatch pentru `polymarket-phase*`); nicio discrepanță | — |

Pre-commit: un singur hook local `gitleaks` (`language: system`, fără versiune pin-uită; binarul lipsește aici).

## 3. Modificări realizate

60 fișiere modificate, 17 noi (948 inserții, 152 ștergeri). Niciun set de date, hash, held-out sau JSON de rezultate din `07_EVALUATION/` modificat (`git diff --stat 07_EVALUATION/` = doar `ontology_write_paths/*.md`; `heldout.json` 0222d5e8…, `dev.json` 90702a1a… neschimbate).

| Fișier | Motiv | Comportament schimbat |
|---|---|---|
| `03_IMPLEMENTATION/packages/memory/controller.py` | A-05, A-08, D-02, D-06, R-01, R-09 | `_refuse_ontology_slot` în `propose/update/archive/supersede/attest/review/promote`; `propose()` refuză id existent (inclusiv indexat-dar-ilizibil); `graph_expansion_status` ∈ {`ok`, `ok_noop_budget_zero`, `ok_noop_all_neighbours_seeded`, `ok_noop_no_eligible_neighbours`} + `graph_expansion_budget_resolved` în trace; `storage_diagnostics()`; singleton de modul construit lazy (`__getattr__`); `ONTOLOGY_SLOT_WRITERS` include `promote_verified_batch.py` |
| `.../learning/consolidation.py` | R-01 | `challenge`/`resolve_challenge` refuză sloturile |
| `.../memory/storage/file_engine.py` | D-01 | `load_report` + eveniment `storage_skipped` pentru fiecare ramură de skip (fără conținut) |
| `.../retrieval/context/retrieval.py` | D-04 | intrarea de cache = `{"notes", "fused_ranking"}`; intrările vechi = miss |
| `.../interfaces/vault_cli.py`, `memory_mcp_server.py` | A-01, R-02 | predicat unic `eligible_for_agent` (carantină, `model_egress`, `content_withheld_from`), fail-closed la excepție |
| `.../vault_access/core.py` | R-02 | fără verdict de controller, `quarantined: true` din frontmatter tot refuză |
| `.../routing/route_cli.py` | E-02 | exit 0 COMPLETED / 2 PENDING_VERIFICATION / 1 FAILED, BLOCKED |
| `.../interfaces/memory_v6_cli.py` | C-03 | default `--output` → `08_OBSERVABILITY/reports/spatial_index.json` |
| `30_SCRIPTS/ingestion/purge_rejected_rows.py` | A-09, R-08 | `purge_rows(manifest_path=…)` validează manifestul bit-exact (`validate_against_slots`) înainte de orice scriere canonică; CLI iese cu 2 |
| `30_SCRIPTS/verification/personal_data_guard.py` | B-03, R-09 | IBAN cu spații/minuscule, CNP după `=`/`:`/`-`, nume de documente extinse (cele ambigue doar pe scanuri/office), nume verificate și pe `.txt/.md/.zip/.eml/.msg/.csv`, mod `--history` (toate căile fiecărui blob) |
| `30_SCRIPTS/verification/write_path_audit.py` | A-10 | `classify()` recunoaște `memory/controller.py`, `learning/consolidation.py` |
| `30_SCRIPTS/verification/exempt_area_secret_scan.py` | B-04 | docstring fără numere care putrezesc |
| `.github/workflows/write-path-audit.yml` | A-10 | pas blocant: `test_ontology_slot_writers`, `test_controller_slot_path`, `test_write_path_audit_classify` |
| `.github/workflows/repository-hygiene.yml` | B-03 | job separat `personal-data-history` (`fetch-depth: 0`): scanarea istoricului nu blochează și nu înroșește check-ul — constatările (doar căi + id-uri de blob) merg în job summary + un `::warning::`; testele noi ale guard-ului în pasul blocant |
| `.github/workflows/security-boundary.yml` + 15 workflow-uri | F-01, F-03 | audit pe `.github/workflows/**`; `persist-credentials: false` |
| `00_GOVERNANCE/VAULT_STATE.md` | sursa de adevăr | rânduri `search()`, `FileStorageEngine`, graph expansion, access policy; defecte deschise noi (istoric, budgets, verifier, state cards) |
| `00_GOVERNANCE/coordination/{BOOTSTRAP_ALL_AGENTS_V1,UNIVERSAL_AGENT_MEMORY_PROTOCOL_V1}.md` | C-01 | căi către `00_GOVERNANCE/coordination/` |
| `00_GOVERNANCE/coordination/**/CURRENT.md` (6) | C-02 | banner + `status: HISTORICAL_SNAPSHOT` (în corp, nu în frontmatter — `test_notes_strict_yaml` fixează câmpurile) |
| `README.md`, `README.en.md` | D-06, D-07, R-04 | nucleu cognitiv „wired behind flags, OFF by default"; OpenStax „frozen in `0bb6791a5`"; rândul `04_MEMORY/` = legacy write root |
| `07_EVALUATION/ontology_write_paths/{CONTROLLER_SLOT_PATH,WRITE_PATHS}.md`, `20_TESTS/fixtures/ontology_slot_writers.json` | A-05, R-06 | calea controller-ului: CLOSED 2026-10-14; `promote_verified_batch` descris corect (verdicte decise de agent, scriere doar sub `--update-slots`) |
| `20_TESTS/{test_vault_state_accuracy,test_readme_references,test_agent_docs_memory_interfaces}.py` | C-04, C-05 | fără fallback pe basename; rădăcini explicite (repo, `03_IMPLEMENTATION/packages`, …); regex acceptă directoare goale și rădăcini nenumerotate |
| `20_TESTS/conftest.py`, `pytest.ini` | E-08, E-06 | telemetria redirecționată la tmp; markere înregistrate |
| `20_TESTS/memory_controller/test_git_isolation.py` | A-08 interacțiune | vault propriu în copia gitignored, id proaspăt |
| `20_TESTS/fixtures/unreadable_notes_allowlist.json` | checkpoint | fișierul de checkpoint listat ca `plain_document`, ca frații lui |
| 17 teste noi (lista în §10) | acceptanță | fiecare a picat înainte de fix și trece după (dovezi §4) |

## 4. Teste executate

Toate cu `python -m pytest -q -p no:cacheprovider` (+ `--strict-markers` la suită), `PYTHONDONTWRITEBYTECODE=1`, `ANTIGRAVITY_ARTIFACT_DIR`/`AI_MEMORY_VAULT_HOME` în scratch.

| Comandă / lot | Exit | Rezultat |
|---|---|---|
| Suita completă, bază `0b967fe8` | 0 | 3830 passed, 22 skipped, 9 xfailed (722 s) |
| Suita completă după loturile 1-5 (înainte de review) | 1 | 4057 passed, 6 failed, 22 skipped, 6 xfailed — cele 6: 5 × parity test dependent de ordine (runtime setup ridica excepție → `None`), 1 × checkpoint-ul meu neînregistrat în allowlist-ul de note ilizibile; ambele cauze rezolvate (§5) |
| **Suita completă finală** (diff final) | 0 | 4072 passed, 22 skipped, 6 xfailed (695 s) |
| Lot A (acces/purge/audit): 11 fișiere + `test_controller_slot_path` | 0 | 232 passed, 3 xfailed (xfail-urile eliminate ulterior de W4) |
| `test_vault_routes_quarantine_parity.py` | înainte: 2 failed / după: 12 passed | A-01, R-02 (inclusiv „fără verdict → frontmatter refuză", „verdict care aruncă → refuz") |
| `test_purge_canonical_gate.py` + `test_purge_rejected_rows.py` + `test_ontology_slot_writers.py` | înainte: 3 failed / după: 40 passed | A-09, R-08 (apelant Python gated) |
| `test_write_path_audit_classify.py` | înainte: 2 failed / după: 4 passed | A-10 |
| Lot B: guard + hygiene + exempt + workflow tests | 0 | 115 passed; `personal_data_guard.py` PASS; `exempt_area_secret_scan.py` FINDINGS=0; `repository_hygiene.py` FAILURES=0 |
| `test_personal_data_guard_formats.py` (+ regression) | înainte: 26 failed / după: 79 passed | B-03; `--history` pe repo real: exit 1, 32 constatări / 30 căi (4 documente cunoscute + vectori de test third-party + 2 PNG cu nume de document în istoricul RAW_IMPORTS) — nicio valoare tipărită |
| Lot C: 5 teste + layout + canonical roots | 0 | 85 passed |
| `test_bootstrap_cold_start_paths.py` / `test_coordination_state_sha.py` | înainte: 3 / 6 failed → după: 8 / 11 passed | C-01, C-02 |
| Lot D: 13 fișiere + `memory_controller/` | 1 → 0 după §5 | 755 passed, 2 failed (interacțiuni rezolvate: `test_git_isolation`, `test_notes_strict_yaml`) |
| `test_storage_load_report.py` / `test_retrieval_cache_ranking.py` / `test_graph_expansion_noop_status.py` / `test_controller_propose_no_overwrite.py` / `test_readme_cognitive_core_claim.py` / `test_curriculum_frozen_set_hash.py` | 3/1/5/3/2/– failed → 4/2/9/5/4/3 passed | D-01, D-04, D-02, A-08, D-06, D-07 |
| `test_controller_slot_path.py` (+ R-01: 5 operații × HUMAN/ADMIN) | 0 | 33 passed cu `test_consolidation` |
| Lot E/F: 13 module + bridge | 0 | 205 passed; `workflow_security_audit.py` tăcut, exit 0 |
| `test_route_cli_exit_codes.py` | înainte: 3/6 failed → 6 passed | E-02 |
| Review independent (spot-check 11 fișiere) | 0 | 265 passed |
| `git diff --check` | 0 | curat |

Limite: niciun agent extern real invocat intenționat (agentul de audit E a rulat accidental o dată `claude -p` cu goal „x"; răspunsul a fost „BLOCKED", fără efecte pe fișiere); `gitleaks` nerulat; CI nerulat (fără push).

## 5. Benchmark

- Held-out v2 rerulat **nemodificat** într-o copie a arborelui (`run_production_arms.py`, 70 s, offline): hash-uri `heldout.json 0222d5e8…8418`, `dev.json 90702a1a…dd39`, sidecar-uri `.sha256` identice înainte/după. Rezultat pe HEAD (corpus 1257/1064): `graph_off` candidate_recall 0.759, context_recall 0.069; `graph_on` (B=10, strict) 25 ok / 7 erori `GraphExpansionDegraded`, context_recall 0.091, McNemar discordant 0/0/0. Rezultatul comis `r016_production_arms_report.json` (corpus 918) arată 0.182 și 2 cazuri „on-only": **afirmația din README („bugetul 5-20 adaugă aceleași 2 cazuri") nu se mai reproduce la HEAD** — nu am modificat README-ul pe acest punct (cere re-baseline de către owner). Anomalie: trei rapoarte comise sunt byte-identice și runner-ul suprascrie `r016_…json` în repo la fiecare rulare.
- Loss funnel v3: hash `eeb53822…7eaa` verificat; cifrele din raport (71,56 % `PAGINATION_CUT`) confirmate din `loss_funnel_cases.json`, nu rerulate.
- Transfer benchmark OpenStax: hash `07b4a338…796d` confirmat (acum și sidecar + test); rerularea cere Gemini API → UNVERIFIABLE.
- Niciun set istoric, prag sau hash modificat.

## 6. Review independent (agent read-only, pe diff)

Decizie: **APPROVE WITH CHANGES** — 0 blockere, 0 controale slăbite. Toate cele 9 puncte au fost aplicate:

| ID | Constatare | Acțiune |
|---|---|---|
| R-01 MAJOR | regula de slot lipsea din `attest/review/promote` și `Consolidator.challenge/resolve_challenge` (probă: `challenge` HUMAN a scris `RECONSOLIDATING` în slot) | acceptat: regula adăugată + test parametrizat; docs aliniate |
| R-02 MAJOR | parity-ul de carantină eșua deschis fără controller / la excepție | acceptat: fail-closed în `eligible_for_agent`; fallback pe frontmatter în `core.py`; 2 teste |
| R-03 MINOR | `test_git_isolation` lăsa un fișier nou la fiecare rulare | acceptat: vault propriu în copia gitignored |
| R-04 MINOR | comentariu orfan în `test_readme_references`; rândul README `04_MEMORY/` | acceptat |
| R-05 MINOR | „not reachable in the repository" supraafirma (doar `main` fetch-uit); dublu `status` în CODEX | acceptat: reformulat „from the refs fetched"; marker în corp pentru toate (frontmatter fixat de `test_notes_strict_yaml`) |
| R-06 MINOR | rândul `promote_verified_batch` („verdicte umane") nesusținut (`decided_by: antigravity`) | acceptat: fixture + WRITE_PATHS corectate; script listat în `ONTOLOGY_SLOT_WRITERS` |
| R-07 MINOR | formula bugetului și B=1000 netestate; IBAN-urile neverificate de reviewer | acceptat: formula și B=1000 scoase din VAULT_STATE; IBAN marcat cu comanda și data măsurării |
| R-08 MINOR | gate-ul de manifest doar în `main()` | acceptat: mutat în `purge_rows()` + test |
| R-09 NIT | `pytest.raises(Exception)`; `history_blobs` reținea o singură cale/blob; `propose` vs id indexat-ilizibil | acceptat: toate trei |

Verificate ca solide de reviewer: cheia de cache include principal/lifecycle/arm (fără scurgere între principali); pin-urile SHA, `permissions`, `timeout` intacte; `load_report` fără conținut; mod `--history` fără valori; markere = etichete, nimic sărit; `test_vault_state_accuracy` fără fallback pe basename.

## 7. Rămase nerezolvate

| Problemă | Motiv concret | Ce lipsește |
|---|---|---|
| A-02/A-03/A-04 — policy unic pe `memory_*`/`vault_*`/REST/CLI; redactare pe `memory_*`; REST prin controller + loopback | schimbare de contract pentru 4 interfețe și pentru clienții REST (`jarvis_web`, `jarvis_v2`) | decizie de arhitectură a owner-ului |
| A-07 — AI_AGENT poate retrograda `verification` pe note ACTIVE | contract documentat explicit în VAULT_STATE | decizie owner |
| D-03 — `04_CONFIG/agent_budgets.json` nu e încărcat (`max_notes` 50 vs 5); cut de candidați înainte de scoring | schimbă punctul de operare al tuturor benchmark-urilor și face live bug-ul de cache (acum reparat) | re-baseline aprobat de owner |
| E-01 — verifier-ul selectat nu e niciodată dispatch-uit | contract nou (`verify()`, format de verdict) | decizie owner |
| C-03 — 21 `agent.md` + skills citează `99_SYSTEM/*` inexistente | nu există înlocuitor neambiguu pentru registrul de capabilități | decizie owner |
| C-08 — granița proiecției Obsidian în `AGENTS.md`/`CLAUDE.md`; C-09 `CLAUDE.md:109` | `AGENTS.md`/`CLAUDE.md` sunt contracte fixate de teste ale owner-ului | editare de către owner |
| B-02, B-05 — documente din docstring negăsite; reguli lipsă în scanner-ul secundar | branch-uri nefetch-uite; extindere de reguli | fetch all + rerun `--history`; decizie pe reguli |
| F-02 — branch protection, status CI pentru HEAD | HTTP 403 | cineva cu acces rulează cele două comenzi `gh api` |
| `30_SCRIPTS/knowledge/retroactive_update_openstax_provenance.py:133` re-propune id-uri existente | script one-off, fără test; ar eșua acum la rerulare | decizie owner (migrare la `update()`) |
| Runner-ul held-out suprascrie `r016_production_arms_report.json` în repo; README-ul citează un lift de graf care nu se mai reproduce | modificare de rezultat comis | re-baseline owner |

## 8. Acțiuni care necesită aprobare umană (NU au fost executate)

1. **Rescrierea istoricului** pentru cele 4 documente din `02_PRODUCT/projects/imported/bot/trade/` (`git filter-repo --invert-paths`), coordonată cu `09_SECURITY/incident_register/R001_HISTORY_REWRITE_PROCEDURE.md`; apoi force-push, purjarea cache-urilor GitHub/PR refs, notificarea fork-urilor. Înainte: fetch-uirea tuturor celor ~190 branch-uri și rerularea `personal_data_guard.py --history`.
2. **Rotația credențialelor** din registrul R001 (10 categorii PENDING) și a parolelor mock din `AuthContext.js` dacă au fost reutilizate.
3. Push/merge al branch-ului `fable/remediation-2026-10-14`; rularea CI; `active_note_integrity.py --record` dacă vreo notă ACTIVE a fost atinsă (niciuna nu a fost).
4. Deciziile de arhitectură din §7.

## 9. Verdict per constatare

`FIXED`: A-01, A-05, A-06 (prin A-05), A-08, A-09, A-10, B-03, B-04 (docstring), B-06, C-01, C-02, C-04, C-05, D-01, D-02 (status), D-04, D-06, D-07, E-02, E-06, E-08, F-01, F-03.
`PARTIALLY FIXED`: B-05 (raportat, nemodificat), C-03 (CLI da; profile nu).
`BLOCKED`: A-02, A-03, A-04, A-07, B-01, C-09, D-03, E-01.
`NOT REPRODUCED`: A-06 (ca scriere de conținut), C-06, D-05, F-05, F-06, ID-009.
`UNVERIFIABLE`: B-02, F-02, rulare gitleaks live, clienți MCP reali, comportamentul agenților externi (E-07).
Raportat fără acțiune: B-07, C-07, C-08, E-03, E-04, E-05, F-04.

## 10. Git status și diff

- Branch `fable/remediation-2026-10-14` pe baza `0b967fe8`; `main` neatins; un commit local `c883858a` (checkpoint + raport incluse); nimic push-uit sau merge-uit. Working tree final: curat.
- `git diff --stat`: 60 fișiere, +948/−152; 17 fișiere noi (14 teste, 1 sidecar `.sha256`, 1 checkpoint, acest raport). `git diff --check`: curat.
- Fișiere noi de test: `test_bootstrap_cold_start_paths`, `test_controller_propose_no_overwrite`, `test_coordination_state_sha`, `test_curriculum_frozen_set_hash`, `test_graph_expansion_noop_status`, `test_inbox_tracked_gate`, `test_personal_data_guard_formats`, `test_readme_cognitive_core_claim`, `test_retrieval_cache_ranking`, `test_route_cli_exit_codes`, `test_storage_load_report`, `test_suite_does_not_write_telemetry`, `test_vault_routes_quarantine_parity`, `test_workflow_policy_real_files`, `test_write_path_audit_classify`.
- Suita completă finală (după review, pe diff-ul final, `--strict-markers`): **4072 passed, 22 skipped, 6 xfailed, exit 0** (695 s). Față de bază: +242 teste, −3 xfail (A-05 închis), 0 regresii. Suita nu mai modifică fișiere din checkout (E-08).
