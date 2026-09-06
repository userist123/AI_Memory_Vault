"""Compile an informal request into a complete English task prompt.

The user states an intent in one line, in Romanian. What reaches another agent
must be a full English brief: verified context, explicit requirements, the
traps that already cost this repository time, the method for measuring, and
acceptance criteria that can fail.

This script does the deterministic half. It reads the vault's live state and
emits a skeleton already populated with facts nobody should have to look up
again: the current commit, the test baseline, corpus and graph sizes, the
recorded methods, and the standing traps. The agent fills in the parts that
require judgement — the task itself, its requirements, what is forbidden, and
what "done" looks like.

Everything emitted is English regardless of the language of the request, per
`01_ARCHITECTURE/memory/Preferences/AI_Facing_Prompts_In_English.md`. Detail
lost in translation is detail lost.

    python 30_SCRIPTS/prompt/compile_task_prompt.py \
        --branch r022/some-work --owner "CLAUDE SONNET" \
        --task "Wire X into Y and measure whether it helps"
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

STATE_CARD = REPO / "00_GOVERNANCE" / "VAULT_STATE.md"
LESSONS = REPO / "01_ARCHITECTURE" / "memory" / "Lessons"
SKILL_CATALOGUES = (
    "01_ARCHITECTURE/knowledge/Master_Skills_Catalog_251.md",
    "01_ARCHITECTURE/knowledge/Agents_Skill_Matrix.md",
)


def head() -> str:
    try:
        out = subprocess.run(
            ["git", "rev-parse", "--short", "HEAD"],
            cwd=REPO, capture_output=True, text=True, check=False,
        )
        return out.stdout.strip() or "unknown"
    except OSError:
        return "unknown"


def measured_state() -> dict[str, str]:
    """Live numbers, not numbers copied from a document that may have rotted."""
    facts: dict[str, str] = {}
    try:
        from graph.synapse_store import SynapseStore
        from retrieval.vault_index import VaultIndex

        index = VaultIndex.load(REPO, include_raw=True, include_archived=True)
        store = SynapseStore.from_index(index)
        facts["corpus notes (VaultIndex)"] = str(len(index))
        facts["graph edges"] = str(len(store.all()))
        nodes = {s.source_id for s in store.all()} | {s.target_id for s in store.all()}
        facts["notes with an edge"] = f"{len(nodes)} ({100 * len(nodes) // max(len(index), 1)}%)"
    except Exception as exc:  # fail loud in the prompt, never silently omit
        facts["graph state"] = f"UNAVAILABLE: {type(exc).__name__}: {exc}"
    try:
        from memory_controller.storage.file_engine import FileStorageEngine

        facts["notes visible to storage"] = str(len(FileStorageEngine(str(REPO)).id_to_path))
    except Exception as exc:
        facts["storage state"] = f"UNAVAILABLE: {type(exc).__name__}: {exc}"
    return facts


def recorded_methods() -> list[tuple[str, str]]:
    """Titles of the procedural-memory notes, so the brief points at method
    rather than making the reader rediscover it."""
    out = []
    if not LESSONS.is_dir():
        return out
    for path in sorted(LESSONS.glob("*.md")):
        text = path.read_text(encoding="utf-8", errors="ignore")
        if "method" not in text[:600]:
            continue
        m = re.search(r"^# (.+)$", text, re.M)
        if m:
            out.append((path.relative_to(REPO).as_posix(), m.group(1).strip()))
    return out


def standing_traps() -> list[str]:
    return [
        "`memory_controller/` is a 19-line `__path__` shim. The implementation is "
        "`03_IMPLEMENTATION/packages/memory/controller.py`. Grepping the shim finds nothing "
        "and is not evidence of absence.",
        "A module existing is not a module being used. Before claiming anything is in "
        "production, grep for consumers excluding tests, benchmarks, 20_TESTS and 07_EVALUATION.",
        "Measure baselines in an isolated `git worktree` at a SHORT path. Never stash or "
        "checkout in the main tree: several agents keep uncommitted work there, and Windows "
        "MAX_PATH bites at the default scratch location.",
        "Commit early. Uncommitted work in this repository has been destroyed before.",
        "Frozen artefacts are hashed over canonical LF bytes. Do not hash raw bytes; a Windows "
        "checkout converts line endings and the guard fires on content nobody changed.",
        "After changing any limit, re-run whatever *derives* a value from it. Arithmetic on a "
        "constant is a dependency no import graph shows.",
    ]



#: What kind of work is being asked for, and what a brief of that kind must
#: contain to be honest.
#:
#: This is the part that used to live only in the head of whoever wrote the
#: brief. Each entry was paid for: an experiment without a stop condition
#: reported a comparison of a baseline against itself; a migration without a
#: recovery path deleted 63 branches before anyone asked whether the work was
#: recoverable; a verification that trusted a commit message concluded a
#: component was wired when it was not.
INTENTS: dict[str, dict[str, object]] = {
    "implement": {
        "summary": "Build or change behaviour.",
        "requirements": [
            "Every new or changed behaviour carries a test that would fail if the "
            "change were wrong.",
            "Nothing is promoted past REVIEW on your own authority.",
            "Preserve existing valid behaviour; legacy error text stays byte-identical "
            "where callers assert on it.",
        ],
        "forbidden": [
            "Do not widen a policy, threshold or allowlist as a side effect of making "
            "something work.",
            "Do not add a compatibility bypass around a gate. Being more restrictive is "
            "allowed; being less is not.",
        ],
        "deliverables": ["Implementation.", "Regression tests.", "Call-path proof that no gate is bypassed."],
    },
    "verify": {
        "summary": "Establish whether a claim about the system is true.",
        "requirements": [
            "Resolve the symbol you actually import before concluding anything about a "
            "module; `memory_controller/` is a shim and grepping it proves nothing.",
            "A commit message, a file name and a docstring are not evidence. Run it.",
            "Report what you could NOT verify as unverified, rather than omitting it.",
        ],
        "forbidden": [
            "Do not conclude absence from an empty search without stating what the "
            "search covered.",
            "Do not repair what you are auditing in the same pass; findings first.",
        ],
        "deliverables": ["Findings with the evidence for each.", "What remains unverified, and why."],
    },
    "measure": {
        "summary": "Determine whether a change actually helps.",
        "requirements": [
            "Two arms, identical in everything but the variable under test, on the same "
            "corpus, filters and principal.",
            "The treatment arm must fail loudly when it cannot actually run. A silent "
            "fallback makes it identical to the baseline and turns 'no significant "
            "difference' into a statement about nothing.",
            "A pre-registered threshold, and a STOP CONDITION stated before running.",
            "Report n, and report a population that is too small as too small.",
        ],
        "forbidden": [
            "Do not modify the benchmark, its threshold or its frozen set to obtain a "
            "result. If a gate blocks the work, report it blocked.",
            "Do not tune against the held-out set.",
            "Do not pool populations that differ. Graph results describe the connected "
            "subset, not the corpus.",
        ],
        "deliverables": [
            "Measurement with n, per class, both arms.",
            "The stop-condition decision, including a NO-GO if that is the answer.",
            "A recommendation, which may be to leave the change disabled.",
        ],
    },
    "fix": {
        "summary": "Repair a defect.",
        "requirements": [
            "Reproduce it first. A fix without a reproduction is a guess.",
            "The test must fail before the fix and pass after.",
            "Enumerate what the fix makes newly reachable — repairing a read path can arm "
            "a destructive write path.",
        ],
        "forbidden": [
            "Do not fix the symptom the test asserts while leaving the cause.",
            "Do not weaken a guard to stop it firing; establish why it fires.",
        ],
        "deliverables": ["The reproduction.", "The fix.", "What the fix newly exposes."],
    },
    "migrate": {
        "summary": "Move, rename or delete existing material.",
        "requirements": [
            "A recovery path before the first destructive operation, verified to work.",
            "Ids are stable across the move; only location changes.",
            "Inbound references are repointed, and any that cannot be resolved are listed "
            "rather than guessed.",
        ],
        "forbidden": [
            "Do not delete anything whose content you have not confirmed exists elsewhere.",
            "Do not treat an empty search as proof that nothing references the target.",
        ],
        "deliverables": [
            "The recovery mechanism, with proof it covers every affected item.",
            "The mapping, old to new.",
            "What was left unresolved.",
        ],
    },
}


def intent_block(intent: str) -> tuple[str, str, str]:
    """Requirements, forbidden and deliverables for a kind of work."""
    spec = INTENTS.get(intent)
    if spec is None:
        known = ", ".join(sorted(INTENTS))
        raise SystemExit(f"unknown intent {intent!r}; known: {known}")
    req = "\n".join(f"{i}. {r}" for i, r in enumerate(spec["requirements"], start=1))
    forb = "\n".join(f"- {f}" for f in spec["forbidden"])
    deliv = "\n".join(f"{i}. {d}" for i, d in enumerate(spec["deliverables"], start=1))
    return req, forb, deliv


TEMPLATE = """Repository: https://github.com/userist123/AI_Memory_Vault_CODEX_READY
Base: current main ({head})
Create: {branch}
Owner: {owner}

## Verified context — measured, do not re-derive

{state}

Read `00_GOVERNANCE/VAULT_STATE.md` before anything else. It records what is
verified true right now and outranks README, CLAUDE.md and AGENTS.md wherever
they disagree.

## Task

{task}

## Requirements — {intent_name}

{requirements}
{n_plus_one}. Zero regression against the stated baseline. Any deviation is
   explained commit by commit.
{n_plus_two}. TODO — anything specific to this task that the kind alone does not cover.

## Forbidden

{forbidden}
- Do not modify a benchmark, threshold or gate to make a result pass. If a gate
  blocks the work, report it blocked.
- TODO — anything out of scope for this task specifically.

## Methods already recorded — read before diagnosing

{methods}

## Standing traps

{traps}

## Skills and data to consult

{skills}

If the task needs a capability none of these cover, say so explicitly rather
than improvising one.

## Method

Isolated worktree at a short path, cherry-pick your commits onto the baseline,
run the suite in both, and diff the FAILED name sets:

    git worktree add --detach C:/Users/user/Documents/Codex/<name> <base-sha>
    git -C C:/Users/user/Documents/Codex/<name> config core.longpaths true

## Deliverables

{deliverables}
{d_plus_one}. Measurement against the baseline, reported as numbers with an n.
{d_plus_two}. Remaining gaps, stated as gaps.

## Acceptance — a task is finished when all five hold

1. Implemented and committed.
2. Verified by something that would have failed if the change were wrong. A
   green suite is not this on its own.
3. Regressions measured against a stated baseline, in isolation.
4. What remains open written down explicitly, including "nothing".
5. The method recorded per
   `10_DOCUMENTATION/procedures/Recording_A_Solved_Problem.md` if it transfers.

Anything less is unfinished and must be reported as unfinished, with the
remainder named.
"""


def compile_prompt(task: str, branch: str, owner: str, intent: str = "implement") -> str:
    state = "\n".join(f"- {k}: {v}" for k, v in measured_state().items())
    methods = "\n".join(f"- `{p}` — {t}" for p, t in recorded_methods()) or "- none recorded yet"
    traps = "\n".join(f"- {t}" for t in standing_traps())
    skills = "\n".join(
        f"- `{c}`" for c in SKILL_CATALOGUES if (REPO / c).exists()
    ) or "- no skill catalogue found in the indexed roots"
    requirements, forbidden, deliverables = intent_block(intent)
    n_req = len(INTENTS[intent]["requirements"])
    n_del = len(INTENTS[intent]["deliverables"])
    return TEMPLATE.format(
        head=head(), branch=branch, owner=owner, task=task.strip(),
        state=state, methods=methods, traps=traps, skills=skills,
        intent_name=INTENTS[intent]["summary"], requirements=requirements,
        forbidden=forbidden, deliverables=deliverables,
        n_plus_one=n_req + 1, n_plus_two=n_req + 2,
        d_plus_one=n_del + 1, d_plus_two=n_del + 2,
    )


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--task", required=True, help="what must be achieved, in English")
    ap.add_argument("--branch", default="rXXX/describe-the-work")
    ap.add_argument("--owner", default="TBD")
    ap.add_argument(
        "--infer", action="store_true",
        help="propose the kind from the request text and print the reasoning "
             "instead of compiling; the sender still chooses",
    )
    ap.add_argument(
        "--intent", default="implement", choices=sorted(INTENTS),
        help="what kind of work this is; selects the mandatory requirements",
    )
    ap.add_argument("--out", help="write here instead of stdout")
    args = ap.parse_args()

    if args.infer:
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        from infer_intent import infer

        result = infer(args.task)
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        print(result.render())
        if result.intent is None:
            print("\nNo brief compiled. Re-run with an explicit --intent.")
            return 1
        print(f"\nRe-run with --intent {result.intent} to compile the brief.")
        return 0

    text = compile_prompt(args.task, args.branch, args.owner, args.intent)
    if args.out:
        Path(args.out).write_text(text, encoding="utf-8", newline="\n")
        print(f"written to {args.out}")
    else:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        print(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
