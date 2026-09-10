"""Cross-model agreement, and the two ways it can stop being evidence.

Agreement is counted rather than claimed, and no model can inflate it because
none of them sees the others' answers. That is what distinguishes it from the
per-candidate fields, which were all measured and found constant: confidence
is 1.00 on everything including fabricated candidates, and claim_type said
"definition" on every survivor before it was removed from the request.

`occurrences` belongs on a different list. It was recorded as dead at 1 for
47 of 48 concepts, but that was measured on ~44,000-character chunks where
one chunk covers a whole chapter. At 3 pages per chunk recurrence reappears,
and on one book a single model's `occurrences >= 2` recovered 11 concepts
that were all in the four-model core. It was the wrong resolution, not a dead
signal — so do not cite the 47-of-48 number as though it settled anything.

What these tests guard: counting a model against itself, which would make the
number meaningless while still looking plausible, and folding terms loosely
enough that distinct concepts merge into manufactured agreement.
"""

from __future__ import annotations

import json
import pathlib
import subprocess
import sys

import pytest

_REPO = pathlib.Path(__file__).resolve().parents[1]
_SCRIPT = _REPO / "30_SCRIPTS" / "ingestion" / "agree_across_models.py"
sys.path.insert(0, str(_SCRIPT.parent))

import agree_across_models as A  # noqa: E402


def _row(concept: str, model: str, definition: str = "a definition long enough"):
    return {"concept": concept, "model": model, "definition": definition}


def _write(tmp_path: pathlib.Path, name: str, rows: list[dict]) -> pathlib.Path:
    path = tmp_path / name
    path.write_text(json.dumps(rows), encoding="utf-8")
    return path


def test_agreement_counts_distinct_models():
    runs = {
        "a": {"reservoir sampling": _row("Reservoir sampling", "a")},
        "b": {"reservoir sampling": _row("Reservoir Sampling", "b")},
        "c": {"soft target": _row("Soft-targets", "c")},
    }
    combined = A.combine(runs)
    by_term = {r["concept"].lower(): r for r in combined}
    assert by_term["reservoir sampling"]["models_agreeing"] == 2
    assert by_term["reservoir sampling"]["found_by"] == ["a", "b"]
    assert by_term["soft-targets"]["models_agreeing"] == 1
    #: Highest agreement first — the list is a review order, not a set.
    assert combined[0]["models_agreeing"] >= combined[-1]["models_agreeing"]


def test_the_same_model_twice_is_refused(tmp_path):
    """Two runs of one model are one opinion, not two.

    This is the failure that would make the whole signal meaningless while
    still producing a plausible-looking number.
    """
    one = _write(tmp_path, "one.json", [_row("Reservoir sampling", "qwen2.5:7b")])
    two = _write(tmp_path, "two.json", [_row("Reservoir sampling", "qwen2.5:7b")])
    result = subprocess.run(
        [sys.executable, str(_SCRIPT), str(one), str(two),
         "--output-file", str(tmp_path / "out.json")],
        capture_output=True, text=True,
    )
    assert result.returncode != 0
    assert "agreement between a model and itself" in (result.stdout + result.stderr)


def test_a_single_run_is_refused(tmp_path):
    one = _write(tmp_path, "one.json", [_row("Reservoir sampling", "m1")])
    result = subprocess.run(
        [sys.executable, str(_SCRIPT), str(one),
         "--output-file", str(tmp_path / "out.json")],
        capture_output=True, text=True,
    )
    assert result.returncode != 0
    assert "at least two runs" in (result.stdout + result.stderr)


@pytest.mark.parametrize(
    "a,b",
    [
        ("Continual learning (CL)", "continual learning"),
        ("Regularization-based approaches", "regularization based approach"),
        ("Synaptic Consolidation", "synaptic consolidation"),
    ],
)
def test_normalization_folds_presentation_not_meaning(a, b):
    """Both spellings appeared in real runs of the same corpus."""
    assert A.normalize_term(a) == A.normalize_term(b)


@pytest.mark.parametrize(
    "a,b",
    [
        ("Semantic memory", "Episodic memory"),
        ("Supervised loss", "Overall loss"),
        ("Class-IL", "Domain-IL"),
    ],
)
def test_normalization_does_not_merge_different_concepts(a, b):
    """The counting is worthless if the folding is too eager.

    Each of these pairs was produced by the same run and they are distinct
    concepts; merging any pair would manufacture agreement that nobody
    expressed.
    """
    assert A.normalize_term(a) != A.normalize_term(b)


def test_min_models_filters_and_reports_what_it_dropped(tmp_path):
    """The threshold discards real concepts, so it must be explicit.

    Measured: `catastrophic forgetting` and `experience replay` were each
    found by exactly one of four models. A threshold of 2 loses both.
    """
    a = _write(tmp_path, "a.json", [
        _row("Reservoir sampling", "m1"),
        _row("Catastrophic forgetting", "m1"),
    ])
    b = _write(tmp_path, "b.json", [_row("Reservoir sampling", "m2")])
    out = tmp_path / "out.json"

    kept_all = subprocess.run(
        [sys.executable, str(_SCRIPT), str(a), str(b), "--output-file", str(out)],
        capture_output=True, text=True,
    )
    assert kept_all.returncode == 0
    assert len(json.loads(out.read_text(encoding="utf-8"))) == 2

    filtered = subprocess.run(
        [sys.executable, str(_SCRIPT), str(a), str(b),
         "--output-file", str(out), "--min-models", "2"],
        capture_output=True, text=True,
    )
    assert filtered.returncode == 0
    rows = json.loads(out.read_text(encoding="utf-8"))
    assert [r["concept"] for r in rows] == ["Reservoir sampling"]
    assert "discarded: 1" in filtered.stdout


def test_default_threshold_keeps_everything():
    """A filter that silently drops real concepts must be opted into."""
    source = _SCRIPT.read_text(encoding="utf-8")
    assert '"--min-models", type=int, default=1' in source


@pytest.mark.parametrize(
    "plural,singular",
    [
        ("approaches", "approach"),
        ("boundaries", "boundary"),
        ("processes", "process"),
        ("methods", "method"),
        ("cases", "case"),
    ],
)
def test_plural_folding_reaches_the_real_stem(plural, singular):
    """A bare trailing-s strip made "approaches" into "approache".

    It failed on the first real pair it met, which is why the rules are
    listed rather than assumed.
    """
    assert A._singular(plural) == singular


@pytest.mark.parametrize("word", ["loss", "bias", "mass", "class", "ids"])
def test_short_words_and_double_s_are_left_alone(word):
    """Over-eager folding manufactures agreement nobody expressed."""
    assert A._singular(word) == word
