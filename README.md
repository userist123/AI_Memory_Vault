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

# 🧠 AI Memory Vault — Canonical Multi-Agent Memory, Cognitive Engine V6 & Foundation Knowledge System

<p align="center">
  <strong>Stratul canonic de memorie cognitivă, knowledge grafic, 251 skill-uri operaționale, consiliu de 21 agenți și sinteză enciclopedică pe 10 niveluri pentru ecosistemele AI moderne.</strong><br>
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

> **AI Memory Vault** reprezintă infrastructura canonică de persistență, guvernare de cunoștințe, verificare de proveniență și execuție cognitivă pentru agenți autonomi. Acelasi Vault servește simultan **Claude Code, Google Antigravity, OpenAI Codex, asistenți vocali locali (JARVIS) și interfața Obsidian**, eliminând fragmentarea contextului și garantând integritatea matematică și tranzacțională a memoriei pe termen lung.

---

## 🎯 Ce este & Principiul Fundamental

**AI Memory Vault** nu este un simplu depozit de fișiere Markdown și nici o bază de date vectorială izolată. Este un **sistem de operare cognitiv unificat** conceput pentru a rezolva limitările fundamentale ale agenților LLM (amnezia între sesiuni, poluarea contextului, halucinația instrucțiunilor și scrierile neautorizate).

Sistemul permite agenților AI:
1. **Recuperare Parsimonioasă (Sparse Retrieval)**: Încărcarea exclusivă a cunoștințelor și skill-urilor strict necesare pentru sarcina curentă, respectând bugete stricte de tokeni.
2. **Specializare Multi-Agent cu Privilegii Minime**: Rutarea cerințelor către 21 de agenți specializați organizați în Consiliu, echipați cu maxim 2 skill-uri per agent.
3. **Proveniență Imutabilă și Lanț de Custodie**: Fiecare fapt, decizie sau procedură are atașat nivelul de încredere, autorul, sursa externă și hash-ul criptografic.
4. **Izolare între Date și Instrucțiuni**: Memoria externă recuperată este marcată prin granițe XML explicite (`<untrusted_memory>`) pentru a preveni atacurile de tip indirect prompt injection.
5. **Separare între Schiță (Draft) și Verificare Canonică**: Niciun agent AI nu poate promova automat date brute în cunoștințe verificate fără atestare umană sau audit formal.
6. **Sincronizare Multi-Client și Graf Semantic**: Navigare vizuală prin Obsidian fără a genera memorii concurente paralele.

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

## 🏗️ Arhitectura de Sistem pe 5 Straturi

Sistemul este organizat pe 5 straturi bine delimitate, separând stocarea fizică, logica cognitivă, cunoștințele durabile, competențele de execuție și interfețele utilizator:

```text
                                  HUMAN / USER / OPERATOR
                                             │
                       ┌─────────────────────┴─────────────────────┐
                       ▼                                           ▼
             CLAUDE CODE / CODEX / ANTIGRAVITY              JARVIS HUD & VOICE
                       │                                           │
                       └─────────────────────┬─────────────────────┘
                                             ▼
       ═══════════════════════════════════════════════════════════════════════════
       STRATUL 1: AUTORIZARE, GUVERNANȚĂ & PERSISTENȚĂ (memory_controller/)
       ═══════════════════════════════════════════════════════════════════════════
        • MemoryController Boundary (I-001..I-012, I-RETRIEVAL)
        • Principal & Role RBAC (HUMAN, ADMIN, AI_AGENT, SYSTEM)
        • SQLite WAL Storage Engine (BEGIN IMMEDIATE, PRAGMA busy_timeout=5000)
        • Atomic File Operations (os.replace via temporary checkpoints)
        • Criptographic Audit Logger (Chained SHA-256 Tamper-Evident Ledger)
                                             │
                                             ▼
       ═══════════════════════════════════════════════════════════════════════════
       STRATUL 2: NUCLEUL COGNITIV & RETRIEVAL V6 (cognitive_core/)
       ═══════════════════════════════════════════════════════════════════════════
        • Bio-Inspired ACT-R Activation Decay & Sleep-Phase Consolidation
        • Global Workspace Theory (GWT) Competitive Broadcast
        • Multi-Graph Retrieval (Semantic, Temporal, Causal, Entity Graph)
        • Spreading Activation Re-Ranking & LoCoMo Retrieval Benchmark
        • Council Budget Controller & Real Usage Token Telemetry (B1-B5)
                                             │
                                             ▼
       ═══════════════════════════════════════════════════════════════════════════
       STRATUL 3: OBSIDIAN CANONICAL VAULT (00_CORE .. 99_SYSTEM)
       ═══════════════════════════════════════════════════════════════════════════
        • 00_CORE: Identitate, Reguli de operare, Protocoale, MOC-uri centrale
        • 01_KNOWLEDGE: Registre canonice, Arhitecturi, 10 Tiers Foundation Books
        • 02_PROJECTS / 03_PROCEDURES: Continuitate proiecte și proceduri rulate
        • 04_MEMORY: Decizii, lecții învățate, erori clasificate, experiențe
        • 05_RESOURCES / 06_INBOX: Referințe externe și granița RAW_EXTERNAL
        • 99_SYSTEM: Contracte de sistem, profiluri runtime, bugete de context
                                             │
                                             ▼
       ═══════════════════════════════════════════════════════════════════════════
       STRATUL 4: AGENȚI, SKILLS & EXTENSII (.agents/, skills/, .claude-plugin/)
       ═══════════════════════════════════════════════════════════════════════════
        • 21 Subagenți Specializați (Roluri izolate, manifesturi stricte)
        • 251 Skill-uri Operaționale Locale (.agents/skills/ cu SKILL.md canonic)
        • 12-Phase RAW Ingestion Pipeline (06_INBOX/RAW_IMPORTS/skills/)
        • Claude Code Marketplace Plugin (.claude-plugin/plugin.json)
                                             │
                                             ▼
       ═══════════════════════════════════════════════════════════════════════════
       STRATUL 5: PROIECTE PILOT INTEGRATE (projects/)
       ═══════════════════════════════════════════════════════════════════════════
        • jarvis_cognitive_brain: Creier cognitiv autonom OODA, VAD Silero + Whisper,
          Kokoro-82M ONNX TTS, FastMCP IoT Home Assistant, 3D Web HUD
        • jarvis_web: Asistent vocal web standalone WebGL Three.js & Web Audio SFX
```

---

## 🧬 Arhitectura Cognitivă Bio-Inspirată (ACT-R & GWT)

Sistemul integrează în `cognitive_core/` 5 module inspirate din neuroștiințele cognitive și arhitecturile cognitive clasice pentru a guverna relevanța, uitarea controlată și reconcilierea cunoștințelor:

### 1. ACT-R Activation Decay (`cognitive_core/activation.py`)
Modelează dinamica memoriei declarative conform teoriei ACT-R (John R. Anderson, 2004). Nivelul de activare bazală $B_i$ al unei note este calculat pe baza istoricului temporal al accesărilor:
$$B_i = \ln\left( \sum_{k=1}^{n} t_k^{-d} \right)$$
Unde $t_k$ este timpul scurs de la accesarea $k$, iar $d$ este factorul de degradare (decay parameter, implicit $0.5$). Memoriile nefolosite scad sub pragul `DORMANT_THRESHOLD` ($-2.0$), fiind excluse din căutările parsimonioase standard pentru a proteja fereastra de context.

### 2. Reconsolidarea Memoriei (`cognitive_core/consolidation.py`)
Inspirat din teoria reconsolidării neurobiologice (Nader et al., 2000). Atunci când o cunoștință canonică activă este contrazisă de dovezi noi de execuție sau de un audit refuzat, nota intră în starea tranzitorie `RECONSOLIDATING`. Versiunea anterioară este arhivată cu amprentă criptografică, permițând actualizarea plastică fără pierderi ireversibile de date.

### 3. Modulul Motivațional & Utilitate (`cognitive_core/motivation.py`)
Calculează utilitatea acțiunilor și strategiilor de rezolvare conform ecuației de utilitate:
$$U = P \cdot G - C$$
Unde $P$ este probabilitatea de succes estimată, $G$ valoarea recompensei (furnizată de `VerifierAgent` sau validarea testelor `pytest`), iar $C$ costul asociat (timp de calcul, consum tokeni). Ponderile sunt actualizate prin Exponential Moving Average (EMA).

### 4. Global Workspace Theory (`cognitive_core/global_workspace.py`)
Implementează modelul Global Workspace propus de Bernard Baars (1988) și Stanislas Dehaene (2001). Reprezintă un hub concurențial cu capacitate strict limitată (Working Memory). Subagenții consiliului (`Router`, `Retrieval`, `Verifier`, `Critic`) trimit mesaje candidate; modulul organizează competiția pe baza utilității și a scorului de relevanță, iar candidatul câștigător este difuzat (*broadcasted*) către toți agenții activi ca focus atențional comun.

### 5. Rădăcini Neuromorfe SNN (`cognitive_core/neuromorphic/`)
Modul experimental curat ce oferă o punte între reprezentările simbolice (note ID) și dinamica sub-simbolică a creierului:
- **Neuron LIF (`lif_neuron.py`)**: Dinamica potențialului de membrană $\tau_m \frac{dV}{dt} = -(V - V_{reset}) + I_{syn}$, prag de descărcare $V_{th}$ și perioadă refractară.
- **Plasticitate Sinaptică STDP (`stdp_synapse.py`)**: Spike-Timing-Dependent Plasticity pentru învățarea asocierilor temporale:
  $$\Delta W = A_+ \exp\left(-\frac{\Delta t}{\tau_+}\right) \text{ dacă } \Delta t > 0, \quad \Delta W = -A_- \exp\left(\frac{\Delta t}{\tau_-}\right) \text{ dacă } \Delta t < 0$$
- **Rețea Spiking (`snn_network.py`)**: Mapare asociativă între trenurile de impulsuri și conceptele din graf.

---

## 🚀 Extensia de Arhitectură Memory V6 (v6.0.0 Engine)

Extensia aditivă **Memory V6** completează sistemul de bază cu 17 componente industriale pentru ciclul de viață al memoriilor:

| Nr. | Componentă V6 | Modul Python | Responsabilitate & Comportament Tehnic |
|:---:|:---|:---|:---|
| **1** | **Ephemeral Sensor Buffer** | `cognitive_core/sensor_buffer.py` | Colectează evenimentele brute de interacțiune în memorie RAM fără a polua stocarea durabilă. |
| **2** | **Atomic Memory Extractor** | `cognitive_core/extraction.py` | Extrage fapte, decizii, proceduri și lecții folosind euristici deterministe și regex structurat. |
| **3** | **Ollama Local Extractor** | `cognitive_core/ollama_extractor.py` | Adaptor opțional pentru extragere semantică augmentată prin modele locale (ex. `qwen2.5-coder`). |
| **4** | **Proposal Queue** | `cognitive_core/proposal_queue.py` | Coadă de triaj izolată în `06_INBOX/` cu stări controlate (`PENDING`, `APPROVED`, `REJECTED`). |
| **5** | **Conflict Detector** | `cognitive_core/conflict_detector.py` | Detectează contradicțiile de fapte și negațiile logice înainte de promovarea în memoria canonică. |
| **6** | **Controlled Queue Promoter** | `cognitive_core/queue_promoter.py` | Permite promovarea în `MemoryController.propose()` exclusiv pentru candidații aprobați de om. |
| **7** | **Multi-Graph Memory** | `cognitive_core/multi_graph.py` | 4 grafuri derivate: graf semantic, temporal, cauzal (decizie -> efect) și graf de entități. |
| **8** | **Spreading Activation** | `cognitive_core/spreading_activation.py` | Difuzie de activare pe graful relațional limitată strict la orizontul de $\le 2$ hop-uri (bounded expansion). |
| **9** | **Hybrid Ranked Search** | `cognitive_core/ranked_search.py` | Re-clasare hibridă combinând potrivirea lexicală, proximitatea semantică și activarea ACT-R. |
| **10** | **Sleep-Phase Consolidation** | `cognitive_core/sleep_consolidation.py` | Proces de mentenanță nocturnă: arhivare automată, degradare de activare și curățare referințe. |
| **11** | **Obsidian Report View** | `cognitive_core/report_view.py` | Generează automat rapoarte sintetice Markdown cu legături bidirecționale `[[wikilinks]]`. |
| **12** | **LoCoMo Benchmark Harness** | `cognitive_core/benchmarks/` | Suite de evaluare a performanței de recuperare: calculează Precision@K, Recall@K și MRR. |
| **13** | **Memory V6 CLI** | `cognitive_core/memory_v6_cli.py` | Interfață de linie de comandă unificată pentru operare, triaj, aprobare și consolidare. |
| **14** | **Memory Event Bus** | `cognitive_core/event_bus.py` | Magistrală de evenimente asincronă pentru decuplarea componentelor cognitive. |
| **15** | **Context Pack Builder** | `cognitive_core/context_pack_builder.py` | Asamblează pachete minimale de context respectând bugetul strict stabilit prin `Council_Context_Budget.md`. |
| **16** | **Token Telemetry Auditor** | `cognitive_core/actual_usage_telemetry.py` | Măsoară consumul real de tokeni per apel și per agent (B1–B5 efficiency framework). |
| **17** | **Automated CI/CD Workflows** | `.github/workflows/` | Pipeline-uri automate: `memory-v6-tests.yml` (validare pytest) și `memory-consolidation.yml`. |

### Runbook CLI Memory V6

Ciclul de viață de la extragere până la consolidarea în Obsidian se execută prin următorul flux standard:

```powershell
# 1. Extrage fapte, decizii sau lecții dintr-o conversație/text și trimite-le în coada de triaj
python -m cognitive_core.memory_v6_cli extract --text "Am decis: folosim SQLite WAL mode cu busy_timeout 5000ms pentru prevenirea blocajelor." --enqueue

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

## 🛡️ Modelul de Securitate, Încredere & Invariante

Securitatea sistemului de memorie este formalizată prin contracte matematice verificate prin 518 teste automate.

### Nomenclatura Canonică a Modelului de Securitate
- **`P0`**: Denumirea priorității maxime *Phase 4.3 P0 Security Hardening*.
- **`P0-001..P0-015`**: Cele 15 contracte de testare adversarială definite în `99_SYSTEM/Phase43_P0_Implementation_Contract.md`.
- **`I-001..I-012`**: Invariantele canonice de securitate a memoriei.
- **`I-RETRIEVAL`**: Invarianta de regăsire unificată securizată.
- **`P1 / P2 / P3`**: Niveluri criminalistice de prioritate (corectitudine funcțională, arhitectură, mentenanță).
- **`P16 / P17 / P18`**: Invariante de telemetrie hardware și lanț de custodie criminalistică.
- **`P0–P18`**: Sintagmă umbrelă sintetică utilizată în documentație; nu reprezintă 19 invariante secvențiale de memorie.

### Invariantele de Graniță a Încrederii (I-001..I-012, I-RETRIEVAL)
- **`I-001 (AI Self-Verification Gated)`**: Agenții AI (`Principal.AI_AGENT`) nu au permisiunea de a seta atributul `verification = "verified"`. Doar operatorul uman sau administratorul pot confirma veridicitatea unei cunoștințe.
- **`I-002 (Privileged Provenance Gated)`**: Agenții AI nu pot revendica proveniențe privilegiate (`user`, `official`, `experience`, `import`). Valorile permise pentru AI sunt: `execution`, `ai`, `inference`, `unknown`.
- **`I-003 (Creation Lifecycle Restricted)`**: Agenții AI pot propune memorii doar în stările `{RAW, CLASSIFIED, NORMALIZED, REVIEW}`. Promovarea în starea `ACTIVE` necesită atestare umană directă (`attest()`).
- **`I-004 (Attestation Authorization)`**: Numai `Principal.HUMAN` și `Principal.ADMIN` pot invoca metoda `controller.attest()` sub acțiunea `Operation.ATTEST`.
- **`I-005 (Provenance Immutability)`**: Câmpul `provenance.source_type` este strict imutabil după crearea inițială a notei. Orice încercare de alterare este respinsă de nucleu.
- **`I-RETRIEVAL (Unified Secure Retrieval Invariant)`**: Toate interogările de memorie (prin REST API sau CLI fallback) trebuie să treacă prin `MemoryController.search()`. Dacă serverul este offline, se utilizează exclusiv:
  ```powershell
  python -m cognitive_core.recall_cli --query "termen căutat"
  ```
  Scanarea directă neautentificată a sistemului de fișiere (`os.walk`, scanare brută) este **strict interzisă** în toate runtime-urile.

### Invariante Hardware & Criminalistice (P16–P18)
- **`P16 (Hardware Telemetry Immutability)`**: Datele fizice extrase din SO (VID, PID, Serial Number hardware, capacitate fizică, System Host ID, hash SHA-256) sunt strict read-only; interfețele UI blochează orice tentativă de modificare manuală.
- **`P17 (Friendly Name Isolation)`**: Utilizatorul poate edita exclusiv eticheta logică prietenoasă a volumului și metadatele administrative, fără a afecta identificatorii fizici.
- **`P18 (Forensics Chain of Custody)`**: Orice transfer de date asociază automat amprenta hardware a mediului detectat fizic în jurnalul de audit tamper-evident.

### Principii Operaționale Derivate din Literatură
1. **The Production Reality Invariant (DDIA / Huyen)**: Orice componentă verificată doar pe structuri mock în memorie este marcată `TEST_VERIFIED`, niciodată `RUNTIME_VERIFIED` pentru producție. Toate interacțiunile de stocare sunt verificate pe motoare reale (`SQLiteStorageEngine` în mod WAL cu tranzacții atomice `BEGIN IMMEDIATE`).
2. **Izolarea Datelor de Instrucțiuni (Pai / Zvarydchuk)**: Memoriile recuperate sunt încapsulate în tag-uri XML explicite (`<untrusted_memory id="..." lifecycle="...">...</untrusted_memory>`) și tratate exclusiv ca date pasive, niciodată ca meta-instrucțiuni executabile.
3. **Expansiune Asociativă Delimitată (Russell-Norvig AIMA)**: Propagarea activării în graf este limitată strict la o rază de $\le 2$ hop-uri și $\le 5$ candidați pentru a preveni explozia combinatorie a contextului.

---

## 🏛️ Consiliul celor 21 de Agenți Specializați

Arhitectura multi-agent este coordonată prin principiul *Least Privilege*. Fiecare agent are o identitate strict definită în `.agents/agents/` și acces la un set parsimonios de capabilități cartografiate în `01_KNOWLEDGE/Agents_Skill_Matrix.md`:

| Nr. | Nume Agent | Domeniu & Rol Primar | Privilegii Stocare | Skill-uri Principale Alocate |
|:---:|:---|:---|:---|:---|
| **1** | `agentic_workflow_orchestrator` | Orchestrator de fluxuri de lucru multi-agent și pipeline-uri gh-aw | Read, Propose | `agent-squad`, `agentic-workflows`, `workflow-automation` |
| **2** | `backend_systems_engineer` | Arhitectură API backend, microservicii, worker pools și tranzacții | Read, Propose | `fastapi-pro`, `csharp-pro`, `golang-pro`, `nodejs-backend-patterns` |
| **3** | `compiler_and_tooling_engineer` | Compilatoare, AST, optimizări de runtime, lintere și CLI tooling | Read, Propose | `rust-pro`, `cpp-pro`, `cli-mastery`, `python-packaging` |
| **4** | `content_strategist` | Arhitectură informațională, documentație tehnică și comunicare | Read, Propose | `brain-to-docs`, `technical-tutorials`, `docs-architect` |
| **5** | `database_and_persistence_engineer`| SQLite WAL, migrații, indexare, caching Redis și sharding | Read, Propose | `sqlite-wal-optimization`, `postgresql`, `redis-cli`, `ef-core` |
| **6** | `frontend_saas_engineer` | Aplicații web SaaS moderne, Next.js, stări optimiste și performanță | Read, Propose | `nextjs-best-practices`, `typescript-pro`, `tanstack-query-expert` |
| **7** | `game_engineer` | Sisteme de joc 2D/3D, bucle de randare, fizică și arhitectură ECS | Read, Propose | `3d-games`, `threejs`, `godot-gdscript-patterns`, `shader-programming-glsl` |
| **8** | `local_ai_engineer` | Integrare LLM locale, Ollama, vLLM, ONNX, Whisper și cuantizare | Read, Propose | `local-llm-expert`, `vllm-inference-optimization`, `pydantic-ai` |
| **9** | `memory_controller_architect` | Păstrătorul nucleului canonic de memorie, invariante și audit | Read, Write, Attest (Admin) | `vault-operations`, `memory-safety-patterns`, `context-management` |
| **10** | `polyglot_systems_architect` | Sisteme eterogene, interoperabilitate C#/Python/Rust/Go și FFI | Read, Propose | `clean-architecture-backend`, `microservices-patterns`, `ddd-tactical` |
| **11** | `quant_developer` | Analiză cantitativă, modele de risc, algoritmi de semnal și backtesting | Read, Propose | `python-trading-systems`, `risk-metrics-calculation`, `backtesting` |
| **12** | `secops_auditor` | Audit de securitate, detectare vulnerabilități și conformitate OWASP | Read, Audit | `security-invariants`, `owasp-top-10-audit`, `pki-certificate-mgmt` |
| **13** | `site_reliability_and_devops_architect` | Fiabilitate, infrastructură ca cod, containere și CI/CD pipelines | Read, Propose | `docker-expert`, `kubernetes-orchestration`, `prometheus-grafana` |
| **14** | `system_architecture_agent` | Design de ansamblu, modele C4, diagrame de domeniu și decizii ADR | Read, Propose | `senior-architect`, `c4-container`, `architecture-decision-records` |
| **15** | `threat_hunting_analyst` | Detecție intruziuni, forensic memory analysis și threat modeling | Read, Audit | `threat-hunting`, `digital-forensics`, `malware-analysis` |
| **16** | `ui_sensei_architect` | Filosofia sistemelor de design UI, coeziune vizuală și tokeni semantici | Read, Propose | `ui-skills`, `design-system-foundation`, `radix-ui-design-system` |
| **17** | `ui_ux_designer` | Ergonomie interfețe, accesibilitate WCAG, wireframes și micro-interacțiuni | Read, Propose | `ux-flow`, `ui-a11y`, `ux-audit`, `tailwind-design-system` |
| **18** | `web_creative_developer` | Efecte WebGL avansate, Three.js shadere, animații Lenis și GSAP | Read, Propose | `webgl-laser`, `webgl-landing-steering`, `gsap-scrolltrigger` |
| **19** | `web_design_engineer_agent` | Punte între ingineria de cod și designul grafic de nivel internațional | Read, Propose | `garden-web-design-engineer`, `xiaopu-web-design`, `frontend-design` |
| **20** | `web_quality_engineer` | Core Web Vitals, audit Lighthouse, randare optimă și profiling DOM | Read, Propose | `web-quality-audit`, `core-web-vitals`, `pagespeed-enhancer` |
| **21** | `wpf_engineer` | Aplicații desktop Windows, XAML avansat, MVVM Toolkit și arhitectură C# | Read, Propose | `csharp-wpf-desktop`, `mvvm-toolkit`, `dotnet-best-practices` |

---

## 📦 Corpusul de Skills (251 Locale & Index Global)

Sistemul menține o ierarhie strictă a competențelor pentru a preveni poluarea executabilă:

```text
.agents/skills/                   # 251 SKILL-URI OPERAȚIONALE LOCALE
  ├── 3d-games/SKILL.md
  ├── agent-squad/SKILL.md
  ├── learn/SKILL.md              # Skill de instruire & caiet de teme
  └── ... (251 foldere)

06_INBOX/RAW_IMPORTS/skills/      # GRANIȚA RAW_EXTERNAL (Izolare strictă)
  ├── _SOURCE_REGISTRY.json       # Registrul provenienței surselor externe
  ├── _REGISTRY.json              # Catalogul global al resurselor descoperite
  ├── _DEDUPLICATION.json         # Amprente SHA-256 și eliminarea duplicatelor
  └── _LICENSES.json              # Maparea licențelor (MIT, Apache, etc.)
```

### Pipeline-ul de Ingestie Externă în 12 Faze
Orice skill sau pachet extern importat din comunitate (GitHub, HuggingFace, etc.) trece obligatoriu prin pipeline-ul canonic de izolare:
1. **Crawl & Raw Capture**: Salvare în `06_INBOX/RAW_IMPORTS/` cu `SOURCE.json` asociat.
2. **Deduplicare Criptografică**: Calcul hash SHA-256 pentru eliminarea duplicatelor și a fork-urilor paralele.
3. **Analiză Semantică & Clasificare**: Identificarea tipului (Skill veritabil, bibliotecă, proiect complet sau documentație).
4. **Filtrare de Securitate**: Verificare anti-malware, scanare comenzi de sistem și blocare scripturi neautorizate.
5. **Normalizare Structură**: Conversie la formatul canonic `SKILL.md` cu frontmatter standardizat YAML.
6. **Cartografiere Dependențe & Licențe**: Înregistrarea termenilor legali în `_LICENSES.json`.
7. **Izolare în Coada de Triaj**: Trimitere în `06_INBOX/` fără drept de execuție automată.
8. **Analiză de Conflict**: Rulare euristică de detectare a contradicțiilor cu abilitățile existente.
9. **Revizuire & Audit de Cod**: Validare de către agenții `SecOpsAuditor` și `SystemArchitectureAgent`.
10. **Aprobare Umană**: Confirmare explicită a operatorului conform invariantului `I-003`.
11. **Promovare în `.agents/skills/`**: Mutare atomică în catalogul activ al sistemului.
12. **Sincronizare Registre & MOC Obsidian**: Actualizare automată în `01_KNOWLEDGE/Master_Skills_Catalog_251.md` și legăturile de graf.

---

## 📚 Sinteza Cărților Fundamentale (Tiers 1–10) & Caietul cu 60 de Teme Practice

Pentru a asigura o bază teoretică de elită, sistemul integrează o sinteză enciclopedică pe 10 niveluri (61 note canonice în `01_KNOWLEDGE/BOOKS/`, validate JSON Schema Draft-07), conectată la un **Caiet de Teme și Aplicații Practice** conținând 60 de exerciții aplicate complete:

```text
01_KNOWLEDGE/BOOKS/
  ├── Caiet_Teme_Aplicatii_Practice_Carti.md              # Podul canonic de sinteză
  ├── Tier_1_DDIA_Sinteza_Capitole_01_12.md               # Tier 1 (DDIA - Martin Kleppmann)
  ├── Tier_2_AIMA_Sinteza_Parti_01_07.md                  # Tier 2 (AIMA 4e - Russell & Norvig)
  ├── Tier_3_Agentic_Architecture_Sinteza.md              # Tier 3 (Arhitectura Agenților Autonomi)
  ├── Tier_4_LLM_Apps_RAG_Sinteza.md                      # Tier 4 (Aplicații LLM & RAG Avansat)
  ├── Tier_5_ML_Systems_Sinteza.md                        # Tier 5 (Sisteme ML în Producție - Chip Huyen)
  ├── Tier_6_Deep_Learning_Sinteza.md                     # Tier 6 (Deep Learning - Goodfellow et al.)
  ├── Tier_7_Memory_Systems_Prompt_Eng_Sinteza.md         # Tier 7 (Sisteme de Memorie & Prompt Eng.)
  ├── Tier_8_AI_Security_Hardening_Sinteza.md             # Tier 8 (Securitate AI & OWASP LLM Top 10)
  ├── Tier_9_High_Performance_Concurrency_Sinteza.md     # Tier 9 (Scalare, Concurrency & Baze de Date)
  ├── Tier_10_Frontier_Cognitive_Neuro_AI_Sinteza.md      # Tier 10 (Sisteme Cognitive Hibride & SNN)
  └── ... (61 fișiere Draft-07 compliant)

.agents/skills/learn/references/
  └── caiet_de_teme_si_aplicatii_practice.md              # 60 de Teme practice (4.696 linii)
```

### Tabelul de Corelare a celor 10 Tiers cu Teme Practice

| Tier | Domeniu & Cărți de Referință | Concepte Fundamentale Tratate | Teme Practice Alocate |
|:---:|:---|:---|:---:|
| **Tier 1** | **Arhitectură & Baze de Date Distribuite**<br>*(Designing Data-Intensive Applications — Kleppmann)* | Tranzacții ACID, izolare SSI, moduri de stocare (LSM vs B-Tree), SQLite WAL, consens Raft/Paxos, replicare master-slave. | **Teme 1 – 6** |
| **Tier 2** | **Inteligență Artificială Modernă**<br>*(AIMA 4th Ed. — Russell & Norvig)* | Agenți raționali, planificare clasică PDDL, căutare euristică A*, decizii markoviene (MDP), teoria jocurilor. | **Teme 7 – 12** |
| **Tier 3** | **Arhitectura Sistemelor de Agenți AI**<br>*(Agentic Architectures — Zvarydchuk, Weng)* | Buclă OODA (Observe-Orient-Decide-Act), ReAct, Reflexion, delegare de roluri cu privilegii minime, auto-corecție. | **Teme 13 – 18** |
| **Tier 4** | **Ingineria Aplicațiilor cu LLM & RAG**<br>*(Building LLM Apps — Chip Huyen, Briggs)* | RAG ierarhic, chunking semantic, vector stores hibride, GraphRAG, reranking Cross-Encoder, evaluare RAGAs. | **Teme 19 – 24** |
| **Tier 5** | **Sisteme de Machine Learning în Producție**<br>*(Designing ML Systems — Chip Huyen)* | Data drift, concept drift, feature stores, monitorare continuă a performanței, pipeline-uri de inferență reziliente. | **Teme 25 – 30** |
| **Tier 6** | **Deep Learning & Rețele Neuronale**<br>*(Deep Learning — Goodfellow, Bengio, Courville)* | Backpropagation matematic, regularizare, Transformer self-attention, optimizatori adaptivi (AdamW), dinamica gradientului. | **Teme 31 – 36** |
| **Tier 7** | **Sisteme de Memorie Avansată & Prompt Eng.**<br>*(Memory Systems for LLMs — Pai, Liu)* | Memorie episodică vs semantică, benchmark-uri LoCoMo/LongMemEval, demarcare instrucțiuni/date XML, bugete de context. | **Teme 37 – 42** |
| **Tier 8** | **Securitate Ofensivă/Defensivă & Hardening**<br>*(OWASP LLM Top 10, NIST AI RMF)* | Indirect prompt injection, jailbreak defenses, audit SHA-256 tamper-evident, invariante I-001..I-012, atestare umană. | **Teme 43 – 48** |
| **Tier 9** | **Scalare, Concurrency & Performanță**<br>*(High-Performance Systems — C#, Rust, Go)* | Tranzacții SQLite concurente (`BEGIN IMMEDIATE`), asincronism Go routines/Rust Tokio, optimizare memorie, pooling. | **Teme 49 – 54** |
| **Tier 10** | **Frontiera Cercetării Cognitive & Neuro-AI**<br>*(Neuromorphic Computing, GWT, Active Inference)* | Neuroni Leaky Integrate-and-Fire (LIF), sinapse plastice STDP, Global Workspace Theory, inferență activă Karl Friston. | **Teme 55 – 60** |

*Fiecare temă include cerințe riguroase, contract de date, cod Python complet executabil, aserțiuni de testare și instrucțiuni de rulare directă.*

---

## 🚀 Proiecte Ecosistem & Aplicații Integrate

În folderul `projects/` sunt găzduite implementările pilot care folosesc Vault-ul ca creier persistent:

### 1. `projects/jarvis_cognitive_brain` (Creier Cognitiv Autonom)
Un asistent cognitiv local autonom ("Creier Vorbitor") construit pe o buclă OODA completă:
- **Buclă OODA Completă**: `Observe` (clasificare voce/text) $\to$ `Retrieve` (recuperare asociativă din Vault) $\to$ `Reason/Plan` $\to$ `Act` (execuție scule FastMCP) $\to$ `Reflect/Consolidate` (salvare lecții).
- **Cascaded Audio Engine**: Captură continuă cu Silero VAD (prag tăcere 500ms), transcriere STT locală prin `faster-whisper` și sinteză vocală neurală locală prin modelul `Kokoro-82M` (ONNX).
- **Mecanism Barge-in / AEC**: Întreruperea instantanee a redării audio și anularea inferenței LLM active la detecția vocii utilizatorului.
- **FastMCP IoT Home Assistant**: Server de scule MCP (`JarvisControls`) care interoghează și comandă dispozitivele smart-home printr-un API local simulat (`/api/states`).
- **Web HUD 3D**: Panou de control grafic în timp real care afișează stările vocale, etapele de gândire și graful de memorie activ.

### 2. `projects/jarvis_web` (Asistent Vocal Web Standalone)
- Interfață de comandă bazată pe Web Speech API (recunoaștere și sinteză vocală 100% gratuită în browser).
- Vizualizator holografic 3D reactor WebGL creat cu Three.js, randat la 60 FPS.
- Efecte sonore sci-fi tactice sintetizate direct prin Web Audio API (fără fișiere audio externe).
- Conectat direct la REST API-ul Memory Vault (`http://127.0.0.1:8000/api/v1/search`).

---

## 🔌 Ecosistemul Claude Code Plugin & Obsidian

Repository-ul este configurat din start ca un plugin Claude Code oficial:

```text
.claude-plugin/
  ├── plugin.json         # Metadatele oficiale ale pluginului Claude
  └── marketplace.json    # Înregistrarea în catalogul de plugin-uri
skills/
  └── ai-memory-vault/    # Punctul de intrare pentru Claude Code
commands/
  ├── memory.md           # Comanda /memory (căutare și inspectare)
  ├── memory-sync.md      # Comanda /memory-sync (sincronizare cu Obsidian)
  └── memory-status.md    # Comanda /memory-status (verificare integritate și stocare)
```

### Sincronizarea Semantică cu Obsidian
Obsidian funcționează ca interfața vizuală de navigare și audit a aceluiași Vault, fără a dubla nucleul cognitiv:
- MOC-uri centrale în `00_CORE/GRAPH/` (inclusiv `07 Knowledge Domains Map.md`).
- Toate notele folosesc legături bidirecționale standard `[[wikilinks]]`.
- Frontmatter-ul YAML respectă schema canonică: `category`, `status`, `version`, `confidence`, `verification`, `provenance_status`, `relations`.

---

## ⚡ Ghid de Pornire Rapidă, Testare & Verificare

### Cerințe Preliminare
- Python 3.11+ (recomandat Python 3.11 / 3.12 / 3.14)
- Git
- PowerShell (pe Windows) sau Bash (pe Linux/macOS)

### Clonare & Instalare
```powershell
# Clonare repository
git clone https://github.com/userist123/AI_Memory_Vault_CODEX_READY.git
cd AI_Memory_Vault_CODEX_READY

# Instalare dependențe minime (dacă este necesar)
pip install pytest pyyaml
```

### Rularea Suitei de Teste Pytest (Verificarea Criteriilor de Done)
Toate cele 518 teste din `cognitive_core/tests/` trebuie să treacă impecabil cu 0 erori:

```powershell
# Rulare suita completă de teste cognitive & de securitate
python -m pytest cognitive_core/tests/ -q

# Rezultat așteptat:
# 518 passed, 2 skipped in ~15s
```

### Verificarea Integrității Lanțului Criptografic de Audit
```powershell
python -c "from memory_controller.audit.logger import AuditLogger; logger = AuditLogger(); print('Audit chain valid:', logger.verify_integrity())"
# Rezultat așteptat: Audit chain valid: True
```

### Interogare Rapidă prin CLI Securizat (Offline Fallback I-RETRIEVAL)
```powershell
# Interogare securizată conform invariantului I-RETRIEVAL
python -m cognitive_core.recall_cli --query "SQLite WAL"
```

---

## 🤝 Protocolul de Coordonare Multi-Agent

Atunci când mai mulți agenți autonomi sau asistenți de programare (Antigravity, Claude Code, Codex, ChatGPT) operează pe acest repository, aceștia trebuie să respecte cu strictețe următorul protocol:

1. **Unica Sursă de Adevăr pentru Lucru Activ**: Fisierele `09_COORDINATION/todo.md` și `09_COORDINATION/lessons.md` reprezintă stratul canonic de coordonare.
2. **Verificare Pre-Flight**: Înainte de a atinge orice fișier sau cod, agentul verifică `09_COORDINATION/todo.md` pentru a se asigura că o altă sesiune nu lucrează deja pe aceeași sarcină.
3. **Proprietate Asumată (Ownership)**: Când începe o sarcină, agentul o revendică în `todo.md`. La finalizare, adaugă marcajul `owner: <agent_name>` și timestamp ISO 8601.
4. **Nucleul Protejat (Protected Core Invariant)**: Modulele cognitive înghețate (`Planner`, `PlanComplexityAnalyzer`, `CouncilBudgetController`, `Council_Orchestrator.py`, `ContextPackBuilder`, `council_token_telemetry.py`) **nu pot fi modificate** fără specificație auditată explicit. Respectarea este verificată de `cognitive_core/tests/test_protected_core_boundaries.py`.
5. **Dovezi Empirice Reale (Fără Speculație)**: Nicio sarcină nu este marcată completă fără atașarea dovezilor de execuție directă (ieșirea testelor `pytest` trecute cu succes).

---

## 📜 Criterii de Finalizare (Definition of Done)

O sarcină sau actualizare în AI Memory Vault este considerată completă exclusiv atunci când:
- [x] Sistemul de fișiere, registrele JSON și frontmatter-ul YAML sunt 100% coerente.
- [x] Proveniența fiecărei informații este înregistrată imutabil conform invariantelor `I-001..I-012`.
- [x] Toate modificările de cod trec suita `pytest` (518 teste validate, 0 eșecuri).
- [x] Lanțul criptografic SHA-256 din `audit_log.jsonl` rămâne valid și neîntrerupt.
- [x] Granița `RAW_EXTERNAL` este respectată (niciun fișier extern nu rulează neautorizat).
- [x] Graful MOC din Obsidian (`00_CORE/GRAPH/`) este sincronizat semantic.
- [x] Modificările sunt documentate clar în `09_COORDINATION/todo.md` și `lessons.md`.

---

## 🔗 Legături Utile & Documente Canonice

- **Registrul Central de Skill-uri (251)**: [`01_KNOWLEDGE/Master_Skills_Catalog_251.md`](01_KNOWLEDGE/Master_Skills_Catalog_251.md)
- **Matricea Agenți ↔ Skill-uri**: [`01_KNOWLEDGE/Agents_Skill_Matrix.md`](01_KNOWLEDGE/Agents_Skill_Matrix.md)
- **Caietul de Teme Practice (60 Teme)**: [`.agents/skills/learn/references/caiet_de_teme_si_aplicatii_practice.md`](.agents/skills/learn/references/caiet_de_teme_si_aplicatii_practice.md)
- **Sinteza Cărților Fundamentale (Tiers 1–10)**: [`01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md`](01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md)
- **Harta Domeniilor de Cunoștințe (MOC)**: [`00_CORE/GRAPH/07 Knowledge Domains Map.md`](00_CORE/GRAPH/07%20Knowledge%20Domains%20Map.md)
- **Arhitectura Memory V6**: [`99_SYSTEM/Memory_V6_Architecture.md`](99_SYSTEM/Memory_V6_Architecture.md)
- **Contractul de Sincronizare Obsidian**: [`99_SYSTEM/Obsidian_Skill_Agent_Memory_Sync.md`](99_SYSTEM/Obsidian_Skill_Agent_Memory_Sync.md)
- **Contractul de Securitate Phase 4.3 P0**: [`99_SYSTEM/Phase43_P0_Implementation_Contract.md`](99_SYSTEM/Phase43_P0_Implementation_Contract.md)

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[07 Knowledge Domains Map]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
