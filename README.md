---
category: index
status: active
version: 6.0.0
confidence: very_high
verification: verified
provenance_status: official
relations:
  - "00_CORE/Identity.md"
  - "00_CORE/Rules.md"
  - "00_CORE/Memory_Protocol.md"
  - "00_CORE/GRAPH/07 Knowledge Domains Map.md"
  - "01_KNOWLEDGE/Agents_Skill_Matrix.md"
  - "01_KNOWLEDGE/Master_Skills_Catalog_251.md"
  - "01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md"
  - "99_SYSTEM/Memory_V6_Architecture.md"
  - "99_SYSTEM/Obsidian_Skill_Agent_Memory_Sync.md"
  - "05_RESOURCES/Obsidian/Skill_Agent_Memory_MOC.md"
  - ".claude-plugin/plugin.json"
  - ".claude-plugin/marketplace.json"
  - "skills/ai-memory-vault/SKILL.md"
---

# 🧠 AI Memory Vault (CODEX-Ready)

<p align="center">
  <strong>Vault Obsidian de memorie persistentă pentru agenți AI, cuplat cu un motor cognitiv Python (<code>cognitive_core/</code>) care implementează un pipeline multi-agent (Council) cu memorie episodică/semantică, planificare, reflecție și audit de consum de tokeni.</strong><br>
  Memorie durabilă partajată între Codex, Claude Code, Antigravity, agenți locali și Obsidian — fără memorii concurente paralele.
</p>

<p align="center">
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY"><img alt="Architecture" src="https://img.shields.io/badge/Architecture-v6.0.0--ACTIVE-0ea5e9.svg"></a>
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY/actions"><img alt="Pytest" src="https://img.shields.io/badge/Pytest-518%20Passed-22c55e.svg"></a>
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY/tree/main/.claude-plugin"><img alt="Claude Code Plugin" src="https://img.shields.io/badge/Claude%20Code-Plugin%20Ready-a855f7.svg"></a>
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY/tree/main/.agents/agents"><img alt="Agents Council" src="https://img.shields.io/badge/Agents-21%20Specialized-f97316.svg"></a>
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY/tree/main/.agents/skills"><img alt="Local Skills" src="https://img.shields.io/badge/Local%20Skills-251%20Cataloged-10b981.svg"></a>
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY/tree/main/01_KNOWLEDGE/BOOKS"><img alt="Foundation Books" src="https://img.shields.io/badge/Foundation%20Books-10%20Tiers%20%7C%2060%20Teme-6366f1.svg"></a>
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY/tree/main/99_SYSTEM"><img alt="Security Invariants" src="https://img.shields.io/badge/Security-P0--P18%20%7C%20I--001..I--012-ef4444.svg"></a>
  <a href="https://github.com/userist123/AI_Memory_Vault_CODEX_READY/tree/main/memory_controller"><img alt="Storage Engine" src="https://img.shields.io/badge/Storage-SQLite%20WAL%20%2B%20Markdown-eab308.svg"></a>
  <a href="https://obsidian.md/"><img alt="Obsidian" src="https://img.shields.io/badge/Obsidian-Synchronized%20MOC-8b5cf6.svg"></a>
</p>

> **Codex Operating Contract**: [`AGENTS.md`](AGENTS.md) | **Invariante de Securitate**: [`vault_cognitive_rules.md`](.agents/rules/vault_cognitive_rules.md) | **API Memorie**: `memory_controller/`

**AI Memory Vault** este stratul canonic de memorie, knowledge, skills, agents, procedures, provenance și audit pentru ecosistemul AI. Același Vault poate fi folosit de **Codex, Claude Code, Google Antigravity, agenții locali (JARVIS) și Obsidian**, fără memorii canonice paralele.

---

## 🎯 Ce este & Principiul Fundamental

AI Memory Vault este o infrastructură persistentă pentru agenți AI care trebuie să poată:
- recupera doar cunoștințele relevante pentru o sarcină (sparse context);
- folosi skills și agenți specializați cu privilegii minime;
- păstra proveniența și nivelul de încredere (fără auto-atestare AI);
- separa conținutul extern RAW de memoria canonică;
- sincroniza knowledge, skills, agents, procedures și Obsidian;
- menține continuitatea între sesiuni și clienți AI;
- păstra auditabilitatea operațiunilor de memorie (jurnal SHA-256 tamper-evident);
- evolua prin ingestie controlată din surse externe și sinteză enciclopedică pe 10 niveluri.

```text
===================================================================================
                               PRINCIPIUL FUNDAMENTAL
===================================================================================
                                  ONE VAULT
                             ONE CANONICAL MEMORY
                             MULTIPLE AI CLIENTS
                         MULTIPLE SPECIALIZED AGENTS
                             CONTROLLED RETRIEVAL
                               CONTROLLED WRITE
                               FULL PROVENANCE
                            MEASURABLE VERIFICATION
===================================================================================
```

---

## 📁 Structura vault-ului (Obsidian)

| Folder | Conținut |
|---|---|
| `00_CORE` | Identitate, reguli, protocol operațional AI, model de confidence, arhitectură sistem (+ variante legacy Claude/Perplexity, `GRAPH/`) |
| `01_KNOWLEDGE` | Note atomice: stack tehnic, securitate, design/UI, arhitecturi (WPF, trading algoritmic, multi-agent), skill catalogs, 10 Tiers Foundation Books (`01_KNOWLEDGE/BOOKS/`), referințe externe (`EXTERNAL_SKILLS/`) |
| `02_PROJECTS` | Proiecte active și continuitate |
| `03_PROCEDURES` | Proceduri operaționale repetabile |
| `04_MEMORY` | Memorie episodică: `Decisions/`, `Errors/`, `Experiences/`, `Lessons/`, `Preferences/` + fișiere `policy-lesson_*.md` |
| `05_RESOURCES` | Resurse și navigare Obsidian |
| `06_INBOX` | `RAW_IMPORTS` — inbox pentru conținut neprocesat și granița `RAW_EXTERNAL/skills/` |
| `90_TEMPLATES` | Șabloane frontmatter canonice |
| `99_SYSTEM` | Fișiere de sistem ale vault-ului, contracte de context, bugete tokeni și protocoale de sincronizare |
| `.obsidian` | Config Obsidian, inclusiv `graph.json` cu grupuri de culori pe foldere |

**Fișiere root:** `AGENTS.md`, `CLAUDE.md`, `PROJECT.md`, `ORIGINAL_REQUEST.md`, `REVIEW_QUEUE.md`, `TEST_INFRA.md`, `TEST_READY.md`.

> **Notă**: multe fișiere din `01_KNOWLEDGE` și `04_MEMORY` au denumiri `test_*`, `unknown_*` — sunt artefacte de test/import automat, nerevizuite încă (lifecycle `unverified`).

---

## 🧠 `cognitive_core/` — Motorul Cognitiv

Peste 60 de module Python organizate pe funcții cognitive precise:

### 1. Pipeline Council (B1–B5) — orchestrare multi-model cu audit de consum
- `council_model_execution.py` (B1) — execută modelele Council prin provideri (`model_provider.py`, `fake_model_provider.py`, `local_provider.py`, `openai_provider.py`) cu rutare pe tier (`model_tier_router.py`)
- `model_tier_router.py` — rutare pe tier-uri de model (light / standard / heavy)
- `council_usage_audit.py` (B2/B4) — telemetrie per-call (`actual_usage_telemetry.py`)
- `council_efficiency_report.py` (B5) — agregare istorică, verdict de eficiență, export CSV/Markdown/JSON
- `council_budget_controller.py` — control buget tokeni la nivel de Council

### 2. Memorie și Cogniție
- `working_memory.py`, `global_workspace.py`, `spreading_activation.py`, `activation.py`, `attention.py`
- `consolidation.py`, `sleep_consolidation.py`, `deduplication.py`, `conflict_detector.py`
- `recall.py`, `ranked_search.py`, `qdrant_retrieval.py`, `spatial_index.py`, `semantic.py`
- `reflection.py`, `reasoning.py`, `planning.py`, `plan_complexity_analyzer.py`, `learning.py`, `motivation.py`

### 3. Orchestrare și Execuție
- `orchestrator.py`, `executive.py`, `executive_model_execution_bridge.py`, `multi_graph.py`, `synapse.py`
- `dispatch_cli.py`, `recall_cli.py`, `memory_v6_cli.py`, `skill_router.py`, `tool_router.py`

### 4. Securitate și Infrastructură
- `security_audit.py`, `git_hooks.py`, `sensor_buffer.py`, `evaluation.py`, `report_view.py`, `proposal_queue.py`, `queue_promoter.py`, `version.py`, `trading_decisions.py`
- `agents/`, `benchmarks/`, `neuromorphic/`

### 5. Teste
- `cognitive_core/tests/` — peste 70 de fișiere de test (`pytest`), acoperind fiecare modul de mai sus, plus teste end-to-end (`test_end_to_end_workflow.py`), adversariale (`test_milestone4_adversarial_challenger*.py`, `test_milestone5_*`), și de regresie (`test_council_efficiency_report_fix.py`).
- **Rezultat curent**: **518 passed, 2 skipped (100% verde)**.

---

## 🧬 Arhitectura Cognitivă Bio-Inspirată (Brain Upgrade — ACT-R & GWT)

Sistemul integrează 5 module inspirate din neuroștiințele cognitive pentru o funcționare autonomă de tip creier:

1. **ACT-R Activation Decay (`cognitive_core/activation.py`)**:
   - Calculează activarea bazală $B_i = \ln(\sum_j t_j^{-d})$ (Anderson, 2004). Notele neaccesate scad natural și trec în stare `DORMANT_THRESHOLD` (-2.0), fiind excluse din căutările standard pentru economisirea contextului.
2. **Reconsolidarea Memoriei (`cognitive_core/consolidation.py`)**:
   - Memoriile canonice contrazise intră în starea `RECONSOLIDATING` cu istoricul versiunilor salvat, permițând actualizări plastice fără pierderi de date (Nader et al., 2000).
3. **Modulul Motivațional & Utilitate (`cognitive_core/motivation.py`)**:
   - Urmărește utilitatea acțiunilor $U = P \cdot G - C$ prin Exponential Moving Average și oferă bonusuri dinamice de atenție pe baza recompenselor reale de la `VerifierAgent`.
4. **Global Workspace Theory (`cognitive_core/global_workspace.py`)**:
   - Hub competitiv central în care agenții Consiliului (`Router`, `Retrieval`, `Verifier`, `Critic`) trimit propuneri, iar propunerea cu scor maxim este difuzată (*broadcast*) global tuturor agenților (Baars, 1988; Dehaene et al., 2001).
5. **Rădăcini Neuromorfe SNN (`cognitive_core/neuromorphic/`)**:
   - Neuroni Leaky Integrate-and-Fire (`lif_neuron.py`) cu ecuația $\tau_m \frac{dV}{dt} = -(V - V_{reset}) + I_{syn}$, sinapse plastice cu învățare temporală STDP (`stdp_synapse.py`) și rețea spiking de legătură simbolic/sub-simbolic (`snn_network.py`).

---

## 🚀 Extensia de Arhitectură Memory V6 (v6.0.0 Engine)

Vault-ul integrează extensia aditivă **Memory V6** care adaugă 17 componente modulare:

1. **Buffer Ephemeral de Senzori (`cognitive_core/sensor_buffer.py`)**: Păstrează evenimentele brute de sesiune în memorie RAM fără a polua stocarea canonică.
2. **Extractor Atomic de Memorie (`cognitive_core/extraction.py`)**: Extragere deterministă de fapte, decizii, proceduri și lecții prin euristici structurate.
3. **Adapter Local Ollama (`cognitive_core/ollama_extractor.py`)**: Extragere semantică opțională folosind LLM-uri locale (ex. `qwen2.5-coder`).
4. **Coadă de Propuneri (`cognitive_core/proposal_queue.py`)**: Coadă de triaj în `06_INBOX/` cu stări controlate (`PENDING`, `APPROVED`, `REJECTED`).
5. **Detector de Conflicte (`cognitive_core/conflict_detector.py`)**: Detector euristic de conflicte, contradicții și negații logice.
6. **Queue Promoter Controlat (`cognitive_core/queue_promoter.py`)**: Promovează doar candidații marcați `APPROVED` de oameni direct în `MemoryController.propose()`.
7. **Memorie Multi-Graf (`cognitive_core/multi_graph.py`)**: 4 grafuri derivate (semantic, temporal, cauzal, entități).
8. **Spreading Activation (`cognitive_core/spreading_activation.py`)**: Difuzie de activare relațională limitată strict la orizontul de $\le 2$ hop-uri (bounded associative expansion).
9. **Căutare Clasată Hibridă (`cognitive_core/ranked_search.py`)**: Re-clasare hibridă combinând scorul lexical, vectorial și activarea ACT-R.
10. **Sleep-Phase Consolidation (`cognitive_core/sleep_consolidation.py`)**: Mentenanță automată de noapte (arhivare, scădere activare, curățare referințe).
11. **Obsidian Report View (`cognitive_core/report_view.py`)**: Raportare mentenanță randată ca notă Markdown cu legături `[[wikilinks]]`.
12. **LoCoMo Benchmark Harness (`cognitive_core/benchmarks/`)**: Măsurare automată Precision@K, Recall@K și MRR.
13. **CLI Unificat V6 (`cognitive_core/memory_v6_cli.py`)**: Punct de intrare unic în linia de comandă.
14. **Memory Event Bus (`cognitive_core/event_bus.py`)**: Decuplare reactivă prin evenimente asincrone.
15. **Context Pack Builder (`cognitive_core/context_pack_builder.py`)**: Asamblare de context sparse sub bugetul protocolului de context.
16. **Token Telemetry Auditor (`cognitive_core/actual_usage_telemetry.py`)**: Telemetrie consum tokeni per apel și agent.
17. **Fluxuri CI/CD GitHub Actions (`.github/workflows/`)**: `memory-v6-tests.yml` și `memory-consolidation.yml`.

### Runbook CLI Memory V6
```powershell
# 1. Extragere fapte & adăugare în coadă (opțional cu Ollama local)
python -m cognitive_core.memory_v6_cli extract --text "Am decis: folosim SQLite WAL mode cu busy_timeout 5000ms." --enqueue

# 2. Revizuire coadă cu detectare conflicte
python -m cognitive_core.memory_v6_cli review --show-conflicts

# 3. Aprobare umană & Promovare controlată
python -m cognitive_core.memory_v6_cli approve <candidate_id> --reviewer human
python -m cognitive_core.memory_v6_cli promote-approved --principal ai_agent

# 4. Maintenance & Randare Raport Obsidian
python -m cognitive_core.memory_v6_cli consolidate --render

# 5. Rulare Benchmark de Recuperare LoCoMo
python -m cognitive_core.memory_v6_cli benchmark --retrieval graph
```

---

## 🛡️ Stratul de Memorie Canonică (`memory_controller/`) & Invariante

`MemoryController` este punctul unic de intrare pentru orice citire sau scriere de memorie canonică. Acesta garantează:

- **Autorizare pe Operațiuni**: politici per operațiune (`propose`, `read`, `search`, `review`, `promote`, `archive`, `update`, `supersede`, `attest`) asociate rolurilor `HUMAN`, `AI_AGENT`, `ADMIN`.
- **Proveniență**: fiecare notă înregistrează `source_type` (`user`, `official`, `ai`, `inference`, `execution`, `import`). `AI_AGENT` nu poate revendica surse umane sau oficiale (`I-002`).
- **Ciclu de Viață Controlat**: `RAW -> CLASSIFIED -> NORMALIZED -> REVIEW -> VERIFIED -> ACTIVE -> {SUPERSEDED, ARCHIVED}`. Agenții AI pot propune doar până la starea `REVIEW` (`I-003`).
- **Atestare Umană Dedicată**: `verification` poate ajunge la `verified` exclusiv prin metoda `attest()`, apelabilă doar de `HUMAN`/`ADMIN` (`I-001`, `I-004`).
- **Audit Criptografic**: fiecare operațiune este salvată în `audit_log.jsonl` cu lanț de hash-uri SHA-256 tamper-evident.
- **Supersedare Atomică**: înlocuire explicită, fără cicluri, protejând memoriile verificate de om împotriva suprascrierilor automate.
- **Motoare de Stocare**: `FileStorageEngine` (fișiere Markdown+YAML compatibile cu Obsidian) și `SQLiteStorageEngine` (mod WAL, `PRAGMA busy_timeout=5000`, tranzacții atomice `BEGIN IMMEDIATE`).
- **Regăsire Unificată Securizată (`I-RETRIEVAL`)**: interogările de memorie trec exclusiv prin API-ul securizat sau prin `python -m cognitive_core.recall_cli --query ...`. Scanările directe brute de fișiere sunt strict interzise.

---

## 🏛️ Consiliul celor 21 de Agenți Specializați

Arhitectura este guvernată de un consiliu multi-agent coordonat prin `01_KNOWLEDGE/Agents_Skill_Matrix.md`:

| Nr. | Agent | Domeniu & Rol Primar | Acces | Skill-uri Principale Alocate |
|:---:|:---|:---|:---|:---|
| **1** | `agentic_workflow_orchestrator` | Orchestrator fluxuri multi-agent și gh-aw | Read, Propose | `agent-squad`, `agentic-workflows`, `workflow-automation` |
| **2** | `backend_systems_engineer` | Arhitectură API backend, microservicii, worker pools | Read, Propose | `fastapi-pro`, `csharp-pro`, `golang-pro`, `nodejs-backend-patterns` |
| **3** | `compiler_and_tooling_engineer` | Compilatoare, AST, runtime, lintere și CLI | Read, Propose | `rust-pro`, `cpp-pro`, `cli-mastery`, `python-packaging` |
| **4** | `content_strategist` | Arhitectură informațională și documentație tehnică | Read, Propose | `brain-to-docs`, `technical-tutorials`, `docs-architect` |
| **5** | `database_and_persistence_engineer`| SQLite WAL, migrații, indexare și caching Redis | Read, Propose | `sqlite-wal-optimization`, `postgresql`, `redis-cli`, `ef-core` |
| **6** | `frontend_saas_engineer` | Aplicații web SaaS, Next.js, stări optimiste | Read, Propose | `nextjs-best-practices`, `typescript-pro`, `tanstack-query-expert` |
| **7** | `game_engineer` | Sisteme de joc 2D/3D, bucle de randare, fizică | Read, Propose | `3d-games`, `threejs`, `godot-gdscript-patterns`, `shader-programming-glsl` |
| **8** | `local_ai_engineer` | Modele locale Ollama, vLLM, ONNX, Whisper, cuantizare | Read, Propose | `local-llm-expert`, `vllm-inference-optimization`, `pydantic-ai` |
| **9** | `memory_controller_architect` | Păstrătorul nucleului canonic de memorie și audit | Read, Write, Attest | `vault-operations`, `memory-safety-patterns`, `context-management` |
| **10** | `polyglot_systems_architect` | Sisteme eterogene, interoperabilitate polyglotă | Read, Propose | `clean-architecture-backend`, `microservices-patterns`, `ddd-tactical` |
| **11** | `quant_developer` | Analiză cantitativă, modele de risc, backtesting | Read, Propose | `python-trading-systems`, `risk-metrics-calculation`, `backtesting` |
| **12** | `secops_auditor` | Audit de securitate, conformitate OWASP | Read, Audit | `security-invariants`, `owasp-top-10-audit`, `pki-certificate-mgmt` |
| **13** | `site_reliability_and_devops_architect` | Fiabilitate, containere, K8s, CI/CD pipelines | Read, Propose | `docker-expert`, `kubernetes-orchestration`, `prometheus-grafana` |
| **14** | `system_architecture_agent` | Design de ansamblu, modele C4, decizii ADR | Read, Propose | `senior-architect`, `c4-container`, `architecture-decision-records` |
| **15** | `threat_hunting_analyst` | Detecție intruziuni, forensic memory analysis | Read, Audit | `threat-hunting`, `digital-forensics`, `malware-analysis` |
| **16** | `ui_sensei_architect` | Filosofia sistemelor de design UI, coeziune vizuală | Read, Propose | `ui-skills`, `design-system-foundation`, `radix-ui-design-system` |
| **17** | `ui_ux_designer` | Ergonomie interfețe, accesibilitate WCAG | Read, Propose | `ux-flow`, `ui-a11y`, `ux-audit`, `tailwind-design-system` |
| **18** | `web_creative_developer` | Efecte WebGL avansate, shadere Three.js, GSAP | Read, Propose | `webgl-laser`, `webgl-landing-steering`, `gsap-scrolltrigger` |
| **19** | `web_design_engineer_agent` | Punte între ingineria de cod și designul internațional | Read, Propose | `garden-web-design-engineer`, `xiaopu-web-design`, `frontend-design` |
| **20** | `web_quality_engineer` | Core Web Vitals, audit Lighthouse, profiling DOM | Read, Propose | `web-quality-audit`, `core-web-vitals`, `pagespeed-enhancer` |
| **21** | `wpf_engineer` | Aplicații desktop Windows, MVVM Toolkit, XAML | Read, Propose | `csharp-wpf-desktop`, `mvvm-toolkit`, `dotnet-best-practices` |

---

## 📦 Corpusul de Skills (251 Locale & Index Global)

- **Operational Skills (`.agents/skills/`)**: 251 skill-uri locale cu instrucțiuni `SKILL.md` complete, inventariate în `01_KNOWLEDGE/Master_Skills_Catalog_251.md`.
- **RAW External Boundary (`06_INBOX/RAW_IMPORTS/skills/`)**: Graniță strictă de izolare pentru sursele externe (Crawl -> Deduplicare SHA-256 -> Clasificare -> Filtrare de securitate -> Normalizare YAML -> Triaj -> Aprobare umană -> Promovare).

---

## 📚 Sinteza Cărților Fundamentale (Tiers 1–10) & Caietul cu 60 de Teme Practice

Pentru a asigura o fundamentare teoretică completă, sistemul integrează 61 de note canonice în `01_KNOWLEDGE/BOOKS/` (validate JSON Schema Draft-07), asociate unui **Caiet de Teme și Aplicații Practice** conținând 60 de exerciții aplicate complete:

| Tier | Domeniu & Lucrări de Referință | Concepte Fundamentale Tratate | Teme Practice Alocate |
|:---:|:---|:---|:---:|
| **Tier 1** | **Arhitectură & Baze de Date Distribuite** (*DDIA* — Kleppmann) | Tranzacții ACID, izolare SSI, moduri de stocare (LSM vs B-Tree), SQLite WAL, consens Raft. | **Teme 1 – 6** |
| **Tier 2** | **Inteligență Artificială Modernă** (*AIMA 4e* — Russell & Norvig) | Agenți raționali, planificare clasică PDDL, căutare euristică A*, decizii markoviene (MDP). | **Teme 7 – 12** |
| **Tier 3** | **Arhitectura Sistemelor de Agenți AI** (*Agentic Architectures* — Zvarydchuk, Weng) | Buclă OODA (Observe-Orient-Decide-Act), ReAct, Reflexion, delegare de roluri cu privilegii minime. | **Teme 13 – 18** |
| **Tier 4** | **Ingineria Aplicațiilor cu LLM & RAG** (*Building LLM Apps* — Huyen, Briggs) | RAG ierarhic, chunking semantic, vector stores hibride, GraphRAG, reranking Cross-Encoder. | **Teme 19 – 24** |
| **Tier 5** | **Sisteme de Machine Learning în Producție** (*Designing ML Systems* — Chip Huyen) | Data drift, concept drift, feature stores, monitorare continuă a performanței. | **Teme 25 – 30** |
| **Tier 6** | **Deep Learning & Rețele Neuronale** (*Deep Learning* — Goodfellow et al.) | Backpropagation, regularizare, Transformer self-attention, optimizatori adaptivi (AdamW). | **Teme 31 – 36** |
| **Tier 7** | **Sisteme de Memorie Avansată & Prompt Eng.** (*Memory Systems* — Pai, Liu) | Memorie episodică vs semantică, benchmark-uri LoCoMo/LongMemEval, demarcare XML. | **Teme 37 – 42** |
| **Tier 8** | **Securitate Ofensivă/Defensivă & Hardening** (*OWASP LLM Top 10, NIST AI RMF*) | Indirect prompt injection, jailbreak defenses, audit SHA-256, invariante I-001..I-012. | **Teme 43 – 48** |
| **Tier 9** | **Scalare, Concurrency & Performanță** (*High-Performance Systems* — C#, Rust, Go) | Tranzacții SQLite concurente (`BEGIN IMMEDIATE`), asincronism Go/Rust Tokio, pooling. | **Teme 49 – 54** |
| **Tier 10** | **Frontiera Cercetării Cognitive & Neuro-AI** (*Neuromorphic, GWT, Active Inference*) | Neuroni LIF, sinapse plastice STDP, Global Workspace Theory, inferență activă Karl Friston. | **Teme 55 – 60** |

- **Caietul de Teme Practice (4.696 linii)**: [`.agents/skills/learn/references/caiet_de_teme_si_aplicatii_practice.md`](.agents/skills/learn/references/caiet_de_teme_si_aplicatii_practice.md)
- **Podul de Sinteză a Cărților**: [`01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md`](01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md)

---

## 🛠️ `scripts/`
- `generate_b4_baseline.py` — generator baseline B4: rulează N execuții Council prin `FakeModelProvider`, produce rapoarte per-run în `reports/b4/`, agregă cu B5 (`b5_report.json`, CSV-uri, `B5_TOKEN_EFFICIENCY_REPORT.md`)
- `skill_ingestion.py` — ingestie skill-uri externe

## 📦 Alte Componente Root
- `vault_api.py` — API pentru interacțiune cu vault-ul
- `memory_controller/`, `config/`, `agents/`, `commands/`, `skills/` — module suport
- `xau_kinetic/`, `XAU_Kinetic.Desktop/`, `XAU_Kinetic_Standalone/`, `projects/` — proiecte satelit (trading/desktop)
- `projects/jarvis_cognitive_brain/` — asistent cognitiv local autonom OODA (Whisper, Kokoro-82M ONNX, FastMCP Home Assistant, HUD 3D)
- `projects/jarvis_web/` — asistent vocal web standalone WebGL Three.js & Web Audio SFX
- `compute_nodes.json`, `pytest.ini`, `requirements-memory-v6.txt`
- `audit_log.jsonl` (~34.6 MB) și `test_audit_log.jsonl` (~3.1 MB) — loguri de audit brute, generate automat

---

## 🗺️ Stratul de Navigare Graf Obsidian — `00_CORE/GRAPH/`

Un set de note de tip `type: moc` (Map of Content) ce oferă navigare vizuală umană peste graful Obsidian:
- `00 Core Map`, `01 Cognitive System Map`, `02 Memory Knowledge Map`, `06 Obsidian Graph Map`, `07 Knowledge Domains Map`, `08 Memory Subsystems Map`, `09 Agent Evidence Map`, `10 Imports and Sources Map`, `11 Templates and System Map`, `12 Projects and Procedures Map`, `13 Root and Control Map`, plus hărțile per-categorie pentru Lessons, Decisions, Errors, Experiences și Preferences, toate conectate la `Knowledge Graph Home`.
- Fisierele `.obsidian/graph.json` definesc culorile pe foldere pentru claritate vizuală.

> **Acest strat este exclusiv pentru navigarea vizuală umană în Obsidian.** Nu are rol de runtime în `MemoryController` sau `cognitive_core` și nu alterează scorurile de regăsire.

---

## 🎯 Ce își propune proiectul în mod activ (Direcția Activă)

Direcția activă este transformarea acestui Vault în **backend-ul canonic partajat de stare a proiectelor pentru multipli agenți de codare AI** (locali și cloud) care lucrează pe aceleași baze de cod în timp — astfel încât continuarea lucrului să nu necesite re-trimiterea manuală a istoricului conversațional:

- Un **protocol de handoff multi-agent documentat** (`09_COORDINATION/todo.md` și `lessons.md`) cu proprietar (`owner`), timestamp ISO 8601, blocaje și criterii de review.
- Un mecanism de **review-gate** pentru decizii de arhitectură, securitate sau contracte pe care un agent de codare nu trebuie să le ia unilateral.
- Evitarea strictă a duplicării arhitecturii de memorie atunci când sunt integrate scule externe.

---

## ⚖️ Disciplina de Status (Status Discipline)

Acest repository distinge explicit între **corectitudinea codului** (verificabilă prin inspectarea unui diff) și **verificarea runtime** (care impune execuția reală a suitei `pytest` pe un checkout efectiv). Tratați orice afirmație de tip "rezolvat" sau "test trecut" ca lipsită de valoare dacă nu citează commit-ul efectiv și ieșirea reală a testelor.

```powershell
# Execuție suită completă de teste (pytest)
python -m pytest cognitive_core/tests/ -q

# Rezultat așteptat:
# 518 passed, 2 skipped in ~15s
```

---

## 🕒 Istoric Recent (Schimbări Majore)

- **`6e256a9`** — `feat(B4)`: adăugat generator baseline B4 (`generate_b4_baseline.py`) + test de regresie, validat prin CLI și programatic (3 rapoarte JSON, `b5_report.json` cu `run_count == 3`, Markdown cu secțiune "Council efficiency verdict").
- **`0f4e0f3`** — `fix(B5)`: corectat bug în `council_efficiency_report.py` unde `top_optimization_candidate` combina independent agentul cu cel mai mare consum individual și tier-ul cu cel mai mare consum agregat, producând perechi ireale (ex. `"CRITIC / light"` când CRITIC rula mereu pe `standard`). Fix-ul grupează consumul pe perechea reală `(agent_id, model_tier)` observată în date. Adăugate 10 teste de regresie (`test_council_efficiency_report_fix.py`).
- **`dca725bfa`** — `docs(readme)`: restaurat master README canonic în limba română, integrat sinteza Tiers 1–10 cărți fundamentale, 60 teme de laborator, 21 agenți, 251 skills și verificat suitele de securitate P0–P18.

---

## 📝 Note de Mentenanță

- Fișierele `council_efficiency_report.py` și restul pipeline-ului B1–B5 erau marcate FROZEN în instrucțiunile de proiect anterioare; fix-ul de mai sus a fost aplicat cu autorizare explicită — merită confirmat statutul FROZEN cu echipa/`AGENTS.md`.
- `01_KNOWLEDGE` și `04_MEMORY` conțin numeroase fișiere placeholder (`test_*.md`, `unknown_*.md`, `policy-lesson_*.md`) rezultate din import/generare automată — candidate pentru curățare sau revizuire lifecycle (`unverified` → `verified`).
- Fișierele `*_Claude_Legacy.md` și `*_Perplexity_Legacy.md` din `00_CORE`/`01_KNOWLEDGE` sunt versiuni istorice păstrate pentru compatibilitate — de evaluat dacă mai sunt necesare.

---

## 📜 Principiul Final

> **AI Memory Vault nu este doar un folder de fișiere. Este memoria canonică partajată a ecosistemului AI.**
>
> Skills adaugă capabilități. Agents adaugă specializare. Knowledge adaugă context durabil. Procedures adaugă moduri de lucru. Memory păstrează experiența și deciziile. Provenance și verification stabilesc încrederea. Obsidian oferă navigare și audit. Claude, Antigravity și Codex sunt clienți ai aceleiași memorii, nu proprietari ai unor memorii separate.

---

## 🔗 Legături Utile & Graf Obsidian

- **Repository**: [https://github.com/userist123/AI_Memory_Vault_CODEX_READY](https://github.com/userist123/AI_Memory_Vault_CODEX_READY)
- **Claude Plugin**: `.claude-plugin/`
- **Memorie Canonică**: `memory_controller/` + `04_MEMORY/`
- **Skill-uri Operaționale**: `.agents/skills/`
- **Graniță RAW External**: `06_INBOX/RAW_IMPORTS/skills/`
- **Matrice Agenți**: `01_KNOWLEDGE/Agents_Skill_Matrix.md`
- **Sincronizare Obsidian**: `99_SYSTEM/Obsidian_Skill_Agent_Memory_Sync.md`

### 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[07 Knowledge Domains Map]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
