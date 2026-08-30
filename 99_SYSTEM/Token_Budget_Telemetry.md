---
type: system
category: observability
status: active
version: 1.0.0
document_kind: telemetry_contract
document_status: active
---

# Token Budget Telemetry

The council runtime should record context cost without storing full prompts or sensitive content.

## Required counters

```yaml
run_id: ""
complexity: simple|moderate|complex|high-risk
agents_selected: 0
skills_selected: 0
memory_items_selected: 0
input_tokens_estimate: 0
specialist_output_tokens: 0
synthesis_input_tokens: 0
rejected_context_items: 0
deduplicated_context_items: 0
staged_rounds: 0
```

## Budget checks

```text
agents_selected <= 3
skills_selected <= agents_selected * 2
memory_items_selected <= 5
synthesis_input_tokens <= 2500
specialist_output_tokens <= 600 per specialist
```

A limit may be exceeded only by staged execution with an explicit reason.

## Privacy

Telemetry must contain counters and identifiers only. Do not persist full prompts, secrets, raw files, credentials, or complete memory contents in telemetry.

## Regression rule

Flag a token regression when the same task class increases estimated input context by more than 20% without a documented capability or evidence gain.
