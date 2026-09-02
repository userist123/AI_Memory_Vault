"""memory_controller/effectiveness_stats.py — Statistical Foundations for Capability Effectiveness.

Provides sample-size-aware statistical estimators:
1. Wilson Score Interval lower bound for binomial proportion confidence.
2. Minimum sample size guards (preventing overconfidence on small samples like 1/1).
3. Laplace smoothing for probability estimation under sparse observations.

Invariants:
- Deterministic output across all platforms.
- Strict argument validation (rejects invalid trials, successes, confidence values).
- No external heavy statistical dependencies (pure Python standard library).
"""
from __future__ import annotations

import math
from typing import Any, Dict, Optional, Tuple

MIN_SAMPLE_SIZE = 5

# Common critical values for normal distribution quantile lookup
_Z_LOOKUP = {
    0.80: 1.281551565545,
    0.85: 1.439531470938,
    0.90: 1.644853626951,
    0.95: 1.959963984540,
    0.98: 2.326347874041,
    0.99: 2.575829303549,
    0.995: 2.807033768344,
    0.999: 3.290526731492,
}


def _normal_quantile(p: float) -> float:
    """Compute the quantile function (probit) for standard normal distribution.

    Uses Acklam's algorithm with absolute error < 1.15e-9.
    """
    if p <= 0.0 or p >= 1.0:
        raise ValueError(f"Probability must be strictly between 0 and 1, got {p}")

    # Coefficients in rational approximations
    a = [
        -3.969683028665376e01,
        2.209460984245205e02,
        -2.759285104469687e02,
        1.383577518672690e02,
        -3.066479806614716e01,
        2.506628277459239e00,
    ]
    b = [
        -5.447609879822406e01,
        1.615858368580409e02,
        -1.556989798598866e02,
        6.680131188771972e01,
        -1.328068155288572e01,
    ]
    c = [
        -7.784894002430293e-03,
        -3.223964580411365e-01,
        -2.400758277161838e00,
        -2.549732539343734e00,
        4.374664141464968e00,
        2.938163982698783e00,
    ]
    d = [
        7.784695709041462e-03,
        3.224671290700398e-01,
        2.445134137142996e00,
        3.754408661907416e00,
    ]

    p_low = 0.02425
    p_high = 1.0 - p_low

    if p < p_low:
        q = math.sqrt(-2.0 * math.log(p))
        return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / (
            ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1.0)
        )
    elif p <= p_high:
        q = p - 0.5
        r = q * q
        return (
            (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5])
            * q
            / ((((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1.0))
        )
    else:
        q = math.sqrt(-2.0 * math.log(1.0 - p))
        return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / (
            ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1.0)
        )


def _get_z(confidence: float) -> float:
    """Retrieve or calculate the two-sided z-score for a given confidence level."""
    if confidence <= 0.0 or confidence >= 1.0:
        raise ValueError(f"confidence must be strictly between 0.0 and 1.0, got {confidence}")

    # Check fast lookup table
    for level, z_val in _Z_LOOKUP.items():
        if abs(confidence - level) < 1e-6:
            return z_val

    alpha = 1.0 - confidence
    p = 1.0 - alpha / 2.0
    return _normal_quantile(p)


def laplace_smoothed_rate(successes: int, trials: int) -> float:
    """Calculate Laplace-smoothed success rate: (successes + 1) / (trials + 2).

    Parameters:
        successes: Number of successful trials (>= 0).
        trials: Total number of trials (>= successes).

    Returns:
        float in range [0.0, 1.0].
    """
    if not isinstance(successes, int) or isinstance(successes, bool):
        raise TypeError(f"successes must be an integer, got {type(successes).__name__}")
    if not isinstance(trials, int) or isinstance(trials, bool):
        raise TypeError(f"trials must be an integer, got {type(trials).__name__}")

    if trials < 0:
        raise ValueError(f"trials cannot be negative, got {trials}")
    if successes < 0:
        raise ValueError(f"successes cannot be negative, got {successes}")
    if successes > trials:
        raise ValueError(f"successes ({successes}) cannot exceed trials ({trials})")

    return (successes + 1.0) / (trials + 2.0)


def wilson_lower_bound(
    successes: int,
    trials: int,
    confidence: float = 0.95,
) -> float:
    """Calculate the lower bound of the Wilson score confidence interval.

    Formula:
        center = p + z^2 / (2n)
        spread = z * sqrt(p(1-p)/n + z^2/(4n^2))
        lower  = (center - spread) / (1 + z^2/n)

    Parameters:
        successes: Number of positive outcomes (>= 0).
        trials: Total number of trials (>= successes).
        confidence: Confidence level (0.0 < confidence < 1.0), defaults to 0.95.

    Returns:
        Deterministic float in [0.0, 1.0].
    """
    if not isinstance(successes, int) or isinstance(successes, bool):
        raise TypeError(f"successes must be an integer, got {type(successes).__name__}")
    if not isinstance(trials, int) or isinstance(trials, bool):
        raise TypeError(f"trials must be an integer, got {type(trials).__name__}")
    if not isinstance(confidence, (int, float)) or isinstance(confidence, bool):
        raise TypeError(f"confidence must be a float, got {type(confidence).__name__}")

    if trials < 0:
        raise ValueError(f"trials cannot be negative, got {trials}")
    if successes < 0:
        raise ValueError(f"successes cannot be negative, got {successes}")
    if successes > trials:
        raise ValueError(f"successes ({successes}) cannot exceed trials ({trials})")

    confidence = float(confidence)
    if confidence <= 0.0 or confidence >= 1.0:
        raise ValueError(f"confidence must be strictly between 0.0 and 1.0, got {confidence}")

    if trials == 0 or successes == 0:
        return 0.0

    z = _get_z(confidence)
    n = float(trials)
    p = float(successes) / n
    z2 = z * z

    denom = 1.0 + z2 / n
    center = p + z2 / (2.0 * n)
    variance_term = (p * (1.0 - p) / n) + (z2 / (4.0 * n * n))
    spread = z * math.sqrt(max(0.0, variance_term))

    lower = (center - spread) / denom
    return max(0.0, min(1.0, lower))


def evaluate_proportion(
    successes: int,
    trials: int,
    confidence: float = 0.95,
) -> Dict[str, Any]:
    """Evaluates binomial proportion with sample size guard, Wilson bound, and Laplace smoothing.

    Returns:
        Dictionary containing:
        - successes: int
        - trials: int
        - observed_rate: float
        - smoothed_rate: float
        - wilson_lower_bound: float
        - sample_size: int
        - status: 'INSUFFICIENT_DATA' | 'VALID'
        - min_sample_size: int
    """
    # Validation performed by underlying estimators
    w_lower = wilson_lower_bound(successes, trials, confidence)
    smoothed = laplace_smoothed_rate(successes, trials)
    obs_rate = (float(successes) / float(trials)) if trials > 0 else 0.0

    status = "INSUFFICIENT_DATA" if trials < MIN_SAMPLE_SIZE else "VALID"

    return {
        "successes": successes,
        "trials": trials,
        "sample_size": trials,
        "observed_rate": round(obs_rate, 4),
        "smoothed_rate": round(smoothed, 4),
        "wilson_lower_bound": round(w_lower, 4),
        "status": status,
        "min_sample_size": MIN_SAMPLE_SIZE,
        "confidence": float(confidence),
    }
