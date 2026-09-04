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

<div align="center">

# 🧠 `AI Memory Vault`
### *Autonomous Cognitive Brain, Multi-Agent Memory & Cognitive Engine V6*

[![Architecture](https://img.shields.io/badge/Architecture-v6.0.0--ACTIVE-0284c7?style=for-the-badge&logo=blueprint&logoColor=white)](https://github.com/userist123/AI_Memory_Vault_CODEX_READY)
[![Tests](https://img.shields.io/badge/Pytest-518%20Passed-10b981?style=for-the-badge&logo=pytest&logoColor=white)](https://github.com/userist123/AI_Memory_Vault_CODEX_READY/actions)
[![Agents](https://img.shields.io/badge/Council-21%20Specialized%20Agents-f97316?style=for-the-badge&logo=openai&logoColor=white)](01_KNOWLEDGE/Agents_Skill_Matrix.md)
[![Skills](https://img.shields.io/badge/Skills-251%20Local%20%7C%2050K%2B%20Global-6366f1?style=for-the-badge&logo=probot&logoColor=white)](01_KNOWLEDGE/Master_Skills_Catalog_251.md)
[![Foundation Books](https://img.shields.io/badge/Knowledge-10%20Tiers%20%7C%2060%20Drills-8b5cf6?style=for-the-badge&logo=gitbook&logoColor=white)](01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md)
[![Security Invariants](https://img.shields.io/badge/Security-P0--P18%20%7C%20I--001..I--012-ef4444?style=for-the-badge&logo=auth0&logoColor=white)](99_SYSTEM/Phase43_P0_Implementation_Contract.md)
[![Storage](https://img.shields.io/badge/Storage-SQLite%20WAL%20%2B%20Markdown-eab308?style=for-the-badge&logo=sqlite&logoColor=white)](memory_controller/)
[![Claude Code](https://img.shields.io/badge/Claude%20Code-Plugin%20Marketplace-a855f7?style=for-the-badge&logo=anthropic&logoColor=white)](.claude-plugin/)
[![Obsidian](https://img.shields.io/badge/Obsidian-Synced%20MOC%20Graph-7c3aed?style=for-the-badge&logo=obsidian&logoColor=white)](00_CORE/GRAPH/07%20Knowledge%20Domains%20Map.md)

<p align="center">
  <strong>Un spațiu cognitiv unificat, persistent și imun la amnezie pentru agenți AI autonomi.</strong><br>
  Memorie durabilă, bibliotecă de 251 skill-uri operaționale, sinteză enciclopedică pe 10 niveluri, proceduri verificate și telemetrie tranzacțională partajată între <code>Claude Code</code>, <code>Google Antigravity</code>, <code>OpenAI Codex</code>, asistenți vocali locali (<code>JARVIS</code>) și interfața <code>Obsidian</code>.
</p>

</div>

---

> [!IMPORTANT]
> **Codex Operating Contract**: [`AGENTS.md`](AGENTS.md) | **Invariante de Securitate**: [`vault_cognitive_rules.md`](.agents/rules/vault_cognitive_rules.md) | **API Memorie**: `memory_controller/`
>
> **AI Memory Vault** este stratul canonic de memorie, cunoștințe, abilități, agenți, proceduri, proveniență și audit pentru ecosistemul AI. Același Vault servește toți clienții AI fără a crea memorii paralele concurente și fără a re-trimite istoricul conversațional complet.

---

## 🎯 1. Viziunea & Principiul Fundamental

Proiectul nu este un simplu folder de notițe Markdown și nici o bază de date vectorială izolată.
Obiectivul pe termen lung este un **sistem de operare cognitiv** în care cunoștințele persistente sunt activate selectiv, utilizate eficient, verificate prin dovezi empirice și îmbunătățite continuu.

```text
┌─────────────────────────────────────────────────────────────────────────────────┐
│                              HUMAN / OPERATOR                                   │
└────────────────────────────────────────┬────────────────────────────────────────┘
                                         │
                                         ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│                              EXECUTIVE / CONTROL                                │
└────────────────────────────────────────┬────────────────────────────────────────┘
                                         │
             ┌───────────────────────────┼───────────────────────────┐
             ▼                           ▼                           ▼
        📚 MEMORY                   ⚡ SKILLS                    🤖 AGENTS
             │                           │                           │
             └───────────────────────────┼───────────────────────────┘
                                         ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│                     SELECTIVE RETRIEVAL & BOUNDED CONTEXT                       │
└────────────────────────────────────────┬────────────────────────────────────────┘
                                         ▼
                                   PLAN / ROUTE
                                         │
                  ┌──────────────────────┼──────────────────────┐
                  ▼                      ▼                      ▼
               DIRECT               SPECIALIST               COUNCIL
                  │                      │                      │
                  └──────────────────────┼──────────────────────┘
                                         ▼
                                       TOOLS
                                         │
                                         ▼
                                   VERIFICATION
                                         │
                                         ▼
                                      OUTCOME
                                         │
                                         ▼
                                      EVIDENCE
                                         │
                                         ▼
                                   CONSOLIDATION
                                         │
                                         ▼
                              BETTER FUTURE RETRIEVAL
```

<div align="center">

```text
╔═════════════════════════════════════════════════════════════════════════════════╗
║                            PRINCIPIUL FUNDAMENTAL                               ║
╠═════════════════════════════════════════════════════════════════════════════════╣
║     ONE VAULT              ONE CANONICAL MEMORY        MULTIPLE AI CLIENTS      ║
║     SPECIALIZED AGENTS     CONTROLLED RETRIEVAL        CONTROLLED WRITE         ║
║     FULL PROVENANCE        TRACEABLE AUDIT             MEASURED VERIFICATION    ║
╚═════════════════════════════════════════════════════════════════════════════════╝
```

</div>

---

## 📁 2. Structura Vault-ului (Obsidian)

| Folder | Rol & Conținut Canonic |
|:---|:---|
| `00_CORE` | Identitate, reguli, protocol operațional AI, model de încredere, arhitectură sistem (+ variante legacy Claude/Perplexity, `GRAPH/`) |
| `01_KNOWLEDGE` | Note atomice: stack tehnic, securitate, design/UI, arhitecturi (WPF, trading algoritmic, multi-agent), cataloage de skill-uri, sinteza celor 10 Tiers de Cărți Fundamentale (`01_KNOWLEDGE/BOOKS/`), referințe externe |
| `02_PROJECTS` | Proiecte active, stare de execuție și documente de continuitate |
| `03_PROCEDURES` | Proceduri operaționale repetabile și ghiduri pas-cu-pas |
| `04_MEMORY` | Memorie episodică: `Decisions/`, `Errors/`, `Experiences/`, `Lessons/`, `Preferences/` + sute de fișiere `policy-lesson_*.md` |
| `05_RESOURCES` | Materiale de referință, surse externe și navigație Obsidian |
| `06_INBOX` | `RAW_IMPORTS/` — inbox pentru conținut neprocesat și granița de izolare `RAW_EXTERNAL/skills/` |
| `90_TEMPLATES` | Șabloane frontmatter canonice |
| `99_SYSTEM` | Fișiere de sistem ale vault-ului, contracte de context, bugete tokeni și protocoale de sincronizare |
| `.obsidian` | Configurația Obsidian Vault, inclusiv `graph.json` cu maparea cromatică pe foldere |

**Fișiere root de control:** `AGENTS.md`, `CLAUDE.md`, `PROJECT.md`, `ORIGINAL_REQUEST.md`, `REVIEW_QUEUE.md`, `TEST_INFRA.md`, `TEST_READY.md`.

> [!NOTE]
> Multe fișiere din `01_KNOWLEDGE` și `04_MEMORY` au denumiri de forma `test_*`, `unknown_*` — sunt artefacte de test/import automat, păstrate în lifecycle `unverified` până la promovarea explicită.

---

## 🧠 3. `cognitive_core/` — Motorul Cognitiv

Peste 60 de module Python organizate modular pe responsabilități cognitive clare:

<details open>
<summary><b>🔍 Vezi harta detaliată a modulelor cognitive din <code>cognitive_core/</code></b></summary>

### 🏛️ Pipeline Council (B1–B5) — orchestrare multi-model cu audit de consum
- `council_model_execution.py` (B1) — execută modelele Council prin provideri (`model_provider.py`, `fake_model_provider.py`, `local_provider.py`, `openai_provider.py`) cu rutare pe tier (`model_tier_router.py`).
- `model_tier_router.py` — rutare deterministă pe tier-uri de model (`light` / `standard` / `heavy`).
- `council_usage_audit.py` (B2/B4) — telemetrie per-call (`actual_usage_telemetry.py`).
- `council_efficiency_report.py` (B5) — agregare istorică, verdict de eficiență, export CSV/Markdown/JSON.
- `council_budget_controller.py` — control buget tokeni la nivel de Council.

### 🧬 Memorie & Dinamică Cognitivă
- `working_memory.py`, `global_workspace.py`, `spreading_activation.py`, `activation.py`, `attention.py`
- `consolidation.py`, `sleep_consolidation.py`, `deduplication.py`, `conflict_detector.py`
- `recall.py`, `ranked_search.py`, `qdrant_retrieval.py`, `spatial_index.py`, `semantic.py`
- `reflection.py`, `reasoning.py`, `planning.py`, `plan_complexity_analyzer.py`, `learning.py`, `motivation.py`

### ⚡ Orchestrare & Execuție
- `orchestrator.py`, `executive.py`, `executive_model_execution_bridge.py`, `multi_graph.py`, `synapse.py`
- `dispatch_cli.py`, `recall_cli.py`, `memory_v6_cli.py`, `skill_router.py`, `tool_router.py`

### 🛡️ Securitate & Infrastructură
- `security_audit.py`, `git_hooks.py`, `sensor_buffer.py`, `evaluation.py`, `report_view.py`, `proposal_queue.py`, `queue_promoter.py`, `version.py`, `trading_decisions.py`
- Sub-pachete specializate: `agents/`, `benchmarks/`, `neuromorphic/`

### 🧪 Suita de Teste
- `cognitive_core/tests/` — peste 70 de suite de testare pytest acoperind fiecare componentă, plus teste end-to-end (`test_end_to_end_workflow.py`), teste adversariale (`test_milestone4_adversarial_challenger*.py`, `test_milestone5_*`) și de regresie (`test_council_efficiency_report_fix.py`).
- **Rezultat**: **`518 passed, 2 skipped (100% succes)`**.

</details>

---

## 🔬 4. Arhitectura Cognitivă Bio-Inspirată (Brain Upgrade — ACT-R & GWT)

Sistemul integrează în `cognitive_core/` 5 module inspirate direct din neuroștiințele cognitive pentru o autonomie superioară:

1. **ACT-R Activation Decay (`cognitive_core/activation.py`)**:
   Modelează dinamica memoriei declarative conform teoriei ACT-R (Anderson, 2004). Nivelul de activare bazală $B_i$ al unei note este calculat pe baza istoricului temporal al accesărilor:
   $$B_i = \ln\left( \sum_{k=1}^{n} t_k^{-d} \right)$$
   Memoriile nefolosite scad sub pragul `DORMANT_THRESHOLD` ($-2.0$), fiind excluse din căutările standard pentru a proteja bugetul de tokeni.
2. **Reconsolidarea Memoriei (`cognitive_core/consolidation.py`)**:
   Inspirat din teoria reconsolidării neurobiologice (Nader et al., 2000). Când o cunoștință canonică activă este contrazisă de dovezi noi de execuție, nota intră în starea tranzitorie `RECONSOLIDATING`. Versiunea anterioară este arhivată cu amprentă criptografică, permițând actualizarea fără pierderi ireversibile.
3. **Modulul Motivațional & Utilitate (`cognitive_core/motivation.py`)**:
   Calculează utilitatea acțiunilor și strategiilor conform ecuației:
   $$U = P \cdot G - C$$
   Unde $P$ este probabilitatea de succes estimată, $G$ valoarea recompensei de la `VerifierAgent`, iar $C$ costul asociat (timp, consum tokeni). Ponderile sunt actualizate prin Exponential Moving Average (EMA).
4. **Global Workspace Theory (`cognitive_core/global_workspace.py`)**:
   Implementează modelul Global Workspace (Baars, 1988; Dehaene et al., 2001). Subagenții consiliului (`Router`, `Retrieval`, `Verifier`, `Critic`) trimit propuneri candidate; candidatul cu utilitatea maximă este selectat și difuzat (*broadcasted*) către toți agenții activi ca focus atențional comun.
5. **Rădăcini Neuromorfe SNN (`cognitive_core/neuromorphic/`)**:
   Punte experimentală curată între reprezentarea simbolică și dinamica neuronală cu impulsuri:
   - *Neuron LIF (`lif_neuron.py`)*: $\tau_m \frac{dV}{dt} = -(V - V_{reset}) + I_{syn}$, prag $V_{th}$ și perioadă refractară.
   - *Plasticitate STDP (`stdp_synapse.py`)*: Spike-Timing-Dependent Plasticity pentru învățarea asocierilor temporale.
   - *Rețea Spiking (`snn_network.py`)*: asociere între concepte din graf și trenuri de impulsuri neuronale.

---

## 🚀 5. Extensia de Arhitectură Memory V6 (v6.0.0 Engine)

Extensia aditivă **Memory V6** adaugă 17 componente industriale pentru ciclul complet de viață al memoriilor:

| Nr. | Componentă V6 | Modul Python | Responsabilitate & Comportament Tehnic |
|:---:|:---|:---|:---|
| **1** | **Ephemeral Sensor Buffer** | `cognitive_core/sensor_buffer.py` | Colectează evenimentele brute de interacțiune în memorie RAM fără a polua stocarea durabilă. |
| **2** | **Atomic Memory Extractor** | `cognitive_core/extraction.py` | Extrage fapte, decizii, proceduri și lecții folosind euristici deterministe și regex structurat. |
| **3** | **Ollama Local Extractor** | `cognitive_core/ollama_extractor.py` | Adaptor opțional pentru extragere semantică augmentată prin modele locale (ex. `qwen2.5-coder`). |
| **4** | **Proposal Queue** | `cognitive_core/proposal_queue.py` | Coadă de triaj izolată în `06_INBOX/` cu stări controlate (`PENDING`, `APPROVED`, `REJECTED`). |
| **5** | **Conflict Detector** | `cognitive_core/conflict_detector.py` | Detectează contradicțiile de fapte și negațiile logice înainte de promovarea în memoria canonică. |
| **6** | **Controlled Queue Promoter** | `cognitive_core/queue_promoter.py` | Permite promovarea în `MemoryController.propose()` exclusiv pentru candidații aprobați de om. |
| **7** | **Multi-Graph Memory** | `cognitive_core/multi_graph.py` | 4 grafuri derivate: graf semantic, temporal, cauzal (decizie -> efect) și graf de entități. |
| **8** | **Spreading Activation** | `cognitive_core/spreading_activation.py` | Difuzie de activare pe graful relațional limitată strict la orizontul de $\le 2$ hop-uri. |
| **9** | **Hybrid Ranked Search** | `cognitive_core/ranked_search.py` | Re-clasare hibridă combinând potrivirea lexicală, proximitatea semantică și activarea ACT-R. |
| **10** | **Sleep-Phase Consolidation** | `cognitive_core/sleep_consolidation.py` | Mentenanță nocturnă: arhivare automată, degradare de activare și curățare referințe. |
| **11** | **Obsidian Report View** | `cognitive_core/report_view.py` | Generează automat rapoarte sintetice Markdown cu legături bidirecționale `[[wikilinks]]`. |
| **12** | **LoCoMo Benchmark Harness** | `cognitive_core/benchmarks/` | Suite de evaluare a performanței de recuperare: calculează Precision@K, Recall@K și MRR. |
| **13** | **Memory V6 CLI** | `cognitive_core/memory_v6_cli.py` | Interfață de linie de comandă unificată pentru operare, triaj, aprobare și consolidare. |
| **14** | **Memory Event Bus** | `cognitive_core/event_bus.py` | Magistrală de evenimente asincronă pentru decuplarea componentelor cognitive. |
| **15** | **Context Pack Builder** | `cognitive_core/context_pack_builder.py` | Asamblează pachete minimale de context respectând bugetul strict stabilit prin protocol. |
| **16** | **Token Telemetry Auditor** | `cognitive_core/actual_usage_telemetry.py` | Măsoară consumul real de tokeni per apel și per agent (B1–B5 efficiency framework). |
| **17** | **Automated CI/CD Workflows** | `.github/workflows/` | Pipeline-uri automate: `memory-v6-tests.yml` (validare pytest) și `memory-consolidation.yml`. |

### 💻 Runbook CLI Memory V6
```powershell
# 1. Extrage fapte, decizii sau lecții și trimite-le în coada de triaj
python -m cognitive_core.memory_v6_cli extract --text "Am decis: folosim SQLite WAL mode cu busy_timeout 5000ms." --enqueue

# 2. Inspectează coada de propuneri și analizează conflictele detectate automat
python -m cognitive_core.memory_v6_cli review --show-conflicts

# 3. Aprobă un candidat în mod explicit prin intervenție umană
python -m cognitive_core.memory_v6_cli approve <candidate_id> --reviewer human

# 4. Promovează candidații aprobați direct în nucleul canonic (respectând invariantul I-003)
python -m cognitive_core.memory_v6_cli promote-approved --principal ai_agent

# 5. Rulează faza de mentenanță (Sleep Consolidation) și randează raportul de sinteză pentru Obsidian
python -m cognitive_core.memory_v6_cli consolidate --render

# 6. Măsoară precizia de recuperare prin benchmark-ul LoCoMo
python -m cognitive_core.memory_v6_cli benchmark --retrieval graph
```

---

## 🛡️ 6. Stratul de Memorie Canonică (`memory_controller/`) & Invariante

`MemoryController` este punctul unic de acces autorizat pentru citirea și scrierea memoriei canonice, garantând integritatea tranzacțională și izolarea securizată:

- **Autorizare Scoped**: verifică rolul principalului (`HUMAN`, `AI_AGENT`, `ADMIN`) pentru fiecare operațiune (`propose`, `read`, `search`, `review`, `promote`, `archive`, `update`, `supersede`, `attest`).
- **Proveniență Imutabilă (`I-002`, `I-005`)**: `source_type` (`user`, `official`, `ai`, `inference`, `execution`, `import`) nu poate fi modificat după creare, iar agenții AI nu pot emite proveniențe umane.
- **Ciclu de Viață Controlat (`I-003`)**: `RAW -> CLASSIFIED -> NORMALIZED -> REVIEW -> VERIFIED -> ACTIVE -> {SUPERSEDED, ARCHIVED}`. Promovarea în `ACTIVE` necesită atestare umană.
- **Atestare Umană Dedicată (`I-001`, `I-004`)**: `verification = "verified"` poate fi acordat exclusiv prin metoda `attest()`, restricționată la `HUMAN` și `ADMIN`.
- **Audit Criptografic SHA-256**: fiecare acțiune este salvată în `audit_log.jsonl` cu lanț de hash-uri tamper-evident.
- **Supersedare Atomică**: înlocuire atomică a cunoștințelor vechi, protejând datele atestate de om de suprascrieri automate AI.
- **Persistență SQLite WAL**: `PRAGMA busy_timeout=5000` și tranzacții atomice `BEGIN IMMEDIATE`, cu checkpointing atomic via `os.replace`.
- **Regăsire Unificată Securizată (`I-RETRIEVAL`)**: interogările de memorie trec exclusiv prin API sau prin `python -m cognitive_core.recall_cli --query ...`. Scanările directe brute (`os.walk`) sunt **strict interzise**.

---

## 🏛️ 7. Consiliul celor 21 de Agenți Specializați

Consiliul operează conform principiului *Least Privilege*, fiecare agent având un profil strict definit în `.agents/agents/` și o mapare în `01_KNOWLEDGE/Agents_Skill_Matrix.md`:

<details open>
<summary><b>👥 Tabelul celor 21 de Agenți Specializați</b></summary>

| Nr. | Agent | Domeniu & Rol Primar | Acces Stocare | Skill-uri Principale Alocate |
|:---:|:---|:---|:---:|:---|
| **1** | `agentic_workflow_orchestrator` | Orchestrator de fluxuri de lucru multi-agent și pipeline-uri gh-aw | Read, Propose | `agent-squad`, `agentic-workflows`, `workflow-automation` |
| **2** | `backend_systems_engineer` | Arhitectură API backend, microservicii, worker pools și tranzacții | Read, Propose | `fastapi-pro`, `csharp-pro`, `golang-pro`, `nodejs-backend-patterns` |
| **3** | `compiler_and_tooling_engineer` | Compilatoare, AST, optimizări runtime, lintere și CLI tooling | Read, Propose | `rust-pro`, `cpp-pro`, `cli-mastery`, `python-packaging` |
| **4** | `content_strategist` | Arhitectură informațională, documentație tehnică și comunicare | Read, Propose | `brain-to-docs`, `technical-tutorials`, `docs-architect` |
| **5** | `database_and_persistence_engineer`| SQLite WAL, migrații, indexare, caching Redis și sharding | Read, Propose | `sqlite-wal-optimization`, `postgresql`, `redis-cli`, `ef-core` |
| **6** | `frontend_saas_engineer` | Aplicații web SaaS moderne, Next.js, stări optimiste și performanță | Read, Propose | `nextjs-best-practices`, `typescript-pro`, `tanstack-query-expert` |
| **7** | `game_engineer` | Sisteme de joc 2D/3D, bucle de randare, fizică și arhitectură ECS | Read, Propose | `3d-games`, `threejs`, `godot-gdscript-patterns`, `shader-programming-glsl` |
| **8** | `local_ai_engineer` | Integrare LLM locale, Ollama, vLLM, ONNX, Whisper și cuantizare | Read, Propose | `local-llm-expert`, `vllm-inference-optimization`, `pydantic-ai` |
| **9** | `memory_controller_architect` | Păstrătorul nucleului canonic de memorie, invariante și audit | Read, Write, Attest | `vault-operations`, `memory-safety-patterns`, `context-management` |
| **10** | `polyglot_systems_architect` | Sisteme eterogene, interoperabilitate polyglotă C#/Python/Rust/Go | Read, Propose | `clean-architecture-backend`, `microservices-patterns`, `ddd-tactical` |
| **11** | `quant_developer` | Analiză cantitativă, modele de risc, semnale și backtesting | Read, Propose | `python-trading-systems`, `risk-metrics-calculation`, `backtesting` |
| **12** | `secops_auditor` | Audit de securitate, detectare vulnerabilități și conformitate OWASP | Read, Audit | `security-invariants`, `owasp-top-10-audit`, `pki-certificate-mgmt` |
| **13** | `site_reliability_and_devops_architect` | Fiabilitate, infrastructură ca cod, containere și CI/CD | Read, Propose | `docker-expert`, `kubernetes-orchestration`, `prometheus-grafana` |
| **14** | `system_architecture_agent` | Design de ansamblu, modele C4, diagrame de domeniu și decizii ADR | Read, Propose | `senior-architect`, `c4-container`, `architecture-decision-records` |
| **15** | `threat_hunting_analyst` | Detecție intruziuni, forensic memory analysis și threat modeling | Read, Audit | `threat-hunting`, `digital-forensics`, `malware-analysis` |
| **16** | `ui_sensei_architect` | Filosofia sistemelor de design UI, coeziune vizuală și tokeni semantici | Read, Propose | `ui-skills`, `design-system-foundation`, `radix-ui-design-system` |
| **17** | `ui_ux_designer` | Ergonomie interfețe, accesibilitate WCAG, wireframes și UX flow | Read, Propose | `ux-flow`, `ui-a11y`, `ux-audit`, `tailwind-design-system` |
| **18** | `web_creative_developer` | Efecte WebGL avansate, shadere Three.js, animații Lenis și GSAP | Read, Propose | `webgl-laser`, `webgl-landing-steering`, `gsap-scrolltrigger` |
| **19** | `web_design_engineer_agent` | Punte între ingineria de cod și designul grafic internațional | Read, Propose | `garden-web-design-engineer`, `xiaopu-web-design`, `frontend-design` |
| **20** | `web_quality_engineer` | Core Web Vitals, audit Lighthouse, randare optimă și profiling DOM | Read, Propose | `web-quality-audit`, `core-web-vitals`, `pagespeed-enhancer` |
| **21** | `wpf_engineer` | Aplicații desktop Windows, XAML avansat, MVVM Toolkit și C# | Read, Propose | `csharp-wpf-desktop`, `mvvm-toolkit`, `dotnet-best-practices` |

</details>

---

## 📦 8. Corpusul de Skills (251 Locale & Index Global)

Sistemul menține o ierarhie strictă între abilitățile active și materialele externe neprocesate:

- **Biblioteca de Skill-uri Operaționale (`.agents/skills/`)**: 251 de abilități verificate, catalogate în [`01_KNOWLEDGE/Master_Skills_Catalog_251.md`](01_KNOWLEDGE/Master_Skills_Catalog_251.md).
- **Skill-ul UI/UX Pro Max (`.agents/skills/ui-ux-pro-max/SKILL.md`)**: Abilitate integrată ce oferă inteligență de design modern, ghidare pe tokeni semantici și reguli ergonomice complementare skill-urilor specifice (`ui-ux-review`, `web-quality-audit`).
- **Granița RAW_EXTERNAL (`06_INBOX/RAW_IMPORTS/skills/`)**: Pipeline de securitate în 12 faze (Crawl -> Deduplicare SHA-256 -> Clasificare -> Normalizare YAML -> Triaj în Inbox -> Aprobare Umană -> Promovare).
- **Încărcare Progresivă (Progressive Disclosure)**:
  `Metadate abilitate` $\to$ `Reguli specifice` $\to$ `Referințe detaliate` $\to$ `Ghidare per stack tehnic`.

---

## 🧰 9. Model Providers, Derivare Bugete & Economie de Execuție

### Decuplare Provideri & Rutare Deterministă
```text
                       ModelTierRouter
                              │
               ┌──────────────┼──────────────┐
               ▼              ▼              ▼
             Fake           Local          OpenAI
            provider        Ollama        Responses
               │              │              │
               └──────────────┼──────────────┘
                              ▼
                    ModelResponse + Usage
                              │
                              ▼
                     ActualUsageTelemetry
```

### Derivarea Deterministă a Bugetului de Council
Deciziile de activare ale consiliului sunt derivate strict din planul de execuție real:
```text
ActivePlan.steps ──► PlanComplexityAnalyzer ──► ExecutionMode ──► CouncilBudgetController ──► CouncilTier ──► SubagentSpec.model_tier
```
- Sarcinile simple evită activarea inutilă a consiliului;
- Sarcinile moderate rulează pe tier-ul `light`;
- Schimbările distructive escaladează pe tier-ul `heavy` cu audit de securitate;
- Orice re-planificare (`replan`) recalculează automat rutele de model și bugetul.

---

## 📚 10. Sinteza Cărților Fundamentale (Tiers 1–10) & Caietul cu 60 de Teme Practice

Sistemul integrează 61 de note de cunoștințe durabile în `01_KNOWLEDGE/BOOKS/`, validate JSON Schema Draft-07, conectate direct la un **Caiet de Teme și Aplicații Practice** conținând 60 de exerciții aplicate complete:

<details open>
<summary><b>📖 Vezi corelarea celor 10 Tiers cu cele 60 de Teme de Laborator</b></summary>

| Tier | Domeniu & Cărți de Bază | Concepte Fundamentale Tratate | Teme Practice Alocate |
|:---:|:---|:---|:---:|
| **Tier 1** | **Arhitectură & Baze de Date Distribuite**<br>*(DDIA — Martin Kleppmann)* | Tranzacții ACID, izolare SSI, moduri de stocare (LSM vs B-Tree), SQLite WAL, consens Raft. | **Teme 1 – 6** |
| **Tier 2** | **Inteligență Artificială Modernă**<br>*(AIMA 4e — Russell & Norvig)* | Agenți raționali, planificare clasică PDDL, căutare euristică A*, decizii markoviene (MDP). | **Teme 7 – 12** |
| **Tier 3** | **Arhitectura Sistemelor de Agenți AI**<br>*(Agentic Architectures — Zvarydchuk, Weng)* | Buclă OODA (Observe-Orient-Decide-Act), ReAct, Reflexion, delegare de roluri cu privilegii minime. | **Teme 13 – 18** |
| **Tier 4** | **Ingineria Aplicațiilor cu LLM & RAG**<br>*(Building LLM Apps — Chip Huyen, Briggs)* | RAG ierarhic, chunking semantic, vector stores hibride, GraphRAG, reranking Cross-Encoder. | **Teme 19 – 24** |
| **Tier 5** | **Sisteme de Machine Learning în Producție**<br>*(Designing ML Systems — Chip Huyen)* | Data drift, concept drift, feature stores, monitorare continuă a performanței. | **Teme 25 – 30** |
| **Tier 6** | **Deep Learning & Rețele Neuronale**<br>*(Deep Learning — Goodfellow et al.)* | Backpropagation, regularizare, Transformer self-attention, optimizatori adaptivi (AdamW). | **Teme 31 – 36** |
| **Tier 7** | **Sisteme de Memorie Avansată & Prompt Eng.**<br>*(Memory Systems — Pai, Liu)* | Memorie episodică vs semantică, benchmark-uri LoCoMo/LongMemEval, demarcare XML. | **Teme 37 – 42** |
| **Tier 8** | **Securitate Ofensivă/Defensivă & Hardening**<br>*(OWASP LLM Top 10, NIST AI RMF)* | Indirect prompt injection, jailbreak defenses, audit SHA-256, invariante I-001..I-012. | **Teme 43 – 48** |
| **Tier 9** | **Scalare, Concurrency & Performanță**<br>*(High-Performance Systems — C#, Rust, Go)* | Tranzacții SQLite concurente (`BEGIN IMMEDIATE`), asincronism Go/Rust Tokio, pooling. | **Teme 49 – 54** |
| **Tier 10** | **Frontiera Cercetării Cognitive & Neuro-AI**<br>*(Neuromorphic, GWT, Active Inference)* | Neuroni LIF, sinapse plastice STDP, Global Workspace Theory, inferență activă Karl Friston. | **Teme 55 – 60** |

- **Caietul de Teme Practice (4.696 linii de cod și aserțiuni)**: [`.agents/skills/learn/references/caiet_de_teme_si_aplicatii_practice.md`](.agents/skills/learn/references/caiet_de_teme_si_aplicatii_practice.md)
- **Podul de Sinteză a Cărților**: [`01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md`](01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md)

</details>

---

## 🗺️ 11. Planul de Dezvoltare & Direcția de Evoluție Cognitivă

Direcția strategică de cercetare și evoluție vizează **îmbunătățirea controlată prin dovezi empirice**, refuzând modificările autonome nesupravegheate:

```text
┌─────────────────────────────────────────────────────────────────────────────────┐
│                                   REAL TASK                                     │
└────────────────────────────────────────┬────────────────────────────────────────┘
                                         │
                                         ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│                                EXECUTION TRACE                                  │
└────────────────────────────────────────┬────────────────────────────────────────┘
                                         │
                                         ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│                               OUTCOME + EVIDENCE                                │
└────────────────────────────────────────┬────────────────────────────────────────┘
                                         │
                                         ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│                                   EVALUATION                                    │
└────────────────────────────────────────┬────────────────────────────────────────┘
                                         │
                                         ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│                     PATTERN / FAILURE / PROCEDURE DISCOVERY                     │
└────────────────────────────────────────┬────────────────────────────────────────┘
                                         │
                                         ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│                              CAPABILITY CANDIDATE                               │
└────────────────────────────────────────┬────────────────────────────────────────┘
                                         │
                                         ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│                                SANDBOX / SHADOW                                 │
└────────────────────────────────────────┬────────────────────────────────────────┘
                                         │
                                         ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│                              REGRESSION + HOLDOUT                               │
└────────────────────────────────────────┬────────────────────────────────────────┘
                                         │
                                         ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│                             HUMAN-GATED PROMOTION                               │
└─────────────────────────────────────────────────────────────────────────────────┘
```

### Capabilități în Curs de Dezvoltare (Milestones Roadmap)
1. **Descoperire de Capabilități din Experiență**: extragerea automată a tiparelor de succes din urmele de execuție.
2. **Minarea Erorilor și Corecțiilor**: transformarea automată a eșecurilor în reguli preventive de tip poka-yoke.
3. **Rafinarea & Versionarea Skill-urilor**: îmbunătățirea incrementală a instrucțiunilor din `SKILL.md`.
4. **Optimizarea Politicii de Recuperare**: acordaj fin al parametrilor de spreading activation pe baza utilității observate.
5. **Urmărirea Stării Cognitive & Incertitudinii**: măsurarea clarității contextului înainte de execuție.
6. **Rutare Adaptivă de Model**: selecția dinamică a providerului în funcție de complexitatea semantică.
7. **Consolidare Hibridă Memorie/Abilități**: sleep-phase clustering între lecții episodice și proceduri active.
8. **Retragere & Rollback Controlat**: arhivarea automată a abilităților devenite redundante.

---

## 🧭 12. Cele 10 Principii de Design ale Sistemului

1. **Unică Sursă Canonică de Adevăr**: Fără duplicate sau instanțe concurente ale aceleiași decizii.
2. **Dovezi Reale în Locul Încrederii Oarbe**: Încrederea este doar un metadata; validarea vine din teste și execuție reală.
3. **Recuperare Parsimonioasă Înainte de Umflarea Contextului**: Contextul mai mare nu este automat context mai bun.
4. **Alocare Deterministă de Resurse**: Riscul, complexitatea și consumul de tokeni trebuie să fie predictibile și reproductibile.
5. **Promovare Durabilă Gated Uman**: Nicio informație externă nu devine memorie canonică verificată fără aprobare umană.
6. **Proveniența Supraviețuiește Ingestiei**: Orice sursă își păstrează autorul, hash-ul și adresa de origine.
7. **Compoziție de Skill-uri fără Coliziuni**: Abilitățile generale ghidează; skill-urile specializate execută pașii exacți.
8. **Eșecul este Dată Științifică**: Erorile clasificate sunt cele mai valoroase semnale de învățare.
9. **Măsurare Înainte de Automatizare**: Orice mecanism trebuie să poată fi observat și auditat înainte de a deveni autonom.
10. **Câștigul de Capabilitate Bate Numărul de Componente**: O arhitectură mai mare nu este automat o arhitectură mai inteligentă.

---

## 🛠️ 13. `scripts/` & Componente Root Satelit

- **`generate_b4_baseline.py`**: generator baseline B4 — rulează $N$ execuții Council prin `FakeModelProvider`, produce rapoarte per-run în `reports/b4/`, și le agregă în B5 (`b5_report.json`, CSV-uri, `B5_TOKEN_EFFICIENCY_REPORT.md`).
- **`skill_ingestion.py`**: utilitar pentru ingestia și clasificarea skill-urilor externe.
- **`label_council_outcome.py`**: marcare empirică a rezultatelor de execuție ale consiliului.
- **`projects/jarvis_cognitive_brain/`**: asistent vocal autonom OODA (VAD Silero, Faster-Whisper, Kokoro-82M TTS, FastMCP Home Assistant, HUD 3D).
- **`projects/jarvis_web/`**: interfață vocală web standalone WebGL Three.js & Web Audio SFX.
- **Alte componente**: `vault_api.py`, `memory_controller/`, `config/`, `agents/`, `commands/`, `skills/`, proiectele desktop `xau_kinetic/`, `audit_log.jsonl` și `test_audit_log.jsonl`.

---

## 🕒 14. Istoric Recent (Schimbări Majore) & Note de Mentenanță

- **`6e256a9`** — `feat(B4)`: adăugat generator baseline B4 (`generate_b4_baseline.py`) + teste de regresie, validat prin CLI și programatic (3 rapoarte JSON, `b5_report.json` cu `run_count == 3`, secțiunea "Council efficiency verdict").
- **`0f4e0f3`** — `fix(B5)`: corectat bug în `council_efficiency_report.py` unde `top_optimization_candidate` combina independent agentul și tier-ul, producând perechi nerealiste. Fix-ul grupează consumul pe perechea reală `(agent_id, model_tier)`. Adăugate 10 teste de regresie (`test_council_efficiency_report_fix.py`).
- **`cbf2c1569`** — `docs(readme)`: restaurat master README canonic în limba română, integrat structura utilizatorului, sinteza Tiers 1–10 cărți fundamentale, 60 teme de laborator, 21 agenți, 251 skills și verificat suitele de securitate P0–P18.

### Note de Mentenanță
- Fișierele `council_efficiency_report.py` și restul pipeline-ului B1–B5 sunt componente de referință stabile — orice modificare necesită confirmare explicită.
- `01_KNOWLEDGE` și `04_MEMORY` conțin fișiere placeholder (`test_*`, `unknown_*`) rezultate din importuri automate — candidate pentru revizuire lifecycle (`unverified` $\to$ `verified`).
- Fișierele `*_Claude_Legacy.md` și `*_Perplexity_Legacy.md` sunt versiuni istorice păstrate pentru compatibilitate retrospectivă.

---

## 📌 15. Starea Curentă a Capabilităților

| Capabilitate Sistem | Stare Operațională | Note de Implementare |
|:---|:---:|:---|
| **Vault Canonic Persistent** | 🟢 Activ | Structură pe 10 directoare, frontmatter YAML validat Draft-07 |
| **Nucleu Cognitiv Memory V6** | 🟢 Activ | 17 componente industriale, ACT-R decay, GWT, multi-graph |
| **Consiliu Multi-Agent (21 Agenți)** | 🟢 Activ | Roluri cu privilegii minime, rutare pe capabilități |
| **Rutare Deterministă Bugete Tokeni** | 🟢 Activ | Analiză complexitate plan, tier-uri light/standard/heavy |
| **Provideri Model (Fake / Local / OpenAI)** | 🟢 Implementat | Decuplare prin `ModelTierRouter`, suport complet Ollama |
| **Telemetrie Consum Real & Raportare B4/B5** | 🟢 Implementat | Măsurare per-call, export CSV/Markdown/JSON |
| **Biblioteca de 251 Skill-uri Locale** | 🟢 Activ | Toate fișierele `SKILL.md` verificate |
| **Sinteză Cărți Fundamentale (Tiers 1–10)** | 🟢 Activ | 61 note canonice în `01_KNOWLEDGE/BOOKS/` |
| **Caiet de Teme Practice (60 Teme)** | 🟢 Activ | 4.696 linii de cod și aserțiuni în `learn/references/` |
| **Invariante de Securitate P0–P18 & I-001..I-012** | 🟢 Verificat | 518 teste pytest trecute cu succes (0 eșecuri) |
| **Stocare SQLite WAL & Audit SHA-256** | 🟢 Activ | `PRAGMA busy_timeout=5000`, lanț criptografic neîntrerupt |
| **Plugin Claude Code & MOC Obsidian** | 🟢 Sincronizat | Comenzi `/memory`, `/memory-sync`, graf Obsidian integrat |
| **Evoluție Autonomă Continuă a Skill-urilor** | 🟡 În Cercetare | Faza următoare conform planului de dezvoltare |

---

## ⚡ 16. Ghid de Pornire Rapidă & Testare

```powershell
# 1. Clonare repository
git clone https://github.com/userist123/AI_Memory_Vault_CODEX_READY.git
cd AI_Memory_Vault_CODEX_READY

# 2. Rularea suitei complete de 518 teste cognitive & de securitate
python -m pytest cognitive_core/tests/ -q
# Rezultat așteptat: 518 passed, 2 skipped in ~16s

# 3. Verificarea integrității lanțului criptografic de audit SHA-256
python -c "from memory_controller.audit.logger import AuditLogger; logger = AuditLogger(); print('Audit chain valid:', logger.verify_integrity())"
# Rezultat așteptat: Audit chain valid: True

# 4. Interogare rapidă securizată (Offline Fallback I-RETRIEVAL)
python -m cognitive_core.recall_cli --query "SQLite WAL"
```

---

## 🔗 17. Legături de Navigare Canonică & Graf Obsidian

- [Arhitectura Memory V6](99_SYSTEM/Memory_V6_Architecture.md)
- [Matricea Agenți ↔ Skill-uri](01_KNOWLEDGE/Agents_Skill_Matrix.md)
- [Catalogul Master de 251 Skill-uri](01_KNOWLEDGE/Master_Skills_Catalog_251.md)
- [Caietul de Teme și Aplicații Practice (60 Teme)](.agents/skills/learn/references/caiet_de_teme_si_aplicatii_practice.md)
- [Sinteza Cărților Fundamentale (Tiers 1–10)](01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md)
- [Harta Domeniilor de Cunoștințe MOC](00_CORE/GRAPH/07%20Knowledge%20Domains%20Map.md)
- [Standardele Frontend UI/UX](01_KNOWLEDGE/MOC_Frontend_UI_UX_Standards.md)
- [Filosofia de Design UI Sensei](01_KNOWLEDGE/UI_Sensei_Design_Philosophy.md)
- [Abilitatea UI/UX Pro Max](.agents/skills/ui-ux-pro-max/SKILL.md)
- [Sincronizarea Skill/Agent/Memory Obsidian](99_SYSTEM/Obsidian_Skill_Agent_Memory_Sync.md)
- [Granița de Ingestie Externă](06_INBOX/RAW_IMPORTS/skills/)

### 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[07 Knowledge Domains Map]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]

---

<div align="center">
  <sub><b>AI_Memory_Vault_CODEX_READY</b> — persistent memory, selective cognition, controlled capabilities.</sub>
</div>
