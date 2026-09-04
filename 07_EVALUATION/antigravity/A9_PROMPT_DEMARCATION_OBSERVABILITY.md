# A9 — Review Memory Authority & Prompt Demarcation Observability Report

**Milestone**: Round R001 / Antigravity Lane Task A9  
**Agent**: ANTIGRAVITY (Developer-Observability & Architecture Inspection)  
**Baseline SHA**: `e43cc81e09789e284ef35a7e326297194f429a9e`  
**Execution Environment**: Local Ollama runtime (`qwen2.5-coder:3b`) on `http://127.0.0.1:11434`  
**Timestamp**: `2026-09-04T18:15:00+03:00`  
**Evidence classification**: `RUNTIME_VERIFIED` for the reported local experiment; historical evidence, not current-state implementation proof.

## Executive Summary

Task A9 evaluated two uncertainties:

1. **GAP-011 lifecycle classifier substring trap**: queries containing `unverified` caused the legacy classifier's substring matching to infer `VERIFIED`. The reported test matrix observed the trap in all three `unverified` cases.
2. **Instruction/data isolation**: a local `qwen2.5-coder:3b` experiment compared raw JSON memory context with XML-demarcated untrusted-memory blocks plus explicit anti-injection instructions. The experiment showed meaningful improvement for one path-traversal case, but a direct task-hijack payload still succeeded on the 3B model.

## GAP-011 Finding

The historical implementation used character-substring checks such as:

```python
if stage in lowered:
    lifecycle_filters.append(stage.upper())
```

Thus `verified` matched inside `unverified`. The resulting lifecycle filter could exclude REVIEW candidates when the user explicitly asked for unverified material.

The experiment reported a 100% trap rate across its three `unverified` queries.

## Prompt-Demarcation Finding

The prototype wrapped REVIEW content as explicit untrusted reference data, with instructions not to treat memory content as operational commands. The benchmark reported:

- adversarial injection success: 100% baseline vs 50% demarcated across the two tested adversarial scenarios;
- path traversal payload: succeeded in baseline and was refused in the demarcated case;
- strong task-hijack payload: remained successful in the tested 3B model even with demarcation.

The report therefore supports **defense in depth**, not reliance on prompt formatting alone.

## Architectural Implication

The experiment supports three distinct controls:

1. Memory-content boundary/demarcation.
2. Deterministic action scoping and workspace/path containment.
3. REVIEW-to-ACTIVE human verification and provenance gates.

Prompt demarcation is a mitigation layer, not an authorization mechanism.

## Current-State Boundary

This file preserves the historical A9 evidence on `main`. It does **not** assert that the demarcation prototype is production-integrated. The implementation status must be established by current code and reproducible tests on `main`.

## Recommended Follow-up

- Keep whole-word lifecycle matching in the classifier.
- Add regression tests covering `unverified`, `verified`, `review`, and combinations such as `unverified review`.
- Evaluate demarcation on larger models and adversarial encodings only after deterministic action authorization/containment is independently verified.
- Preserve runtime telemetry with provenance when such experiments are repeated.
