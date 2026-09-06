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

## Requirements

1. TODO — state each requirement so that it can be checked, not interpreted.
2. Zero regression against the stated baseline. Any deviation is explained
   commit by commit.
3. Every new or changed behaviour carries a test that would fail if the change
   were wrong.

## Forbidden

- Do not modify a benchmark, threshold or gate to make a result pass. If a gate
  blocks the work, report it blocked.
- Do not promote anything past `REVIEW` on your own authority.
- TODO — name what is out of scope for this task specifically.

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

1. Implementation.
2. Regression tests for every new behaviour.
3. Measurement against the baseline, reported as numbers with an n.
4. Remaining gaps, stated as gaps.

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


def compile_prompt(task: str, branch: str, owner: str) -> str:
    state = "\n".join(f"- {k}: {v}" for k, v in measured_state().items())
    methods = "\n".join(f"- `{p}` — {t}" for p, t in recorded_methods()) or "- none recorded yet"
    traps = "\n".join(f"- {t}" for t in standing_traps())
    skills = "\n".join(
        f"- `{c}`" for c in SKILL_CATALOGUES if (REPO / c).exists()
    ) or "- no skill catalogue found in the indexed roots"
    return TEMPLATE.format(
        head=head(), branch=branch, owner=owner, task=task.strip(),
        state=state, methods=methods, traps=traps, skills=skills,
    )


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--task", required=True, help="what must be achieved, in English")
    ap.add_argument("--branch", default="rXXX/describe-the-work")
    ap.add_argument("--owner", default="TBD")
    ap.add_argument("--out", help="write here instead of stdout")
    args = ap.parse_args()

    text = compile_prompt(args.task, args.branch, args.owner)
    if args.out:
        Path(args.out).write_text(text, encoding="utf-8", newline="\n")
        print(f"written to {args.out}")
    else:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        print(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
