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

## Layers

### 00_CORE

Regulile si identitatea sistemului.

### 01_KNOWLEDGE

Cunostinte stabile si reutilizabile.

### 02_PROJECTS

Starea proiectelor.

### 03_PROCEDURES

Proceduri verificabile.

### 04_MEMORY

Experiente, erori, lectii, decizii, preferinte.

### 05_RESOURCES

Surse si materiale de referinta.

### 06_INBOX

Captura si import brut.

### 90_TEMPLATES

Modele pentru note noi.

### 99_SYSTEM

Specificatii pentru clasificare, RAG, graph si control.

## Data Flow

`Input -> Classification -> Retrieval -> Planning -> Reasoning -> Action -> Validation -> Memory Update`

## Principle

Modelul AI nu este sursa unica de adevar. Vault-ul furnizeaza context, iar validatorul decide daca rezultatul este suficient de sigur.
