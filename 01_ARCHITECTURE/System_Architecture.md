---
type: core
category: architecture
status: active
version: 1.0.0
id: "330fa4bc-5b7c-4fb0-8d80-bcfa148a29c9"
document_kind: specification
document_status: active
provenance_status: incomplete
relations: []
implementation_status: documentation_only
---

# System Architecture

## High-Level

```text
USER
  |
  v
PERCEPTION
  |
  v
ROUTER
  |
  +--> MEMORY RETRIEVAL
  |
  +--> PLANNER
  |
  +--> SECURITY / POLICY
          |
          v
       REASONER
          |
          v
     DECISION ENGINE
          |
          v
       TOOL BUS
          |
          v
       VALIDATOR
       /       \
     PASS      FAIL
      |          |
      v          v
   OUTPUT      REPLAN
                 |
                 v
              WATCHDOG
```

## Layers (Numbered Semantic Spine)

### 00_GOVERNANCE
Regulile, identitatea sistemului, protocoalele de execuție multi-agent, revizuirea și coordonarea agenților.

### 01_ARCHITECTURE
Arhitectura de sistem, cunoștințe canonice durabile (`01_ARCHITECTURE/knowledge/`), grafuri și hărți MOC (`01_ARCHITECTURE/graphs/`), și memoria structurată (`01_ARCHITECTURE/memory/`).

### 02_PRODUCT
Specificații de produs, obiective globale (`Goals.md`) și starea de continuitate a proiectelor (`02_PRODUCT/projects/`).

### 03_IMPLEMENTATION
Cod de producție, componente și servicii aplicație.

### 04_CONFIG
Configurații runtime mașină, bugete agenți și mapări de modele.

### 05_DATA
Stocare locală, baze de date SQLite WAL și persistență.

### 06_INBOX
Captură și import brut (izolat, local-only conform contractului).

### 07_EVALUATION
Rapoarte de evaluare, benchmark-uri, scoruri de realitate și evidențe empirice.

### 08_OBSERVABILITY
Telemetrie, trasabilitate și probe de monitorizare.

### 09_SECURITY
Invarianți de securitate, audituri de graniță de încredere și conformitate.

### 10_DOCUMENTATION
Proceduri operaționale verificabile (`10_DOCUMENTATION/procedures/`), resurse externe și documentații (`10_DOCUMENTATION/resources/`).

### 20_TESTS
Infrastructură și suite de testare automată.

### 30_SCRIPTS
Unelte operaționale, mentenanță și scripturi de ingestie.

### 40_EXPERIMENTS
Harness-uri experimentale și validări empirice.

### 50_ARTIFACTS
Pachete generate, programe construite și exporturi.

### 80_ARCHIVE
Duplicate moștenite, snapshot-uri și fișiere istorice arhivate.

### 99_META
Jurnale de migrare, template-uri canonice și inventare de metadate.

## Data Flow

`Input -> Classification -> Retrieval -> Planning -> Reasoning -> Action -> Validation -> Memory Update`

## Principle

Modelul AI nu este sursa unica de adevar. Vault-ul furnizeaza context, iar validatorul decide daca rezultatul este suficient de sigur.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
