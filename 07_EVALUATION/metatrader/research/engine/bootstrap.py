"""
engine/bootstrap.py — Stationary Block Bootstrap (Politis & Romano 1994) și
Testul Hansen Superior Predictive Ability (SPA, Hansen 2005) / White Reality Check.
"""

from typing import Dict, List, Tuple
import numpy as np


def stationary_bootstrap_indices(n: int, q: float, rng: np.random.RandomState) -> np.ndarray:
    """
    Generează un vector de n indici de resamplare folosind Stationary Bootstrap
    (Politis & Romano 1994), unde lungimea fiecărui bloc urmează o distribuție geometrică
    cu parametrul p_geom = 1 / q (lungime medie q).
    """
    p_geom = 1.0 / q
    indices = np.empty(n, dtype=np.int64)

    current_idx = rng.randint(0, n)
    indices[0] = current_idx

    for t in range(1, n):
        if rng.rand() < p_geom:
            current_idx = rng.randint(0, n)
        else:
            current_idx = (current_idx + 1) % n
        indices[t] = current_idx

    return indices


def compute_stationary_bootstrap_ci(
    series: np.ndarray,
    b_reps: int = 2000,
    q: float = 10.0,
    alpha: float = 0.05,
    seed: int = 42
) -> Tuple[float, float, float]:
    """
    Calculează intervalul de încredere (1 - alpha) pentru media unei serii temporale
    prin Stationary Block Bootstrap.
    """
    n = len(series)
    if n == 0:
        return 0.0, 0.0, 0.0

    mean_obs = float(np.mean(series))
    rng = np.random.RandomState(seed)
    boot_means = np.empty(b_reps, dtype=np.float64)

    for b in range(b_reps):
        idx = stationary_bootstrap_indices(n, q, rng)
        boot_means[b] = np.mean(series[idx])

    ci_lower = float(np.percentile(boot_means, 100.0 * (alpha / 2.0)))
    ci_upper = float(np.percentile(boot_means, 100.0 * (1.0 - alpha / 2.0)))
    return mean_obs, ci_lower, ci_upper


def hansen_spa_test(
    loss_benchmark: np.ndarray,
    loss_models: np.ndarray,
    b_reps: int = 2000,
    q: float = 10.0,
    seed: int = 42
) -> Dict[str, float]:
    """
    Evaluează Testul Hansen (2005) Superior Predictive Ability (SPA).
    
    H_0: max_k E[ d_k ] <= 0 (niciun model nu depășește benchmark-ul).
    d_{k, t} = loss_models[t, k] - loss_benchmark[t] (sau excess return over benchmark).
    """
    t_len, m_models = loss_models.shape
    if t_len != len(loss_benchmark):
        raise ValueError("Lungimile seriilor diferă!")

    # Diferențe de performanță (excess return)
    d = np.empty((t_len, m_models), dtype=np.float64)
    for k in range(m_models):
        d[:, k] = loss_models[:, k] - loss_benchmark

    d_bar = np.mean(d, axis=0)  # vector M

    # Resamplare bootstrap
    rng = np.random.RandomState(seed)
    boot_means = np.empty((b_reps, m_models), dtype=np.float64)

    for b in range(b_reps):
        idx = stationary_bootstrap_indices(t_len, q, rng)
        boot_means[b, :] = np.mean(d[idx, :], axis=0)

    # Standard error al mediei estimate prin bootstrap
    se_hat = np.std(boot_means, axis=0)
    se_hat = np.where(se_hat < 1e-12, 1e-12, se_hat)

    # Statistica studentizată observată T_SPA = max_k max(0, d_bar_k / SE(d_bar_k))
    t_k = d_bar / se_hat
    t_spa_obs = float(np.max(np.maximum(t_k, 0.0)))
    best_model_idx = int(np.argmax(t_k))

    # Centrarea Hansen (2005)
    # g_k = d_bar_k dacă d_bar_k >= - sqrt( 2 * ln(ln(T)) ) * se_hat
    c_thresh = -np.sqrt(2.0 * np.log(max(np.log(max(t_len, 3)), 1.01))) * se_hat
    g_hansen = np.where(d_bar >= c_thresh, d_bar, 0.0)

    # Distribuțiile nule de bootstrap
    t_spa_boot = np.empty(b_reps, dtype=np.float64)
    t_white_boot = np.empty(b_reps, dtype=np.float64)

    for b in range(b_reps):
        # Hansen SPA studentized
        z_b_hansen = (boot_means[b, :] - g_hansen) / se_hat
        t_spa_boot[b] = np.max(np.maximum(z_b_hansen, 0.0))

        # White Reality Check studentized
        z_b_white = (boot_means[b, :] - d_bar) / se_hat
        t_white_boot[b] = np.max(np.maximum(z_b_white, 0.0))

    p_val_spa = float(np.mean(t_spa_boot >= t_spa_obs))
    p_val_white = float(np.mean(t_white_boot >= t_spa_obs))

    return {
        "test_stat": round(t_spa_obs, 4),
        "p_value_spa": round(p_val_spa, 4),
        "p_value_white": round(p_val_white, 4),
        "best_model_idx": best_model_idx,
    }
