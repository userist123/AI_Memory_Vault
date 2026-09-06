# R001 C8 — Real provider run

Evidence level: `RUNTIME_VERIFIED` for the recorded local run. This is not independent verification by another agent.

## Run identity

```text
git_commit=e43cc81e09789e284ef35a7e326297194f429a9
provider=Ollama local
endpoint=http://127.0.0.1:11434
m1=qwen2.5-coder:3b
m2=qwen2.5-coder:7b
timestamp=2026-09-04T15:46:30Z
```

Command:

```text
python 07_EVALUATION/retrieval_fusion/experiment_runner.py
```

Observed completion:

```text
Retrieval Fusion Lab Run Complete!
```

The run used the repository's configured 15-case corpus, R1–R4 strategies,
and full-context comparison for both local models. Raw results are in
`07_EVALUATION/reports/retrieval_fusion_report.json` in the run worktree.

## Observed aggregate results

| strategy | candidate recall | fact recall | context recall | M1 accuracy | M2 accuracy | avg tokens | avg latency ms |
|---|---:|---:|---:|---:|---:|---:|---:|
| R1 | 0.6000 | 0.6607 | 0.5553 | 0.4622 | 0.5067 | 1665.5 | 1552.1 |
| R2 | 0.6333 | 0.7000 | 0.5907 | 0.5578 | 0.5944 | 1642.9 | 1354.8 |
| R3 | 0.7667 | 0.7067 | 0.5707 | 0.5222 | 0.5167 | 1529.5 | 1160.9 |
| R4 | 0.6333 | 0.7000 | 0.5573 | 0.6078 | 0.5856 | 1610.9 | 2049.9 |
| FULL_CONTEXT | 1.0000 | 0.6773 | 0.6773 | 0.6389 | 0.7289 | 3226.9 | 892.8 |

## Interpretation

The run is evidence that the configured code path executed against a real local provider and produced measurable differences. It does not prove causal memory usefulness: the corpus is repository-authored, the strategies differ in more than one signal, and no randomized paired treatment/control design is implemented here. Full-context wins both model accuracy aggregates against R1–R3 and M2 against R4; R4 has the best M1 selective-retrieval accuracy but the highest selective-retrieval latency.


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
