# R001 C16 — Activation docstring warning

Evidence: `RUNTIME_VERIFIED` for the warning reproduction and
`TEST_VERIFIED` for the narrow fix.

The full regression on the C15 baseline produced one repeatable warning:

```text
cognitive_core/activation.py:7: SyntaxWarning: "\\s" is an invalid escape sequence
```

The sequence appeared only in the mathematical example inside the module
docstring. The docstring was changed to a raw string, preserving its rendered
content while preventing Python from interpreting the LaTeX slash as an invalid
escape. No activation logic was changed.

Validation command:

```text
python -W error::SyntaxWarning -m pytest -q cognitive_core/tests/test_activation.py cognitive_core/tests/test_recall.py
```

Observed output:

```text
.....                                                                    [100%]
5 passed in 0.54s
```

This record documents the warning and narrow historical repair. It does not
claim that the old-layout implementation itself belongs on the current runtime
path.


## 🔗 Legături Sinaptice
- [[07_EVALUATION/README|Evaluation Hub]]
- [[15 Artifacts and Dynamic Evidence Map]]
- [[Knowledge Graph Home]]
