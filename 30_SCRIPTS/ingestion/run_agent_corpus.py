#!/usr/bin/env python3
"""
run_agent_corpus.py — drive the whole 20-book corpus through the agent path.

This is the counterpart to the local-provider orchestrator: same corpus, same
gates, same report shape, but the reading is done by an agent (Antigravity,
Codex, ChatGPT) instead of a model provider. Nothing here calls a model. It
prepares work and it scores work.

    prepare   writes one <name>_chunks.json per book for the agent to read,
              plus a manifest saying what order to do them in
    collect   gates every <name>_candidates.json the agent wrote back and
              produces staging/corpus_run_report.json

## Order matters, and it is not the order the local run used

`prepare` sorts the books by chunk count, largest first, and that is the
single decision in this file worth arguing about.

`occurrences` — how many distinct sections of a book define the same term — is
the only ranking signal in this pipeline that carries information. Confidence
is 1.00 on nearly everything, including candidates whose evidence turned out to
be fabricated. Review happens in descending `occurrences`, with a floor of 3.

A concept cannot occur in three distinct sections of a book that has fewer than
three sections. The local run went smallest-first and its first five books had
1, 1, 4, 4 and 7 chunks; every one of them reported `candidates_at_ge_3: 0`,
which says nothing about the extractor and everything about the arithmetic.
The books where recurrence can be measured at all — Newell, Schacter, Minsky,
Soar — are the large ones, and they come first here.

So the early numbers from this run are comparable to nothing in the local run,
and that is deliberate: they are the numbers that can actually be read.

## Use

    python 30_SCRIPTS/ingestion/run_agent_corpus.py prepare \\
        --work-dir scratch/agent_corpus

    # the agent reads scratch/agent_corpus/<name>_chunks.json and writes
    # scratch/agent_corpus/<name>_candidates.json — see
    # 00_GOVERNANCE/coordination/BOOK_INGESTION_HANDOFF.md for the format

    python 30_SCRIPTS/ingestion/run_agent_corpus.py collect \\
        --work-dir scratch/agent_corpus --agent-label antigravity

`collect` is resumable and is meant to be run repeatedly: a book with no
candidates file yet is reported as pending, not as a book that yielded nothing.
Those two are very different and the report keeps them apart.
"""

from __future__ import annotations

import argparse
import datetime
import json
import pathlib
import re
import statistics
import sys
from collections import Counter
from typing import Any

_HERE = pathlib.Path(__file__).resolve().parent
_REPO = _HERE.parents[1]
sys.path.insert(0, str(_HERE))

from gate_agent_candidates import gate  # noqa: E402
from model_extract_concepts import (  # noqa: E402
    load_slot_questions,
    split_into_structural_chunks,
)

MANIFEST = _HERE / "corpus_manifest.json"

#: A book with fewer sections than this cannot produce a candidate at the
#: review floor of occurrences>=3, no matter how well it is read. It is still
#: worth extracting — it just must not be read as evidence about yield.
MIN_CHUNKS_FOR_RECURRENCE = 3


#: Local model servers, on the ports they use out of the box. The point of
#: probing them is not to stop anything — it is to keep the report honest.
LOCAL_PROVIDER_PORTS = {
    "ollama": 11434,
    "lm-studio": 1234,
    "llama.cpp": 8080,
    "text-generation-webui": 5000,
}


def probe_local_providers(timeout: float = 1.5) -> dict[str, Any]:
    """Record whether a local model server was up while this ran.

    `provider` is written as "agent" on every row this file produces, and the
    value of that label depends entirely on an agent having actually read the
    passages. An agent that instead wrote a script to call a 7B model would
    produce rows labelled "agent" that are nothing of the sort, and no amount
    of asking prevents that.

    So this does not prevent it. It observes it, into the report, next to the
    numbers the reader is about to trust: which local servers answered, and
    what they had loaded. A clean run says so; a run with llama3.1:8b resident
    in VRAM says that too, and the reader gets to weigh it.

    Absence of a server is weak evidence — the work could have gone through a
    remote endpoint — so this is never a pass/fail gate, only a recorded
    observation. It is deliberately cheap and never raises.
    """
    import urllib.error
    import urllib.request

    seen: dict[str, Any] = {}
    for name, port in LOCAL_PROVIDER_PORTS.items():
        try:
            with urllib.request.urlopen(
                f"http://localhost:{port}/", timeout=timeout
            ) as response:
                seen[name] = {"port": port, "responding": True,
                              "status": response.status}
        except urllib.error.HTTPError as exc:
            #: An HTTP error is still a server answering.
            seen[name] = {"port": port, "responding": True, "status": exc.code}
        except Exception:
            continue

    if "ollama" in seen:
        try:
            with urllib.request.urlopen(
                "http://localhost:11434/api/ps", timeout=timeout
            ) as response:
                loaded = json.loads(response.read() or b"{}").get("models", [])
            seen["ollama"]["models_loaded"] = [m.get("name") for m in loaded]
        except Exception:
            pass

    return {
        "checked_at": datetime.datetime.now().astimezone().isoformat(),
        "local_servers_responding": seen,
        "clean": not seen,
    }



#: The hundred-odd commonest English words. Continuous prose is roughly 35-50%
#: of these; an index, a bibliography or a badly OCR'd page is far below its
#: own book's level.
_COMMON_WORDS = frozenset("""
the of and to a in that is was it for as with his he be not on this by had at
but from have are they you or an will we one all were her she there would their
him been has when who which them what so up out if about into than its time can
could no other some only two may these first also new like our over think most
after more such then any very my me do did does how now
""".split())

#: A chunk this far below its own book's median is not prose. Relative, not
#: absolute, because the absolute rate says nothing across books: Newell's
#: median is 0.456 and a survey paper's is 0.218, and both are fine.
LOW_PROSE_RATIO = 0.65


def prose_rate(text: str) -> float:
    """Fraction of words that are among the commonest English words.

    Three families of text-quality metric have now failed on this corpus at the
    document level — function-word rate, intra-word punctuation, low-vowel rate,
    and a letter-trigram model trained on the corpus's own clean books. All of
    them ranked a damaged book as clean. The reason is that OCR damage here is
    partial: short common words survive it ("in development and developed a
    Soar vpvtem that evhtbiled vach transitionv"), so a book-level average stays
    respectable while individual pages are unreadable.

    Compared against its own book's median, the same measure works, because the
    comparison is then within one scan of one document.

    What it flags is not only corruption. Of the 90 chunks it marks across the
    corpus, some are OCR damage — Newell's name index reads "Add(cos.(.W..385
    Brnr.J.S.415" — and others are perfectly clean back matter: subject indexes
    from Schacter and Soar, pages of terms and page numbers. Both are useless to
    read for concepts, which is why the flag is named for what it measures
    rather than for what caused it.
    """
    words = re.findall(r"[a-z']+", text.lower())
    if not words:
        return 0.0
    return sum(1 for w in words if w in _COMMON_WORDS) / len(words)


def load_books() -> list[dict[str, str]]:
    return json.loads(MANIFEST.read_text(encoding="utf-8"))["books"]


def _chunks_for(
    book: dict[str, str], corpus_root: pathlib.Path
) -> tuple[pathlib.Path, list[dict[str, Any]] | None]:
    path = corpus_root / book["rel_path"]
    if not path.exists():
        return path, None
    return path, split_into_structural_chunks(
        path.read_text(encoding="utf-8", errors="replace")
    )


def prepare(args: argparse.Namespace) -> int:
    """Write the chunks for every book, largest first."""
    slots = load_slot_questions()
    args.work_dir.mkdir(parents=True, exist_ok=True)

    prepared: list[dict[str, Any]] = []
    missing: list[str] = []

    for book in load_books():
        path, chunks = _chunks_for(book, args.corpus_root)
        if chunks is None:
            missing.append(book["short_name"])
            continue
        if not chunks:
            #: Converted to a file that chunks to nothing. Loud, not skipped:
            #: this exact shape once let eight books report success while
            #: producing zero extractable text.
            missing.append(f'{book["short_name"]} (0 chunks)')
            continue

        rates = [prose_rate(c["content"]) for c in chunks]
        floor = statistics.median(rates) * LOW_PROSE_RATIO
        low = [i for i, r in enumerate(rates) if r < floor]

        out = args.work_dir / f'{book["short_name"]}_chunks.json'
        out.write_text(json.dumps({
            "source_file": book["rel_path"],
            "chunk_count": len(chunks),
            "low_prose_chunks": low,
            "slots": slots,
            "chunks": [
                {
                    "chunk_index": i,
                    "heading": c["heading"],
                    "content": c["content"],
                    "prose_rate": round(rates[i], 3),
                    #: Index pages and OCR damage both land here. Reading either
                    #: for concepts produces terms like "Parr of mr".
                    "low_prose": i in low,
                }
                for i, c in enumerate(chunks)
            ],
        }, indent=2, ensure_ascii=False), encoding="utf-8")

        prepared.append({
            "short_name": book["short_name"],
            "rel_path": book["rel_path"],
            "chunks_file": str(out.relative_to(_REPO)) if out.is_relative_to(_REPO) else str(out),
            "total_chunks": len(chunks),
            "low_prose_chunks": len(low),
            "recurrence_measurable": len(chunks) >= MIN_CHUNKS_FOR_RECURRENCE,
        })

    prepared.sort(key=lambda b: -b["total_chunks"])
    for rank, book in enumerate(prepared, start=1):
        book["order"] = rank

    (args.work_dir / "manifest.json").write_text(
        json.dumps({"books": prepared}, indent=2, ensure_ascii=False), encoding="utf-8"
    )

    total = sum(b["total_chunks"] for b in prepared)
    flat = [b for b in prepared if not b["recurrence_measurable"]]
    print(f"prepared   {len(prepared)} books, {total:,} chunks -> {args.work_dir}")
    print(f"{'order':>5}  {'chunks':>6}  book")
    for b in prepared:
        mark = "" if b["recurrence_measurable"] else "   <- too few sections for occurrences>=3"
        low = f'  ({b["low_prose_chunks"]} not prose)' if b["low_prose_chunks"] else ""
        print(f'{b["order"]:>5}  {b["total_chunks"]:>6}  {b["short_name"]}{low}{mark}')
    total_low = sum(b["low_prose_chunks"] for b in prepared)
    if total_low:
        print(f"\n{total_low} chunk(s) are not continuous prose — indexes, "
              "bibliographies, OCR damage. They are marked low_prose in the "
              "chunks files, not removed; reading them for concepts is how a "
              "name index becomes a candidate.")
    if flat:
        print(f"\n{len(flat)} book(s) cannot reach the review floor on their own; "
              "extract them, but do not read their yield as a result.")
    if missing:
        print(f"\nnot prepared ({len(missing)}): {', '.join(missing)}", file=sys.stderr)
        return 1
    return 0


def collect(args: argparse.Namespace) -> int:
    """Gate whatever the agent has written back so far."""
    report: list[dict[str, Any]] = []
    pending: list[str] = []
    staging = _REPO / "staging"

    for book in load_books():
        name = book["short_name"]
        candidates = args.work_dir / f"{name}_candidates.json"
        if not candidates.exists():
            #: Not the same as a book that yielded nothing, and the report is
            #: not allowed to blur the two.
            pending.append(name)
            continue

        path, chunks = _chunks_for(book, args.corpus_root)
        if not chunks:
            print(f"{name}: source missing or unchunkable, skipped", file=sys.stderr)
            continue

        out = staging / f"{name}.json"
        rej = staging / f"{name}_rej.json"
        gate(argparse.Namespace(
            input_file=path,
            candidates=candidates,
            source_book=name,
            output_file=out,
            rejects_file=rej,
            agent_label=args.agent_label,
        ))

        rows = json.loads(out.read_text(encoding="utf-8"))
        rejected = json.loads(rej.read_text(encoding="utf-8"))
        histogram = Counter(r.get("occurrences", 1) for r in rows)
        report.append({
            "short_name": name,
            "rel_path": book["rel_path"],
            "total_chunks": len(chunks),
            "recurrence_measurable": len(chunks) >= MIN_CHUNKS_FOR_RECURRENCE,
            "candidates_submitted": len(json.loads(candidates.read_text(encoding="utf-8"))),
            "candidates_kept": len(rows),
            "candidates_at_ge_2": sum(1 for r in rows if r.get("occurrences", 1) >= 2),
            "candidates_at_ge_3": sum(1 for r in rows if r.get("occurrences", 1) >= 3),
            "occurrence_histogram": {str(k): v for k, v in sorted(histogram.items())},
            "total_rejections": len(rejected),
            "rejections_by_reason": dict(Counter(r["reason"] for r in rejected).most_common()),
            "agent_label": args.agent_label,
        })

    probe = probe_local_providers()
    staging.mkdir(parents=True, exist_ok=True)
    out = staging / args.report_name
    out.write_text(json.dumps({
        "agent_label": args.agent_label,
        "local_provider_probe": probe,
        "books": report,
    }, indent=2, ensure_ascii=False), encoding="utf-8")

    kept = sum(b["candidates_kept"] for b in report)
    at3 = sum(b["candidates_at_ge_3"] for b in report)
    reasons: Counter[str] = Counter()
    for b in report:
        reasons.update(b["rejections_by_reason"])

    print(f"\ncollected  {len(report)} of {len(load_books())} books -> {out}")
    print(f"  kept                    {kept}")
    print(f"  at occurrences>=3       {at3}")
    print(f"  rejected                {sum(reasons.values())}  {dict(reasons.most_common())}")
    if pending:
        print(f"  pending ({len(pending)})         {', '.join(pending)}")

    if probe["clean"]:
        print("  local providers         none responding at collect time")
    else:
        for name, info in probe["local_servers_responding"].items():
            models = info.get("models_loaded")
            extra = f"  loaded={models}" if models else ""
            print(f"  LOCAL PROVIDER UP       {name} on :{info['port']}{extra}")
        print("  ^ recorded in the report. These rows are labelled provider="
              '"agent"; weigh that label accordingly.')
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="command", required=True)

    p = sub.add_parser("prepare", help="write chunks for every book, largest first")
    p.add_argument("--work-dir", required=True, type=pathlib.Path)
    p.add_argument("--corpus-root", type=pathlib.Path, default=_REPO,
                   help="where 06_INBOX/Carti lives. The converted .txt files "
                        "are untracked, so a git worktree does not have them; "
                        "point this at the main checkout when running from one.")
    p.set_defaults(func=prepare)

    c = sub.add_parser("collect", help="gate the candidates written back so far")
    c.add_argument("--work-dir", required=True, type=pathlib.Path)
    c.add_argument("--corpus-root", type=pathlib.Path, default=_REPO,
                   help="where 06_INBOX/Carti lives; see prepare --corpus-root")
    c.add_argument("--agent-label", required=True,
                   help="what produced these candidates, recorded in every row")
    c.add_argument("--report-name", default="agent_corpus_run_report.json",
                   help="written under staging/; kept distinct from the "
                        "local run's report so the two can be compared")
    c.set_defaults(func=collect)

    args = ap.parse_args()
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
