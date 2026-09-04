# JARVIS AI Memory Vault Command Center

JARVIS este control plane-ul vizual si operational peste AI Memory Vault V6.

## Arhitectura

```text
Browser
  ↓
JARVIS Command Center :3000
  ↓
Memory Vault API :8000
  ├── /status
  ├── /metrics
  ├── /agents
  ├── /skills
  ├── /proposals
  ├── /search
  ├── /route
  └── /propose
  ↓
MemoryController / Cognitive Core / Agent Registry / Operational Skills
```

JARVIS nu este o memorie separata. Vault-ul ramane sursa canonica.

## Functionalitati

- Memory retrieval pe memoria canonica.
- Agent Council real din `data/agents.json`.
- Agent routing pe domeniu si skill-uri.
- Skill registry din `.agents/skills/**/SKILL.md`.
- Memory V6 proposal queue.
- Controlled memory proposal prin `MemoryController`.
- Vault metrics live.
- Diagnostics pentru API, agent registry si skill registry.
- Execution timeline pentru actiunile efective din Command Center.
- Responsive full-screen HUD cu rail stanga, core central si intelligence rail dreapta.

## Design contract

Skill-ul operational folosit pentru acest produs este:

`.agents/skills/jarvis-command-center/SKILL.md`

El extinde `ui-sensei`, `web_design_engineer_agent`, `web_creative_developer` si `web_quality_engineer` pentru stilul Command Center.

Principii:

- dark Obsidian + cyan/blue operational HUD;
- 4/8/16/24px rhythm;
- informatie densa, fara ornament fara functie;
- un singur focal point: JARVIS core;
- keyboard focus si reduced motion;
- fara valori de telemetry prezentate ca fiind reale daca API-ul nu le furnizeaza.

## Pornire Windows

Din `projects/jarvis_web`:

```bat
start.bat
```

Launcherul este relativ la checkout si porneste:

```text
Memory Vault API     http://127.0.0.1:8000
JARVIS Command Center http://127.0.0.1:3000
```

## Teste

Web smoke test:

```bat
node test\smoke_test.cjs
```

API smoke test read-only:

```bat
python test\api_smoke_test.py
```

## Servicii necesare

- Python 3.x
- Node.js

Nu sunt necesare dependente npm pentru Command Center-ul local.
