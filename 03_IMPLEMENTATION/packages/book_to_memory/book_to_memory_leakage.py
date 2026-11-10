"""Book-to-Memory evaluation-set leakage checker (PR #209 finding B07).

Compares every question/scenario set the track evaluates on against every benchmark or held-out
set it (or its neighbours in ``07_EVALUATION/``) uses, and reports four kinds of overlap:

* ``exact``       the raw text is identical;
* ``normalised``  the text is identical after Unicode folding, case folding, diacritic and
                  punctuation removal and whitespace collapse;
* ``near``        word 3-gram Jaccard >= ``NEAR_NGRAM_THRESHOLD`` or content-word Jaccard >=
                  ``NEAR_TOKEN_THRESHOLD`` (paraphrase-level duplicates);
* ``gold``        the two items point at the same gold note id (a shared target, which is a
                  weaker signal: two different questions about one note are not a duplicate,
                  but a system tuned on one can answer the other from memory of the note).

The thresholds are module constants, fixed before the real run; they are not tuned on the result.
The module is pure standard library and reads only the set files it is given.

Nothing here decides whether a set is "clean" on its own authority: ``summarise`` returns the
counts and the caller states them. A checker that finds a problem must report it.
"""
from __future__ import annotations

import hashlib
import itertools
import json
import re
import unicodedata
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Sequence, Set, Tuple

#: Word-3-gram Jaccard at or above which two texts count as near-duplicates.
NEAR_NGRAM_THRESHOLD = 0.5
#: Content-word (stop words removed) Jaccard at or above which two texts count as near-duplicates.
NEAR_TOKEN_THRESHOLD = 0.7
#: Pairs scoring at least this on either measure are listed for manual review even if not flagged.
REVIEW_FLOOR = 0.3

#: Roles of a set. A comparison is leakage-relevant when one side is a ``heldout`` set and the
#: other is anything else (a ``scenario`` / development set, or another benchmark's held-out part).
ROLE_SCENARIO = "scenario"
ROLE_HELDOUT = "heldout"

_STOPWORDS = frozenset(
    "a an and are as at be by can do does for from how in is it of on or that the this to was "
    "what when where which who why with within without into than then there these those their "
    "its if not no yes all any each both between across via per about over under also".split()
)


class LeakageCheckError(ValueError):
    """Raised when a set file cannot be read or is in an unknown shape."""


@dataclass(frozen=True)
class Item:
    """One question or scenario of a set."""

    set_name: str
    item_id: str
    text: str
    gold_ids: Tuple[str, ...] = ()
    split: str = ""

    @property
    def key(self) -> str:
        return f"{self.set_name}:{self.item_id}"


@dataclass
class EvalSet:
    """A named collection of items with a role and a provenance hash of the file it came from."""

    name: str
    role: str
    path: str
    items: List[Item]
    file_sha256: str = ""

    def to_summary(self) -> Dict[str, Any]:
        return {
            "name": self.name,
            "role": self.role,
            "path": self.path,
            "items": len(self.items),
            "file_sha256": self.file_sha256,
        }


# ---------------------------------------------------------------------------------------------
# Normalisation and similarity
# ---------------------------------------------------------------------------------------------
def normalise(text: str) -> str:
    """Fold a text to a comparison key: NFKD, drop combining marks, casefold, keep alphanumerics."""
    folded = unicodedata.normalize("NFKD", text or "")
    folded = "".join(ch for ch in folded if not unicodedata.combining(ch)).casefold()
    folded = re.sub(r"[^0-9a-zЀ-ӿ]+", " ", folded)
    return re.sub(r"\s+", " ", folded).strip()


def tokens(text: str) -> List[str]:
    norm = normalise(text)
    return norm.split() if norm else []


def content_tokens(text: str) -> Set[str]:
    return {t for t in tokens(text) if t not in _STOPWORDS and len(t) > 1}


def word_ngrams(text: str, n: int = 3) -> Set[Tuple[str, ...]]:
    toks = tokens(text)
    if len(toks) < n:
        return {tuple(toks)} if toks else set()
    return {tuple(toks[i : i + n]) for i in range(len(toks) - n + 1)}


def jaccard(a: Set[Any], b: Set[Any]) -> float:
    if not a and not b:
        return 0.0
    union = a | b
    return len(a & b) / len(union) if union else 0.0


# ---------------------------------------------------------------------------------------------
# Loading sets
# ---------------------------------------------------------------------------------------------
def _read_json(path: Path) -> Any:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        raise LeakageCheckError(f"cannot read {path}: {exc}") from exc


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_cases_json(name: str, path: Path, role: str, *, text_key: str = "query",
                    gold_key: str = "gold_relevant_notes", list_key: str = "cases",
                    id_key: str = "id", only_split: Optional[str] = None,
                    exclude_split: Optional[str] = None) -> EvalSet:
    """Load a JSON set whose items are dicts under ``list_key`` (or the top-level list)."""
    data = _read_json(path)
    cases = data.get(list_key) if isinstance(data, dict) else data
    if not isinstance(cases, list):
        raise LeakageCheckError(f"{name}: no list under '{list_key}' in {path}")
    items: List[Item] = []
    for c in cases:
        split = str(c.get("split", ""))
        if only_split is not None and split != only_split:
            continue
        if exclude_split is not None and split == exclude_split:
            continue
        gold = c.get(gold_key) or []
        if isinstance(gold, str):
            gold = [gold]
        items.append(Item(name, str(c.get(id_key)), str(c.get(text_key, "")),
                          tuple(str(g) for g in gold), split))
    return EvalSet(name, role, str(path), items, _sha256(path))


def load_jsonl(name: str, path: Path, role: str, *, text_key: str = "query",
               gold_key: str = "relevant_ids") -> EvalSet:
    """Load a JSON-lines set, one case per line."""
    items: List[Item] = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        c = json.loads(line)
        items.append(Item(name, str(c.get("id")), str(c.get(text_key, "")),
                          tuple(str(g) for g in (c.get(gold_key) or [])), str(c.get("split", ""))))
    return EvalSet(name, role, str(path), items, _sha256(path))


def load_from_spec(spec: Dict[str, Any], base: Optional[Path] = None) -> EvalSet:
    """Load one set from a plain-dict spec: ``name``, ``path``, ``role`` and optional keys.

    ``format`` is ``json`` (default) or ``jsonl``; the other optional keys are the keyword arguments
    of :func:`load_cases_json` / :func:`load_jsonl`.
    """
    path = Path(spec["path"])
    if base is not None and not path.is_absolute():
        path = base / path
    role = spec.get("role", ROLE_SCENARIO)
    if role not in (ROLE_SCENARIO, ROLE_HELDOUT):
        raise LeakageCheckError(f"{spec.get('name')}: unknown role {role!r}")
    kwargs = {k: spec[k] for k in ("text_key", "gold_key") if k in spec}
    if spec.get("format", "json") == "jsonl":
        return load_jsonl(spec["name"], path, role, **kwargs)
    kwargs.update({k: spec[k] for k in ("list_key", "id_key", "only_split", "exclude_split") if k in spec})
    return load_cases_json(spec["name"], path, role, **kwargs)


# ---------------------------------------------------------------------------------------------
# Comparison
# ---------------------------------------------------------------------------------------------
@dataclass
class PairFinding:
    kind: str  # exact | normalised | near | gold | review
    a: str
    b: str
    a_text: str
    b_text: str
    ngram_jaccard: float
    token_jaccard: float
    shared_gold: Tuple[str, ...] = ()

    def to_dict(self) -> Dict[str, Any]:
        return {
            "kind": self.kind,
            "a": self.a,
            "b": self.b,
            "a_text": self.a_text,
            "b_text": self.b_text,
            "ngram_jaccard": round(self.ngram_jaccard, 4),
            "token_jaccard": round(self.token_jaccard, 4),
            "shared_gold": list(self.shared_gold),
        }


def classify_pair(a: Item, b: Item) -> List[PairFinding]:
    """Return every finding that applies to the pair (a text finding and/or a gold finding)."""
    findings: List[PairFinding] = []
    ng = jaccard(word_ngrams(a.text), word_ngrams(b.text))
    tj = jaccard(content_tokens(a.text), content_tokens(b.text))
    kind: Optional[str] = None
    if a.text.strip() and a.text.strip() == b.text.strip():
        kind = "exact"
    elif normalise(a.text) and normalise(a.text) == normalise(b.text):
        kind = "normalised"
    elif ng >= NEAR_NGRAM_THRESHOLD or tj >= NEAR_TOKEN_THRESHOLD:
        kind = "near"
    elif max(ng, tj) >= REVIEW_FLOOR:
        kind = "review"
    if kind:
        findings.append(PairFinding(kind, a.key, b.key, a.text, b.text, ng, tj))
    shared = tuple(sorted(set(a.gold_ids) & set(b.gold_ids)))
    if shared:
        findings.append(PairFinding("gold", a.key, b.key, a.text, b.text, ng, tj, shared))
    return findings


def compare_sets(a: EvalSet, b: EvalSet) -> List[PairFinding]:
    """Compare every item of ``a`` with every item of ``b`` (all pairs)."""
    out: List[PairFinding] = []
    for x in a.items:
        for y in b.items:
            if x.key == y.key:
                continue
            out.extend(classify_pair(x, y))
    return out


def compare_within(s: EvalSet, split_a: str, split_b: str) -> List[PairFinding]:
    """Compare the items of one set that sit in two different splits."""
    left = [i for i in s.items if i.split == split_a]
    right = [i for i in s.items if i.split == split_b]
    out: List[PairFinding] = []
    for x in left:
        for y in right:
            out.extend(classify_pair(x, y))
    return out


def _counts(findings: Iterable[PairFinding]) -> Dict[str, int]:
    c = {"exact": 0, "normalised": 0, "near": 0, "gold": 0, "review": 0}
    for f in findings:
        c[f.kind] += 1
    return c


def text_overlaps(findings: Iterable[PairFinding]) -> int:
    """Number of exact + normalised + near pairs (the text-level leaks)."""
    c = _counts(findings)
    return c["exact"] + c["normalised"] + c["near"]


def run_check(sets: Sequence[EvalSet], within: Sequence[Tuple[str, str, str]] = ()) -> Dict[str, Any]:
    """Compare every pair of sets and, for ``within`` triples (set, split, split), the splits of a set.

    Returns a JSON-serialisable report. A pair of sets is *relevant* (it can leak held-out
    content) when at least one side has the ``heldout`` role.
    """
    pair_reports: List[Dict[str, Any]] = []
    for a, b in itertools.combinations(sets, 2):
        findings = compare_sets(a, b)
        counts = _counts(findings)
        pair_reports.append({
            "a": a.name,
            "b": b.name,
            "relevant": ROLE_HELDOUT in (a.role, b.role),
            "comparisons": len(a.items) * len(b.items),
            "counts": counts,
            "text_overlaps": text_overlaps(findings),
            "findings": [f.to_dict() for f in findings],
        })
    within_reports: List[Dict[str, Any]] = []
    by_name = {s.name: s for s in sets}
    for name, sa, sb in within:
        s = by_name[name]
        findings = compare_within(s, sa, sb)
        counts = _counts(findings)
        within_reports.append({
            "set": name,
            "split_a": sa,
            "split_b": sb,
            "comparisons": sum(1 for i in s.items if i.split == sa) * sum(1 for i in s.items if i.split == sb),
            "counts": counts,
            "text_overlaps": text_overlaps(findings),
            "findings": [f.to_dict() for f in findings],
        })
    return {
        "thresholds": {
            "near_ngram_jaccard": NEAR_NGRAM_THRESHOLD,
            "near_token_jaccard": NEAR_TOKEN_THRESHOLD,
            "review_floor": REVIEW_FLOOR,
        },
        "sets": [s.to_summary() for s in sets],
        "pairs": pair_reports,
        "within": within_reports,
        "summary": summarise(pair_reports, within_reports),
    }


def summarise(pairs: Sequence[Dict[str, Any]], within: Sequence[Dict[str, Any]]) -> Dict[str, Any]:
    """Totals over the relevant pairs and the within-set split comparisons."""
    rel = [p for p in pairs if p["relevant"]]
    total = {"exact": 0, "normalised": 0, "near": 0, "gold": 0, "review": 0}
    for group in (rel, within):
        for p in group:
            for k, v in p["counts"].items():
                total[k] += v
    return {
        "relevant_pairs": len(rel),
        "all_pairs": len(pairs),
        "within_set_comparisons": len(within),
        "text_overlaps": total["exact"] + total["normalised"] + total["near"],
        "exact": total["exact"],
        "normalised": total["normalised"],
        "near": total["near"],
        "shared_gold_pairs": total["gold"],
        "review_pairs": total["review"],
    }


def clean_subset(target: EvalSet, against: Sequence[EvalSet]) -> Dict[str, Any]:
    """Items of ``target`` with no text-level overlap and no shared gold id with any set in ``against``.

    This is the subset on which a held-out result is not exposed to the sets one tuned on.
    """
    dropped: Dict[str, List[str]] = {}
    for item in target.items:
        reasons: List[str] = []
        for other in against:
            for o in other.items:
                if o.key == item.key:
                    continue
                for f in classify_pair(item, o):
                    if f.kind in ("exact", "normalised", "near"):
                        reasons.append(f"{f.kind}:{o.key}")
                    elif f.kind == "gold":
                        reasons.append(f"gold:{o.key}:{','.join(f.shared_gold)}")
        if reasons:
            dropped[item.item_id] = reasons
    kept = [i.item_id for i in target.items if i.item_id not in dropped]
    return {
        "target": target.name,
        "against": [s.name for s in against],
        "items": len(target.items),
        "kept": kept,
        "dropped": dropped,
    }
