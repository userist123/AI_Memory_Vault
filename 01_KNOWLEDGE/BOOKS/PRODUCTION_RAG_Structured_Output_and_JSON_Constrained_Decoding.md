---
id: b5e54702-b7c4-5354-9ffd-462d40d5e00d
type: knowledge
lifecycle: REVIEW
category: llm/structured_output_constrained_decoding
tags:
- llm-apps
- pai
- structured-output
- json-schema
- constrained-decoding
- grammar-guided
- pydantic
- function-calling
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Pai-Designing-LLM-Apps-Ch4-Ch6
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/EXPERT_RAG_Speculative_Decoding_and_Prefix_Caching.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/CAPSTONE_RAG_DPO_Alignment_and_Contrastive_Reasoning.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# LLM Apps Production: Ieșiri Structurate și Decodificare Constrânsă pe JSON Schema

**Sursă**: Suhas Pai, *Designing Large Language Model Applications* (Capitolele 4-6: Structured Outputs & Reliability)
**Domeniu**: Fiabilitatea Output-urilor, Grammar-Guided Generation, JSON Schema Enforcement

---

## 1. Problema Output-urilor Nestructurate

LLM-urile generează text liber. Când o aplicație necesită JSON valid, apar:

| Tip Eșec | Frecvență | Consecință |
| :--- | :--- | :--- |
| JSON invalid (lipsă `}`) | ~5-15% | `json.loads()` crash |
| Câmp lipsă | ~10-20% | `KeyError` downstream |
| Tip incorect (string vs int) | ~5-10% | Logică de business eronată |
| Câmpuri extra / hallucinate | ~5-8% | Date neașteptate în pipeline |

---

## 2. Strategii de Structurare (de la slab la puternic)

### 2.1 Prompt Engineering (Slab)

```
Răspunde EXCLUSIV cu JSON valid în formatul:
{"name": string, "age": int, "city": string}
```

**Limitări**: Modelul poate ignora instrucțiunea, specialmente la output-uri lungi.

### 2.2 JSON Mode (Mediu)

API-urile moderne (OpenAI, Anthropic) oferă `response_format: { type: "json_object" }`:
- Garantează JSON valid sintactic
- **NU garantează** conformitate cu schema specifică
- Modelul poate genera câmpuri extra sau omite câmpuri obligatorii

### 2.3 JSON Schema Constrained Decoding (Puternic)

```python
# OpenAI Structured Outputs
response = client.chat.completions.create(
    model="gpt-4o",
    response_format={
        "type": "json_schema",
        "json_schema": {
            "name": "person",
            "strict": True,
            "schema": {
                "type": "object",
                "properties": {
                    "name": {"type": "string"},
                    "age": {"type": "integer", "minimum": 0},
                    "city": {"type": "string"}
                },
                "required": ["name", "age", "city"],
                "additionalProperties": False
            }
        }
    }
)
```

**Mecanism intern**: La fiecare pas de decodificare, tokenii care ar produce JSON invalid sunt mascați (probability → 0).

### 2.4 Grammar-Guided Generation (Foarte Puternic)

Sisteme precum `llama.cpp` / `Outlines` / `vLLM` folosesc finite-state automata:

```
JSON_GRAMMAR:
  value   → object | array | string | number | "true" | "false" | "null"
  object  → "{" (pair ("," pair)*)? "}"
  pair    → string ":" value
  ...
```

La fiecare token, automatul permite doar tokeniii care mențin gramatica validă.

---

## 3. Pydantic ca Schemă de Validare

### 3.1 Pattern-ul Standard

```python
from pydantic import BaseModel, Field
from typing import Literal

class MemoryNote(BaseModel):
    id: str = Field(pattern=r'^[0-9a-f]{8}-.*')
    type: Literal["knowledge", "project", "procedure"]
    lifecycle: Literal["RAW", "CLASSIFIED", "NORMALIZED", "REVIEW", "ACTIVE"]
    category: str
    tags: list[str] = Field(min_length=1)
    confidence: Literal["low", "medium", "high"]

# Validare
note = MemoryNote.model_validate_json(llm_output)  
# Aruncă ValidationError dacă schema nu este respectată
```

### 3.2 Avantaje peste JSON Schema Raw

| Aspect | JSON Schema | Pydantic |
| :--- | :--- | :--- |
| **Validare** | Static (compile-time) | Runtime + type hints |
| **Coercion** | Limitată | `"42"` → `42` automat |
| **Custom validators** | Nu | `@field_validator` |
| **Serializare** | Manual | `.model_dump_json()` |
| **Docstring** | `description` field | Inline Python docstrings |

---

## 4. Function Calling ca Structurare Implicită

### 4.1 Mecanismul

Când un model primește definiția unei funcții:
```json
{
  "name": "search_memory",
  "parameters": {
    "type": "object",
    "properties": {
      "query": {"type": "string"},
      "limit": {"type": "integer", "default": 5}
    },
    "required": ["query"]
  }
}
```

Modelul produce un **tool call structurat** care respectă schema — este echivalent cu constrained decoding pe schema funcției.

### 4.2 Parallel Function Calls

Modele precum GPT-4o pot genera **multiple tool calls** într-un singur turn:
```json
[
  {"name": "search_memory", "arguments": {"query": "DDIA chapter 3"}},
  {"name": "search_memory", "arguments": {"query": "LSM-Tree compaction"}}
]
```

Agentul executor trebuie să:
1. Verifice că fiecare tool call are parametri valizi
2. Execute apelurile în paralel (dacă independente)
3. Agreghe rezultatele pentru pasul următor

---

## 5. Retry și Self-Healing

### 5.1 Retry cu Feedback de Validare

```python
for attempt in range(MAX_RETRIES := 3):
    raw = llm.generate(prompt)
    try:
        result = MemoryNote.model_validate_json(raw)
        return result
    except ValidationError as e:
        prompt = f"Output-ul anterior a eșuat validarea:\n{e}\nCorectează și re-generează."

raise StructuredOutputError("Failed after 3 attempts")
```

### 5.2 Fallback Hierarchy

```
JSON Schema Constrained Decoding (100% valid)
    ↓ (nedisponibil)
JSON Mode + Pydantic Validation + Retry
    ↓ (3 eșecuri)
Regex Extraction din text liber
    ↓ (eșec)
Eroare explicită cu context
```

---

## 6. Aplicabilitate în Memory Vault

- **`validate_frontmatter()`** din `memory_controller/validation/schema.py` este exact un JSON Schema validator aplicat pe YAML frontmatter
- **Note AI-generate** trebuie să respecte schema Draft-07 — constrained decoding ar garanta asta la generare
- **Tool calls** din agentul vault (search, attest, archive) sunt deja structurate prin definiții de funcții

---

## Referințe Obsidian

- [[EXPERT_RAG_Speculative_Decoding_and_Prefix_Caching]]
- [[CAPSTONE_RAG_DPO_Alignment_and_Contrastive_Reasoning]]
- [[HARDENING_RAG_Context_Poisoning_and_Adversarial_Retrieval]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
