---
id: 92846e4c-f1e9-5603-83da-0c78116e6410
type: knowledge
lifecycle: REVIEW
category: architecture/agent_protocols
tags:
- agent-architecture
- zvarydchuk
- fastmcp
- mcp-protocol
- tool-sandboxing
- least-privilege
- schema-validation
- memory-types
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: "06_INBOX/RAW_IMPORTS/BOOKS/Vasyl-Zvarydchuk-Agent-Powered-Apps"
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Agent_Architecture_and_Tool_Orchestration.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/System_Architecture.md
---

# Agenți Avansați: Protocoale de Unelte, FastMCP & Izolarea Sandboxing

**Sursă**: Vasyl Zvarydchuk, *Building Agent-Powered Applications*  
**Domeniu**: Protocoale de Interfațare (MCP), Securitatea Execuției & Taxonomia Memoriei

---

## 1. Arhitectura Protocolului Model Context Protocol (FastMCP)

FastMCP standardizează expunerea capabilităților locale/remote către modele de limbaj prin mesaje JSON-RPC 2.0:
- **Descoperirea Capabilităților (Capabilities Negotiation)**: La conectare, clientul și serverul negociază versiunea protocolului și setul de resurse (`resources`), unelte (`tools`) și prompturi (`prompts`).
- **Reflecția Schemelor (Schema Reflection)**: Fiecare unealtă își generează automat schema Draft-07 din clase Pydantic sau type-hints Python, garantând validarea strictă a argumentelor înainte ca funcția nativă să fie invocată.
- **Canale de Transport**:
  - `stdio`: Comunicație locală securizată prin pipe-uri standard, fără expunere de porturi de rețea.
  - `SSE` (Server-Sent Events) peste HTTP: Comunicație distribuită cu streaming asincron și autentificare prin token de sesiune.

---

## 2. Izolarea Uneltelor (Tool Sandboxing) & Limitarea Riscurilor

Orice apel de unealtă de către un agent autonom prezintă riscuri de securitate (Path Traversal, Denial of Service, Command Injection).

### Cele 5 Bariere de Izolare
1. **Validare Sintactică**: Type checking riguros pe argumente prin Pydantic (`strict=True`, `extra='forbid'`).
2. **Limitare de Spațiu de Lucru (Chroot / Workspace Boundary)**: Calea oricărui fișier accesat trebuie rezolvată cu `os.path.realpath` și verificată că are prefixul `WORKSPACE_ROOT`. Orice secvență `..` sau link simbolic către afara spațiului declanșează excepția `SecurityBoundaryViolation`.
3. **Plafon Temporal (Execution Timeout)**: Fiecare apel are un timeout strict (ex: 5000ms); la expirare, procesul este ucis automat via `SIGKILL`/`terminate()`.
4. **Scoping Least Privilege**: Agenții au acces strict la subsetul de unelte declarat în profilul lor (`Agent_Capability_Registry.md`).
5. **Jurnalizare Tamper-Evident**: Orice invocare de unealtă și rezultatul acesteia generează un eveniment criptografic în `audit_log.jsonl`.

---

## 3. Taxonomia Memoriei în Sistemele de Agenți

Zvarydchuk definește 4 tipuri ortogonale de memorie pentru agenți:
1. **Memorie de Scurtă Durată (Context / Working Memory)**: Bufferul activ de jetoane din fereastra promptului (`wm.json`). Se comprimă dinamic la depășirea bugetului.
2. **Memorie Episodică (Execution Traces)**: Jurnalul detaliat al interacțiunilor trecute, al pașilor parcurși și al rezultatelor uneltelor (`telemetry/execution_traces/`).
3. **Memorie Semantică (Knowledge Vault)**: Fapte durabile, reguli, definiții de sisteme și cărți structurate în Markdown Obsidian (`01_KNOWLEDGE/`).
4. **Memorie Procedurală (Skills & SOPs)**: Instrucțiuni pas cu pas și scripturi reutilizabile pentru îndeplinirea unor sarcini specializate (`.agents/skills/`).

---

## 4. Playbook Operațional: Ce fac când integrez o nouă unealtă în sistem?

1. **Definesc modelul Pydantic pentru argumente**: Nu accept niciodată `**kwargs` libere sau `dict` nevalidat.
2. **Aplic decoratorul de validare a limitelor de disc**: Verific că toate căile sunt închise ermetic în workspace.
3. **Tratez apelurile ca tranzacții auditate**: Înregistrez actorul, operația și hash-ul rezultatului în audit log.
