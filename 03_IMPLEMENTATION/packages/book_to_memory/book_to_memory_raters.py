"""Multi-rater records, agreement statistics and aggregation (PR #209 finding B05).

A single rater (a person or a model) is not evidence: nothing says how much another rater would
disagree. This module defines

* the **rating record** (rater id, model, blind flag, item id, label, rationale) and its JSON schema;
* the agreement statistics: Cohen's kappa (2 raters, optionally weighted), Fleiss' kappa (N raters,
  nominal) and Krippendorff's alpha (N raters, missing values allowed; nominal, ordinal, interval,
  ratio);
* :func:`aggregate_ratings`, which refuses non-blind ratings for a primary analysis, requires at
  least two *independent* raters, and reports a consensus per item with the agreement statistics.

The statistics are checked in ``20_TESTS/test_book_to_memory_raters.py`` against textbook worked
examples (Cohen 1960 / Wikipedia 2x2, Fleiss 1971 worked example, Krippendorff 2011 reliability
example). Pure standard library.
"""
from __future__ import annotations

import json
import math
from collections import Counter, defaultdict
from dataclasses import dataclass, asdict, field
from datetime import datetime, timezone
from typing import Any, Dict, Hashable, Iterable, List, Mapping, Optional, Sequence, Tuple

SCHEMA_VERSION = "b2m-rating-record/1"
RATER_KINDS = ("human", "model")

#: Policy-02 usage-test bands on the 0-10 rubric total (section 6 of the policy).
BAND_PASS, BAND_RETRY, BAND_FAIL = "PASS", "RETRY", "FAIL"

STATUS_OK = "OK"
STATUS_INSUFFICIENT_RATERS = "INSUFFICIENT_RATERS"

#: JSON schema (draft-07) of one rating record; ``08_RESEARCH/BOOK_TO_MEMORY/rating_record.schema.json`` is this dict.
RATING_RECORD_SCHEMA: Dict[str, Any] = {
    "$schema": "http://json-schema.org/draft-07/schema#",
    "title": "Book-to-Memory rating record",
    "description": "One rating of one item by one rater. Blind ratings only count toward a primary analysis.",
    "type": "object",
    "additionalProperties": False,
    "required": ["schema_version", "rater_id", "rater_kind", "model", "blind", "item_id", "label", "rationale"],
    "properties": {
        "schema_version": {"const": SCHEMA_VERSION},
        "rater_id": {"type": "string", "minLength": 1,
                     "description": "Stable identifier of the rater (a person's code or a model run id)."},
        "rater_kind": {"enum": list(RATER_KINDS)},
        "model": {"type": ["string", "null"], "minLength": 1,
                  "description": "Model id for a model rater; null for a human. Two raters with the same model id are one source."},
        "blind": {"type": "boolean",
                  "description": "True only if the rater did not know the condition (with/without note) of the item."},
        "item_id": {"type": "string", "minLength": 1,
                    "description": "Opaque item id from the blind packet, never the trial id."},
        "label": {"type": ["number", "string"], "description": "The rating: a rubric total (number) or a category (string)."},
        "rationale": {"type": "string", "description": "Why; free text, may be empty only for a null-effort rating, which is rejected."},
        "timestamp": {"type": "string", "description": "ISO 8601, UTC."},
    },
}


class RatingError(ValueError):
    """A rating record or an agreement computation is malformed."""


@dataclass(frozen=True)
class RatingRecord:
    rater_id: str
    rater_kind: str
    model: Optional[str]
    blind: bool
    item_id: str
    label: Any
    rationale: str
    timestamp: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
    schema_version: str = SCHEMA_VERSION

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)

    @property
    def source_key(self) -> str:
        """Raters sharing a model id are one source; humans are identified by rater id."""
        return f"model:{self.model}" if self.rater_kind == "model" and self.model else f"human:{self.rater_id}"


def validate_rating(data: Mapping[str, Any]) -> RatingRecord:
    """Validate a plain dict against the schema and return the record; raises :class:`RatingError`."""
    if not isinstance(data, Mapping):
        raise RatingError("a rating must be an object")
    allowed = set(RATING_RECORD_SCHEMA["properties"])
    extra = sorted(set(data) - allowed)
    if extra:
        raise RatingError(f"unknown fields: {extra}")
    missing = [k for k in RATING_RECORD_SCHEMA["required"] if k not in data]
    if missing:
        raise RatingError(f"missing fields: {missing}")
    if data["schema_version"] != SCHEMA_VERSION:
        raise RatingError(f"schema_version must be {SCHEMA_VERSION!r}")
    for key in ("rater_id", "item_id"):
        if not isinstance(data[key], str) or not data[key].strip():
            raise RatingError(f"{key} must be a non-empty string")
    if data["rater_kind"] not in RATER_KINDS:
        raise RatingError(f"rater_kind must be one of {RATER_KINDS}")
    model = data["model"]
    if data["rater_kind"] == "model":
        if not isinstance(model, str) or not model.strip():
            raise RatingError("a model rater must name its model")
    elif model is not None:
        raise RatingError("a human rater has model null")
    if not isinstance(data["blind"], bool):
        raise RatingError("blind must be a boolean")
    label = data["label"]
    if isinstance(label, bool) or not isinstance(label, (int, float, str)) \
            or (isinstance(label, float) and not math.isfinite(label)) \
            or (isinstance(label, str) and not label.strip()):
        raise RatingError("label must be a finite number or a non-empty string")
    if not isinstance(data["rationale"], str) or not data["rationale"].strip():
        raise RatingError("a rating needs a rationale")
    ts = data.get("timestamp")
    if ts is not None and not isinstance(ts, str):
        raise RatingError("timestamp must be a string")
    return RatingRecord(
        rater_id=data["rater_id"].strip(), rater_kind=data["rater_kind"], model=model, blind=data["blind"],
        item_id=data["item_id"].strip(), label=label, rationale=data["rationale"],
        timestamp=ts or datetime.now(timezone.utc).isoformat(),
    )


def load_ratings(lines: Iterable[str]) -> List[RatingRecord]:
    """Parse JSON-lines of rating records (blank lines skipped)."""
    out = []
    for n, line in enumerate(lines, 1):
        if not line.strip():
            continue
        try:
            out.append(validate_rating(json.loads(line)))
        except (ValueError, TypeError) as exc:
            raise RatingError(f"line {n}: {exc}") from exc
    return out


def band(total: float) -> str:
    """Policy-02 band of a 0-10 rubric total."""
    if total >= 8:
        return BAND_PASS
    if total >= 5:
        return BAND_RETRY
    return BAND_FAIL


# ---------------------------------------------------------------------------------------------
# Agreement statistics
# ---------------------------------------------------------------------------------------------
def cohens_kappa(rater_a: Sequence[Hashable], rater_b: Sequence[Hashable], weights: Optional[str] = None) -> Optional[float]:
    """Cohen's kappa for two raters over the same items; ``weights`` is None, "linear" or "quadratic".

    Weighted kappa orders the categories by their sorted values (numbers or strings). Returns None when
    chance agreement is perfect (both raters used one identical single category), where kappa is undefined.
    """
    if len(rater_a) != len(rater_b):
        raise RatingError("the two raters must rate the same items")
    n = len(rater_a)
    if n == 0:
        raise RatingError("no items")
    cats = sorted(set(rater_a) | set(rater_b), key=lambda v: (str(type(v)), v))
    idx = {c: i for i, c in enumerate(cats)}
    k = len(cats)
    obs = [[0.0] * k for _ in range(k)]
    for a, b in zip(rater_a, rater_b):
        obs[idx[a]][idx[b]] += 1.0 / n
    row = [sum(obs[i]) for i in range(k)]
    col = [sum(obs[i][j] for i in range(k)) for j in range(k)]
    if weights is None:
        w = [[0.0 if i == j else 1.0 for j in range(k)] for i in range(k)]
    elif weights in ("linear", "quadratic"):
        if k == 1:
            w = [[0.0]]
        else:
            p = 1 if weights == "linear" else 2
            w = [[(abs(i - j) / (k - 1)) ** p for j in range(k)] for i in range(k)]
    else:
        raise RatingError("weights must be None, 'linear' or 'quadratic'")
    d_obs = sum(w[i][j] * obs[i][j] for i in range(k) for j in range(k))
    d_exp = sum(w[i][j] * row[i] * col[j] for i in range(k) for j in range(k))
    if d_exp == 0.0:
        return None
    return 1.0 - d_obs / d_exp


def fleiss_kappa(table: Sequence[Sequence[int]]) -> Optional[float]:
    """Fleiss' kappa. ``table[i][j]`` = number of raters who put item ``i`` in category ``j``.

    Every item must have been rated by the same number of raters (>= 2). Returns None when chance
    agreement is 1 (all ratings in one category), where kappa is undefined.
    """
    if not table:
        raise RatingError("empty table")
    n_raters = sum(table[0])
    if n_raters < 2:
        raise RatingError("Fleiss' kappa needs at least 2 raters per item")
    k = len(table[0])
    for row in table:
        if len(row) != k:
            raise RatingError("ragged table")
        if sum(row) != n_raters:
            raise RatingError("every item must have the same number of ratings")
    n_items = len(table)
    p_i = [(sum(c * c for c in row) - n_raters) / (n_raters * (n_raters - 1)) for row in table]
    p_bar = sum(p_i) / n_items
    p_j = [sum(row[j] for row in table) / (n_items * n_raters) for j in range(k)]
    p_e = sum(p * p for p in p_j)
    if p_e >= 1.0:
        return None
    return (p_bar - p_e) / (1.0 - p_e)


def krippendorff_alpha(units: Mapping[Hashable, Sequence[Any]], level: str = "nominal") -> Optional[float]:
    """Krippendorff's alpha for any number of raters with missing values.

    ``units`` maps each unit (item) to the values the raters gave it; missing ratings are simply absent.
    Units with fewer than 2 values are not pairable and are ignored. ``level`` is "nominal", "ordinal",
    "interval" or "ratio". Returns None when no disagreement is expected (a single value overall).
    """
    if level not in ("nominal", "ordinal", "interval", "ratio"):
        raise RatingError("level must be nominal, ordinal, interval or ratio")
    pairable = {u: list(v) for u, v in units.items() if len(v) >= 2}
    if not pairable:
        raise RatingError("no unit has two or more ratings")
    values = sorted({v for vs in pairable.values() for v in vs}, key=lambda v: (str(type(v)), v))
    index = {v: i for i, v in enumerate(values)}
    m = len(values)
    # Coincidence matrix.
    o = [[0.0] * m for _ in range(m)]
    for vs in pairable.values():
        mu = len(vs)
        for a in range(mu):
            for b in range(mu):
                if a != b:
                    o[index[vs[a]]][index[vs[b]]] += 1.0 / (mu - 1)
    n_c = [sum(o[c]) for c in range(m)]
    n = sum(n_c)
    if m == 1:
        return None

    def delta2(c: int, k: int) -> float:
        if c == k:
            return 0.0
        if level == "nominal":
            return 1.0
        vc, vk = values[c], values[k]
        if level == "interval":
            return float(vc - vk) ** 2
        if level == "ratio":
            return (float(vc - vk) / float(vc + vk)) ** 2
        lo, hi = (c, k) if c < k else (k, c)
        s = sum(n_c[g] for g in range(lo, hi + 1)) - (n_c[c] + n_c[k]) / 2.0
        return s * s

    d_o = sum(o[c][k] * delta2(c, k) for c in range(m) for k in range(m))
    d_e = sum(n_c[c] * n_c[k] * delta2(c, k) for c in range(m) for k in range(m)) / (n - 1)
    if d_e == 0.0:
        return None
    return 1.0 - d_o / d_e


def interpret_kappa(value: Optional[float]) -> str:
    """Landis & Koch (1977) verbal bands."""
    if value is None:
        return "undefined"
    if value < 0:
        return "poor"
    for limit, name in ((0.20, "slight"), (0.40, "fair"), (0.60, "moderate"), (0.80, "substantial")):
        if value <= limit:
            return name
    return "almost perfect"


# ---------------------------------------------------------------------------------------------
# Aggregation
# ---------------------------------------------------------------------------------------------
def aggregate_ratings(records: Sequence[RatingRecord], *, min_sources: int = 2, require_blind: bool = True,
                      numeric: bool = True) -> Dict[str, Any]:
    """Combine ratings of many raters into a consensus per item plus agreement statistics.

    * ``require_blind``: non-blind ratings are set aside (listed under ``excluded_unblind``), never mixed in.
    * Raters that share a source key (same model id) count as one source; if fewer than ``min_sources``
      independent sources remain, the result is ``INSUFFICIENT_RATERS`` and carries no consensus.
    * ``numeric``: labels are numbers; the consensus is the mean and the median, and Krippendorff's alpha is
      reported at interval and ordinal level. Otherwise labels are categories (consensus = plurality).
    * One rating per (rater, item); a second one is an error, not an overwrite.
    """
    used: List[RatingRecord] = []
    excluded: List[str] = []
    for r in records:
        if require_blind and not r.blind:
            excluded.append(f"{r.rater_id}:{r.item_id}")
        else:
            used.append(r)
    seen = set()
    for r in used:
        key = (r.rater_id, r.item_id)
        if key in seen:
            raise RatingError(f"duplicate rating by {r.rater_id} for item {r.item_id}")
        seen.add(key)
    sources = sorted({r.source_key for r in used})
    result: Dict[str, Any] = {
        "status": STATUS_OK, "sources": sources, "raters": sorted({r.rater_id for r in used}),
        "excluded_unblind": excluded, "items": {}, "agreement": {},
    }
    if len(sources) < min_sources:
        result["status"] = STATUS_INSUFFICIENT_RATERS
        result["reason"] = f"{len(sources)} independent source(s) of ratings, {min_sources} required"
        return result

    by_item: Dict[str, List[RatingRecord]] = defaultdict(list)
    for r in used:
        by_item[r.item_id].append(r)
    for item, rs in sorted(by_item.items()):
        labels = [r.label for r in rs]
        entry: Dict[str, Any] = {"n_ratings": len(rs), "labels": {r.rater_id: r.label for r in rs}}
        if numeric:
            nums = sorted(float(x) for x in labels)
            entry["mean"] = math.fsum(nums) / len(nums)
            mid = len(nums) // 2
            entry["median"] = nums[mid] if len(nums) % 2 else (nums[mid - 1] + nums[mid]) / 2.0
            entry["range"] = nums[-1] - nums[0]
        else:
            counts = Counter(labels).most_common()
            entry["consensus"] = counts[0][0] if len(counts) == 1 or counts[0][1] > counts[1][1] else None
            entry["counts"] = dict(counts)
        entry["unanimous"] = len(set(labels)) == 1
        result["items"][item] = entry

    units = {item: [r.label for r in rs] for item, rs in by_item.items()}
    agreement: Dict[str, Any] = {"items": len(by_item), "items_with_2plus_ratings": sum(1 for v in units.values() if len(v) >= 2)}
    try:
        if numeric:
            agreement["krippendorff_alpha_interval"] = krippendorff_alpha(units, "interval")
            agreement["krippendorff_alpha_ordinal"] = krippendorff_alpha(units, "ordinal")
            band_units = {item: [band(float(x)) for x in v] for item, v in units.items()}
            agreement["krippendorff_alpha_bands_nominal"] = krippendorff_alpha(band_units, "nominal")
            fk = _fleiss_from_units(band_units)
            agreement["fleiss_kappa_bands"] = fk
        else:
            agreement["krippendorff_alpha_nominal"] = krippendorff_alpha(units, "nominal")
            agreement["fleiss_kappa"] = _fleiss_from_units(units)
    except RatingError as exc:
        agreement["error"] = str(exc)
    two = _two_source_pairs(by_item)
    if two is not None:
        a, b = two
        agreement["cohens_kappa"] = cohens_kappa(a, b) if not numeric else cohens_kappa([band(float(x)) for x in a], [band(float(x)) for x in b])
        agreement["cohens_kappa_n_items"] = len(a)
    result["agreement"] = agreement
    return result


def _fleiss_from_units(units: Mapping[str, Sequence[Hashable]]) -> Optional[float]:
    """Fleiss' kappa on the items rated by the modal number of raters (a Fleiss table needs equal counts)."""
    sizes = Counter(len(v) for v in units.values() if len(v) >= 2)
    if not sizes:
        return None
    n_raters = max(sizes, key=lambda s: (sizes[s], s))
    rows = [v for v in units.values() if len(v) == n_raters]
    cats = sorted({x for v in rows for x in v}, key=lambda x: (str(type(x)), x))
    table = [[sum(1 for x in v if x == c) for c in cats] for v in rows]
    return fleiss_kappa(table)


def _two_source_pairs(by_item: Mapping[str, Sequence[RatingRecord]]) -> Optional[Tuple[List[Any], List[Any]]]:
    """If exactly two raters rated a common set of items, their aligned labels (else None)."""
    raters = sorted({r.rater_id for rs in by_item.values() for r in rs})
    if len(raters) != 2:
        return None
    a, b = [], []
    for rs in by_item.values():
        m = {r.rater_id: r.label for r in rs}
        if raters[0] in m and raters[1] in m:
            a.append(m[raters[0]])
            b.append(m[raters[1]])
    return (a, b) if a else None
