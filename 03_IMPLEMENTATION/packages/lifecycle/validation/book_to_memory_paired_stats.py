"""Paired statistics for the Book-to-Memory ablation harness (pure standard library).

Provides what ``PREREGISTRATION_B03_B05.md`` section 6 names: a paired t-test with a t confidence
interval and ``d_z``, a seeded sign-flip permutation test, a seeded percentile bootstrap interval and
the Holm adjustment. The Student-t distribution is computed from the regularised incomplete beta
function (continued fraction), so no SciPy is needed; the unit tests check it against published
critical values.
"""
from __future__ import annotations

import math
import random
from itertools import product
from typing import Dict, List, Optional, Sequence, Tuple


class StatsError(ValueError):
    """Raised for inputs a statistic cannot be computed from."""


# ---------------------------------------------------------------------------------------------
# Student t distribution
# ---------------------------------------------------------------------------------------------
def _betacf(a: float, b: float, x: float, max_iter: int = 400, eps: float = 3e-16) -> float:
    """Continued fraction for the incomplete beta function (modified Lentz)."""
    tiny = 1e-300
    qab, qap, qam = a + b, a + 1.0, a - 1.0
    c = 1.0
    d = 1.0 - qab * x / qap
    d = tiny if abs(d) < tiny else d
    d = 1.0 / d
    h = d
    for m in range(1, max_iter + 1):
        m2 = 2 * m
        aa = m * (b - m) * x / ((qam + m2) * (a + m2))
        d = 1.0 + aa * d
        d = tiny if abs(d) < tiny else d
        c = 1.0 + aa / c
        c = tiny if abs(c) < tiny else c
        d = 1.0 / d
        h *= d * c
        aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2))
        d = 1.0 + aa * d
        d = tiny if abs(d) < tiny else d
        c = 1.0 + aa / c
        c = tiny if abs(c) < tiny else c
        d = 1.0 / d
        delta = d * c
        h *= delta
        if abs(delta - 1.0) < eps:
            break
    return h


def betainc_reg(a: float, b: float, x: float) -> float:
    """Regularised incomplete beta function I_x(a, b)."""
    if x <= 0.0:
        return 0.0
    if x >= 1.0:
        return 1.0
    ln_front = math.lgamma(a + b) - math.lgamma(a) - math.lgamma(b) + a * math.log(x) + b * math.log1p(-x)
    front = math.exp(ln_front)
    if x < (a + 1.0) / (a + b + 2.0):
        return front * _betacf(a, b, x) / a
    return 1.0 - front * _betacf(b, a, 1.0 - x) / b


def t_cdf(t: float, df: float) -> float:
    """P(T <= t) for a Student t variable with ``df`` degrees of freedom."""
    if df <= 0:
        raise StatsError("degrees of freedom must be positive")
    x = df / (df + t * t)
    tail = 0.5 * betainc_reg(df / 2.0, 0.5, x)
    return 1.0 - tail if t > 0 else tail


def t_ppf(p: float, df: float) -> float:
    """Quantile of the Student t distribution (bisection on :func:`t_cdf`)."""
    if not 0.0 < p < 1.0:
        raise StatsError("p must be in (0, 1)")
    if p == 0.5:
        return 0.0
    lo, hi = -1e3, 1e3
    for _ in range(200):
        mid = (lo + hi) / 2.0
        if t_cdf(mid, df) < p:
            lo = mid
        else:
            hi = mid
        if hi - lo < 1e-12:
            break
    return (lo + hi) / 2.0


# ---------------------------------------------------------------------------------------------
# Descriptive helpers
# ---------------------------------------------------------------------------------------------
def mean(xs: Sequence[float]) -> float:
    if not xs:
        raise StatsError("mean of an empty sequence")
    return math.fsum(xs) / len(xs)


def sample_sd(xs: Sequence[float]) -> float:
    n = len(xs)
    if n < 2:
        raise StatsError("sample standard deviation needs at least 2 values")
    m = mean(xs)
    return math.sqrt(math.fsum((x - m) ** 2 for x in xs) / (n - 1))


# ---------------------------------------------------------------------------------------------
# Paired tests
# ---------------------------------------------------------------------------------------------
def paired_differences(with_scores: Sequence[float], without_scores: Sequence[float]) -> List[float]:
    if len(with_scores) != len(without_scores):
        raise StatsError("paired samples must have the same length")
    return [float(a) - float(b) for a, b in zip(with_scores, without_scores)]


def paired_t_test(diffs: Sequence[float], confidence: float = 0.95) -> Dict[str, Optional[float]]:
    """Paired t-test on the differences ``d_i`` (H0: mean 0), two-sided, with a t confidence interval.

    A zero-variance difference vector has no defined t statistic: ``t``, ``p_two_sided`` and ``dz``
    come back ``None`` (and the interval collapses on the mean) rather than a fabricated number.
    """
    n = len(diffs)
    if n < 2:
        raise StatsError("a paired t-test needs at least 2 pairs")
    md = mean(diffs)
    sd = sample_sd(diffs)
    df = n - 1
    se = sd / math.sqrt(n)
    out: Dict[str, Optional[float]] = {
        "n": n, "mean_diff": md, "sd_diff": sd, "se": se, "df": df,
        "t": None, "p_two_sided": None, "ci_low": md, "ci_high": md, "dz": None, "confidence": confidence,
    }
    if sd == 0.0:
        return out
    t = md / se
    p = 2.0 * (1.0 - t_cdf(abs(t), df))
    crit = t_ppf(1.0 - (1.0 - confidence) / 2.0, df)
    out.update({"t": t, "p_two_sided": min(1.0, max(0.0, p)), "ci_low": md - crit * se,
                "ci_high": md + crit * se, "dz": md / sd})
    return out


def sign_flip_permutation_test(diffs: Sequence[float], seed: int, n_draws: int = 100000,
                               exact_max_n: int = 20) -> Dict[str, object]:
    """Two-sided sign-flip permutation test of mean(d) = 0.

    Exact over all 2**n sign patterns when ``n <= exact_max_n``; otherwise ``n_draws`` seeded random
    sign patterns (the observed pattern is counted, so p is never 0).
    """
    n = len(diffs)
    if n < 1:
        raise StatsError("no differences")
    observed = abs(math.fsum(diffs))
    tol = 1e-12 * max(1.0, observed)
    if n <= exact_max_n:
        extreme = 0
        total = 2 ** n
        for signs in product((1.0, -1.0), repeat=n):
            if abs(math.fsum(s * d for s, d in zip(signs, diffs))) >= observed - tol:
                extreme += 1
        return {"method": "exact", "p_two_sided": extreme / total, "draws": total}
    rng = random.Random(seed)
    extreme = 1
    for _ in range(n_draws):
        s = math.fsum(d if rng.random() < 0.5 else -d for d in diffs)
        if abs(s) >= observed - tol:
            extreme += 1
    return {"method": "monte_carlo", "p_two_sided": extreme / (n_draws + 1), "draws": n_draws, "seed": seed}


def bootstrap_ci_mean(diffs: Sequence[float], seed: int, n_boot: int = 10000,
                      confidence: float = 0.95) -> Tuple[float, float]:
    """Seeded percentile bootstrap interval of the mean difference."""
    n = len(diffs)
    if n < 2:
        raise StatsError("a bootstrap interval needs at least 2 values")
    rng = random.Random(seed)
    means = sorted(math.fsum(diffs[rng.randrange(n)] for _ in range(n)) / n for _ in range(n_boot))
    lo_i = int(((1.0 - confidence) / 2.0) * n_boot)
    hi_i = int((1.0 - (1.0 - confidence) / 2.0) * n_boot) - 1
    return means[lo_i], means[max(lo_i, hi_i)]


def holm_adjust(pvalues: Sequence[float]) -> List[float]:
    """Holm step-down adjusted p-values, in the order given."""
    m = len(pvalues)
    order = sorted(range(m), key=lambda i: pvalues[i])
    adjusted = [0.0] * m
    running = 0.0
    for rank, i in enumerate(order):
        running = max(running, min(1.0, (m - rank) * pvalues[i]))
        adjusted[i] = running
    return adjusted
