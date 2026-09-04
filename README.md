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
  - "01_KNOWLEDGE/Agents_Skill_Matrix.md"
  - "01_KNOWLEDGE/Master_Skills_Catalog_251.md"
  - "99_SYSTEM/Memory_V6_Architecture.md"
  - "99_SYSTEM/Obsidian_Skill_Agent_Memory_Sync.md"
  - "05_RESOURCES/Obsidian/Skill_Agent_Memory_MOC.md"
  - ".claude-plugin/plugin.json"
  - ".claude-plugin/marketplace.json"
  - "skills/ai-memory-vault/SKILL.md"
---
# AI Memory Vault (CODEX-Ready)

# 🧠 AI Memory Vault — Canonical Multi-Agent Memory & Memory V6 Engine
Vault Obsidian de memorie persistentă pentru agenți AI, cuplat cu un motor cognitiv Python (`cognitive_core/`) care implementează un pipeline multi-agent (Council) cu memorie episodică/semantică, planificare, reflecție și audit de consum de tokeni.

[![Architecture](https://img.shields.io/badge/Architecture-v6.0.0--ACTIVE-blue.svg)](https://github.com/userist123/AI_Memory_Vault_CODEX_READY)
[![Tests](https://img.shields.io/badge/Pytest-496%20Passed-brightgreen.svg)](https://github.com/userist123/AI_Memory_Vault_CODEX_READY/actions)
[![Claude Plugin](https://img.shields.io/badge/Claude-Code%20Plugin-purple.svg)](https://github.com/userist123/AI_Memory_Vault_CODEX_READY/tree/main/.claude-plugin)
[![Agents](https://img.shields.io/badge/Agents-21-orange.svg)](https://github.com/userist123/AI_Memory_Vault_CODEX_READY/tree/main/.agents/agents)
[![Skills](https://img.shields.io/badge/Local%20Skills-251-brightgreen.svg)](https://github.com/userist123/AI_Memory_Vault_CODEX_READY/tree/main/.agents/skills)
[![Obsidian](https://img.shields.io/badge/Obsidian-Synchronized-7c3aed.svg)](https://github.com/userist123/AI_Memory_Vault_CODEX_READY/tree/main/.obsidian)
## Structura vault-ului (Obsidian)

> **AI Memory Vault** este stratul canonic de memorie, knowledge, skills, agents, procedures, provenance si audit pentru ecosistemul AI. Acelasi Vault poate fi folosit de **Codex, Claude Code, agentii locali si Obsidian**, fara memorii canonice paralele.
| Folder | Conținut |
|---|---|
| `00_CORE` | Identitate, reguli, protocol operațional AI, model de confidence, arhitectură sistem (+ variante legacy Claude/Perplexity, `GRAPH/`) |
| `01_KNOWLEDGE` | ~60 note atomice: stack tehnic, securitate, design/UI, arhitecturi (WPF, trading algoritmic, multi-agent), skill catalogs, referințe externe (`EXTERNAL_SKILLS/`) |
| `02_PROJECTS` | Proiecte active |
| `03_PROCEDURES` | Proceduri operaționale |
| `04_MEMORY` | Memorie episodică: `Decisions/`, `Errors/`, `Experiences/`, `Lessons/`, `Preferences/` + sute de fișiere `policy-lesson_*.md` generate automat |
| `05_RESOURCES` | Resurse |
| `06_INBOX` | `RAW_IMPORTS` — inbox pentru conținut neprocesat |
| `90_TEMPLATES` | Șabloane frontmatter |
| `99_SYSTEM` | Fișiere de sistem ale vault-ului |
| `.obsidian` | Config Obsidian |

---
Fișiere root: `AGENTS.md`, `CLAUDE.md`, `PROJECT.md`, `ORIGINAL_REQUEST.md`, `REVIEW_QUEUE.md`, `TEST_INFRA.md`, `TEST_READY.md`.

## 🎯 Ce este
> Notă: multe fișiere din `01_KNOWLEDGE` și `04_MEMORY` au denumiri `test_*`, `unknown_*` — sunt artefacte de test/import automat, nerevizuite încă (lifecycle `unverified`).

AI Memory Vault este o infrastructura persistenta pentru agenti AI care trebuie sa poata:
## `cognitive_core/` — motorul cognitiv

- recupera doar cunostintele relevante pentru o sarcina;
- folosi skills si agenti specializati;
- pastra provenance si nivelul de incredere;
- separa continutul extern RAW de memoria canonica;
- sincroniza knowledge, skills, agents, procedures si Obsidian;
- mentine continuitatea intre sesiuni si clienti AI;
- pastra auditabilitatea operatiunilor de memorie;
- evolua prin ingestie controlata de surse externe.
Peste 60 de module Python organizate pe funcții cognitive:

Principiul fundamental:
**Pipeline Council (B1–B5)** — orchestrare multi-model cu audit de consum:
- `council_model_execution.py` (B1) — execută modelele Council prin provideri (`model_provider.py`, `fake_model_provider.py`, `local_provider.py`, `openai_provider.py`)
- `model_tier_router.py` — routare pe tier-uri de model (light/standard/heavy)
- `council_usage_audit.py` (B2/B4) — telemetrie per-call (`actual_usage_telemetry.py`)
- `council_efficiency_report.py` (B5) — agregare istorică, verdict de eficiență, export CSV/Markdown/JSON
- `council_budget_controller.py` — control buget tokeni

```text
ONE VAULT
ONE CANONICAL MEMORY
MULTIPLE AI CLIENTS
MULTIPLE SPECIALIZED AGENTS
CONTROLLED RETRIEVAL
CONTROLLED WRITE
FULL PROVENANCE
```
**Memorie și cogniție:**
- `working_memory.py`, `global_workspace.py`, `spreading_activation.py`, `activation.py`, `attention.py`
- `consolidation.py`, `sleep_consolidation.py`, `deduplication.py`, `conflict_detector.py`
- `recall.py`, `ranked_search.py`, `qdrant_retrieval.py`, `spatial_index.py`, `semantic.py`
- `reflection.py`, `reasoning.py`, `planning.py`, `plan_complexity_analyzer.py`, `learning.py`, `motivation.py`

---
**Orchestrare și execuție:**
- `orchestrator.py`, `executive.py`, `executive_model_execution_bridge.py`, `multi_graph.py`, `synapse.py`
- `dispatch_cli.py`, `recall_cli.py`, `memory_v6_cli.py`, `skill_router.py`, `tool_router.py`

# 🧩 Arhitectura
**Securitate și infrastructură:**
- `security_audit.py`, `git_hooks.py`, `sensor_buffer.py`, `evaluation.py`, `report_view.py`, `proposal_queue.py`, `queue_promoter.py`, `version.py`, `trading_decisions.py`
- `agents/`, `benchmarks/`, `neuromorphic/`

```text
                     HUMAN / USER
                           │
          ┌────────────────┼────────────────┐
          ▼                ▼                ▼
        Codex          Claude Code      Local Agents
          │                │                │
          └────────────────┼────────────────┘
                           ▼
                  ┌─────────────────┐
                  │ AI MEMORY VAULT │
                  │ CANONICAL LAYER │
                  └────────┬────────┘
                           │
        ┌──────────────────┼──────────────────┐
        ▼                  ▼                  ▼
     Memory          Skills / Agents     Knowledge / Procedures
        │                  │                  │
        └──────────────────┼──────────────────┘
                           ▼
                  Retrieval / Orchestration
                           │
                    MemoryController
                           │
              ┌────────────┴────────────┐
              ▼                         ▼
        Provenance / Audit       Verification / Lifecycle
              │                         │
              └────────────┬────────────┘
                           ▼
                  Markdown + SQLite/WAL
                           │
                           ▼
                        Obsidian
```
**Teste:** `cognitive_core/tests/` — peste 70 de fișiere de test (pytest), acoperind fiecare modul de mai sus, plus teste end-to-end (`test_end_to_end_workflow.py`), adversariale (`test_milestone4_adversarial_challenger*.py`, `test_milestone5_*`), și de regresie (`test_council_efficiency_report_fix.py`).

Obsidian este interfata de navigare si audit uman a aceluiasi Vault. Nu este un al doilea Cognitive Core.
## `scripts/`
- `generate_b4_baseline.py` — generator baseline B4: rulează N execuții Council prin `FakeModelProvider`, produce rapoarte per-run în `reports/b4/`, agregă cu B5 (`b5_report.json`, CSV-uri, `B5_TOKEN_EFFICIENCY_REPORT.md`)
- `skill_ingestion.py` — ingestie skill-uri externe

---
## Alte componente root
- `vault_api.py` — API pentru interacțiune cu vault-ul
- `memory_controller/`, `config/`, `agents/`, `commands/`, `skills/` — module suport
- `xau_kinetic/`, `XAU_Kinetic.Desktop/`, `XAU_Kinetic_Standalone/`, `projects/` — proiecte satelit (trading/desktop)
- `compute_nodes.json`, `pytest.ini`, `requirements-memory-v6.txt`
- `audit_log.jsonl` (~34.6 MB) și `test_audit_log.jsonl` (~3.1 MB) — loguri de audit brute, generate automat

# 🤖 Claude Code Plugin
## Istoric recent (schimbări majore)

Repository-ul contine deja stratul Claude Code:
- **`6e256a9`** — `feat(B4)`: adăugat generator baseline B4 (`generate_b4_baseline.py`) + test de regresie, validat prin CLI și programatic (3 rapoarte JSON, `b5_report.json` cu `run_count == 3`, Markdown cu secțiune "Council efficiency verdict").
- **`0f4e0f3`** — `fix(B5)`: corectat bug în `council_efficiency_report.py` unde `top_optimization_candidate` combina independent agentul cu cel mai mare consum individual și tier-ul cu cel mai mare consum agregat, producând perechi ireale (ex. `"CRITIC / light"` când CRITIC rula mereu pe `standard`). Fix-ul grupează consumul pe perechea reală `(agent_id, model_tier)` observată în date. Adăugate 10 teste de regresie (`test_council_efficiency_report_fix.py`).

```text
.claude-plugin/
├── plugin.json
└── marketplace.json
```
## Note de mentenanță

si componentele:

```text
skills/ai-memory-vault/SKILL.md
agents/memory-librarian.md
commands/memory.md
commands/memory-sync.md
commands/memory-status.md
```

Pluginul foloseste Vault-ul existent ca **memorie externa canonica**. Nu incearca sa incarce tot repository-ul in context.

Fluxul este:

```text
Claude request
      ↓
AI Memory Vault Skill
      ↓
Determine relevant domain
      ↓
Retrieve relevant memory / skills / agents / procedures
      ↓
Reason / Plan / Act
      ↓
Verify
      ↓
Controlled memory write / proposal
      ↓
Provenance + Audit
```

---

# 🧠 Memory Controller & Cognitive Core

`memory_controller/` este granita pentru citirea si scrierea memoriei canonice.

### 🧠 Arhitectura Cognitivă Bio-Inspirată (Brain Upgrade — ACT-R & GWT)

Sistemul integrează 4 module cognitive bio-inspirate pentru o funcționare autonomă de tip creier:

1. **ACT-R Activation Decay (`cognitive_core/activation.py`)**:
   - Calculează activarea bazală $B_i = \ln(\sum_j t_j^{-d})$ (Anderson 2004). Notele neaccesate scad natural și trec în stare `DORMANT_THRESHOLD` (-2.0).
2. **Reconsolidarea Memoriei (`cognitive_core/consolidation.py`)**:
   - Memoriile canonice contrazise intră în starea `RECONSOLIDATING` cu istoricul versiunilor salvat, permițând actualizări plastice fără pierderi de date (Nader et al. 2000).
3. **Modulul Motivațional & Utilitate (`cognitive_core/motivation.py`)**:
   - Urmărește utilitatea acțiunilor $U = P \cdot G - C$ prin Exponential Moving Average și oferă bonusuri dinamice de atenție pe baza recompenselor reale de la `VerifierAgent`.
4. **Global Workspace Theory (`cognitive_core/global_workspace.py`)**:
   - Hub competitiv central în care agenții Consiliului (`Router`, `Retrieval`, `Verifier`, `Critic`) trimit propuneri, iar propunerea cu scor maxim este difuzată (*broadcast*) global tuturor agenților (Baars 1988; Dehaene et al. 2001).

### 🚀 Extensia de Arhitectură Memory V6 (v6.0.0 Engine)

Vault-ul integrează extensia aditivă **Memory V6** care adaugă 17 componente modulare:

1. **Buffer Ephemeral de Senzori (`cognitive_core/sensor_buffer.py`)**: Păstrează evenimentele brute de sesiune în memorie fără a polua stocarea canonică.
2. **Extractor Atomic de Memorie + Adapter Local Ollama (`cognitive_core/extraction.py` & `ollama_extractor.py`)**: Extragere deterministă de fapte, decizii, proceduri și lecții, augmentată opțional cu modele locale Ollama (`--use-ollama`).
3. **Coadă de Propuneri & Detector de Conflicte (`cognitive_core/proposal_queue.py` & `conflict_detector.py`)**: Coadă de triaj în `06_INBOX/` cu detector euristic de conflicte și negații.
4. **Queue Promoter Controlat (`cognitive_core/queue_promoter.py`)**: Promovează doar candidații marcați `APPROVED` de oameni direct în `MemoryController.propose()`.
5. **Memorie Multi-Graf & Spreading Activation (`cognitive_core/multi_graph.py` & `spreading_activation.py`)**: 4 grafuri derivate (semantic, temporal, cauzal, entități) cu re-clasare prin activare difuză ACT-R (`cognitive_core/ranked_search.py`).
6. **Sleep-Phase Consolidation & Obsidian Renderer (`cognitive_core/sleep_consolidation.py` & `report_view.py`)**: Raportare mentenanță read-only randată ca notă Markdown cu legături `[[wikilinks]]` în Obsidian.
7. **Harnasament Benchmark LoCoMo (`cognitive_core/benchmarks/`)**: Măsurare automată Precision@K, Recall@K și MRR.
8. **Fluxuri CI/CD Automatizate (`.github/workflows/`)**: `memory-v6-tests.yml` (teste pytest automate pe fiecare commit) și `memory-consolidation.yml` (consolidare programată).

```bash
# 1. Extragere fapte & adăugare în coadă (opțional cu Ollama local)
python -m cognitive_core.memory_v6_cli extract --text "Am decis: folosim SQLite WAL." --enqueue

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

Principii:

- autorizare pe operatiuni si roluri;
- provenance obligatorie;
- lifecycle controlat;
- verificare separata de creare;
- audit criptografic;
- persistenta Markdown + SQLite/WAL;
- separarea continutului RAW de continutul canonical.

Lifecycle-ul conceptual este:

```text
RAW → CLASSIFIED → NORMALIZED → REVIEW → VERIFIED → ACTIVE
                                             ↓
                                  SUPERSEDED / ARCHIVED
```

Un agent nu transforma automat continut extern in adevar verificat.

---

# 🏛️ Agent Council

Vault-ul contine o retea de agenti specializati, coordonata prin capability/skill matching. Domeniile includ:

- system architecture;
- backend engineering;
- frontend / SaaS;
- web creative development;
- web design engineering;
- web quality / performance;
- UI/UX;
- WPF / .NET;
- compilers and tooling;
- polyglot systems;
- DevOps / SRE;
- security / SecOps;
- threat hunting;
- database / persistence;
- local AI / LLM;
- quantitative development;
- game engineering;
- content strategy;
- agentic workflows;
- memory architecture.

Matricea agent ↔ skill este mentinuta in:

`01_KNOWLEDGE/Agents_Skill_Matrix.md`

Agentii trebuie sa respecte **least privilege** si sa foloseasca doar capabilities relevante.

---

# 📦 Skills Architecture

Exista doua niveluri principale.

## Operational Skills

```text
.agents/skills/
```

Aici se afla skill-urile operationale locale. Catalogul este:

`01_KNOWLEDGE/Master_Skills_Catalog_251.md`

## RAW External Skills

```text
06_INBOX/RAW_IMPORTS/skills/
```

Aceasta este granita `RAW_EXTERNAL` pentru skills si developer knowledge importate din GitHub si alte surse.

Fluxul este:

```text
External Source
      ↓
Recursive Discovery
      ↓
Deduplication
      ↓
Classification
      ↓
Provenance
      ↓
Validation
      ↓
RAW_EXTERNAL
      ↓
Review / Promotion
      ↓
Canonical Skill / Knowledge
```

Nu orice repository este skill. Backend projects, compilers, runtimes si programming-language repositories sunt reference sources daca nu contin explicit skill definitions.

---

# 🔎 Recursive Discovery

Sursele externe sunt seed-uri, nu lista finala. Discovery-ul poate urmari recursiv:

- `SKILL.md`;
- `skills/`;
- `agents/`;
- `instructions/`;
- `prompts/`;
- skill collections;
- documentatie tehnica;
- link-uri catre alte skill repositories;
- framework/backend/language resources relevante pentru agentic development.

Discovery-ul trebuie sa previna ciclurile, duplicatele, mirrors/forks si importarea aplicatiilor intregi drept skills.

Continutul extern este tratat ca **untrusted input** si nu este executat automat in timpul ingestiei.

---

# 🌐 Backend & Programming Knowledge

Vault-ul poate indexa surse pentru:

```text
Backend
├── REST / GraphQL / gRPC
├── .NET / ASP.NET
├── Node / Express / NestJS
├── Django / Flask
├── Rails / Spring
├── Laravel / Symfony / PHP
├── Vapor / Swift
├── databases / ORM
├── auth / security
├── testing / observability
└── deployment

Programming
├── C / C++ / C# / F#
├── Rust / Go / Python
├── TypeScript / JavaScript
├── Java / Kotlin / Scala
├── Swift / Dart / Julia
├── Ruby / PHP
├── Elixir / Erlang
├── OCaml / Haskell / PureScript
├── Nim / Crystal / Zig / Odin / V
└── compilers / runtimes / tooling
```

Acestea imbogatesc knowledge graph-ul, dar sunt promovate in skills operationale doar dupa clasificare si validare.

---

# 🔐 Provenance & Trust

Pentru continut extern trebuie sa putem determina:

```text
source_repository
source_url
source_path
source_commit
source_branch
license
author
discovered_from
discovery_depth
sha256
status
```

`SOURCE.json` este standardul de provenance pentru pachetele RAW unde este disponibil.

---

# 🗺️ Obsidian Synchronization

Obsidian este sincronizat semantic cu:

- Skills;
- Agents;
- Knowledge;
- Procedures;
- Projects;
- Memory;
- Provenance;
- Validation;
- References.

Documentele centrale sunt:

- `99_SYSTEM/Obsidian_Skill_Agent_Memory_Sync.md`
- `05_RESOURCES/Obsidian/Skill_Agent_Memory_MOC.md`
- `01_KNOWLEDGE/Agents_Skill_Matrix.md`
- `01_KNOWLEDGE/Master_Skills_Catalog_251.md`

Relatia principala:

```text
Skill ↔ Agent ↔ Capability ↔ Procedure ↔ Knowledge ↔ Memory ↔ Obsidian
```

---

# 📁 Structura repository-ului

```text
AI_Memory_Vault_CODEX_READY/
│
├── .claude-plugin/             # Claude Code plugin / marketplace metadata
├── .agents/
│   ├── agents/                 # Agent profiles
│   ├── rules/                  # Cognitive/security rules
│   └── skills/                 # Operational local skills
│
├── 00_CORE/                    # Identity, rules, memory protocol
├── 01_KNOWLEDGE/               # Durable knowledge and registries
├── 02_PROJECTS/                # Project continuity
├── 03_PROCEDURES/              # Repeatable procedures
├── 04_MEMORY/                  # Decisions, lessons, experiences
├── 05_RESOURCES/               # Resources / Obsidian navigation
├── 06_INBOX/                   # Incoming / RAW material
│   └── RAW_IMPORTS/skills/     # RAW_EXTERNAL boundary
├── 99_SYSTEM/                  # System contracts / synchronization
│
├── skills/                     # Plugin-level Claude skills
├── agents/                     # Plugin-level Claude agents
├── commands/                   # Claude commands
├── hooks/                      # Plugin hooks
├── scripts/                    # Supporting automation
├── memory_controller/          # Canonical memory boundary
└── cognitive_core/             # Retrieval / reasoning / reflection
```

---

# 🔄 Synchronization Contract

Toate AI clients folosesc aceeasi memorie canonica:

```text
                 CANONICAL VAULT
                       │
          ┌────────────┼────────────┐
          ▼            ▼            ▼
        Codex        Claude       Obsidian
          │            │            │
          └────────────┼────────────┘
                       ▼
                 Shared Memory
```

Reguli:

1. Nu crea memorii canonice paralele.
2. Nu transforma automat RAW in VERIFIED.
3. Foloseste retrieval relevant, nu dump integral de context.
4. Pastreaza provenance.
5. Respecta lifecycle-ul memoriei.
6. Nu suprascrie knowledge mai autoritativ cu surse mai slabe.
7. Detecteaza si marcheaza conflictele.
8. Actualizeaza registry-urile cand skill/agent relations se schimba.
9. Nu executa continut extern doar pentru ca a fost importat.
10. Pastreaza separarea dintre ingestion, validation, promotion si execution.

---

# 🛡️ Security

Continutul extern este untrusted input.

Nu se executa automat:

- executabile;
- installers;
- npm scripts;
- Python scripts;
- build scripts;
- binaries;
- dependency installers.

Ingestion-ul este read/analyze/hash/classify-first.

---

# 📊 Registries & Audit

Cand exista, acestea sunt fisierele centrale pentru inventarul skill corpus:

```text
06_INBOX/RAW_IMPORTS/skills/_SOURCE_REGISTRY.json
06_INBOX/RAW_IMPORTS/skills/_REGISTRY.json
06_INBOX/RAW_IMPORTS/skills/_DISCOVERY_GRAPH.json
06_INBOX/RAW_IMPORTS/skills/_DEDUPLICATION.json
06_INBOX/RAW_IMPORTS/skills/_LICENSES.json
06_INBOX/RAW_IMPORTS/skills/_VALIDATION_REPORT.md
06_INBOX/RAW_IMPORTS/skills/_PROGRAMMING_SOURCES.json
06_INBOX/RAW_IMPORTS/skills/_BACKEND_SOURCES.json
```

Registries trebuie sa ramana coerente cu filesystem-ul real.

---

# 🚀 Quick Start

```powershell
git clone https://github.com/userist123/AI_Memory_Vault_CODEX_READY.git
cd AI_Memory_Vault_CODEX_READY
git fetch origin
git pull origin main
```

Verifica sincronizarea:

```powershell
git status
git branch --show-current
git log -1 --oneline
```

---

# 🧪 Definition of Done

O schimbare majora este completa cand:

- filesystem-ul si registry-urile sunt coerente;
- provenance este disponibila;
- duplicatele sunt detectate;
- skill-urile sunt clasificate corect;
- agent ↔ skill relations sunt actualizate;
- Obsidian navigation este actualizata;
- Claude plugin metadata este valida;
- nu exista memorii canonice concurente;
- RAW_EXTERNAL security boundary este pastrata;
- testele relevante trec;
- repository-ul este sincronizat cu `main`.

---

# 📜 Principiul final

> **AI Memory Vault nu este doar un folder de fisiere. Este memoria canonica partajata a ecosistemului AI.**
>
> Skills adauga capabilities. Agents adauga specializare. Knowledge adauga context durabil. Procedures adauga moduri de lucru. Memory pastreaza experienta si deciziile. Provenance si verification stabilesc increderea. Obsidian ofera navigare si audit. Claude si Codex sunt clienti ai aceleiasi memorii, nu proprietari ai unor memorii separate.

---

## 🔗 Links

**Repository:** https://github.com/userist123/AI_Memory_Vault_CODEX_READY

**Claude Plugin:** `.claude-plugin/`

**Canonical Memory:** `memory_controller/` + `04_MEMORY/`

**Operational Skills:** `.agents/skills/`

**RAW External Skills:** `06_INBOX/RAW_IMPORTS/skills/`

**Agent Matrix:** `01_KNOWLEDGE/Agents_Skill_Matrix.md`

**Obsidian Sync:** `99_SYSTEM/Obsidian_Skill_Agent_Memory_Sync.md`

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
- Fișierele `council_efficiency_report.py` și restul pipeline-ului B1–B5 erau marcate FROZEN în instrucțiunile de proiect anterioare; fix-ul de mai sus a fost aplicat cu autorizare explicită — merită confirmat statutul FROZEN cu echipa/`AGENTS.md`.
- `01_KNOWLEDGE` și `04_MEMORY` conțin numeroase fișiere placeholder (`test_*.md`, `unknown_*.md`, `policy-lesson_*.md`) rezultate din import/generare automată — candidate pentru curățare sau revizuire lifecycle (`unverified` → `verified`).
- Fișierele `*_Claude_Legacy.md` și `*_Perplexity_Legacy.md` din `00_CORE`/`01_KNOWLEDGE` sunt versiuni istorice păstrate pentru compatibilitate — de evaluat dacă mai sunt necesare.
