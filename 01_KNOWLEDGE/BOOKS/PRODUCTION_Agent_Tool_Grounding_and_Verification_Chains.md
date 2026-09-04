---
id: af8f06d3-d304-59cf-b06b-b4ba406b78d4
type: knowledge
lifecycle: REVIEW
category: agents/tool_grounding_verification
tags:
- agent-architecture
- zvarydchuk
- tool-use
- grounding
- verification-chain
- evidence-based
- chain-of-verification
- hallucination-prevention
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Zvarydchuk-Building-Agent-Powered-Apps-Ch5-Ch7
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/EXPERT_Agent_State_Checkpoints_and_Human_in_the_Loop.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/HARDENING_Agent_Adversarial_Defense_and_Egress_Firewalls.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# Agent Production: Grounding pe Instrumente și Lanțuri de Verificare

**Sursă**: Vasyl Zvarydchuk, *Building Agent-Powered Applications* (Capitolele 5-7: Tool Use Patterns & Verification)
**Domeniu**: Fiabilitatea Executării Instrumentelor, Prevenirea Halucinațiilor Agenților, Lanțuri de Evidență

---

## 1. Problema Grounding-ului în Agenți

Un agent care folosește instrumente (tool-calling) trebuie să:
1. **Selecteze** instrumentul corect pentru sarcină
2. **Parametrizeze** corect apelul
3. **Verifice** output-ul instrumentului înainte de a-l folosi în raționament
4. **Atribuie** rezultatul la sursa corectă (proveniență)

### 1.1 Taxonomia Erorilor de Tool Use

| Tip Eroare | Descriere | Frecvență |
| :--- | :--- | :--- |
| **Tool Misselection** | Agentul alege un instrument inadecvat | ~15% din eșecuri |
| **Parameter Hallucination** | Parametri fabricați (ex: URL inexistent) | ~25% din eșecuri |
| **Output Misinterpretation** | Agentul ignoră sau distorsionează rezultatul | ~30% din eșecuri |
| **Missing Verification** | Agentul acceptă output-ul fără validare | ~30% din eșecuri |

---

## 2. Chain-of-Verification (CoVe)

### 2.1 Protocolul în 4 Pași

```
Pas 1: GENERATE — Agentul produce un răspuns inițial
Pas 2: PLAN VERIFICATION — Generează întrebări de verificare specifice
Pas 3: EXECUTE VERIFICATION — Execută verificările (tool calls, search)
Pas 4: REVISE — Corectează răspunsul pe baza evidenței
```

### 2.2 Exemplu Concret

```
User: "Care este populația Clujului?"

Pas 1: "Populația Clujului este ~325,000 locuitori"
Pas 2: Verificări planificate:
  - Q1: "Care este sursa acestei cifre?"
  - Q2: "Când a fost ultimul recensământ?"
Pas 3: Tool call: search("populație Cluj-Napoca recensământ 2021")
  → Rezultat: "324,576 conform INS 2021"
Pas 4: Răspuns revizuit cu citare: "324,576 locuitori (INS, 2021)"
```

---

## 3. Tool Output Validation Patterns

### 3.1 Schema Validation Gate

```python
def validate_tool_output(output: dict, expected_schema: dict) -> bool:
    """Validează output-ul instrumentului contra schemei așteptate."""
    required_fields = expected_schema.get("required", [])
    for field in required_fields:
        if field not in output:
            raise ToolOutputError(f"Missing required field: {field}")
    
    # Type checking
    for field, expected_type in expected_schema.get("types", {}).items():
        if field in output and not isinstance(output[field], expected_type):
            raise ToolOutputError(f"Type mismatch: {field}")
    
    return True
```

### 3.2 Confidence-Gated Forwarding

Nu toate output-urile de instrumente au aceeași fiabilitate:

| Sursă | Confidence | Politică |
| :--- | :--- | :--- |
| Bază de date internă (SQL query) | 🟢 High | Forward direct |
| API extern verificat | 🟡 Medium | Forward cu citare |
| Web search rezultat | 🟠 Low-Medium | Cross-verificare necesară |
| Inferență model (no tool) | 🔴 Low | Necesită grounding |

### 3.3 Idempotent Tool Retry

Când un instrument eșuează, agentul trebuie să:
1. **Clasifice** eroarea: tranzientă (timeout, 503) vs permanentă (404, 403)
2. **Retry cu backoff** pentru erori tranziente (max 3 încercări)
3. **Fallback** la instrument alternativ pentru erori permanente
4. **Raporteze** eșecul dacă nicio alternativă nu funcționează

---

## 4. Evidence Attribution Chain

### 4.1 Structura unui Evidence Record

```json
{
  "claim": "Populația Cluj-Napoca este 324,576",
  "evidence_type": "tool_output",
  "tool_name": "web_search",
  "tool_call_id": "call_abc123",
  "source_url": "https://insse.ro/...",
  "retrieval_timestamp": "2026-09-04T19:00:00Z",
  "confidence": "high",
  "verification_status": "cross_verified"
}
```

### 4.2 Chain of Custody pentru Output-uri

```
Tool Output → Schema Validation → Confidence Gate → Evidence Record → Agent Response
     ↓              ↓                    ↓                ↓
  Raw data      Type check          Threshold         Provenance
                                    filter            attribution
```

---

## 5. Grounding în Memory Vault

### 5.1 Principiul I-002 Aplicat la Tool Use

Din `I-002 (Privileged Provenance Gated)`:
- Tool output-urile sunt clasificate ca `source_type: execution`
- Agentul NU poate pretinde că output-ul vine de la `user` sau `official`
- Lanțul de evidență trebuie păstrat intact

### 5.2 XML Demarcation Pattern (din `AGENTS.md`)

```xml
<tool_output tool="web_search" call_id="call_abc123" confidence="medium">
  Populația Cluj-Napoca conform recensământului 2021 este 324,576.
</tool_output>
```

Acest pattern izolează datele recuperate de instrucțiunile agentului, prevenind prompt injection prin tool output.

---

## 6. Anti-Patternuri de Tool Grounding

| Anti-Pattern | Risc | Soluție |
| :--- | :--- | :--- |
| Acceptarea orbească a output-ului | Propagare de erori/halucinații | Schema validation + CoVe |
| Tool output ca instrucțiune | Prompt injection prin date | XML demarcation boundary |
| Retry infinit fără backoff | DoS pe serviciul extern | Exponential backoff + max 3 |
| Lipsa atribuirii | Imposibilitate de audit | Evidence attribution chain |

---

## Referințe Obsidian

- [[EXPERT_Agent_State_Checkpoints_and_Human_in_the_Loop]]
- [[HARDENING_Agent_Adversarial_Defense_and_Egress_Firewalls]]
- [[CAPSTONE_Agent_Swarm_Blackboard_and_Dynamic_Orchestration]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
