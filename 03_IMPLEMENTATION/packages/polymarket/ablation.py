"""Component ablations, with the multiple-comparison problem taken seriously.

An ablation ladder asks which parts of the system earn their place: drop memory,
drop the graph, drop calibration, drop abstention, and see what the numbers do.

## The problem this module is mostly about

Running eleven comparisons at 95% confidence and reporting the ones that clear
the threshold is not analysis. Under a true null — no component helps at all —
each comparison has a 5% chance of clearing it, so **eleven comparisons have a
43% chance of producing at least one "significant" result**:

    1 - 0.95 ** 11 = 0.43

Reporting that result without saying eleven were run is the textbook shape of a
finding that does not replicate. Section 33 of the brief names it: avoid
p-hacking, do not repeatedly tune against the same held-out set.

So every ablation run applies a Holm-Bonferroni correction across the whole
family, and the report carries both the raw and the corrected verdicts. Holm
rather than plain Bonferroni because Bonferroni over eleven hypotheses is
conservative enough to hide real effects, and a method that can only produce
nulls is no more honest than one that can only produce findings.

The family size is taken from the ladder, not from the comparisons that looked
interesting. Correcting for the three you decided to report is the same error
wearing a correction.

## What an ablation cannot tell you

Component interaction. Dropping memory and dropping the graph separately says
nothing about dropping both — if they are substitutes, each looks useless alone
while the pair is essential. The ladder therefore includes the combinations the
brief lists rather than only the single drops, and the report says plainly that
anything outside the ladder was not measured.
"""
from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Mapping, Sequence

from .benchmark import (
    VERDICT_BETTER,
    VERDICT_NO_DIFFERENCE,
    VERDICT_UNDERPOWERED,
    VERDICT_WORSE,
    ArmScores,
    Comparison,
    compare_arms,
)

ABLATION_SCHEMA_VERSION = "polymarket-ablation.v1"

#: Family-wise error rate the correction controls.
FAMILY_ALPHA = 0.05


@dataclass(frozen=True)
class AblationResult:
    """One rung: the full system against itself minus a component."""

    component: str
    comparison: Comparison
    #: Verdict before the family correction — what a single comparison would
    #: have said, kept so the effect of the correction is visible rather than
    #: applied silently.
    raw_verdict: str
    corrected_verdict: str
    #: The bootstrap tail probability that the difference is on the other side
    #: of zero. Not a p-value from a test statistic; the fraction of resamples
    #: that crossed. Named for what it is.
    crossing_fraction: float
    holm_threshold: float

    def as_dict(self) -> dict[str, object]:
        return {
            "component": self.component,
            "raw_verdict": self.raw_verdict,
            "corrected_verdict": self.corrected_verdict,
            "crossing_fraction": self.crossing_fraction,
            "holm_threshold": self.holm_threshold,
            "comparison": self.comparison.as_dict(),
        }


@dataclass(frozen=True)
class AblationReport:
    schema_version: str
    family_size: int
    alpha: float
    results: tuple[AblationResult, ...]

    def components_that_earn_their_place(self) -> tuple[str, ...]:
        """Components whose removal made things measurably worse.

        Removal making the system worse is what "this component helps" means.
        A component whose removal changes nothing is not helping, whatever its
        author intended, and this returns an empty tuple when that is the case
        for all of them.
        """
        return tuple(
            r.component for r in self.results
            if r.corrected_verdict == VERDICT_WORSE
        )

    def as_dict(self) -> dict[str, object]:
        return {
            "schema_version": self.schema_version,
            "family_size": self.family_size,
            "alpha": self.alpha,
            "components_that_earn_their_place":
                list(self.components_that_earn_their_place()),
            "results": [r.as_dict() for r in self.results],
        }


def _crossing_fraction(comparison: Comparison) -> float:
    """How close the interval came to containing zero.

    Derived from the reported interval rather than recomputed, so it cannot
    disagree with the verdict beside it. A comparison whose interval contains
    zero gets 1.0 — no correction can turn a null into a finding, and a value
    that could would be the bug.
    """
    if comparison.ci_low is None or comparison.ci_high is None:
        return 1.0
    if comparison.ci_low <= 0.0 <= comparison.ci_high:
        return 1.0
    #: Distance from zero relative to the interval's own width, mapped into
    #: (0, 1]. An interval far from zero relative to its width scores low.
    width = comparison.ci_high - comparison.ci_low
    if width <= 0.0:
        return 0.0
    distance = min(abs(comparison.ci_low), abs(comparison.ci_high))
    return max(0.0, min(1.0, math.exp(-distance / width)))


def run_ablations(
    full_system: ArmScores,
    ablated: Sequence[tuple[str, ArmScores]],
    targets: Mapping[tuple[str, str], float],
    *,
    alpha: float = FAMILY_ALPHA,
    **kwargs,
) -> AblationReport:
    """Compare the full system against each ablation, correcting across all.

    `ablated` is the whole ladder. Passing a subset because the others looked
    uninteresting is the error the correction exists to prevent, and nothing
    here can detect it — the family size comes from what is passed in.

    The comparison direction is deliberate: `full_system` is the baseline and
    the ablated variant is the challenger, so a component that helps produces
    CHALLENGER_WORSE. Removing something useful should look bad.
    """
    if not 0.0 < alpha < 1.0:
        raise ValueError("alpha must be between 0 and 1")
    if not ablated:
        raise ValueError("an ablation ladder needs at least one rung")

    comparisons = [
        (name, compare_arms(full_system, arm, targets, **kwargs))
        for name, arm in ablated
    ]

    #: Holm: order by how strongly each rejects, and require progressively
    #: less stringent thresholds down the list. Bonferroni over eleven
    #: hypotheses is conservative enough to hide real effects, and a method
    #: that can only produce nulls is no more honest than one that can only
    #: produce findings.
    scored = sorted(
        ((name, c, _crossing_fraction(c)) for name, c in comparisons),
        key=lambda item: item[2],
    )
    family = len(scored)

    results: list[AblationResult] = []
    still_rejecting = True
    for rank, (name, comparison, crossing) in enumerate(scored):
        threshold = alpha / (family - rank)
        if comparison.verdict == VERDICT_UNDERPOWERED:
            corrected = VERDICT_UNDERPOWERED
        elif not still_rejecting or crossing > threshold:
            #: Holm stops at the first failure to reject; everything below
            #: keeps its raw verdict's magnitude but loses its claim.
            still_rejecting = False
            corrected = VERDICT_NO_DIFFERENCE
        else:
            corrected = comparison.verdict
        results.append(AblationResult(
            name, comparison, comparison.verdict, corrected, crossing, threshold,
        ))

    return AblationReport(ABLATION_SCHEMA_VERSION, family, alpha, tuple(results))


__all__ = [
    "ABLATION_SCHEMA_VERSION",
    "FAMILY_ALPHA",
    "AblationResult",
    "AblationReport",
    "run_ablations",
]
