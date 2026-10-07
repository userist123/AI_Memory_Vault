# Deviations from the preregistration

Written before the results existed, and appended to afterwards if anything else
departs from the protocol. A deviation recorded after seeing the numbers is not
a deviation record, it is an excuse.

## D-1 — the frozen corpus the v3 figures were measured against is gone

`run_retrieval_benchmark_v3.py` points at a separate checkout,
`c:\Users\Marius\Documents\Codex\vault_b3ada1b54`, so the graph and the note
corpus stayed fixed while arms varied. That directory does not exist on this
machine any more.

This run therefore uses the corpus at the current commit of this branch. What
that costs, precisely:

- **Within this run, nothing.** All five arms see the same index object, in the
  same process. The arm comparison — which is what the preregistration is about
  — is unaffected.
- **Across runs, comparability.** The absolute figures here are not directly
  comparable with the 39/130 and 21/130 quoted elsewhere, because the corpus has
  changed since `b3ada1b54`. Any such comparison in the report is labelled.

The benchmark *cases* are still the frozen file, verified by SHA-256
(`eeb53822ae5f022df89290d6fca789c1d4387b7572ed70103a7c08aae5b57eaa`) at the
start of the run; the run aborts on a mismatch. It is the corpus searched, not
the questions asked, that differs.

## D-2 — graph expansion off is a consequence, not a choice

The preregistration says graph expansion is off. While writing the harness I
found the stronger reason: `ranking_arm` is read only inside the `if not
is_expansion_enabled:` branch (controller.py:947). With expansion on, every arm
sorts identically and all five comparisons would be void by the experiment's own
sabotage rule. So graph-OFF is not a simplification of the production path; it
is the only configuration in which the question has an answer at all.

This is not a departure from production: `enable_graph_expansion` defaults to
`False` in the constructor (controller.py:201) and no production caller turns it
on. But it does mean the arm would become inert the moment anyone did, which is
worth an owner's attention and is outside what this experiment can settle.

## D-3 — how close this is to the production path, checked rather than assumed

The production path is `recall_cli.get_memory_controller()`, which both the
`vault-memory` MCP server and the CLI go through. It builds
`MemoryController(storage)` — no index, no arm, no expansion. So:

| | production | this run |
|---|---|---|
| storage | `SQLiteStorageEngine` if `vault_memory.sqlite3` exists and is non-empty, else `FileStorageEngine(vault_root)` | `FileStorageEngine(".")` |
| ranking arm | unset → `fused_score` | each arm explicitly, `fused_score` as reference |
| graph expansion | unset → `False` | `False` |
| index | `None` | loaded |

`vault_memory.sqlite3` does not exist in this repository, so production takes the
`FileStorageEngine` branch — the same engine used here.

The index is the one real difference, and inside `search()` it is read in exactly
two places: a cache-version string, and the graph-expansion validity check.
Candidate generation reads the storage pool, not the index. With expansion off,
passing an index cannot change which notes are returned or in what order. That is
reasoning about the code, not a measurement, and is recorded as such.

## D-4 — a correction to the preregistration's own text

`PREREGISTRATION.md`, under "What stands", says the ASCII tokenizer is called
only from `interfaces/benchmarks/retrieval_ab.py` and that its blast radius is a
benchmark script. **That is wrong, and the error is mine.**

`retrieval/context/candidate_generation.py` imports `tokenize` (line 50) and
calls it on every document and on the query (lines 155 and 158). That is the
production candidate-generation path. The tokenizer defect — `învățare` becoming
`['nv', 'are']` — affects real retrieval.

The preregistration is not rewritten: it was committed before the results and
stays as it was. The correction lives here.

Why the tokenizer experiment nevertheless measured zero difference across three
arms, which is the observation I misexplained: the `memory_controller`
compatibility shim gives `__path__` entries spanning `retrieval/`, `memory/` and
others, so the same file is imported under two names and becomes two distinct
module objects.

    memory_controller.context.candidate_generation   <- what the controller uses
    retrieval.context.candidate_generation           <- what the experiment patched
    same file on disk, different module objects, id differs, `is` is False

`eval_tokenizer_experiment.py` does `import retrieval.context.candidate_generation
as cg` and then `cg.tokenize = tok_fn`. The controller's `generate_candidates`
belongs to the other module object and keeps the original `tokenize`. So the
patch was inert and all three arms were the same arm — which the experiment's
report presents as a finding, at 21/130 three times over.

This is a hazard for anything in this repository that monkeypatches a module
attribute, not just that one script.
